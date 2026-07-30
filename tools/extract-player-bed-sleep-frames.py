#!/usr/bin/env python3
"""Extract the approved imagegen sheets into 64 aligned sleep frames.

The source sheets are four-by-four RGBA contact sheets after chroma-key
removal.  This tool selects the authored primary poses plus extra in-betweens,
normalizes every character around the shared Unity hip pivot, and writes the
64 source frames consumed by ``build-player-bed-sleep-atlas.py``.
"""

from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import sys
import tempfile

try:
    from PIL import Image
except ImportError as exc:  # pragma: no cover
    raise SystemExit(
        "error: Pillow is required; install it with "
        "'python -m pip install Pillow'."
    ) from exc


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SHEETS = (
    ROOT / "ArtSource" / "Player" / "BedSleep" / "Keyed"
)
DEFAULT_OUTPUT = ROOT / "ArtSource" / "Player" / "BedSleep"

GRID_SIZE = 4
CANVAS_SIZE = (128, 96)
HIP_PNG = (64, 56)
MAX_SUBJECT_SIZE = (116, 84)
TRANSPARENT = (0, 0, 0, 0)
EXTRA_INDICES = frozenset((1, 3, 5, 7, 9, 11, 13, 14))


class ExtractionError(RuntimeError):
    """A concise source-sheet or output-contract failure."""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--sheet-dir",
        type=Path,
        default=DEFAULT_SHEETS,
        help=f"RGBA 4x4 keyed sheets (default: {DEFAULT_SHEETS}).",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT,
        help=f"Destination for frame-000..063.png (default: {DEFAULT_OUTPUT}).",
    )
    return parser.parse_args()


def phase_schedule(
    primary_name: str,
    inbetween_name: str,
) -> list[tuple[str, int]]:
    schedule: list[tuple[str, int]] = []
    for index in range(16):
        schedule.append((primary_name, index))
        if index in EXTRA_INDICES:
            schedule.append((inbetween_name, index))
    if len(schedule) != 24:
        raise ExtractionError(
            f"Internal phase schedule must contain 24 poses, got {len(schedule)}."
        )
    return schedule


def build_schedule() -> list[tuple[str, int]]:
    schedule = phase_schedule(
        "lie-down-primary.png",
        "lie-down-inbetweens.png",
    )
    schedule.extend(("sleep-loop.png", index) for index in range(16))
    schedule.extend(
        phase_schedule(
            "wake-primary.png",
            "wake-inbetweens.png",
        )
    )
    if len(schedule) != 64:
        raise ExtractionError(
            f"Internal animation schedule must contain 64 poses, got {len(schedule)}."
        )
    return schedule


def load_sheets(
    sheet_dir: Path,
    schedule: list[tuple[str, int]],
) -> dict[str, Image.Image]:
    sheets: dict[str, Image.Image] = {}
    for name in sorted({name for name, _ in schedule}):
        path = sheet_dir / name
        if not path.is_file():
            raise ExtractionError(f"Missing keyed source sheet: {path}")
        try:
            with Image.open(path) as opened:
                if opened.format != "PNG" or opened.mode != "RGBA":
                    raise ExtractionError(
                        f"{path.name} must be an RGBA PNG, got "
                        f"{opened.format or 'unknown'} {opened.mode!r}."
                    )
                width, height = opened.size
                if width < 512 or height < 512:
                    raise ExtractionError(
                        f"{path.name} is only {width}x{height}; expected a "
                        "high-resolution 4x4 pose sheet."
                    )
                opened.load()
                sheets[name] = opened.copy()
        except ExtractionError:
            raise
        except OSError as exc:
            raise ExtractionError(f"Cannot read {path}: {exc}") from exc
    return sheets


def occupied_ranges(
    counts: list[int],
    minimum_count: int,
    maximum_gap: int = 12,
) -> list[tuple[int, int]]:
    occupied = [
        index
        for index, count in enumerate(counts)
        if count >= minimum_count
    ]
    if not occupied:
        return []

    ranges: list[tuple[int, int]] = []
    first = occupied[0]
    previous = first
    for index in occupied[1:]:
        if index - previous > maximum_gap:
            ranges.append((first, previous))
            first = index
        previous = index
    ranges.append((first, previous))
    return ranges


