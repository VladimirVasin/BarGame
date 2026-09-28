#!/usr/bin/env python3
"""Bake bounded, linear response data beside the existing exterior albedos.

This is data processing, not a colour-texture generator. The allowlisted albedos
and their palettes remain byte-for-byte unchanged. Families separate aggregate,
cracks, joints, fibres and organic deposits using wrap-filtered frequency bands;
colour is never interpreted directly as displacement. Height is a gentle local
detail signal only. World-scale use, road repairs, weather and adjacency belong
to the runtime surface recipe and existing terrain/snow owners.

RGBA: smoothness multiplier, centred detail height, cavity tendency, opaque.
All maps inherit their source UV orientation and metres-per-tile. Generate with
``python tools/build-ground-surface-maps.py``; ``--check`` compares all bytes,
source/output hashes, import settings, ranges and periodic seam measurements
without writing anything. Requires NumPy and Pillow, no Blender or Unity.
"""

from __future__ import annotations

import argparse
from dataclasses import asdict, dataclass
import hashlib
import io
import json
from pathlib import Path
import re
import sys

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/Textures/SurfaceResponse"
MANIFEST = ROOT / "ArtSource/Surfaces/ground-surface-maps.json"
SIZE = 512


@dataclass(frozen=True)
class Recipe:
    meaning: str
    smoothness: float
    grain: float
    cavity: float
    broad: float
    height: float
    organic: float = 0.0


RECIPES = {
    "asphalt": Recipe("Rough aggregate; existing dark repairs and cracks retain distinct response.",
                      0.58, 0.13, 0.17, 0.10, 0.034),
    "paint": Recipe("Intact marking paint is smoother; existing chips expose rough aggregate.",
                    0.78, 0.055, 0.22, 0.05, 0.014),
    "concrete": Recipe("Concrete pores and joints interrupt gently polished high areas.",
                       0.56, 0.10, 0.19, 0.06, 0.027),
    "paving": Recipe("Worn paving tops and recessed dirty joints use different response.",
                     0.64, 0.085, 0.25, 0.06, 0.033),
    "granite": Recipe("Fine stone grain beneath relatively smooth old granite faces.",
                      0.76, 0.09, 0.12, 0.035, 0.018),
    "rock": Recipe("Bedding and fractures remain broad; only local stone grain perturbs normals.",
                   0.52, 0.09, 0.19, 0.075, 0.032),
    "soil": Recipe("Low humus response, sparse embedded grains and darker organic hollows.",
                   0.43, 0.10, 0.11, 0.06, 0.031, 0.35),
    "grass": Recipe("Matte grass and earth gaps; no shiny individual blades.",
                    0.36, 0.055, 0.07, 0.035, 0.018, 0.55),
    "gravel": Recipe("Scattered stone faces over rough fines and recessed small gaps.",
                     0.48, 0.12, 0.16, 0.065, 0.037),
    "sand": Recipe("Fine sand remains matte; broad compacted regions vary gently.",
                   0.43, 0.065, 0.08, 0.075, 0.017),
    "timber": Recipe("Source-aligned fibres; worn faces smoother than old board grooves.",
                     0.61, 0.07, 0.25, 0.04, 0.025),
    "snow": Recipe("Soft wind-packed snow bands; no per-grain sparkle or invented tracks.",
                   0.68, 0.025, 0.07, 0.055, 0.012),
}

