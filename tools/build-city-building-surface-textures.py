#!/usr/bin/env python3
"""Build deterministic material sheets for the four city building districts.

FacadePrimary and FacadeSecondary are whole-building atlases rather than a
small sample repeated over a tower.  Their four equal-width columns are Front,
Rear, Left and Right; V runs once from the bottom to the top of the visible
role.  Plinth is a separate non-repeating full-face sheet: every authored face
consumes the complete 0..1 range, preserving one grounded vertical wear
history without tiling.

Roof, Metal and WindowFrame remain seamless physical-scale sheets because
those roles are made from many independently sized parts.  None of the sheets
paints apertures, windows, signs, text or story details into the albedo.
Pillow is the only direct dependency; the periodic noise and tone contract
are reused from build-home-textures.py.

The normal build owns the PNGs and their Unity import metadata.  GUIDs derive
from stable project-relative asset paths, so rebuilding never churns scene or
prefab references.  --verify builds and validates everything in memory and
writes nothing.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import random
import re
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageStat


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = (
    ROOT / "Assets" / "Resources" / "Textures" / "CityBuildingSurfaces"
)
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "City" / "BuildingSurfaces"

_home_spec = importlib.util.spec_from_file_location(
    "build_home_textures",
    Path(__file__).resolve().parent / "build-home-textures.py",
)
if _home_spec is None or _home_spec.loader is None:
    raise RuntimeError("Could not load the shared deterministic texture engine.")
home = importlib.util.module_from_spec(_home_spec)
sys.modules["build_home_textures"] = home
_home_spec.loader.exec_module(home)


SHEET_SIZE = home.SHEET_SIZE
SIDE_ORDER = ("Front", "Rear", "Left", "Right")
SIDE_COLUMNS = len(SIDE_ORDER)
COLUMN_WIDTH = SHEET_SIZE // SIDE_COLUMNS
COLUMN_GUTTER = 4
COLUMN_CONTENT_WIDTH = COLUMN_WIDTH - COLUMN_GUTTER * 2

MEAN_LUMINANCE_TARGET = 0.35
MEAN_LUMINANCE_TOLERANCE = 0.02
MAXIMUM_NIGHT_FACADE_CHANNEL = 0.616
ALBEDO_COMPENSATION = 1.0 / 0.62
BRIGHTNESS_ERROR_LIMIT = 0.08
EDGE_DELTA_LIMIT = 16.0
SEAM_RATIO_LIMIT = 2.5
CHANNEL_RATIO_LIMIT = 1.22
ATLAS_COLUMN_SEPARATION_FLOOR = 2.0
ATLAS_BOTTOM_WEAR_FLOOR = 3.0

NIGHT_FACADE_PEAKS = {
    "Industrial": 0.3056,
    "Supermarket": 0.3108,
    "OldTown": 0.3420,
    "Nightlife": 0.3661,
    "Residential": 0.4055,
    "PlayerHome": 0.5120,
    "Bar": 0.6160,
}

BASE = 170


@dataclass(frozen=True)
class SurfaceSpec:
    district: str
    surface: str
    layout: str
    material: str
    grammar: str
    cast: tuple[float, float, float]
    smoothness: float
    metallic: float
    tint: tuple[float, float, float]
    meters_per_tile: float | None = None
    contrast_floor: int = 24
    runtime_max_size: int = 1024

    @property
    def key(self) -> str:
        return f"{self.district}/{self.surface}"

    @property
    def seed(self) -> int:
        digest = hashlib.sha256(
            f"BarPromenade/CityBuildingSurface/{self.key}".encode("utf-8")
        ).digest()
        return int.from_bytes(digest[:8], "big")


def atlas(
    district: str,
    surface: str,
    material: str,
    grammar: str,
    cast: tuple[float, float, float],
    smoothness: float,
    metallic: float,
    tint: tuple[float, float, float],
) -> SurfaceSpec:
    return SurfaceSpec(
        district,
        surface,
        "building-side-atlas",
        material,
        grammar,
        cast,
        smoothness,
        metallic,
        tint,
    )


def tile(
    district: str,
    surface: str,
    material: str,
    grammar: str,
    cast: tuple[float, float, float],
    smoothness: float,
    metallic: float,
    tint: tuple[float, float, float],
    meters_per_tile: float,
) -> SurfaceSpec:
    return SurfaceSpec(
        district,
        surface,
        "meter-tile",
        material,
        grammar,
        cast,
        smoothness,
        metallic,
        tint,
        meters_per_tile,
        runtime_max_size=512,
    )


def full_face(
    district: str,
    surface: str,
    material: str,
    grammar: str,
    cast: tuple[float, float, float],
    smoothness: float,
    metallic: float,
    tint: tuple[float, float, float],
) -> SurfaceSpec:
    return SurfaceSpec(
        district,
        surface,
        "full-face",
        material,
        grammar,
        cast,
        smoothness,
        metallic,
        tint,
    )


SURFACE_SPECS: tuple[SurfaceSpec, ...] = (
    atlas("OldTown", "FacadePrimary", "dark aged brick", "old_brick",
          (1.05, 0.99, 0.92), 0.06, 0.00, (0.30, 0.19, 0.14)),
    atlas("OldTown", "FacadeSecondary", "faded lime plaster", "old_plaster",
          (1.03, 1.00, 0.95), 0.07, 0.00, (0.58, 0.47, 0.34)),
    full_face("OldTown", "Plinth", "dirty pale stone", "stone_plinth",
              (1.03, 1.00, 0.94), 0.05, 0.00, (0.22, 0.18, 0.14)),
    tile("OldTown", "Roof", "dark weathered slate", "slate",
         (0.98, 1.00, 1.00), 0.05, 0.00, (0.095, 0.15, 0.15), 4.0),
    tile("OldTown", "Metal", "blackened painted metal", "blackened_metal",
         (1.00, 1.00, 0.98), 0.24, 0.58, (0.10, 0.11, 0.105), 1.6),
    tile("OldTown", "WindowFrame", "aged dark frame paint", "frame_paint",
         (1.03, 1.00, 0.95), 0.16, 0.18, (0.42, 0.36, 0.27), 0.8),

    atlas("Residential", "FacadePrimary", "cold painted concrete panel",
          "residential_panel", (0.96, 1.00, 1.04), 0.09, 0.00,
          (0.20, 0.31, 0.32)),
    atlas("Residential", "FacadeSecondary", "faded repaired panel",
          "residential_repair", (1.02, 1.00, 0.96), 0.09, 0.00,
          (0.46, 0.54, 0.50)),
    full_face("Residential", "Plinth", "damp painted concrete",
              "concrete_plinth", (0.98, 1.00, 1.01), 0.06, 0.00,
              (0.17, 0.23, 0.23)),
    tile("Residential", "Roof", "patched roofing felt", "roof_felt",
         (0.98, 1.00, 1.02), 0.05, 0.00, (0.09, 0.13, 0.15), 4.0),
    tile("Residential", "Metal", "aged galvanized metal", "galvanized",
         (0.98, 1.00, 1.02), 0.26, 0.62, (0.12, 0.16, 0.17), 1.8),
    tile("Residential", "WindowFrame", "weathered frame paint", "frame_paint",
         (1.02, 1.00, 0.96), 0.17, 0.14, (0.62, 0.61, 0.50), 0.8),

    atlas("Industrial", "FacadePrimary", "painted profiled sheet",
          "industrial_sheet", (0.98, 1.00, 1.00), 0.16, 0.20,
          (0.25, 0.27, 0.25)),
    atlas("Industrial", "FacadeSecondary", "smoked utility brick",
          "utility_brick", (1.04, 0.99, 0.93), 0.10, 0.02,
          (0.49, 0.38, 0.16)),
    full_face("Industrial", "Plinth", "oil-marked structural concrete",
              "industrial_plinth", (0.99, 1.00, 0.99), 0.06, 0.00,
              (0.18, 0.20, 0.19)),
    tile("Industrial", "Roof", "sooted roofing sheet", "industrial_roof",
         (0.98, 1.00, 1.01), 0.08, 0.20, (0.12, 0.14, 0.14), 4.0),
    tile("Industrial", "Metal", "oxidized service steel", "service_steel",
         (1.03, 0.99, 0.94), 0.20, 0.68, (0.18, 0.21, 0.20), 1.8),
    tile("Industrial", "WindowFrame", "dirty painted steel frame",
         "frame_paint", (1.02, 1.00, 0.96), 0.14, 0.28,
         (0.42, 0.40, 0.29), 0.8),

    atlas("Nightlife", "FacadePrimary", "faded painted plaster",
          "nightlife_plaster", (1.02, 0.98, 1.03), 0.10, 0.00,
          (0.19, 0.13, 0.25)),
    atlas("Nightlife", "FacadeSecondary", "paint-worn old brick",
          "painted_brick", (0.97, 1.00, 1.02), 0.09, 0.00,
          (0.55, 0.12, 0.38)),
    full_face("Nightlife", "Plinth", "service-side dirty render",
              "nightlife_plinth", (0.99, 1.00, 1.01), 0.06, 0.00,
              (0.15, 0.11, 0.18)),
    tile("Nightlife", "Roof", "dark patched roofing felt", "roof_felt",
         (0.98, 0.99, 1.03), 0.05, 0.00, (0.08, 0.09, 0.13), 4.0),
    tile("Nightlife", "Metal", "dark painted mounting steel", "painted_metal",
         (0.99, 0.98, 1.03), 0.22, 0.58, (0.11, 0.11, 0.16), 1.6),
    tile("Nightlife", "WindowFrame", "aged coloured frame paint",
         "frame_paint", (1.01, 0.98, 1.03), 0.16, 0.20,
         (0.45, 0.22, 0.49), 0.8),
)


def panel_noise(width: int, height: int, rng: random.Random) -> Image.Image:
    small_width = 31
    small_height = 127
    small = Image.new("L", (small_width, small_height))
    small.putdata(
        [rng.randrange(116, 141) for _ in range(small_width * small_height)]
    )
    return small.resize((width, height), Image.Resampling.BICUBIC)


def draw_brick(
    image: Image.Image,
    utility: bool = False,
) -> None:
    draw = ImageDraw.Draw(image)
    row_pitch = 18 if utility else 14
    brick_pitch = 34 if utility else 28
    mortar = BASE - (29 if utility else 23)
    highlight = BASE + (10 if utility else 14)
    for y in range(0, image.height, row_pitch):
        draw.line((0, y, image.width, y), fill=mortar, width=2)
        if y + 2 < image.height:
            draw.line((0, y + 2, image.width, y + 2), fill=highlight, width=1)
        offset = brick_pitch // 2 if (y // row_pitch) % 2 else 0
        for x in range(offset, image.width, brick_pitch):
            draw.line((x, y, x, min(image.height, y + row_pitch)),
                      fill=mortar, width=1)


def draw_panel_joints(image: Image.Image, rng: random.Random) -> None:
    draw = ImageDraw.Draw(image)
    floors = 10
    for floor in range(1, floors):
        y = round(image.height * floor / floors)
        draw.line((0, y - 1, image.width, y - 1), fill=BASE + 14, width=1)
        draw.line((0, y, image.width, y + 1), fill=BASE - 27, width=2)
    for x in range(0, image.width, 62):
        draw.line((x, 0, x, image.height), fill=BASE - 15, width=1)
    for _ in range(28):
        x = rng.randrange(image.width)
        y = rng.randrange(40, image.height - 40)
        draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill=BASE - 35)


def draw_corrugated(image: Image.Image, industrial: bool = False) -> None:
    draw = ImageDraw.Draw(image)
    pitch = 8
    for x in range(0, image.width, pitch):
        draw.line((x, 0, x, image.height), fill=BASE - 20, width=2)
        draw.line((x + 2, 0, x + 2, image.height), fill=BASE + 18, width=1)
    if industrial:
        for y in range(128, image.height, 128):
            draw.line((0, y, image.width, y), fill=BASE - 31, width=3)
            draw.line((0, y - 2, image.width, y - 2), fill=BASE + 11, width=1)


def draw_plaster(image: Image.Image, rng: random.Random) -> None:
    draw = ImageDraw.Draw(image)
    for _ in range(22):
        x = rng.randrange(image.width)
        y = rng.randrange(image.height)
        length = rng.randint(18, 75)
        points = [(x, y)]
        for step in range(1, 4):
            points.append((
                x + rng.randint(-8, 8),
                min(image.height - 1, y + length * step // 3),
            ))
        draw.line(points, fill=BASE - rng.randint(19, 28), width=1)


def draw_stone_blocks(image: Image.Image) -> None:
    draw = ImageDraw.Draw(image)
    row_pitch = 58
    for y in range(0, image.height, row_pitch):
        draw.line((0, y, image.width, y), fill=BASE - 24, width=3)
        offset = 44 if (y // row_pitch) % 2 else 0
        for x in range(offset, image.width, 88):
            draw.line((x, y, x, min(image.height, y + row_pitch)),
                      fill=BASE - 18, width=2)


def add_material_grammar(
    panel: Image.Image,
    spec: SurfaceSpec,
    rng: random.Random,
) -> None:
    grammar = spec.grammar
    if grammar in ("old_brick", "painted_brick"):
        draw_brick(panel)
    elif grammar == "utility_brick":
        draw_brick(panel, utility=True)
    elif grammar in ("residential_panel", "residential_repair"):
        draw_panel_joints(panel, rng)
    elif grammar == "industrial_sheet":
        draw_corrugated(panel, industrial=True)
    elif grammar == "stone_plinth":
        draw_stone_blocks(panel)
    elif grammar in (
        "old_plaster",
        "nightlife_plaster",
        "concrete_plinth",
        "industrial_plinth",
        "nightlife_plinth",
    ):
        draw_plaster(panel, rng)
    else:
        raise ValueError(f"Unknown atlas grammar '{grammar}'.")


def add_macro_wear(
    panel: Image.Image,
    spec: SurfaceSpec,
    side_index: int,
    rng: random.Random,
) -> Image.Image:
    strength = (0.88, 1.28, 1.03, 1.13)[side_index]
    if spec.district == "Nightlife" and side_index == 1:
        strength *= 1.14

    wear = Image.new("L", panel.size, 128)
    pixels: list[int] = []
    threshold = 0.77 - 0.035 * side_index
    for y in range(panel.height):
        down = y / (panel.height - 1)
        amount = max(0.0, (down - threshold) / (1.0 - threshold))
        value = round(128 - 46 * strength * amount * amount)
        pixels.extend([value] * panel.width)
    wear.putdata(pixels)

    draw = ImageDraw.Draw(wear)
    drip_count = 10 + side_index * 2
    for _ in range(drip_count):
        x = rng.randrange(panel.width)
        y0 = rng.randrange(90, panel.height - 150)
        length = rng.randrange(45, 230)
        tone = round(128 - rng.randrange(10, 25) * strength)
        draw.line((x, y0, x + rng.randint(-5, 5), y0 + length),
                  fill=tone, width=rng.randint(2, 5))

    for _ in range(7 + side_index):
        x = rng.randrange(panel.width)
        y = rng.randrange(40, panel.height - 80)
        width = rng.randrange(22, 86)
        height = rng.randrange(18, 100)
        tone = round(128 + rng.choice((-1, 1)) * rng.randrange(7, 17))
        draw.ellipse((x - width, y - height, x + width, y + height), fill=tone)

    if spec.grammar in (
        "old_plaster",
        "residential_repair",
        "industrial_sheet",
        "nightlife_plaster",
        "painted_brick",
    ):
        patch_side = spec.seed % SIDE_COLUMNS
        if side_index == patch_side:
            x = rng.randrange(18, max(19, panel.width - 90))
            y = rng.randrange(170, 650)
            width = rng.randrange(48, 92)
            height = rng.randrange(55, 150)
            draw.rectangle((x, y, x + width, y + height),
                           fill=128 + rng.randrange(8, 18))
            draw.line((x, y + height, x + width, y + height),
                      fill=128 - 13, width=3)

    wear = wear.filter(ImageFilter.GaussianBlur(7.0))
    return ImageChops.overlay(panel, wear)


def build_atlas_structure(spec: SurfaceSpec) -> Image.Image:
    atlas_image = Image.new("L", (SHEET_SIZE, SHEET_SIZE), BASE)
    for side_index, _ in enumerate(SIDE_ORDER):
        side_rng = random.Random(spec.seed ^ (0x9E3779B97F4A7C15 * (side_index + 1)))
        panel = Image.new("L", (COLUMN_CONTENT_WIDTH, SHEET_SIZE), BASE)
        material_noise = panel_noise(panel.width, panel.height, side_rng)
        panel = ImageChops.overlay(panel, material_noise)
        add_material_grammar(panel, spec, side_rng)
        panel = add_macro_wear(panel, spec, side_index, side_rng)

        x = side_index * COLUMN_WIDTH
        atlas_image.paste(panel, (x + COLUMN_GUTTER, 0))
        left = panel.crop((0, 0, 1, SHEET_SIZE)).resize(
            (COLUMN_GUTTER, SHEET_SIZE)
        )
        right = panel.crop((panel.width - 1, 0, panel.width, SHEET_SIZE)).resize(
            (COLUMN_GUTTER, SHEET_SIZE)
        )
        atlas_image.paste(left, (x, 0))
        atlas_image.paste(right, (x + COLUMN_WIDTH - COLUMN_GUTTER, 0))
    return atlas_image


def build_full_face_structure(spec: SurfaceSpec) -> Image.Image:
    rng = random.Random(spec.seed ^ 0x46554C4C46414345)
    panel = Image.new("L", (SHEET_SIZE, SHEET_SIZE), BASE)
    material_noise = panel_noise(panel.width, panel.height, rng)
    panel = ImageChops.overlay(panel, material_noise)
    add_material_grammar(panel, spec, rng)
    return add_macro_wear(panel, spec, 0, rng)


def tile_roof_felt(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    for y in range(0, SHEET_SIZE, 128):
        home.wrap_line(draw, (0, y, SHEET_SIZE, y), BASE - 25, width=4)
        home.wrap_line(draw, (0, y + 4, SHEET_SIZE, y + 4), BASE + 10, width=1)
    for _ in range(520):
        x, y = rng.randrange(SHEET_SIZE), rng.randrange(SHEET_SIZE)
        radius = rng.choice((1, 1, 2, 3))
        home.wrap_ellipse(draw, (x - radius, y - radius, x + radius, y + radius),
                          fill=BASE + rng.randint(-28, 22))
    return base


def tile_slate(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    row_pitch = 64
    tile_pitch = 96
    for y in range(0, SHEET_SIZE, row_pitch):
        home.wrap_line(draw, (0, y, SHEET_SIZE, y), BASE - 30, width=3)
        offset = tile_pitch // 2 if (y // row_pitch) % 2 else 0
        for x in range(offset, SHEET_SIZE, tile_pitch):
            home.wrap_line(draw, (x, y, x, y + row_pitch), BASE - 17, width=2)
    return base


def tile_aggregate(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    for _ in range(760):
        x, y = rng.randrange(SHEET_SIZE), rng.randrange(SHEET_SIZE)
        radius = rng.randrange(1, 6)
        home.wrap_ellipse(draw, (x - radius, y - radius, x + radius, y + radius),
                          fill=BASE + rng.randint(-38, 30))
    for _ in range(18):
        x, y = rng.randrange(SHEET_SIZE), rng.randrange(SHEET_SIZE)
        home.wrap_line(draw, (x, y, x + rng.randint(-45, 45),
                              y + rng.randint(25, 85)), BASE - 29, width=2)
    return base


def tile_metal(
    base: Image.Image,
    rng: random.Random,
    oxidation: float,
    ridge_pitch: int,
) -> Image.Image:
    draw = ImageDraw.Draw(base)
    for x in range(4, SHEET_SIZE, ridge_pitch):
        home.wrap_line(draw, (x, 0, x, SHEET_SIZE), BASE - 18, width=2)
        home.wrap_line(draw, (x + 3, 0, x + 3, SHEET_SIZE), BASE + 14, width=1)
    spots = round(80 + oxidation * 190)
    for _ in range(spots):
        x, y = rng.randrange(SHEET_SIZE), rng.randrange(SHEET_SIZE)
        radius = rng.randrange(3, round(9 + oxidation * 16))
        tone = BASE - rng.randrange(14, round(24 + oxidation * 24))
        home.wrap_ellipse(draw, (x - radius, y - radius, x + radius, y + radius),
                          fill=tone)
    return base


def tile_frame_paint(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    for y in range(4, SHEET_SIZE, 16):
        tone = BASE + rng.randint(-12, 12)
        home.wrap_line(draw, (0, y, SHEET_SIZE, y), tone, width=1)
    for _ in range(240):
        x, y = rng.randrange(SHEET_SIZE), rng.randrange(SHEET_SIZE)
        length = rng.randrange(4, 28)
        home.wrap_line(draw, (x, y, x + length, y), BASE - rng.randrange(12, 34),
                       width=1)
    return base


def build_tile_structure(spec: SurfaceSpec, rng: random.Random) -> Image.Image:
    base = Image.new("L", (SHEET_SIZE, SHEET_SIZE), BASE)
    if spec.grammar == "roof_felt":
        return tile_roof_felt(base, rng)
    if spec.grammar == "slate":
        return tile_slate(base, rng)
    if spec.grammar == "aggregate_concrete":
        return tile_aggregate(base, rng)
    if spec.grammar == "blackened_metal":
        return tile_metal(base, rng, 0.28, 64)
    if spec.grammar == "galvanized":
        return tile_metal(base, rng, 0.12, 32)
    if spec.grammar == "service_steel":
        return tile_metal(base, rng, 0.54, 64)
    if spec.grammar == "painted_metal":
        return tile_metal(base, rng, 0.20, 64)
    if spec.grammar == "industrial_roof":
        return tile_metal(base, rng, 0.38, 32)
    if spec.grammar == "frame_paint":
        return tile_frame_paint(base, rng)
    raise ValueError(f"Unknown tile grammar '{spec.grammar}'.")


def tint_image(field: Image.Image, spec: SurfaceSpec) -> tuple[Image.Image, float]:
    tables = home.cast_tables(spec.cast)
    image = Image.merge(
        "RGB",
        (
            field.point(tables[0]),
            field.point(tables[1]),
            field.point(tables[2]),
        ),
    )
    return home.normalise_luminance(image, MEAN_LUMINANCE_TARGET)


def build_sheet(spec: SurfaceSpec) -> tuple[Image.Image, dict]:
    rng = random.Random(spec.seed)
    if spec.layout == "building-side-atlas":
        structure = build_atlas_structure(spec)
    elif spec.layout == "full-face":
        structure = build_full_face_structure(spec)
    else:
        structure = build_tile_structure(spec, rng)
        macro = home.fractal_noise(
            ((8, 1.0), (32, 0.62), (128, 0.38), (256, 0.22), (512, 0.12)),
            rng,
        )
        structure = home.wrap_filter(
            ImageChops.overlay(structure, macro),
            ImageFilter.SMOOTH,
        )

    image, exposure = tint_image(structure, spec)
    asset_path = (
        f"Assets/Resources/Textures/CityBuildingSurfaces/"
        f"{spec.district}/{spec.surface}.png"
    )
    record = {
        "key": spec.key,
        "district": spec.district,
        "surface": spec.surface,
        "material": spec.material,
        "assetPath": asset_path,
        "resourcePath": (
            f"Textures/CityBuildingSurfaces/{spec.district}/{spec.surface}"
        ),
        "unityGuid": stable_guid(asset_path),
        "grammar": spec.grammar,
        "seed": f"0x{spec.seed:016X}",
        "sourceSize": [SHEET_SIZE, SHEET_SIZE],
        "runtimeMaxSize": spec.runtime_max_size,
        "layout": layout_record(spec),
        "metersPerTile": spec.meters_per_tile,
        "meanLinearLuminance": round(home.mean_linear_luminance(image), 6),
        "albedoCompensation": round(ALBEDO_COMPENSATION, 6),
        "smoothness": spec.smoothness,
        "metallic": spec.metallic,
        "exposure": round(exposure, 6),
        "representativeTint": [round(value, 4) for value in spec.tint],
        "containsPaintedWindows": False,
        "containsText": False,
    }
    return image, record


def layout_record(spec: SurfaceSpec) -> dict:
    if spec.layout == "building-side-atlas":
        return {
            "kind": spec.layout,
            "wrapMode": "Clamp",
            "sideOrder": list(SIDE_ORDER),
            "columns": SIDE_COLUMNS,
            "columnPixels": COLUMN_WIDTH,
            "gutterPixels": COLUMN_GUTTER,
            "contentPixels": COLUMN_CONTENT_WIDTH,
            "uContract": "one side across one column content range",
            "vContract": "0=role bottom, 1=role top; no vertical repetition",
        }
    if spec.layout == "full-face":
        return {
            "kind": spec.layout,
            "wrapMode": "Clamp",
            "uContract": "0..1 across each plinth face; no repetition",
            "vContract": "0..1 bottom-to-top per plinth face; no repetition",
        }
    return {
        "kind": spec.layout,
        "wrapMode": "Repeat",
        "metersPerTile": spec.meters_per_tile,
    }


def validate_common(image: Image.Image, spec: SurfaceSpec, record: dict) -> None:
    if image.size != (SHEET_SIZE, SHEET_SIZE):
        raise ValueError(f"{spec.key} is {image.size}, expected 1024 square.")
    if image.mode != "RGB":
        raise ValueError(f"{spec.key} must be opaque RGB, got {image.mode}.")

    low, high = home.luminance_percentiles(image, 0.05, 0.95)
    if high - low < spec.contrast_floor:
        raise ValueError(
            f"{spec.key} contrast {high - low} is below {spec.contrast_floor}."
        )
    mean = record["meanLinearLuminance"]
    if abs(mean - MEAN_LUMINANCE_TARGET) > MEAN_LUMINANCE_TOLERANCE:
        raise ValueError(f"{spec.key} mean luminance {mean:.4f} drifted.")

    if MAXIMUM_NIGHT_FACADE_CHANNEL * ALBEDO_COMPENSATION > 1.0:
        raise ValueError("The albedo compensation would clamp a facade tint.")
    worst = max(
        abs(
            home.srgb_to_linear(min(1.0, peak * ALBEDO_COMPENSATION)) * mean
            / home.srgb_to_linear(peak)
            - 1.0
        )
        for peak in NIGHT_FACADE_PEAKS.values()
    )
    if worst > BRIGHTNESS_ERROR_LIMIT:
        raise ValueError(
            f"{spec.key} shifts facade brightness by {worst * 100:.1f}%."
        )

    means = home.channel_means(image)
    ratio = max(means) / max(1e-6, min(means))
    if ratio > CHANNEL_RATIO_LIMIT:
        raise ValueError(f"{spec.key} channel ratio {ratio:.3f} is too chromatic.")

    record["contrast"] = high - low
    record["brightnessError"] = round(worst, 6)
    record["channelRatio"] = round(ratio, 4)


def validate_tile(image: Image.Image, spec: SurfaceSpec, record: dict) -> None:
    edge_delta = home.mean_line_delta(image, 0, SHEET_SIZE - 1)
    interior = sorted(
        home.mean_line_delta(image, offset, offset + 1)
        for offset in range(0, SHEET_SIZE - 1, 7)
    )
    interior_delta = interior[int(len(interior) * 0.9)]
    seam_ratio = edge_delta / max(1e-6, interior_delta)
    if edge_delta > EDGE_DELTA_LIMIT or seam_ratio > SEAM_RATIO_LIMIT:
        raise ValueError(
            f"{spec.key} is not seamless: edge={edge_delta:.2f}, "
            f"ratio={seam_ratio:.2f}."
        )
    record["edgeDelta"] = round(edge_delta, 4)
    record["seamRatio"] = round(seam_ratio, 4)


def validate_atlas(image: Image.Image, spec: SurfaceSpec, record: dict) -> None:
    grey = image.convert("L")
    signatures: list[Image.Image] = []
    bottom_deltas: list[float] = []
    for side_index in range(SIDE_COLUMNS):
        x0 = side_index * COLUMN_WIDTH + COLUMN_GUTTER
        x1 = (side_index + 1) * COLUMN_WIDTH - COLUMN_GUTTER
        panel = grey.crop((x0, 0, x1, SHEET_SIZE))
        signature = panel.filter(ImageFilter.GaussianBlur(24)).resize(
            (16, 32), Image.Resampling.BOX
        )
        signatures.append(signature)
        middle = ImageStat.Stat(panel.crop((0, 280, panel.width, 610))).mean[0]
        bottom = ImageStat.Stat(panel.crop((0, 880, panel.width, 1016))).mean[0]
        bottom_deltas.append(middle - bottom)

    separations = []
    for first in range(SIDE_COLUMNS):
        for second in range(first + 1, SIDE_COLUMNS):
            difference = ImageChops.difference(
                signatures[first], signatures[second]
            )
            separations.append(ImageStat.Stat(difference).mean[0])
    minimum_separation = min(separations)
    minimum_bottom_wear = min(bottom_deltas)
    if minimum_separation < ATLAS_COLUMN_SEPARATION_FLOOR:
        raise ValueError(
            f"{spec.key} side macro histories are too similar "
            f"({minimum_separation:.2f})."
        )
    if minimum_bottom_wear < ATLAS_BOTTOM_WEAR_FLOOR:
        raise ValueError(
            f"{spec.key} lost grounded wear ({minimum_bottom_wear:.2f})."
        )
    record["minimumSideMacroSeparation"] = round(minimum_separation, 4)
    record["minimumBottomWearDelta"] = round(minimum_bottom_wear, 4)


def validate_full_face(
    image: Image.Image,
    spec: SurfaceSpec,
    record: dict,
) -> None:
    grey = image.convert("L")
    middle = ImageStat.Stat(grey.crop((0, 280, SHEET_SIZE, 610))).mean[0]
    bottom = ImageStat.Stat(grey.crop((0, 880, SHEET_SIZE, 1016))).mean[0]
    bottom_wear = middle - bottom
    edge_delta = home.mean_line_delta(image, 0, SHEET_SIZE - 1)
    if bottom_wear < ATLAS_BOTTOM_WEAR_FLOOR:
        raise ValueError(f"{spec.key} lost grounded wear ({bottom_wear:.2f}).")
    record["bottomWearDelta"] = round(bottom_wear, 4)
    record["nonRepeatingEdgeDelta"] = round(edge_delta, 4)


def validate(image: Image.Image, spec: SurfaceSpec, record: dict) -> None:
    validate_common(image, spec, record)
    if spec.layout == "meter-tile":
        validate_tile(image, spec, record)
    elif spec.layout == "full-face":
        validate_full_face(image, spec, record)
    else:
        validate_atlas(image, spec, record)


def stable_guid(asset_path: str) -> str:
    return hashlib.sha256(
        f"BarPromenade/UnityGuid/{asset_path}".encode("utf-8")
    ).hexdigest()[:32]


def folder_meta(asset_path: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(asset_path)}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    )


def texture_meta(asset_path: str, spec: SurfaceSpec) -> str:
    wrap = 0 if spec.layout == "meter-tile" else 1
    size = spec.runtime_max_size
    return f"""fileFormatVersion: 2
