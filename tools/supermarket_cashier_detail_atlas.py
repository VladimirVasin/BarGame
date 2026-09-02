#!/usr/bin/env python3
"""Deterministic greyscale detail atlas for both supermarket cashiers."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
from pathlib import Path

import atlas_kit


DETAIL_ATLAS_NAME = "SupermarketCashier3DDetailAtlas.png"
DETAIL_ATLAS_SIZE = 256
DETAIL_ATLAS_UV_INSET_PX = 1
DETAIL_ATLAS_REGION_PROP = "bp_atlas_region"
DETAIL_ATLAS_RESERVED_CELL = (0, 0, 64, 64)
DETAIL_ATLAS_WHITE = (255, 255, 255, 255)
DETAIL_ATLAS_GREY_MIN = 140
DETAIL_ATLAS_GREY_MAX = 236


@dataclass(frozen=True)
class CashierAtlasRegion:
    """One bottom-left pixel sub-rect of the atlas owned by a renderer."""

    name: str
    renderer: str
    x: int
    y: int
    width: int
    height: int
    kind: str
    sides: int = 0
    rings: int = 0


CASHIER_ATLAS_REGIONS = (
    CashierAtlasRegion(
        "VestFront.L", "CLO_VestFront.L", 64, 0, 64, 64, "box"),
    CashierAtlasRegion(
        "VestFront.R", "CLO_VestFront.R", 128, 0, 64, 64, "box"),
    CashierAtlasRegion(
        "VestBack", "CLO_VestBack", 192, 0, 64, 64, "box"),
    CashierAtlasRegion(
        "ShirtBib", "CLO_ShirtBib", 0, 64, 64, 64, "box"),
    CashierAtlasRegion(
        "Collar", "CLO_TightCollar", 64, 64, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Sleeve.L", "GEO_Forearm.L", 128, 64, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Sleeve.R", "GEO_Forearm.R", 192, 64, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Trouser.L", "GEO_Thigh.L", 0, 128, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Trouser.R", "GEO_Thigh.R", 64, 128, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Sole.L", "CLO_ShoeSole.L", 128, 128, 64, 64, "box"),
    CashierAtlasRegion(
        "Sole.R", "CLO_ShoeSole.R", 192, 128, 64, 64, "box"),
)


def neutral_grey(value: int):
    if not DETAIL_ATLAS_GREY_MIN <= value <= DETAIL_ATLAS_GREY_MAX:
        raise ValueError(
            f"Atlas ink {value} is outside the "
            f"{DETAIL_ATLAS_GREY_MIN}-{DETAIL_ATLAS_GREY_MAX} band"
        )
    return (value, value, value, 255)


def paint_cashier_detail_atlas():
    """Paint the shared atlas as a stable pure function."""

    canvas = atlas_kit.PixelCanvas(DETAIL_ATLAS_SIZE, DETAIL_ATLAS_SIZE)
    canvas.rect(0, 0, canvas.width, canvas.height, DETAIL_ATLAS_WHITE)
    regions = {region.name: region for region in CASHIER_ATLAS_REGIONS}
    rect = atlas_kit.atlas_rect_bottom_left
    line = atlas_kit.atlas_line_bottom_left

    for name, edge_x in (("VestFront.L", 34), ("VestFront.R", 58)):
        region = regions[name]
        line(
            canvas,
            region.x + edge_x, region.y + 4,
            region.x + edge_x, region.y + 60,
            neutral_grey(168), 2,
        )
        rect(
            canvas,
            region.x + 38, region.y + 40,
            region.x + 54, region.y + 43,
            neutral_grey(180),
        )
        rect(
            canvas,
            region.x + 34, region.y + 16,
            region.x + 62, region.y + 19,
            neutral_grey(205),
        )

    region = regions["VestBack"]
    rect(
        canvas,
        region.x + 4, region.y + 46,
        region.x + 60, region.y + 49,
        neutral_grey(170),
    )
    line(
        canvas,
        region.x + 8, region.y + 30,
        region.x + 56, region.y + 26,
        neutral_grey(196), 2,
    )

    region = regions["ShirtBib"]
    line(
        canvas,
        region.x + 32, region.y + 4,
        region.x + 32, region.y + 60,
        neutral_grey(186), 2,
    )
    for crease_y in (18, 30, 42):
        rect(
            canvas,
            region.x + 36, region.y + crease_y,
            region.x + 58, region.y + crease_y + 2,
            neutral_grey(214),
        )

    region = regions["Collar"]
    for band_y, tone in ((20, 176), (40, 192)):
        rect(
            canvas,
            region.x + 2, region.y + band_y,
            region.x + 62, region.y + band_y + 3,
            neutral_grey(tone),
        )
    rect(
        canvas,
        region.x + 24, region.y + 22,
        region.x + 40, region.y + 41,
        neutral_grey(158),
    )

    for name in ("Sleeve.L", "Sleeve.R"):
        region = regions[name]
        rect(
            canvas,
            region.x + 2, region.y + 14,
            region.x + 62, region.y + 16,
            neutral_grey(190),
        )
        rect(
            canvas,
            region.x + 2, region.y + 48,
            region.x + 62, region.y + 51,
            neutral_grey(172),
        )
        line(
            canvas,
            region.x + 18, region.y + 18,
            region.x + 22, region.y + 46,
            neutral_grey(206), 2,
        )

    for name in ("Trouser.L", "Trouser.R"):
        region = regions[name]
        rect(
            canvas,
            region.x + 2, region.y + 12,
            region.x + 62, region.y + 15,
            neutral_grey(182),
        )
        line(
            canvas,
            region.x + 30, region.y + 16,
            region.x + 34, region.y + 58,
            neutral_grey(210), 2,
        )
        rect(
            canvas,
            region.x + 10, region.y + 20,
            region.x + 54, region.y + 24,
            neutral_grey(198),
        )

    for name in ("Sole.L", "Sole.R"):
        region = regions[name]
        for tread_x in range(region.x + 6, region.x + 58, 8):
            rect(
                canvas,
                tread_x, region.y + 10,
                tread_x + 4, region.y + 54,
                neutral_grey(164),
            )

    return canvas


class DetailAtlasReport:
    """The painted atlas as the manifest and validator see it."""

    def __init__(self, path, sha256, width, height):
        self.path = path
        self.sha256 = sha256
        self.width = width
        self.height = height


def texture_asset_path(path) -> str:
    """Return the atlas as a repo-relative forward-slash asset path."""

    resolved = Path(path).resolve()
    for parent in resolved.parents:
        if (parent / "Assets").is_dir() and (parent / "tools").is_dir():
            return resolved.relative_to(parent).as_posix()
    return resolved.as_posix()


def write_detail_atlas(canvas, path):
    """Write the atlas and prove the on-disk bytes match the painting."""

    payload = canvas.png_bytes()
    expected = hashlib.sha256(payload).hexdigest()
    path.parent.mkdir(parents=True, exist_ok=True)
    canvas.write_png(path)
    written = hashlib.sha256(path.read_bytes()).hexdigest()
    if written != expected:
        raise RuntimeError(
            f"Detail atlas {path} hashes {written} on disk but "
            f"{expected} in memory"
        )

    width, height, _ = atlas_kit.decode_generated_png(payload, str(path))
    if (width, height) != (DETAIL_ATLAS_SIZE, DETAIL_ATLAS_SIZE):
        raise RuntimeError(
            f"Detail atlas {path} is {width}x{height}, expected "
            f"{DETAIL_ATLAS_SIZE} square"
        )

    return DetailAtlasReport(path, expected, width, height)