# Explicit ownership: adding a facade, prop, or interior texture is not implied
# by the presence of an Albedo suffix. These are exterior support surfaces.
SOURCES = {
    "Assets/Resources/Textures/CityRoadAsphaltAlbedo.png": "asphalt",
    "Assets/Resources/Textures/CityRoadMarkingAlbedo.png": "paint",
    "Assets/Resources/Textures/CitySidewalkAlbedo.png": "concrete",
    "Assets/Resources/Textures/CityGroundSoilAlbedo.png": "soil",
    "Assets/Resources/Textures/CityParkLawnAlbedo.png": "grass",
    "Assets/Resources/Textures/CityParkPathAlbedo.png": "gravel",
    "Assets/Resources/Textures/CityParkPlazaAlbedo.png": "paving",
    "Assets/Resources/Textures/CityParkTimberAlbedo.png": "timber",
    "Assets/Resources/Textures/CityParkStoneAlbedo.png": "rock",
    "Assets/Resources/Textures/CityCemeterySoilAlbedo.png": "soil",
    "Assets/Resources/Textures/CityCemeteryGravelAlbedo.png": "gravel",
    "Assets/Resources/Textures/CityCemeteryStoneAlbedo.png": "rock",
    "Assets/Resources/Textures/CityCemeteryGraniteAlbedo.png": "granite",
    "Assets/Resources/Textures/CityRiverPavingAlbedo.png": "paving",
    "Assets/Resources/Textures/CityRiverQuayAlbedo.png": "concrete",
    "Assets/Resources/Textures/CityRiverBedAlbedo.png": "gravel",
    "Assets/Resources/Textures/CitySeacoastSandAlbedo.png": "sand",
    "Assets/Resources/Textures/CitySeacoastConcreteAlbedo.png": "concrete",
    "Assets/Resources/Textures/CitySeacoastPlankAlbedo.png": "timber",
    "Assets/Resources/Textures/CityFringeForefieldAlbedo.png": "soil",
    "Assets/Resources/Textures/CityFringeServiceTrackAlbedo.png": "gravel",
    "Assets/Resources/Textures/CityFringeConcreteAlbedo.png": "concrete",
    "Assets/Resources/Textures/CityFringeMasonryAlbedo.png": "paving",
    "Assets/Resources/Textures/CityMountainRockAlbedo.png": "rock",
    "Assets/Resources/Textures/CityPoiPavingAlbedo.png": "paving",
    "Assets/Resources/Textures/CityPoiTimberAlbedo.png": "timber",
    "Assets/Resources/Textures/MountainRoadAsphaltAlbedo.png": "asphalt",
    "Assets/Resources/Textures/MountainRoadForestFloorAlbedo.png": "soil",
    "Assets/Resources/Textures/MountainRoadSnowAlbedo.png": "snow",
    "Assets/Resources/Textures/MountainRoadStoneAlbedo.png": "rock",
    "Assets/Church/Textures/ChurchStoneAlbedo.png": "rock",
    "Assets/Church/Textures/ChurchFloorAlbedo.png": "paving",
    "Assets/ChurchGarden/Textures/GardenStoneAlbedo.png": "paving",
    "Assets/Resources/City/Port/Textures/PortConcreteAlbedo.png": "concrete",
    "Assets/Resources/City/Port/Textures/PortConcreteWallAlbedo.png": "concrete",
    "Assets/Resources/City/Port/Textures/PortDeckAlbedo.png": "concrete",
    "Assets/Resources/City/Port/Textures/PortTimberAlbedo.png": "timber",
    "Assets/Resources/Village/Textures/VillageJoineryAlbedo.png": "timber",
    "Assets/Resources/Village/Textures/VillageStoneAlbedo.png": "paving",
    "Assets/Resources/Village/Textures/VillagePlasterAlbedo.png": "paving",
}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def periodic_blur(values: np.ndarray, sigma: float) -> np.ndarray:
    """Gaussian low-pass on a torus, without clamped edges or zero padding."""
    fy = np.fft.fftfreq(values.shape[0])[:, None]
    fx = np.fft.rfftfreq(values.shape[1])[None, :]
    kernel = np.exp(-2.0 * np.pi ** 2 * sigma ** 2 * (fx * fx + fy * fy))
    return np.fft.irfft2(np.fft.rfft2(values) * kernel, s=values.shape)


def bounded_band(values: np.ndarray, floor: float = 0.004) -> np.ndarray:
    """Bound signal without amplifying a nearly featureless sheet into noise."""
    scale = max(float(np.percentile(np.abs(values), 85)), floor)
    return np.tanh(values / scale)