guid: {stable_guid(asset_path)}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {size}
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 4
    mipBias: 0
    wrapU: {wrap}
    wrapV: {wrap}
    wrapW: {wrap}
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 100
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Standalone
    maxTextureSize: {size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write_owned_meta(path: Path, contents: str) -> None:
    expected_guid = re.search(r"^guid: ([0-9a-f]{32})$", contents, re.MULTILINE)
    if expected_guid is None:
        raise ValueError(f"Generated meta for {path} has no valid GUID.")
    if path.exists():
        current = path.read_text(encoding="utf-8")
        current_guid = re.search(r"^guid: ([0-9a-f]{32})$", current, re.MULTILINE)
        if current_guid is None or current_guid.group(1) != expected_guid.group(1):
            raise ValueError(
                f"Refusing to replace foreign Unity GUID in '{path}'."
            )
    path.write_text(contents, encoding="utf-8", newline="\n")


def write_unity_metadata(texture_root: Path) -> None:
    asset_root = "Assets/Resources/Textures/CityBuildingSurfaces"
    texture_root.mkdir(parents=True, exist_ok=True)
    write_owned_meta(
        texture_root.with_suffix(".meta"),
        folder_meta(asset_root),
    )
    for district in sorted({spec.district for spec in SURFACE_SPECS}):
        directory = texture_root / district
        directory.mkdir(parents=True, exist_ok=True)
        write_owned_meta(
            directory.with_suffix(".meta"),
            folder_meta(f"{asset_root}/{district}"),
        )
    for spec in SURFACE_SPECS:
        asset_path = f"{asset_root}/{spec.district}/{spec.surface}.png"
        write_owned_meta(
            texture_root / spec.district / f"{spec.surface}.png.meta",
            texture_meta(asset_path, spec),
        )


def build_contact_sheet(
    sheets: list[tuple[SurfaceSpec, Image.Image]],
) -> Image.Image:
    preview = 180
    label_height = 40
    columns = 5
    rows = (len(sheets) + columns - 1) // columns
    contact = Image.new(
        "RGB",
        (columns * (preview + 12) + 12, rows * (preview + label_height + 12) + 12),
        (18, 18, 20),
    )
    draw = ImageDraw.Draw(contact)
    for index, (spec, image) in enumerate(sheets):
        column = index % columns
        row = index // columns
        x = 12 + column * (preview + 12)
        y = 12 + row * (preview + label_height + 12)
        if spec.layout == "meter-tile":
            small = image.resize((preview // 2, preview // 2), Image.Resampling.BOX)
            block = Image.new("RGB", (preview, preview))
            for ty in range(2):
                for tx in range(2):
                    block.paste(small, (tx * preview // 2, ty * preview // 2))
        else:
            block = image.resize((preview, preview), Image.Resampling.BOX)
        contact.paste(block, (x, y))
        draw.text((x, y + preview + 5), spec.key, fill=(210, 210, 204))
        if spec.layout == "building-side-atlas":
            kind = "Front | Rear | Left | Right"
        elif spec.layout == "full-face":
            kind = "full face 0..1"
        else:
            kind = f"tile {spec.meters_per_tile:g}m"
        draw.text((x, y + preview + 20), kind, fill=(142, 146, 148))
    return contact


def save_png(image: Image.Image, path: Path) -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=False, compress_level=9)
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def validate_contract() -> None:
    keys = [spec.key for spec in SURFACE_SPECS]
    if len(keys) != len(set(keys)):
        raise ValueError("Duplicate district/surface output key.")
    expected = {
        (district, surface)
        for district in ("OldTown", "Residential", "Industrial", "Nightlife")
        for surface in (
            "FacadePrimary", "FacadeSecondary", "Plinth", "Roof", "Metal",
            "WindowFrame",
        )
    }
    actual = {(spec.district, spec.surface) for spec in SURFACE_SPECS}
    if not expected.issubset(actual):
        raise ValueError(f"Missing required outputs: {sorted(expected - actual)}")
    if len(actual) != 24 or actual != expected:
        raise ValueError("The runtime contract requires exactly 24 opaque sheets.")
    guids = [
        stable_guid(
            "Assets/Resources/Textures/CityBuildingSurfaces/"
            f"{spec.district}/{spec.surface}.png"
        )
        for spec in SURFACE_SPECS
    ]
    if len(guids) != len(set(guids)):
        raise ValueError("Generated Unity GUID collision.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--textures",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
        help="Destination root for district/surface PNGs and Unity metadata.",
    )
    parser.add_argument(
        "--art-source",
        type=Path,
        default=DEFAULT_ART_SOURCE,
        help="Destination for the measured manifest and contact sheet.",
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Build and validate in memory without writing anything.",
    )
    args = parser.parse_args()

    validate_contract()
    records: list[dict] = []
    built: list[tuple[SurfaceSpec, Image.Image]] = []
    for spec in SURFACE_SPECS:
        image, record = build_sheet(spec)
        validate(image, spec, record)
        built.append((spec, image))
        if args.verify:
            record["sha256"] = hashlib.sha256(image.tobytes()).hexdigest().upper()
        else:
            record["sha256"] = save_png(
                image,
                args.textures / spec.district / f"{spec.surface}.png",
            )
        records.append(record)
        if spec.layout == "meter-tile":
            measure = (
                f"edge={record['edgeDelta']:.2f} "
                f"seam={record['seamRatio']:.2f}x"
            )
        elif spec.layout == "full-face":
            measure = (
                f"edge={record['nonRepeatingEdgeDelta']:.2f} "
                f"ground={record['bottomWearDelta']:.2f}"
            )
        else:
            measure = (
                f"side={record['minimumSideMacroSeparation']:.2f} "
                f"ground={record['minimumBottomWearDelta']:.2f}"
            )
        print(
            f"{'Checked' if args.verify else 'Wrote'} {spec.key} "
            f"mean={record['meanLinearLuminance']:.4f} "
            f"contrast={record['contrast']} {measure}"
        )

    if args.verify:
        print(f"Validated {len(records)} sheets; nothing written.")
        return

    write_unity_metadata(args.textures)
    manifest = {
        "schemaVersion": 1,
        "generator": "tools/build-city-building-surface-textures.py",
        "sheetSize": [SHEET_SIZE, SHEET_SIZE],
        "meanLuminanceTarget": MEAN_LUMINANCE_TARGET,
        "meanLuminanceTolerance": MEAN_LUMINANCE_TOLERANCE,
        "maximumNightFacadeChannel": MAXIMUM_NIGHT_FACADE_CHANNEL,
        "albedoCompensation": round(ALBEDO_COMPENSATION, 6),
        "atlasContract": {
            "surfaces": ["FacadePrimary", "FacadeSecondary"],
            "sideOrder": list(SIDE_ORDER),
            "columns": SIDE_COLUMNS,
            "columnPixels": COLUMN_WIDTH,
            "gutterPixels": COLUMN_GUTTER,
            "contentPixels": COLUMN_CONTENT_WIDTH,
            "vContract": "0=role bottom, 1=role top",
            "wrapMode": "Clamp",
        },
        "fullFaceContract": {
            "surfaces": ["Plinth"],
            "uContract": "0..1 across each plinth face",
            "vContract": "0..1 bottom-to-top per plinth face",
            "wrapMode": "Clamp",
        },
        "tileContract": {
            "surfaces": ["Roof", "Metal", "WindowFrame"],
            "wrapMode": "Repeat",
            "metersPerTileIsPhysical": True,
        },
        "prohibitedBakedContent": [
            "windows", "apertures", "signs", "text", "lore",
        ],
        "sheets": records,
    }
    args.art_source.mkdir(parents=True, exist_ok=True)
    manifest_path = args.art_source / "city-building-surface-textures.json"
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    contact_path = args.art_source / "city-building-surface-contact-sheet.png"
    build_contact_sheet(built).save(
        contact_path,
        format="PNG",
        optimize=False,
        compress_level=9,
    )
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
