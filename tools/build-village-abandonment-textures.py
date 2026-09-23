#!/usr/bin/env python3
"""Measured, deterministic weathered variants of the existing village surfaces."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("village_facade", ROOT / "tools/build-village-facade-textures.py")
facade = importlib.util.module_from_spec(spec)
spec.loader.exec_module(facade)
OUTPUT = ROOT / "Assets/Resources/Village/Textures"
SPECS = (("AbandonedWood", "VillageJoineryAlbedo", 1.4),
         ("AbandonedPlaster", "VillagePlasterAlbedo", 2.4),
         ("AbandonedRoof", "VillageRoofAlbedo", 2.4))

def build():
    originals = facade.build_all()
    result = {}
    for index, (name, donor, pitch) in enumerate(SPECS):
        image = originals[donor].convert("L")
        detail = Image.new("L", image.size, 128)
        draw = ImageDraw.Draw(detail)
        rng = np.random.default_rng(92310 + index)
        size = image.width
        for n in range(95 if index == 0 else 58):
            x, y = (int(v) for v in rng.integers(0, size, 2))
            if index == 0:
                # Weather follows grain; worn pale fibre beside a dark check.
                length = int(rng.integers(30, 230))
                facade.wrapped_line(draw, [(x, y), (x + 3, y + length // 2), (x - 2, y + length)], 74, 3)
                facade.wrapped_line(draw, [(x + 4, y + 12), (x + 6, y + length - 7)], 174, 4)
            elif index == 1:
                # Small lost plaster edges and branching cracks, no painted lighting.
                w, h = (int(v) for v in rng.integers(8, 43, 2))
                facade.wrapped_polygon(draw, [(x-w,y), (x-w//2,y-h), (x+w//3,y-h//2),
                    (x+w,y+h//4), (x+w//4,y+h), (x-w//3,y+h//2)], 83)
                facade.wrapped_line(draw, [(x,y), (x+5,y+18), (x-3,y+31), (x+6,y+47)], 72, 2)
            else:
                facade.wrapped_line(draw, [(x,y), (x+17,y+3), (x+34,y)], 67, 3)
                facade.wrapped_line(draw, [(x,y+5), (x+25,y+6)], 172, 3)
        values = np.asarray(image, dtype=np.float32) + (np.asarray(detail, dtype=np.float32)-128)*.55
        # Preserve mean luminance so authored material tints stay meaningful.
        lo, hi = .1, 3.0
        for _ in range(24):
            gain = (lo + hi) / 2
            pixels = np.clip(values * gain, 0, 255).astype(np.uint8)
            if facade.LINEAR[pixels].mean() < .58: lo = gain
            else: hi = gain
        result[name] = (Image.fromarray(pixels).convert("RGB"), pitch)
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    facade.toolchain.check_python(facade.toolchain.load_config(), ("Pillow", "numpy"))
    images, repeated = build(), build()
    rows = []
    for name, (image, pitch) in images.items():
        assert image.tobytes() == repeated[name][0].tobytes(), "Nondeterministic aged albedo"
        stream = io.BytesIO(); image.save(stream, format="PNG", optimize=False)
        raw = stream.getvalue(); path = OUTPUT / (name + ".png")
        mean = float(facade.LINEAR[np.asarray(image)[:,:,0]].mean())
        assert abs(mean-.58) < .006 and np.asarray(image).std() > 6
        rows.append(dict(name=name, meters_per_tile=pitch, mean_linear_luminance=mean,
            sha256=hashlib.sha256(raw).hexdigest(), donor=SPECS[len(rows)][1]))
        if args.validate_only: assert path.read_bytes() == raw, "Stale " + name
        else: path.write_bytes(raw); facade.write_meta(path)
    data = json.dumps(dict(design_id="village_abandoned_surfaces_v1", generator_version="1.0.0",
        texture_size=facade.SIZE, sheets=rows), indent=2) + "\n"
    manifest = OUTPUT / "VillageAbandonmentTextures.json"
    if args.validate_only: assert manifest.read_text(encoding="utf-8") == data
    else: manifest.write_text(data, encoding="utf-8"); facade.write_meta(manifest)
    print("VILLAGE ABANDONMENT TEXTURES OK: deterministic aged donors, measured mean, opaque maps")

if __name__ == "__main__": main()