def render(source: Path, family: str) -> tuple[np.ndarray, tuple[int, int]]:
    recipe = RECIPES[family]
    with Image.open(source) as image:
        original_size = image.size
        if original_size[0] != original_size[1] or min(original_size) < 256:
            raise ValueError(f"{source.name}: expected square source at least 256 px")
        size = min(SIZE, min(original_size))
        rgb = np.asarray(image.convert("RGB").resize((size, size), Image.Resampling.BOX),
                         dtype=np.float64) / 255.0
    linear = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    luminance = linear @ np.array([0.2126, 0.7152, 0.0722])
    # All features are filtered periodically before nonlinear interpretation.
    # The widest colour variation never feeds the local height channel.
    fine = periodic_blur(luminance, 0.85)
    middle = periodic_blur(luminance, 4.0)
    broad = periodic_blur(luminance, 19.0)
    detail = bounded_band(fine - middle)
    aggregate = periodic_blur(np.abs(detail), 1.1)
    hollow = np.maximum(0.0, -bounded_band(fine - broad))
    broad_signal = bounded_band(broad - np.mean(broad))
    green = np.maximum(0.0, linear[:, :, 1] - (linear[:, :, 0] + linear[:, :, 2]) * 0.5)
    organic = periodic_blur(green / np.maximum(luminance, 0.05), 2.0)
    cavity = np.clip(0.09 + 0.58 * hollow + recipe.organic * organic, 0.0, 0.8)

    smooth = (recipe.smoothness - recipe.grain * aggregate
              - recipe.cavity * cavity + recipe.broad * broad_signal)
    height_signal = detail * 0.7 + bounded_band(middle - broad) * 0.3
    if family == "paint":
        # Marking brightness locates intact pigment, but does not emboss paint.
        smooth += 0.07 * bounded_band(middle - np.median(middle))
        height_signal = detail * 0.45
    elif family == "snow":
        # Existing wind bands stay broad; fine luminance flecks are not bumps.
        height_signal = bounded_band(middle - broad) * 0.65
    elif family in ("soil", "grass", "sand"):
        smooth -= recipe.organic * organic * 0.08
        height_signal *= 0.7

    height = 0.5 + recipe.height * height_signal
    channels = np.stack([np.clip(smooth, 0.25, 0.98), height, cavity,
                         np.ones_like(height)], axis=-1)
    return np.rint(channels * 255.0).astype(np.uint8), original_size


def measurements(pixels: np.ndarray) -> dict:
    values = pixels[:, :, :3].astype(np.float64)
    interior = (np.abs(np.diff(values, axis=0)).mean(axis=(0, 1))
                + np.abs(np.diff(values, axis=1)).mean(axis=(0, 1))) * 0.5
    seam = (np.abs(values[0] - values[-1]).mean(axis=0)
            + np.abs(values[:, 0] - values[:, -1]).mean(axis=0)) * 0.5
    strongest = np.maximum(np.abs(np.diff(values, axis=0)).mean(axis=1).max(axis=0),
                           np.abs(np.diff(values, axis=1)).mean(axis=0).max(axis=0))
    seam_max = np.maximum(np.abs(values[0] - values[-1]).mean(axis=0),
                          np.abs(values[:, 0] - values[:, -1]).mean(axis=0))
    return {
        "channelMin": pixels.min(axis=(0, 1)).tolist(),
        "channelMax": pixels.max(axis=(0, 1)).tolist(),
        "channelMean": np.round(pixels.mean(axis=(0, 1)), 5).tolist(),
        "channelStd": np.round(pixels.std(axis=(0, 1)), 5).tolist(),
        "seamDelta": np.round(seam, 5).tolist(),
        "seamToInteriorRatio": np.round(seam / np.maximum(interior, 0.25), 5).tolist(),
        "seamToStrongestLineRatio": np.round(seam_max / np.maximum(strongest, 0.25), 5).tolist(),
    }


def validate_pixels(name: str, pixels: np.ndarray, stats: dict) -> None:
    if (pixels.shape[0] not in (256, 512) or pixels.shape[1] != pixels.shape[0]
            or pixels.shape[2] != 4):
        raise ValueError(f"{name}: response dimensions must be 256/512 RGBA")
    low, high = stats["channelMin"], stats["channelMax"]
    if low[0] < 64 or high[0] > 250 or low[1] < 115 or high[1] > 141:
        raise ValueError(f"{name}: response or gentle-height range exceeded")
    if high[2] > 204 or low[3] != 255 or high[3] != 255:
        raise ValueError(f"{name}: cavity/opaque range exceeded")
    # Bedding/board joints can naturally cross the repeat boundary. Compare
    # against real internal line transitions, as the existing albedo tools do,
    # instead of treating every authored seam as a flat-field noise process.
    if max(stats["seamToStrongestLineRatio"]) > 1.5:
        raise ValueError(f"{name}: boundary gradient exceeds periodic seam budget")
    if stats["channelStd"][0] < 0.35:
        raise ValueError(f"{name}: smoothness data became uniform")


