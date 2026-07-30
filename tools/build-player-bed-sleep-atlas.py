#!/usr/bin/env python3
"""Validate and pack the authored player bed-sleep animation atlas.

The builder consumes exactly 64 same-sized RGBA PNG frames named
``frame-000.png`` through ``frame-063.png``.  Each source canvas is mapped to
a 128x96 cell with one shared nearest-neighbour cover/crop transform, then
packed into an 8x8 atlas.  Logical frame zero occupies the lower PNG row so
Unity texture-space frame zero starts at y=0.

Pillow is the only dependency.
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
    from PIL import Image, UnidentifiedImageError
except ImportError as exc:  # pragma: no cover - exercised before this module loads.
    raise SystemExit(
        "error: Pillow is required; install it with 'python -m pip install Pillow'."
    ) from exc


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE_DIR = (
    ROOT / "ArtSource" / "Player" / "BedSleep"
)
DEFAULT_OUTPUT = (
    ROOT / "Assets" / "Resources" / "Player" /
    "PlayerBedSleepAtlas.png"
)

FRAME_COUNT = 64
ATLAS_COLUMNS = 8
ATLAS_ROWS = 8
CELL_WIDTH = 128
CELL_HEIGHT = 96
ATLAS_WIDTH = CELL_WIDTH * ATLAS_COLUMNS
ATLAS_HEIGHT = CELL_HEIGHT * ATLAS_ROWS
HIP_ANCHOR = (64, 40)
PNG_HIP_ANCHOR = (HIP_ANCHOR[0], CELL_HEIGHT - HIP_ANCHOR[1])
ALPHA_THRESHOLD = 128
MAX_SOURCE_DIMENSION = 16384
MAX_SOURCE_PIXELS = 67_108_864
TRANSPARENT = (0, 0, 0, 0)

# Inclusive logical-frame ranges.
PHASE_RANGES = (
    ("lie-down", 0, 23),
    ("sleep-loop", 24, 39),
    ("wake-up", 40, 63),
)


class SleepAtlasError(RuntimeError):
    """A concise, user-actionable source or output contract failure."""


@dataclass(frozen=True)
class FitPlan:
    """The one crop shared by every same-sized source frame."""

    source_size: tuple[int, int]
    crop_box: tuple[float, float, float, float]
    source_png_hip_anchor: tuple[float, float]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=DEFAULT_SOURCE_DIR,
        help=(
            "Directory containing frame-000.png through frame-063.png "
            f"(default: {DEFAULT_SOURCE_DIR})."
        ),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
        help=(
            "Destination for the 1024x768 RGBA atlas "
            f"(default: {DEFAULT_OUTPUT})."
        ),
    )
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="Validate and build in memory without creating or changing files.",
    )
    return parser.parse_args()


def validate_static_contract() -> None:
    if FRAME_COUNT != ATLAS_COLUMNS * ATLAS_ROWS:
        raise SleepAtlasError(
            "Internal layout is invalid: frame count must equal atlas cells."
        )
    if (ATLAS_WIDTH, ATLAS_HEIGHT) != (1024, 768):
        raise SleepAtlasError(
            "Internal layout is invalid: atlas must be exactly 1024x768."
        )

    anchor_x, anchor_y = HIP_ANCHOR
    if not (
        0 <= anchor_x < CELL_WIDTH
        and 0 <= anchor_y < CELL_HEIGHT
    ):
        raise SleepAtlasError(
            f"Hip anchor {HIP_ANCHOR} is outside the 128x96 cell."
        )

    covered_frames: list[int] = []
    for label, first, last in PHASE_RANGES:
        if first < 0 or last < first or last >= FRAME_COUNT:
            raise SleepAtlasError(
                f"Animation phase {label!r} has invalid range "
                f"{first}..{last}."
            )
        covered_frames.extend(range(first, last + 1))
    if covered_frames != list(range(FRAME_COUNT)):
        raise SleepAtlasError(
            "Animation phase ranges must cover frames 0..63 exactly once."
        )


def summarize_names(names: Sequence[str], limit: int = 8) -> str:
    visible = list(names[:limit])
    result = ", ".join(visible)
    remaining = len(names) - len(visible)
    if remaining > 0:
        result += f", ... (+{remaining} more)"
    return result


def discover_frame_paths(source_dir: Path) -> list[Path]:
    if not source_dir.exists():
        raise SleepAtlasError(
            f"Source directory does not exist: {source_dir}"
        )
    if not source_dir.is_dir():
        raise SleepAtlasError(
            f"Source path is not a directory: {source_dir}"
        )

    try:
        entries = list(source_dir.iterdir())
    except OSError as exc:
        raise SleepAtlasError(
            f"Cannot list source directory {source_dir}: {exc}"
        ) from exc

    expected_names = [
        f"frame-{index:03d}.png"
        for index in range(FRAME_COUNT)
    ]
    expected_set = set(expected_names)
    actual_by_name = {entry.name: entry for entry in entries}
    frame_like_names = {
        entry.name
        for entry in entries
        if (
            entry.name.lower().startswith("frame-")
            and entry.suffix.lower() == ".png"
        )
    }

    missing = sorted(expected_set - set(actual_by_name))
    unexpected = sorted(frame_like_names - expected_set)
    problems = []
    if missing:
        problems.append(
            "missing: " + summarize_names(missing)
        )
    if unexpected:
        problems.append(
            "unexpected frame-like PNGs: "
            + summarize_names(unexpected)
        )
    if problems:
        raise SleepAtlasError(
            "Expected exactly frame-000.png through frame-063.png; "
            + "; ".join(problems)
        )

    paths = [actual_by_name[name] for name in expected_names]
    non_files = [path.name for path in paths if not path.is_file()]
    if non_files:
        raise SleepAtlasError(
            "Frame paths must be regular files: "
            + summarize_names(non_files)
        )
    return paths


def validate_output_path(output: Path, frame_paths: Sequence[Path]) -> None:
    if output.suffix.lower() != ".png":
        raise SleepAtlasError(
            f"Output must use the .png extension: {output}"
        )
    if output.exists() and output.is_dir():
        raise SleepAtlasError(
            f"Output path is a directory, not a PNG file: {output}"
        )

    resolved_output = output.resolve()
    for frame_path in frame_paths:
        if resolved_output == frame_path.resolve():
            raise SleepAtlasError(
                "Output must not overwrite a source frame: "
                f"{frame_path}"
            )


def load_source_frames(
    frame_paths: Sequence[Path],
) -> tuple[list[Image.Image], tuple[int, int]]:
    frames: list[Image.Image] = []
    common_size: tuple[int, int] | None = None

    for index, path in enumerate(frame_paths):
        try:
            with Image.open(path) as opened:
                if opened.format != "PNG":
                    raise SleepAtlasError(
                        f"{path.name} is not a PNG file "
                        f"(detected {opened.format or 'unknown'})."
                    )
                if getattr(opened, "n_frames", 1) != 1:
                    raise SleepAtlasError(
                        f"{path.name} must contain one image, not an "
                        "animated PNG."
                    )
                if opened.mode != "RGBA":
                    raise SleepAtlasError(
                        f"{path.name} must be RGBA, got mode {opened.mode!r}."
                    )

                width, height = opened.size
                if width < 1 or height < 1:
                    raise SleepAtlasError(
                        f"{path.name} has invalid size {width}x{height}."
                    )
                if (
                    width > MAX_SOURCE_DIMENSION
                    or height > MAX_SOURCE_DIMENSION
                    or width * height > MAX_SOURCE_PIXELS
                ):
                    raise SleepAtlasError(
                        f"{path.name} is {width}x{height}; source frames "
                        f"must be at most {MAX_SOURCE_DIMENSION}px per side "
                        f"and {MAX_SOURCE_PIXELS} pixels total."
                    )

                if common_size is None:
                    common_size = opened.size
                elif opened.size != common_size:
                    raise SleepAtlasError(
                        f"{path.name} is {width}x{height}; all source "
                        f"frames must match frame-000.png at "
                        f"{common_size[0]}x{common_size[1]}."
                    )

                opened.load()
                frames.append(opened.copy())
        except SleepAtlasError:
            raise
        except (OSError, UnidentifiedImageError, ValueError) as exc:
            raise SleepAtlasError(
                f"Cannot read {path.name} as an RGBA PNG: {exc}"
            ) from exc

        if len(frames) != index + 1:
            raise SleepAtlasError(
                f"Internal read failure at logical frame {index}."
            )

    if common_size is None or len(frames) != FRAME_COUNT:
        raise SleepAtlasError(
            f"Expected {FRAME_COUNT} readable frames, got {len(frames)}."
        )
    return frames, common_size


def make_fit_plan(source_size: tuple[int, int]) -> FitPlan:
    source_width, source_height = source_size
    source_anchor_x = (
        source_width * PNG_HIP_ANCHOR[0] / CELL_WIDTH
    )
    source_anchor_y = (
        source_height * PNG_HIP_ANCHOR[1] / CELL_HEIGHT
    )

    # Cover the target cell.  Horizontal excess is centered because the hip
    # x-anchor is centered.  Vertical excess is cropped around the normalized
    # source hip so its Unity bottom-origin y=40 does not drift to y=48.
    if source_width * CELL_HEIGHT >= source_height * CELL_WIDTH:
        crop_height = float(source_height)
        crop_width = (
            source_height * CELL_WIDTH / CELL_HEIGHT
        )
        crop_left = source_anchor_x - (
            crop_width * PNG_HIP_ANCHOR[0] / CELL_WIDTH
        )
        crop_top = 0.0
    else:
        crop_width = float(source_width)
        crop_height = (
            source_width * CELL_HEIGHT / CELL_WIDTH
        )
        crop_left = 0.0
        crop_top = source_anchor_y - (
            crop_height * PNG_HIP_ANCHOR[1] / CELL_HEIGHT
        )

    crop_box = (
        crop_left,
        crop_top,
        crop_left + crop_width,
        crop_top + crop_height,
    )
    epsilon = 1e-7
    if (
        crop_box[0] < -epsilon
        or crop_box[1] < -epsilon
        or crop_box[2] > source_width + epsilon
        or crop_box[3] > source_height + epsilon
        or crop_width <= 0.0
        or crop_height <= 0.0
    ):
        raise SleepAtlasError(
            f"Cannot derive a valid 128x96 fit/crop from "
            f"{source_width}x{source_height}: {crop_box}."
        )

    mapped_anchor_x = (
        (source_anchor_x - crop_box[0])
        * CELL_WIDTH / crop_width
    )
    mapped_anchor_y = (
        (source_anchor_y - crop_box[1])
        * CELL_HEIGHT / crop_height
    )
    if (
        abs(mapped_anchor_x - PNG_HIP_ANCHOR[0]) > epsilon
        or abs(mapped_anchor_y - PNG_HIP_ANCHOR[1]) > epsilon
    ):
        raise SleepAtlasError(
            "Fit/crop would move the shared hip anchor: "
            "PNG top-origin mapping is "
            f"({mapped_anchor_x}, {mapped_anchor_y})."
        )

    return FitPlan(
        source_size=source_size,
        crop_box=crop_box,
        source_png_hip_anchor=(source_anchor_x, source_anchor_y),
    )


def normalize_binary_alpha(
    frame: Image.Image,
    logical_index: int,
) -> Image.Image:
    normalized = Image.new(
        "RGBA",
        (CELL_WIDTH, CELL_HEIGHT),
        TRANSPARENT,
    )
    pixels = []
    opaque_pixels = 0
    for red, green, blue, alpha in frame.get_flattened_data():
        if alpha >= ALPHA_THRESHOLD:
            pixels.append((red, green, blue, 255))
            opaque_pixels += 1
        else:
            pixels.append(TRANSPARENT)
    normalized.putdata(pixels)

    if opaque_pixels == 0:
        raise SleepAtlasError(
            f"frame-{logical_index:03d}.png is empty after fit/crop and "
            f"binary-alpha threshold {ALPHA_THRESHOLD}."
        )
    return normalized


def build_logical_frames(
    sources: Sequence[Image.Image],
    fit_plan: FitPlan,
) -> list[Image.Image]:
    logical_frames = []
    for logical_index, source in enumerate(sources):
        fitted = source.resize(
            (CELL_WIDTH, CELL_HEIGHT),
            resample=Image.Resampling.NEAREST,
            box=fit_plan.crop_box,
        )
        logical_frames.append(
            normalize_binary_alpha(fitted, logical_index)
        )
    return logical_frames


def atlas_png_position(logical_index: int) -> tuple[int, int]:
    if logical_index < 0 or logical_index >= FRAME_COUNT:
        raise SleepAtlasError(
            f"Logical frame index {logical_index} is outside 0..63."
        )
    column = logical_index % ATLAS_COLUMNS
    logical_row = logical_index // ATLAS_COLUMNS
    png_row = ATLAS_ROWS - 1 - logical_row
    return column * CELL_WIDTH, png_row * CELL_HEIGHT


def validate_binary_alpha(image: Image.Image, label: str) -> None:
    alpha_values = set(
        image.getchannel("A").get_flattened_data()
    )
    invalid = sorted(alpha_values - {0, 255})
    if invalid:
        raise SleepAtlasError(
            f"{label} has non-binary alpha values: "
            + summarize_names([str(value) for value in invalid])
        )


def validate_atlas(
    atlas: Image.Image,
    logical_frames: Sequence[Image.Image],
) -> None:
    if atlas.mode != "RGBA":
        raise SleepAtlasError(
            f"Atlas must be RGBA, got mode {atlas.mode!r}."
        )
    if atlas.size != (ATLAS_WIDTH, ATLAS_HEIGHT):
        raise SleepAtlasError(
            f"Atlas must be 1024x768, got "
            f"{atlas.width}x{atlas.height}."
        )
    if len(logical_frames) != FRAME_COUNT:
        raise SleepAtlasError(
            f"Atlas validation expected 64 frames, got "
            f"{len(logical_frames)}."
        )
    validate_binary_alpha(atlas, "Atlas")

    for logical_index, expected in enumerate(logical_frames):
        atlas_x, atlas_y = atlas_png_position(logical_index)
        actual = atlas.crop((
            atlas_x,
            atlas_y,
            atlas_x + CELL_WIDTH,
            atlas_y + CELL_HEIGHT,
        ))
        if actual.tobytes() != expected.tobytes():
            raise SleepAtlasError(
                f"Atlas cell for logical frame {logical_index} differs "
                "from its processed source."
            )
        if actual.getchannel("A").getbbox() is None:
            raise SleepAtlasError(
                f"Atlas cell for logical frame {logical_index} is empty."
            )


def build_atlas(logical_frames: Sequence[Image.Image]) -> Image.Image:
    atlas = Image.new(
        "RGBA",
        (ATLAS_WIDTH, ATLAS_HEIGHT),
        TRANSPARENT,
    )
    for logical_index, frame in enumerate(logical_frames):
        atlas.paste(frame, atlas_png_position(logical_index))
    validate_atlas(atlas, logical_frames)
    return atlas


def write_png_atomic(image: Image.Image, destination: Path) -> str:
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        raise SleepAtlasError(
            f"Cannot create output directory {destination.parent}: {exc}"
        ) from exc

    temporary_path: Path | None = None
    try:
        descriptor, temporary_name = tempfile.mkstemp(
            prefix=f".{destination.name}.",
            suffix=".tmp",
            dir=destination.parent,
        )
        os.close(descriptor)
        temporary_path = Path(temporary_name)
        image.save(
            temporary_path,
            format="PNG",
            optimize=False,
            compress_level=9,
        )

        with Image.open(temporary_path) as reopened:
            if reopened.format != "PNG" or reopened.mode != "RGBA":
                raise SleepAtlasError(
                    "Temporary atlas did not round-trip as an RGBA PNG."
                )
            reopened.load()
            if (
                reopened.size != image.size
                or reopened.tobytes() != image.tobytes()
            ):
                raise SleepAtlasError(
                    "Temporary atlas pixels changed during PNG encoding."
                )

        file_hash = hashlib.sha256(
            temporary_path.read_bytes()
        ).hexdigest().upper()
        os.replace(temporary_path, destination)
        temporary_path = None
        return file_hash
    except SleepAtlasError:
        raise
    except OSError as exc:
        raise SleepAtlasError(
            f"Cannot write atlas atomically to {destination}: {exc}"
        ) from exc
    finally:
        if temporary_path is not None:
            try:
                temporary_path.unlink(missing_ok=True)
            except OSError:
                pass


def run(args: argparse.Namespace) -> None:
    validate_static_contract()
    frame_paths = discover_frame_paths(args.source_dir)
    validate_output_path(args.output, frame_paths)
    sources, source_size = load_source_frames(frame_paths)
    fit_plan = make_fit_plan(source_size)
    logical_frames = build_logical_frames(sources, fit_plan)
    atlas = build_atlas(logical_frames)
    pixel_hash = hashlib.sha256(atlas.tobytes()).hexdigest().upper()

    crop = fit_plan.crop_box
    print(
        f"Validated {FRAME_COUNT} RGBA source frames at "
        f"{source_size[0]}x{source_size[1]}."
    )
    print(
        "Shared nearest fit/crop: "
        f"({crop[0]:.4f}, {crop[1]:.4f}).."
        f"({crop[2]:.4f}, {crop[3]:.4f}); "
        f"Unity bottom-origin hip -> {HIP_ANCHOR}."
    )
    print(
        f"Atlas layout: {ATLAS_COLUMNS}x{ATLAS_ROWS} cells at "
        f"{CELL_WIDTH}x{CELL_HEIGHT}; logical frame 0 is in the "
        "lower PNG row."
    )
    print(f"Atlas pixel SHA256={pixel_hash}")

    if args.validate_only:
        print("Validation passed; no files were written (--validate-only).")
        return

    file_hash = write_png_atomic(atlas, args.output)
    print(
        f"Wrote {args.output} "
        f"({ATLAS_WIDTH}x{ATLAS_HEIGHT}) SHA256={file_hash}"
    )


def main() -> int:
    args = parse_args()
    try:
        run(args)
    except SleepAtlasError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
