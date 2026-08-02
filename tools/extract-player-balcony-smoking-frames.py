#!/usr/bin/env python3
"""Extract four approved 4x4 smoking sheets into 64 aligned frames.

The input sheets must already have alpha (the repository's ``Keyed`` copies
are produced with the imagegen skill's official ``remove_chroma_key.py``
helper). Raw cells use top-left row-major order. Logical frames 24..31 have a
locked reuse map into the coherent side-profile family from the third and
fourth sheets; this removes both loop seams without regenerating art. A shared
base scale plus one locked family-size correction registers each pose by its
planted-foot center and baseline so the hip stays at the runtime pivot instead
of wandering with arm or smoke bounds. The generated three-quarter figure is
compressed horizontally to the proportions of the ordinary side-profile rig.

Frames 000 and 063 are exact copies of the ordinary ``Right`` idle composite.
The adjacent enter/exit frames use a deterministic ordered-dither blend so the
game never cuts directly between two unrelated silhouettes. The contact sheets
already face texture-left, which is the user-approved balcony direction. The
smoking definition therefore applies no runtime mirror.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import os
from pathlib import Path
import sys
import tempfile
from typing import Sequence

try:
    from PIL import Image
except ImportError as exc:  # pragma: no cover - runs before module load.
    raise SystemExit(
        "error: Pillow is required; install it with "
        "'python -m pip install Pillow'."
    ) from exc


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SHEET_DIR = (
    ROOT / "ArtSource" / "Player" / "BalconySmoking" / "Keyed"
)
DEFAULT_OUTPUT_DIR = (
    ROOT / "ArtSource" / "Player" / "BalconySmoking"
)
DEFAULT_IDLE_ATLAS = (
    ROOT / "Assets" / "Resources" / "Player" /
    "PlayerDirectionalAtlas.png"
)

SHEET_NAMES = (
    "enter-000-015.png",
    "enter-loop-016-031.png",
    "loop-032-047.png",
    "exit-048-063.png",
)
SHEET_SIZE_MULTIPLIERS = {
    SHEET_NAMES[0]: 1.0,
    SHEET_NAMES[1]: 1.0,
    # The third and fourth generated sheets contain the same character at
    # about 93% of the first two sheets' height. This measured correction
    # brings the side-profile loop/exit family back to the common 87px body
    # height while retaining one shared base scale and foot anchor.
    SHEET_NAMES[2]: 1.075,
    SHEET_NAMES[3]: 1.075,
}

# Raw sheet cells are zero-based and row-major. The first loop block originally
# came from sheet two's larger three-quarter family, while frames 32..47 came
# from sheet three's smaller strict side profile. These explicit substitutions
# make 47 -> 24 pixel-identical and make 31 -> 32 a near-identical mouth pose.
# All rest sources below retain the low cigarette and contain no exhaled smoke.
LOGICAL_SOURCE_OVERRIDES = {
    24: (SHEET_NAMES[2], 15),  # raw 047: clean low-cigarette side rest
    25: (SHEET_NAMES[3], 0),   # raw 048: adjacent clean side rest
    26: (SHEET_NAMES[2], 6),   # raw 038: clean low-cigarette side rest
    27: (SHEET_NAMES[2], 15),  # raw 047: long rest and exact loop bridge
    28: (SHEET_NAMES[2], 6),   # reverse the lowering arc into a lift
    29: (SHEET_NAMES[2], 5),   # raw 037
    30: (SHEET_NAMES[2], 4),   # raw 036
    31: (SHEET_NAMES[2], 3),   # raw 035: mouth pose nearest raw 032
}
GRID_COLUMNS = 4
GRID_ROWS = 4
FRAMES_PER_SHEET = GRID_COLUMNS * GRID_ROWS
FRAME_COUNT = len(SHEET_NAMES) * FRAMES_PER_SHEET

CANVAS_WIDTH = 128
CANVAS_HEIGHT = 96
CANVAS_SIZE = (CANVAS_WIDTH, CANVAS_HEIGHT)
HIP_PNG = (64, 56)
FOOT_BASELINE_PNG_Y = 92
TARGET_CHARACTER_HEIGHT = 84
HORIZONTAL_CHARACTER_SCALE = 0.62
ANCHOR_ALPHA_THRESHOLD = 128
MIN_SOURCE_COMPONENT_PIXELS = 1500
MIN_OUTPUT_OPAQUE_PIXELS = 256
TRANSPARENT = (0, 0, 0, 0)

# The ordinary rig selects PlayerViewDirection.Right for this dock and camera.
# In the source atlas that is direction cell 2 and it visibly faces texture-
# left. User playtesting established that this unmirrored screen direction is
# the city-facing one. Keep all three toggles explicit so a later accidental
# mirror fails validation instead of silently reversing the character again.
IDLE_DIRECTION_INDEX = 2
IDLE_FRAME_WIDTH = 64
IDLE_FRAME_HEIGHT = 96
SOURCE_FACES_SCREEN_RIGHT = False
EXTRACTOR_FLIP_X = False
RUNTIME_FLIP_X = False
FINAL_FACES_SCREEN_RIGHT = False

# Inclusive logical frames participating in each exact-idle handoff. Frame 0
# and frame 63 are exact. Intermediate alpha coverage uses an 8x8 Bayer matrix
# and overlapping colors use linear interpolation; no random state is involved.
ENTER_HANDOFF_LAST_FRAME = 7
EXIT_HANDOFF_FIRST_FRAME = 58
MAX_HANDOFF_ALPHA_XOR = 0.13
MAX_HANDOFF_RGBA_MAE = 0.08
BAYER_8X8 = (
    (0, 32, 8, 40, 2, 34, 10, 42),
    (48, 16, 56, 24, 50, 18, 58, 26),
    (12, 44, 4, 36, 14, 46, 6, 38),
    (60, 28, 52, 20, 62, 30, 54, 22),
    (3, 35, 11, 43, 1, 33, 9, 41),
    (51, 19, 59, 27, 49, 17, 57, 25),
    (15, 47, 7, 39, 13, 45, 5, 37),
    (63, 31, 55, 23, 61, 29, 53, 21),
)


class ExtractionError(RuntimeError):
    """A concise, user-actionable sheet or frame contract failure."""


@dataclass(frozen=True)
class CharacterAnchor:
    """Largest connected subject component and its planted-foot anchor."""

    component_pixels: int
    left: int
    top: int
    right: int
    bottom: int
    foot_center_x: float
    foot_bottom_y: int

    @property
    def width(self) -> int:
        return self.right - self.left

    @property
    def height(self) -> int:
        return self.bottom - self.top


@dataclass(frozen=True)
class SourceCell:
    """One row-major sheet cell and its independently detected anchor."""

    logical_index: int
    sheet_name: str
    cell_index: int
    image: Image.Image
    anchor: CharacterAnchor


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--sheet-dir",
        type=Path,
        default=DEFAULT_SHEET_DIR,
        help=(
            "Directory containing the four keyed RGBA sheets "
            f"(default: {DEFAULT_SHEET_DIR})."
        ),
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT_DIR,
        help=(
            "Destination for frame-000.png through frame-063.png "
            f"(default: {DEFAULT_OUTPUT_DIR})."
        ),
    )
    parser.add_argument(
        "--idle-atlas",
        type=Path,
        default=DEFAULT_IDLE_ATLAS,
        help=(
            "Ordinary 8-direction 64x96 idle reference atlas used for "
            "the exact start/end handoff "
            f"(default: {DEFAULT_IDLE_ATLAS})."
        ),
    )
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="Validate and extract in memory without writing frame PNGs.",
    )
    return parser.parse_args()


def validate_static_contract() -> None:
    if FRAME_COUNT != 64:
        raise ExtractionError(
            f"Internal schedule must contain 64 frames, got {FRAME_COUNT}."
        )
    if FRAMES_PER_SHEET != 16:
        raise ExtractionError(
            "Every approved contact sheet must contain exactly 16 cells."
        )
    if CANVAS_SIZE != (128, 96) or HIP_PNG != (64, 56):
        raise ExtractionError(
            "Output contract must remain 128x96 with Unity hip (64,40)."
        )
    if not (0 < TARGET_CHARACTER_HEIGHT < CANVAS_HEIGHT):
        raise ExtractionError(
            "Target character height must fit inside the output canvas."
        )
    if not (0.0 < HORIZONTAL_CHARACTER_SCALE <= 1.0):
        raise ExtractionError(
            "Horizontal character scale must be in the range (0, 1]."
        )
    if not (HIP_PNG[1] < FOOT_BASELINE_PNG_Y < CANVAS_HEIGHT):
        raise ExtractionError(
            "Foot baseline must remain below the hip and inside the canvas."
        )
    if set(SHEET_SIZE_MULTIPLIERS) != set(SHEET_NAMES):
        raise ExtractionError(
            "Every locked source sheet must have one size multiplier."
        )
    if any(
        multiplier <= 0.0
        for multiplier in SHEET_SIZE_MULTIPLIERS.values()
    ):
        raise ExtractionError(
            "Sheet size multipliers must be finite positive values."
        )

    schedule = build_source_schedule()
    if len(schedule) != FRAME_COUNT:
        raise ExtractionError(
            f"Logical source map must contain 64 entries, got "
            f"{len(schedule)}."
        )
    if schedule[24] != schedule[47]:
        raise ExtractionError(
            "Logical frames 47 and 24 must share one exact loop bridge."
        )
    if schedule[31] != (SHEET_NAMES[2], 3) or schedule[32] != (
        SHEET_NAMES[2],
        0,
    ):
        raise ExtractionError(
            "Logical frames 31 and 32 must retain the locked mouth-pose join."
        )

    final_faces_screen_right = (
        SOURCE_FACES_SCREEN_RIGHT
        ^ EXTRACTOR_FLIP_X
        ^ RUNTIME_FLIP_X
    )
    if (
        EXTRACTOR_FLIP_X
        or RUNTIME_FLIP_X
        or final_faces_screen_right != FINAL_FACES_SCREEN_RIGHT
    ):
        raise ExtractionError(
            "Orientation contract is invalid: the texture-left source must "
            "reach the smoking renderer without extractor or runtime mirror."
        )
    if IDLE_DIRECTION_INDEX != 2:
        raise ExtractionError(
            "The exact balcony idle handoff must use Right direction cell 2."
        )
    if not (
        0 < ENTER_HANDOFF_LAST_FRAME < 24
        and 48 <= EXIT_HANDOFF_FIRST_FRAME < 63
    ):
        raise ExtractionError(
            "Idle handoff ranges must remain inside enter and exit phases."
        )
    if not (
        0.0 < MAX_HANDOFF_ALPHA_XOR < 1.0
        and 0.0 < MAX_HANDOFF_RGBA_MAE < 1.0
    ):
        raise ExtractionError(
            "Idle handoff difference limits must be normalized fractions."
        )
    if len(BAYER_8X8) != 8 or any(
        len(row) != 8 for row in BAYER_8X8
    ):
        raise ExtractionError("Ordered-dither matrix must remain 8x8.")
    if sorted(value for row in BAYER_8X8 for value in row) != list(range(64)):
        raise ExtractionError(
            "Ordered-dither matrix must contain each threshold 0..63 once."
        )


def build_source_schedule() -> list[tuple[str, int]]:
    schedule = [
        (sheet_name, cell_index)
        for sheet_name in SHEET_NAMES
        for cell_index in range(FRAMES_PER_SHEET)
    ]
    for logical_index, source in LOGICAL_SOURCE_OVERRIDES.items():
        if logical_index < 0 or logical_index >= len(schedule):
            raise ExtractionError(
                f"Logical override {logical_index} is outside 0..63."
            )
        sheet_name, cell_index = source
        if sheet_name not in SHEET_NAMES:
            raise ExtractionError(
                f"Logical override {logical_index} uses unknown sheet "
                f"{sheet_name!r}."
            )
        if cell_index < 0 or cell_index >= FRAMES_PER_SHEET:
            raise ExtractionError(
                f"Logical override {logical_index} uses cell "
                f"{cell_index}, outside 0..15."
            )
        schedule[logical_index] = source
    return schedule


def summarize_names(names: Sequence[str], limit: int = 8) -> str:
    visible = list(names[:limit])
    result = ", ".join(visible)
    remaining = len(names) - len(visible)
    if remaining > 0:
        result += f", ... (+{remaining} more)"
    return result


def load_sheets(sheet_dir: Path) -> dict[str, Image.Image]:
    if not sheet_dir.exists():
        raise ExtractionError(
            f"Keyed sheet directory does not exist: {sheet_dir}"
        )
    if not sheet_dir.is_dir():
        raise ExtractionError(
            f"Keyed sheet path is not a directory: {sheet_dir}"
        )

    expected = set(SHEET_NAMES)
    actual_pngs = {
        path.name
        for path in sheet_dir.iterdir()
        if path.is_file() and path.suffix.lower() == ".png"
    }
    missing = sorted(expected - actual_pngs)
    unexpected = sorted(actual_pngs - expected)
    if missing or unexpected:
        problems = []
        if missing:
            problems.append("missing: " + summarize_names(missing))
        if unexpected:
            problems.append(
                "unexpected PNGs: " + summarize_names(unexpected)
            )
        raise ExtractionError(
            "Expected exactly the four locked keyed sheets; "
            + "; ".join(problems)
        )

    sheets: dict[str, Image.Image] = {}
    common_size: tuple[int, int] | None = None
    for name in SHEET_NAMES:
        path = sheet_dir / name
        try:
            with Image.open(path) as opened:
                if opened.format != "PNG" or opened.mode != "RGBA":
                    raise ExtractionError(
                        f"{name} must be an RGBA PNG, got "
                        f"{opened.format or 'unknown'} {opened.mode!r}."
                    )
                width, height = opened.size
                if width < 512 or height < 512:
                    raise ExtractionError(
                        f"{name} is only {width}x{height}; expected a "
                        "high-resolution 4x4 contact sheet."
                    )
                if common_size is None:
                    common_size = opened.size
                elif opened.size != common_size:
                    raise ExtractionError(
                        f"{name} is {width}x{height}; all four sheets "
                        f"must match {common_size[0]}x{common_size[1]}."
                    )

                opened.load()
                sheet = opened.copy()
                alpha_min, alpha_max = sheet.getchannel("A").getextrema()
                if alpha_min != 0 or alpha_max == 0:
                    raise ExtractionError(
                        f"{name} must contain transparent background and "
                        "visible keyed subjects."
                    )
                assert_transparent_corners(sheet, name)
                sheets[name] = sheet
        except ExtractionError:
            raise
        except OSError as exc:
            raise ExtractionError(
                f"Cannot read keyed sheet {path}: {exc}"
            ) from exc

    return sheets


def load_exact_idle_frame(idle_atlas_path: Path) -> Image.Image:
    """Load the exact ordinary neutral composite for the balcony view."""
    try:
        with Image.open(idle_atlas_path) as opened:
            expected_size = (
                IDLE_FRAME_WIDTH * 8,
                IDLE_FRAME_HEIGHT,
            )
            if opened.format != "PNG" or opened.mode != "RGBA":
                raise ExtractionError(
                    f"Idle atlas must be an RGBA PNG, got "
                    f"{opened.format or 'unknown'} {opened.mode!r}."
                )
            if opened.size != expected_size:
                raise ExtractionError(
                    f"Idle atlas must be {expected_size[0]}x"
                    f"{expected_size[1]}, got {opened.width}x"
                    f"{opened.height}."
                )
            opened.load()
            left = IDLE_DIRECTION_INDEX * IDLE_FRAME_WIDTH
            idle_cell = opened.crop((
                left,
                0,
                left + IDLE_FRAME_WIDTH,
                IDLE_FRAME_HEIGHT,
            ))
    except ExtractionError:
        raise
    except OSError as exc:
        raise ExtractionError(
            f"Cannot read ordinary idle atlas {idle_atlas_path}: {exc}"
        ) from exc

    alpha_values = set(idle_cell.getchannel("A").get_flattened_data())
    if not alpha_values or alpha_values - {0, 255}:
        raise ExtractionError(
            "Ordinary idle reference must use binary alpha."
        )
    if idle_cell.getchannel("A").getbbox() is None:
        raise ExtractionError(
            "Ordinary Right idle reference cell is empty."
        )

    canvas = Image.new("RGBA", CANVAS_SIZE, TRANSPARENT)
    paste_x = HIP_PNG[0] - IDLE_FRAME_WIDTH // 2
    canvas.alpha_composite(idle_cell, (paste_x, 0))
    clear_transparent_rgb(canvas)
    validate_output_frame(canvas, 0)
    return canvas


def assert_transparent_corners(sheet: Image.Image, label: str) -> None:
    sample = max(2, min(sheet.width, sheet.height) // 128)
    corners = (
        (0, 0, sample, sample),
        (sheet.width - sample, 0, sheet.width, sample),
        (0, sheet.height - sample, sample, sheet.height),
        (
            sheet.width - sample,
            sheet.height - sample,
            sheet.width,
            sheet.height,
        ),
    )
    for corner_index, box in enumerate(corners):
        alpha = sheet.getchannel("A").crop(box)
        if alpha.getextrema()[1] != 0:
            raise ExtractionError(
                f"{label} corner {corner_index} is not fully transparent; "
                "run the official chroma-key helper before extraction."
            )


def grid_boundary(total: int, index: int, count: int) -> int:
    if index < 0 or index > count:
        raise ExtractionError(
            f"Grid boundary {index} is outside 0..{count}."
        )
    # Integer half-up rounding avoids banker's-rounding differences.
    return (index * total + count // 2) // count


def crop_cell(sheet: Image.Image, cell_index: int) -> Image.Image:
    if cell_index < 0 or cell_index >= FRAMES_PER_SHEET:
        raise ExtractionError(
            f"Sheet cell {cell_index} is outside 0..15."
        )
    row = cell_index // GRID_COLUMNS
    column = cell_index % GRID_COLUMNS
    left = grid_boundary(sheet.width, column, GRID_COLUMNS)
    right = grid_boundary(sheet.width, column + 1, GRID_COLUMNS)
    top = grid_boundary(sheet.height, row, GRID_ROWS)
    bottom = grid_boundary(sheet.height, row + 1, GRID_ROWS)
    return sheet.crop((left, top, right, bottom))


def find_character_anchor(
    cell: Image.Image,
    label: str,
) -> CharacterAnchor:
    alpha = cell.getchannel("A").tobytes()
    width, height = cell.size
    occupied = bytearray(
        1 if value >= ANCHOR_ALPHA_THRESHOLD else 0
        for value in alpha
    )
    visited = bytearray(width * height)
    largest: list[int] = []

    for start in range(width * height):
        if not occupied[start] or visited[start]:
            continue
        visited[start] = 1
        stack = [start]
        component: list[int] = []
        while stack:
            current = stack.pop()
            component.append(current)
            x = current % width
            y = current // width
            if x > 0:
                append_neighbor(
                    current - 1,
                    occupied,
                    visited,
                    stack,
                )
            if x + 1 < width:
                append_neighbor(
                    current + 1,
                    occupied,
                    visited,
                    stack,
                )
            if y > 0:
                append_neighbor(
                    current - width,
                    occupied,
                    visited,
                    stack,
                )
            if y + 1 < height:
                append_neighbor(
                    current + width,
                    occupied,
                    visited,
                    stack,
                )
        if len(component) > len(largest):
            largest = component

    if len(largest) < MIN_SOURCE_COMPONENT_PIXELS:
        raise ExtractionError(
            f"{label} has no plausible connected character component; "
            f"largest contains {len(largest)} opaque pixels."
        )

    xs = [index % width for index in largest]
    ys = [index // width for index in largest]
    left = min(xs)
    right = max(xs) + 1
    top = min(ys)
    bottom = max(ys) + 1
    character_height = bottom - top
    foot_band_top = max(
        top,
        bottom - max(6, round(character_height * 0.13)),
    )
    foot_xs = [
        index % width
        for index in largest
        if index // width >= foot_band_top
    ]
    if not foot_xs:
        raise ExtractionError(
            f"{label} has no planted-foot pixels in its lower band."
        )

    return CharacterAnchor(
        component_pixels=len(largest),
        left=left,
        top=top,
        right=right,
        bottom=bottom,
        foot_center_x=(min(foot_xs) + max(foot_xs)) * 0.5,
        foot_bottom_y=bottom - 1,
    )


def append_neighbor(
    index: int,
    occupied: bytearray,
    visited: bytearray,
    stack: list[int],
) -> None:
    if occupied[index] and not visited[index]:
        visited[index] = 1
        stack.append(index)


def collect_source_cells(
    sheets: dict[str, Image.Image],
) -> list[SourceCell]:
    cells: list[SourceCell] = []
    for logical_index, (sheet_name, cell_index) in enumerate(
        build_source_schedule()
    ):
        sheet = sheets[sheet_name]
        cell = crop_cell(sheet, cell_index)
        label = f"{sheet_name} cell {cell_index}"
        cells.append(SourceCell(
            logical_index=logical_index,
            sheet_name=sheet_name,
            cell_index=cell_index,
            image=cell,
            anchor=find_character_anchor(cell, label),
        ))

    if len(cells) != FRAME_COUNT:
        raise ExtractionError(
            f"Expected 64 extracted cells, got {len(cells)}."
        )
    return cells


def calculate_shared_scale(cells: Sequence[SourceCell]) -> float:
    tallest = max(cell.anchor.height for cell in cells)
    if tallest <= 0:
        raise ExtractionError(
            "Cannot derive shared scale from an empty character sequence."
        )
    scale = TARGET_CHARACTER_HEIGHT / tallest
    if scale <= 0.0 or scale > 1.0:
        raise ExtractionError(
            f"Implausible shared source scale {scale:.6f}."
        )
    return scale


def normalize_cell(
    source: SourceCell,
    shared_scale: float,
) -> Image.Image:
    cell = source.image
    effective_scale = (
        shared_scale * SHEET_SIZE_MULTIPLIERS[source.sheet_name]
    )
    horizontal_scale = effective_scale * HORIZONTAL_CHARACTER_SCALE
    resized_width = max(1, round(cell.width * horizontal_scale))
    resized_height = max(1, round(cell.height * effective_scale))
    resized = cell.resize(
        (resized_width, resized_height),
        resample=Image.Resampling.LANCZOS,
    )

    mapped_foot_center_x = (
        (source.anchor.foot_center_x + 0.5) * horizontal_scale - 0.5
    )
    mapped_foot_bottom = (
        (source.anchor.foot_bottom_y + 1) * effective_scale
    )
    paste_x = round(HIP_PNG[0] - mapped_foot_center_x)
    paste_y = round(FOOT_BASELINE_PNG_Y - mapped_foot_bottom)

    canvas = Image.new("RGBA", CANVAS_SIZE, TRANSPARENT)
    alpha_composite_clipped(canvas, resized, paste_x, paste_y)
    clear_transparent_rgb(canvas)
    validate_output_frame(canvas, source.logical_index)
    return canvas


def alpha_composite_clipped(
    destination: Image.Image,
    source: Image.Image,
    x: int,
    y: int,
) -> None:
    source_left = max(0, -x)
    source_top = max(0, -y)
    source_right = min(source.width, destination.width - x)
    source_bottom = min(source.height, destination.height - y)
    if source_right <= source_left or source_bottom <= source_top:
        raise ExtractionError(
            "A normalized source cell falls completely outside its canvas."
        )
    clipped = source.crop((
        source_left,
        source_top,
        source_right,
        source_bottom,
    ))
    destination.alpha_composite(
        clipped,
        (x + source_left, y + source_top),
    )


def clear_transparent_rgb(image: Image.Image) -> None:
    pixels = []
    for red, green, blue, alpha in image.get_flattened_data():
        pixels.append(
            TRANSPARENT
            if alpha == 0
            else (red, green, blue, alpha)
        )
    image.putdata(pixels)


def validate_output_frame(frame: Image.Image, logical_index: int) -> None:
    if frame.mode != "RGBA" or frame.size != CANVAS_SIZE:
        raise ExtractionError(
            f"frame-{logical_index:03d} must be a 128x96 RGBA image."
        )

    alpha = frame.getchannel("A")
    thresholded = alpha.point(
        lambda value: 255 if value >= ANCHOR_ALPHA_THRESHOLD else 0,
        mode="1",
    )
    bounds = thresholded.getbbox()
    if bounds is None:
        raise ExtractionError(
            f"frame-{logical_index:03d} is empty after alpha threshold."
        )
    opaque_pixels = sum(
        value >= ANCHOR_ALPHA_THRESHOLD
        for value in alpha.get_flattened_data()
    )
    if opaque_pixels < MIN_OUTPUT_OPAQUE_PIXELS:
        raise ExtractionError(
            f"frame-{logical_index:03d} has only {opaque_pixels} opaque "
            "pixels and resembles a placeholder."
        )

    left, top, right, bottom = bounds
    if left <= 0 or top <= 0 or right >= CANVAS_WIDTH or bottom >= CANVAS_HEIGHT:
        raise ExtractionError(
            f"frame-{logical_index:03d} touches the canvas edge at "
            f"{bounds}; content would be clipped."
        )

    suspicious_green = 0
    for red, green, blue, alpha_value in frame.get_flattened_data():
        if (
            alpha_value >= ANCHOR_ALPHA_THRESHOLD
            and green >= 128
            and green >= red + 48
            and green >= blue + 48
        ):
            suspicious_green += 1
    if suspicious_green:
        raise ExtractionError(
            f"frame-{logical_index:03d} retains {suspicious_green} "
            "opaque chroma-green pixels."
        )


def validate_output_names(output_dir: Path) -> None:
    if output_dir.exists() and not output_dir.is_dir():
        raise ExtractionError(
            f"Output path is not a directory: {output_dir}"
        )
    if not output_dir.exists():
        return

    expected = {
        f"frame-{index:03d}.png"
        for index in range(FRAME_COUNT)
    }
    actual_frame_pngs = {
        path.name
        for path in output_dir.iterdir()
        if (
            path.is_file()
            and path.name.lower().startswith("frame-")
            and path.suffix.lower() == ".png"
        )
    }
    unexpected = sorted(actual_frame_pngs - expected)
    if unexpected:
        raise ExtractionError(
            "Output contains unexpected frame-like PNGs: "
            + summarize_names(unexpected)
        )


def smoothstep(value: float) -> float:
    clamped = max(0.0, min(1.0, value))
    return clamped * clamped * (3.0 - 2.0 * clamped)


def ordered_dither_transition(
    first: Image.Image,
    second: Image.Image,
    second_weight: float,
) -> Image.Image:
    """Blend two pixel frames while retaining binary alpha coverage."""
    if first.size != CANVAS_SIZE or second.size != CANVAS_SIZE:
        raise ExtractionError(
            "Idle handoff inputs must both use the 128x96 canvas."
        )
    weight = max(0.0, min(1.0, second_weight))
    first_pixels = list(first.get_flattened_data())
    second_pixels = list(second.get_flattened_data())
    output_pixels = []

    for index, (left, right) in enumerate(
        zip(first_pixels, second_pixels)
    ):
        left_opaque = left[3] >= ANCHOR_ALPHA_THRESHOLD
        right_opaque = right[3] >= ANCHOR_ALPHA_THRESHOLD
        if not left_opaque and not right_opaque:
            output_pixels.append(TRANSPARENT)
            continue

        if left_opaque and right_opaque:
            output_pixels.append((
                round(left[0] + (right[0] - left[0]) * weight),
                round(left[1] + (right[1] - left[1]) * weight),
                round(left[2] + (right[2] - left[2]) * weight),
                255,
            ))
            continue

        x = index % CANVAS_WIDTH
        y = index // CANVAS_WIDTH
        threshold = (BAYER_8X8[y % 8][x % 8] + 0.5) / 64.0
        chosen = right if threshold < weight else left
        if chosen[3] >= ANCHOR_ALPHA_THRESHOLD:
            output_pixels.append((chosen[0], chosen[1], chosen[2], 255))
        else:
            output_pixels.append(TRANSPARENT)

    output = Image.new("RGBA", CANVAS_SIZE, TRANSPARENT)
    output.putdata(output_pixels)
    clear_transparent_rgb(output)
    return output


def apply_exact_idle_handoffs(
    generated_frames: Sequence[Image.Image],
    exact_idle: Image.Image,
) -> list[Image.Image]:
    if len(generated_frames) != FRAME_COUNT:
        raise ExtractionError(
            f"Idle handoff expected {FRAME_COUNT} generated frames, got "
            f"{len(generated_frames)}."
        )

    frames = [frame.copy() for frame in generated_frames]
    frames[0] = exact_idle.copy()
    enter_denominator = ENTER_HANDOFF_LAST_FRAME + 1
    for frame_index in range(1, ENTER_HANDOFF_LAST_FRAME + 1):
        weight = smoothstep(frame_index / enter_denominator)
        frames[frame_index] = ordered_dither_transition(
            exact_idle,
            generated_frames[frame_index],
            weight,
        )

    exit_denominator = FRAME_COUNT - EXIT_HANDOFF_FIRST_FRAME
    for frame_index in range(EXIT_HANDOFF_FIRST_FRAME, FRAME_COUNT - 1):
        weight = smoothstep(
            (frame_index - EXIT_HANDOFF_FIRST_FRAME + 1) /
            exit_denominator
        )
        frames[frame_index] = ordered_dither_transition(
            generated_frames[frame_index],
            exact_idle,
            weight,
        )
    frames[-1] = exact_idle.copy()

    for frame_index, frame in enumerate(frames):
        validate_output_frame(frame, frame_index)
    return frames


def transition_difference(
    first: Image.Image,
    second: Image.Image,
) -> tuple[float, float]:
    first_pixels = list(first.get_flattened_data())
    second_pixels = list(second.get_flattened_data())
    union_pixels = 0
    alpha_xor_pixels = 0
    rgba_difference = 0

    for left, right in zip(first_pixels, second_pixels):
        left_opaque = left[3] >= ANCHOR_ALPHA_THRESHOLD
        right_opaque = right[3] >= ANCHOR_ALPHA_THRESHOLD
        if not left_opaque and not right_opaque:
            continue
        union_pixels += 1
        if left_opaque != right_opaque:
            alpha_xor_pixels += 1

        left_binary = left if left_opaque else TRANSPARENT
        right_binary = right if right_opaque else TRANSPARENT
        rgba_difference += sum(
            abs(left_binary[channel] - right_binary[channel])
            for channel in range(4)
        )

    if union_pixels == 0:
        raise ExtractionError(
            "Cannot compare an empty animation transition."
        )
    return (
        alpha_xor_pixels / union_pixels,
        rgba_difference / (union_pixels * 4 * 255),
    )


def validate_idle_handoffs(
    frames: Sequence[Image.Image],
    generated_frames: Sequence[Image.Image],
    exact_idle: Image.Image,
) -> dict[str, object]:
    if frames[0].tobytes() != exact_idle.tobytes():
        raise ExtractionError(
            "Logical frame 000 must be pixel-identical to ordinary Right idle."
        )
    if frames[63].tobytes() != exact_idle.tobytes():
        raise ExtractionError(
            "Logical frame 063 must be pixel-identical to ordinary Right idle."
        )
    if frames[0].tobytes() != frames[63].tobytes():
        raise ExtractionError(
            "Start and end handoff frames must be pixel-identical."
        )

    raw_enter_cut = transition_difference(
        exact_idle,
        generated_frames[0],
    )
    raw_exit_cut = transition_difference(
        generated_frames[63],
        exact_idle,
    )
    enter_steps = [
        transition_difference(frames[index], frames[index + 1])
        for index in range(0, ENTER_HANDOFF_LAST_FRAME + 1)
    ]
    exit_steps = [
        transition_difference(frames[index], frames[index + 1])
        for index in range(EXIT_HANDOFF_FIRST_FRAME - 1, 63)
    ]

    if enter_steps[0][0] >= raw_enter_cut[0]:
        raise ExtractionError(
            "First enter handoff step does not improve the raw idle cut."
        )
    if exit_steps[-1][0] >= raw_exit_cut[0]:
        raise ExtractionError(
            "Final exit handoff step does not improve the raw idle cut."
        )

    all_steps = enter_steps + exit_steps
    maximum_alpha_xor = max(value[0] for value in all_steps)
    maximum_rgba_mae = max(value[1] for value in all_steps)
    if (
        maximum_alpha_xor > MAX_HANDOFF_ALPHA_XOR
        or maximum_rgba_mae > MAX_HANDOFF_RGBA_MAE
    ):
        raise ExtractionError(
            "Idle handoff contains an abrupt adjacent step: "
            f"alpha XOR={maximum_alpha_xor:.6f} (limit "
            f"{MAX_HANDOFF_ALPHA_XOR:.6f}), RGBA MAE="
            f"{maximum_rgba_mae:.6f} (limit "
            f"{MAX_HANDOFF_RGBA_MAE:.6f})."
        )

    idle_hash = hashlib.sha256(exact_idle.tobytes()).hexdigest().upper()
    return {
        "idle_hash": idle_hash,
        "raw_enter_cut": raw_enter_cut,
        "raw_exit_cut": raw_exit_cut,
        "enter_steps": enter_steps,
        "exit_steps": exit_steps,
        "enter_max": (
            max(value[0] for value in enter_steps),
            max(value[1] for value in enter_steps),
        ),
        "exit_max": (
            max(value[0] for value in exit_steps),
            max(value[1] for value in exit_steps),
        ),
    }


def validate_loop_seams(
    frames: Sequence[Image.Image],
) -> tuple[tuple[float, float], tuple[float, float]]:
    wrap = transition_difference(frames[47], frames[24])
    inhale_join = transition_difference(frames[31], frames[32])
    if frames[47].tobytes() != frames[24].tobytes() or wrap != (0.0, 0.0):
        raise ExtractionError(
            "Loop wrap 47 -> 24 must be pixel-identical."
        )
    if inhale_join[0] > 0.05 or inhale_join[1] > 0.04:
        raise ExtractionError(
            "Loop join 31 -> 32 exceeds the locked near-seam limits: "
            f"alpha XOR={inhale_join[0]:.6f}, "
            f"RGBA MAE={inhale_join[1]:.6f}."
        )
    return wrap, inhale_join


def write_png_atomic(image: Image.Image, destination: Path) -> str:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary: Path | None = None
    try:
        descriptor, name = tempfile.mkstemp(
            prefix=f".{destination.name}.",
            suffix=".tmp",
            dir=destination.parent,
        )
        os.close(descriptor)
        temporary = Path(name)
        image.save(
            temporary,
            format="PNG",
            optimize=False,
            compress_level=9,
        )
        with Image.open(temporary) as reopened:
            if reopened.format != "PNG" or reopened.mode != "RGBA":
                raise ExtractionError(
                    f"Temporary output for {destination.name} did not "
                    "round-trip as RGBA PNG."
                )
            reopened.load()
            if reopened.size != image.size or reopened.tobytes() != image.tobytes():
                raise ExtractionError(
                    f"Temporary output pixels changed for {destination.name}."
                )

        digest = hashlib.sha256(
            temporary.read_bytes()
        ).hexdigest().upper()
        os.replace(temporary, destination)
        temporary = None
        return digest
    except ExtractionError:
        raise
    except OSError as exc:
        raise ExtractionError(
            f"Cannot write {destination} atomically: {exc}"
        ) from exc
    finally:
        if temporary is not None:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                pass


def run(args: argparse.Namespace) -> None:
    validate_static_contract()
    validate_output_names(args.output_dir)
    sheets = load_sheets(args.sheet_dir)
    cells = collect_source_cells(sheets)
    shared_scale = calculate_shared_scale(cells)
    generated_frames = [
        normalize_cell(cell, shared_scale)
        for cell in cells
    ]
    exact_idle = load_exact_idle_frame(args.idle_atlas)
    frames = apply_exact_idle_handoffs(
        generated_frames,
        exact_idle,
    )
    handoff_metrics = validate_idle_handoffs(
        frames,
        generated_frames,
        exact_idle,
    )
    wrap_difference, inhale_difference = validate_loop_seams(frames)
    pixel_hash = hashlib.sha256(
        b"".join(frame.tobytes() for frame in frames)
    ).hexdigest().upper()

    print(
        f"Validated {len(frames)} keyed cells from {len(sheets)} "
        f"RGBA sheets at {next(iter(sheets.values())).size}."
    )
    print(
        f"Shared base scale={shared_scale:.6f}; side-profile "
        f"correction={SHEET_SIZE_MULTIPLIERS[SHEET_NAMES[2]]:.3f}; "
        f"horizontal character scale={HORIZONTAL_CHARACTER_SCALE:.3f}; "
        "output=128x96; "
        "Unity hip=(64,40); planted-foot baseline=92 PNG y."
    )
    print(
        "Loop seam 47->24: "
        f"alpha XOR={wrap_difference[0]:.6f}, "
        f"RGBA MAE={wrap_difference[1]:.6f}; "
        "join 31->32: "
        f"alpha XOR={inhale_difference[0]:.6f}, "
        f"RGBA MAE={inhale_difference[1]:.6f}."
    )
    raw_enter = handoff_metrics["raw_enter_cut"]
    raw_exit = handoff_metrics["raw_exit_cut"]
    enter_max = handoff_metrics["enter_max"]
    exit_max = handoff_metrics["exit_max"]
    assert isinstance(raw_enter, tuple)
    assert isinstance(raw_exit, tuple)
    assert isinstance(enter_max, tuple)
    assert isinstance(exit_max, tuple)
    print(
        "Exact idle handoff: frame 000 == frame 063 == ordinary Right; "
        f"idle pixel SHA256={handoff_metrics['idle_hash']}."
    )
    print(
        "Handoff alpha-XOR/RGBA-MAE: raw cuts "
        f"enter={raw_enter[0]:.6f}/{raw_enter[1]:.6f}, "
        f"exit={raw_exit[0]:.6f}/{raw_exit[1]:.6f}; "
        f"max stepped enter={enter_max[0]:.6f}/{enter_max[1]:.6f}, "
        f"exit={exit_max[0]:.6f}/{exit_max[1]:.6f}."
    )
    print(
        "Orientation: source faces texture-left; extractor mirror=off; "
        "runtime flipX=off; exact idle source orientation is preserved."
    )
    print(f"Extracted-frame pixel SHA256={pixel_hash}")

    if args.validate_only:
        print("Validation passed; no files were written (--validate-only).")
        return

    hashes = []
    for logical_index, frame in enumerate(frames):
        destination = (
            args.output_dir / f"frame-{logical_index:03d}.png"
        )
        hashes.append(write_png_atomic(frame, destination))
    sequence_hash = hashlib.sha256(
        "".join(hashes).encode("ascii")
    ).hexdigest().upper()
    print(
        f"Wrote {FRAME_COUNT} aligned RGBA frames to "
        f"{args.output_dir}."
    )
    print(f"Source-frame sequence SHA256={sequence_hash}")


def main() -> int:
    args = parse_args()
    try:
        run(args)
    except ExtractionError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