def texture_meta(path: Path) -> bytes:
    guid = hashlib.md5(path.relative_to(ROOT).as_posix().encode("utf-8")).hexdigest()
    return f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  serializedVersion: 13
  externalObjects: {{}}
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 0
    linearTexture: 1
  bumpmap:
    convertToNormalMap: 0
  isReadable: 0
  streamingMipmaps: 0
  textureFormat: 1
  maxTextureSize: 512
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 4
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 0
  textureType: 0
  textureShape: 1
  alphaSource: 1
  alphaIsTransparency: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 512
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    overridden: 0
  - serializedVersion: 4
    buildTarget: Standalone
    maxTextureSize: 512
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    overridden: 1
  userData: GroundSurfaceResponse RGBA=smoothness,height,cavity,opaque
  assetBundleName:
  assetBundleVariant:
""".encode("utf-8")


def validate_meta(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    if not re.search(r"^guid: [0-9a-f]{32}$", text, re.MULTILINE):
        raise ValueError(f"{path.name}: missing valid persistent GUID")
    required = {"enableMipMap": "1", "sRGBTexture": "0", "isReadable": "0",
                "filterMode": "1", "wrapU": "0", "wrapV": "0", "wrapW": "0",
                "textureType": "0", "maxTextureSize": "512", "textureCompression": "0",
                "alphaIsTransparency": "0", "convertToNormalMap": "0"}
    for key, expected in required.items():
        found = re.findall(rf"^\s+{key}: (\S+)$", text, re.MULTILINE)
        if not found or any(value != expected for value in found):
            raise ValueError(f"{path.name}: expected {key}={expected}, found {found}")


def run(check: bool) -> None:
    entries = []
    expected_files = set()
    for relative, family in SOURCES.items():
        source = ROOT / relative
        stem = source.stem.removesuffix("Albedo") + "Response"
        path = OUTPUT / (stem + ".png")
        expected_files.add(path.name)
        source_bytes = source.read_bytes()
        pixels, original_size = render(source, family)
        stats = measurements(pixels)
        validate_pixels(stem, pixels, stats)
        buffer = io.BytesIO()
        Image.fromarray(pixels).save(buffer, format="PNG", optimize=False, compress_level=9)
        png = buffer.getvalue()
        meta = path.with_suffix(".png.meta")
        if check:
            if not path.exists() or path.read_bytes() != png:
                raise ValueError(f"{path.name}: missing or stale; run generator")
        else:
            OUTPUT.mkdir(parents=True, exist_ok=True)
            path.write_bytes(png)
            if not meta.exists():
                meta.write_bytes(texture_meta(path))
        validate_meta(meta)
        if source.read_bytes() != source_bytes:
            raise ValueError(f"{source.name}: source changed during generation")
        entries.append({
            "source": relative,
            "sourceSha256": digest(source_bytes),
            "sourceSize": list(original_size),
            "resourcePath": "Textures/SurfaceResponse/" + stem,
            "family": family,
            "size": pixels.shape[0],
            "sha256": digest(png),
            **stats,
        })

    unexpected = {p.name for p in OUTPUT.glob("*.png")} - expected_files
    if unexpected:
        raise ValueError(f"Unexpected response outputs: {sorted(unexpected)}")
    folder_meta = OUTPUT.with_suffix(".meta")
    if not folder_meta.exists():
        if check:
            raise ValueError("SurfaceResponse folder .meta missing")
        guid = hashlib.md5(OUTPUT.relative_to(ROOT).as_posix().encode("utf-8")).hexdigest()
        folder_meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\n"
                               "DefaultImporter:\n  externalObjects: {}\n  userData:\n"
                               "  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")
    manifest = {
        "version": 1,
        "generator": "tools/build-ground-surface-maps.py",
        "colourSourcesUnchanged": True,
        "channels": {"R": "smoothness multiplier", "G": "gentle centred detail height",
                     "B": "cavity/dirt tendency", "A": "opaque"},
        "import": {"linear": True, "repeat": True, "bilinear": True, "mipmaps": True,
                   "readable": False, "compressed": False, "maxSize": SIZE},
        "placement": "Inherit source UVs and metres-per-tile; macro use/weather remain runtime-owned.",
        "recipes": {name: asdict(recipe) for name, recipe in RECIPES.items()},
        "maps": entries,
    }
    manifest_bytes = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    if check:
        if not MANIFEST.exists() or MANIFEST.read_bytes() != manifest_bytes:
            raise ValueError("Ground response manifest missing or stale; run generator")
    else:
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_bytes(manifest_bytes)
    print(f"{'Verified' if check else 'Generated'} {len(entries)} ground response maps; "
          "source hashes, RGBA ranges, periodic seams and linear importers valid.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="verify determinism without writing")
    args = parser.parse_args()
    try:
        run(args.check)
    except (ValueError, OSError) as error:
        print(f"Ground response validation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