def detect_subject_cells(
    sheet: Image.Image,
    label: str,
) -> list[tuple[int, int, int, int]]:
    width, height = sheet.size
    alpha = sheet.getchannel("A").tobytes()
    row_counts = []
    for y in range(height):
        row = alpha[y * width:(y + 1) * width]
        row_counts.append(sum(value >= 64 for value in row))
    row_ranges = occupied_ranges(row_counts, minimum_count=8)
    if len(row_ranges) != GRID_SIZE:
        raise ExtractionError(
            f"{label} must contain four separated pose rows; detected "
            f"{len(row_ranges)}: {row_ranges}."
        )

    boxes: list[tuple[int, int, int, int]] = []
    for row_index, (top, bottom) in enumerate(row_ranges):
        column_counts = [0] * width
        for y in range(top, bottom + 1):
            row = alpha[y * width:(y + 1) * width]
            for x, value in enumerate(row):
                if value >= 64:
                    column_counts[x] += 1
        column_ranges = occupied_ranges(
            column_counts,
            minimum_count=5,
        )
        if len(column_ranges) != GRID_SIZE:
            raise ExtractionError(
                f"{label} row {row_index} must contain four separated poses; "
                f"detected {len(column_ranges)}: {column_ranges}."
            )
        for left, right in column_ranges:
            padding = 6
            boxes.append((
                max(0, left - padding),
                max(0, top - padding),
                min(width, right + padding + 1),
                min(height, bottom + padding + 1),
            ))

    if len(boxes) != GRID_SIZE * GRID_SIZE:
        raise ExtractionError(
            f"{label} must contain 16 pose bounds, got {len(boxes)}."
        )
    return boxes


def trim_subject(
    sheet: Image.Image,
    cell_index: int,
    label: str,
    subject_cells: list[tuple[int, int, int, int]],
) -> Image.Image:
    if cell_index < 0 or cell_index >= len(subject_cells):
        raise ExtractionError(f"Sheet cell {cell_index} is outside 0..15.")
    cell = sheet.crop(subject_cells[cell_index])
    alpha = cell.getchannel("A").point(
        lambda value: 255 if value >= 64 else 0,
        mode="1",
    )
    bounds = alpha.getbbox()
    if bounds is None:
        raise ExtractionError(f"{label} contains no visible character pixels.")
    left, top, right, bottom = bounds
    padding = 2
    left = max(0, left - padding)
    top = max(0, top - padding)
    right = min(cell.width, right + padding)
    bottom = min(cell.height, bottom + padding)
    subject = cell.crop((left, top, right, bottom))
    if subject.width < 8 or subject.height < 8:
        raise ExtractionError(
            f"{label} subject bounds are implausibly small: "
            f"{subject.width}x{subject.height}."
        )
    return subject


def normalize_subject(subject: Image.Image) -> Image.Image:
    max_width, max_height = MAX_SUBJECT_SIZE
    scale = min(
        max_width / subject.width,
        max_height / subject.height,
    )
    width = max(1, round(subject.width * scale))
    height = max(1, round(subject.height * scale))
    resized = subject.resize(
        (width, height),
        resample=Image.Resampling.LANCZOS,
    )

    # The generated poses keep a stable anatomy and direction.  As the pose
    # becomes horizontal, its hip shifts a little right of the silhouette
    # center; the aspect-based estimate preserves that motion without baking
    # bed-space translation into the sprite cells.
    aspect = width / max(1, height)
    horizontal = min(1.0, max(0.0, (aspect - 0.85) / 1.35))
    hip_fraction_x = 0.50 + (0.07 * horizontal)
    hip_fraction_y = 0.56
    paste_x = round(HIP_PNG[0] - width * hip_fraction_x)
    paste_y = round(HIP_PNG[1] - height * hip_fraction_y)
    paste_x = min(max(0, paste_x), CANVAS_SIZE[0] - width)
    paste_y = min(max(0, paste_y), CANVAS_SIZE[1] - height)

    canvas = Image.new("RGBA", CANVAS_SIZE, TRANSPARENT)
    canvas.alpha_composite(resized, (paste_x, paste_y))
    if canvas.getchannel("A").getbbox() is None:
        raise ExtractionError("Normalized frame unexpectedly became empty.")
    return canvas


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
        image.save(temporary, format="PNG", optimize=False, compress_level=9)
        digest = hashlib.sha256(temporary.read_bytes()).hexdigest().upper()
        os.replace(temporary, destination)
        temporary = None
        return digest
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def run(args: argparse.Namespace) -> None:
    schedule = build_schedule()
    sheets = load_sheets(args.sheet_dir, schedule)
    cells_by_sheet = {
        name: detect_subject_cells(sheet, name)
        for name, sheet in sheets.items()
    }
    hashes: list[str] = []
    for logical_index, (sheet_name, cell_index) in enumerate(schedule):
        label = f"{sheet_name} cell {cell_index}"
        subject = trim_subject(
            sheets[sheet_name],
            cell_index,
            label,
            cells_by_sheet[sheet_name],
        )
        frame = normalize_subject(subject)
        destination = args.output_dir / f"frame-{logical_index:03d}.png"
        hashes.append(write_png_atomic(frame, destination))

    print(
        f"Wrote 64 aligned RGBA frames to {args.output_dir}; "
        f"canvas={CANVAS_SIZE[0]}x{CANVAS_SIZE[1]}, "
        f"Unity hip=(64,40)."
    )
    sequence_hash = hashlib.sha256(
        "".join(hashes).encode("ascii")
    ).hexdigest().upper()
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
