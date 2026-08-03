#!/usr/bin/env python3
"""Validate and pack the authored player cat-feeding interaction atlas.

The builder consumes one transparent 8x8 RGBA contact sheet in top-left
row-major logical order.  It maps every source cell through one shared
nearest-neighbour contain transform into a 128x96 interaction cell and packs
the 64 binary-alpha frames into the layout expected by
PlayerAnimatedInteractionController: logical frame zero is in the lower PNG
row and logical rows advance upward.

Pillow is the only dependency.
"""

from __future__ import annotations

import argparse
from collections import deque
from dataclasses import dataclass
import hashlib
import os
from pathlib import Path
import sys
import tempfile

try:
    from PIL import Image
except ImportError as exc:  # pragma: no cover - runs before module load.
    raise SystemExit(
        "error: Pillow is required; install it with "
        "'python -m pip install Pillow'."
    ) from exc


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = (
    ROOT
    / "ArtSource"
    / "Player"
    / "CatFeeding"
    / "PlayerCatFeedingSource-alpha.png"
)
DEFAULT_OUTPUT = (
    ROOT
    / "Assets"
    / "Resources"
    / "Player"
    / "PlayerCatFeedingAtlas.png"
)

SOURCE_COLUMNS = 8
SOURCE_ROWS = 8
FRAME_COUNT = SOURCE_COLUMNS * SOURCE_ROWS
CELL_WIDTH = 128
CELL_HEIGHT = 96
ATLAS_WIDTH = SOURCE_COLUMNS * CELL_WIDTH
ATLAS_HEIGHT = SOURCE_ROWS * CELL_HEIGHT
HIP_ANCHOR = (64, 40)  # Unity bottom-origin cell coordinate.
PNG_HIP_ANCHOR = (HIP_ANCHOR[0], CELL_HEIGHT - HIP_ANCHOR[1])
ALPHA_THRESHOLD = 128
MIN_SOURCE_WIDTH = 1024
MIN_SOURCE_HEIGHT = 768
MAX_SOURCE_DIMENSION = 16384
MAX_SOURCE_PIXELS = 67_108_864
MIN_OPAQUE_PIXELS = 64
TRANSPARENT = (0, 0, 0, 0)
SOURCE_CELL_INSET = 3
MIN_COMPONENT_AREA_RATIO = 0.0004

PHASE_RANGES = (
    ("present-can", 0, 23),
    ("feeding-action", 24, 39),
    ("return", 40, 63),
)


class PlayerCatFeedingAtlasError(RuntimeError):
    """A concise, user-actionable source or output contract failure."""


@dataclass(frozen=True)
class FitPlan:
    source_cell_size: tuple[int, int]
    fitted_size: tuple[int, int]
    paste_position: tuple[int, int]


def grid_boundary(total: int, index: int, count: int) -> int:
    if index < 0 or index > count:
        raise PlayerCatFeedingAtlasError(
            f"Grid boundary {index} is outside 0..{count}."
        )
    # Integer half-up rounding supports generated sheets such as 1254x1254
    # while remaining stable across Python versions and platforms.
    return (index * total + count // 2) // count


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source",
        type=Path,
        default=DEFAULT_SOURCE,
        help=(
            "Approved transparent 8x8 source sheet "
            f"(default: {DEFAULT_SOURCE})."
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
        help="Validate and build in memory without writing any file.",
    )
    return parser.parse_args()


def validate_static_contract() -> None:
    if FRAME_COUNT != 64:
        raise PlayerCatFeedingAtlasError(
            "Internal source layout must contain exactly 64 frames."
        )
    if (ATLAS_WIDTH, ATLAS_HEIGHT) != (1024, 768):
        raise PlayerCatFeedingAtlasError(
            "Internal runtime atlas must be exactly 1024x768."
        )
    covered: list[int] = []
    for label, first, last in PHASE_RANGES:
        if first < 0 or last < first or last >= FRAME_COUNT:
            raise PlayerCatFeedingAtlasError(
                f"Animation phase {label!r} has invalid range "
                f"{first}..{last}."
            )
        covered.extend(range(first, last + 1))
    if covered != list(range(FRAME_COUNT)):
        raise PlayerCatFeedingAtlasError(
            "Animation phases must cover logical frames 0..63 once."
        )


def load_source(path: Path) -> Image.Image:
    if not path.exists():
        raise PlayerCatFeedingAtlasError(
            "Missing approved player cat-feeding alpha sheet: "
            f"{path}"
        )
    if not path.is_file():
        raise PlayerCatFeedingAtlasError(
            f"Source path is not a file: {path}"
        )

    try:
        with Image.open(path) as opened:
            if opened.format != "PNG" or opened.mode != "RGBA":
                raise PlayerCatFeedingAtlasError(
                    f"{path.name} must be an RGBA PNG, got "
                    f"{opened.format or 'unknown'} {opened.mode!r}."
                )
            width, height = opened.size
            if width < MIN_SOURCE_WIDTH or height < MIN_SOURCE_HEIGHT:
                raise PlayerCatFeedingAtlasError(
                    f"{path.name} is only {width}x{height}; expected an "
                    "approved 8x8 sheet of at least 1024x768."
                )
            if width > MAX_SOURCE_DIMENSION or height > MAX_SOURCE_DIMENSION:
                raise PlayerCatFeedingAtlasError(
                    f"{path.name} exceeds the {MAX_SOURCE_DIMENSION}px "
                    "per-axis safety limit."
                )
            if width * height > MAX_SOURCE_PIXELS:
                raise PlayerCatFeedingAtlasError(
                    f"{path.name} exceeds the {MAX_SOURCE_PIXELS}-pixel "
                    "safety limit."
                )
            opened.load()
            return opened.copy()
    except PlayerCatFeedingAtlasError:
        raise
    except OSError as exc:
        raise PlayerCatFeedingAtlasError(
            f"Cannot read {path}: {exc}"
        ) from exc


def make_fit_plan(source_cell_size: tuple[int, int]) -> FitPlan:
    source_width, source_height = source_cell_size
    scale = min(
        CELL_WIDTH / source_width,
        CELL_HEIGHT / source_height,
    )
    fitted_width = max(1, round(source_width * scale))
    fitted_height = max(1, round(source_height * scale))

    # The source uses the same normalized hip as the runtime canvas.  Mapping
    # this anchor after the shared contain scale makes a square source cell fit
    # as a centered 96x96 image while preserving the Unity (64,40) pivot.
    source_hip_fraction_x = HIP_ANCHOR[0] / CELL_WIDTH
    source_hip_fraction_y = PNG_HIP_ANCHOR[1] / CELL_HEIGHT
    paste_x = round(
        PNG_HIP_ANCHOR[0] -
        (fitted_width * source_hip_fraction_x)
    )
    paste_y = round(
        PNG_HIP_ANCHOR[1] -
        (fitted_height * source_hip_fraction_y)
    )
    if (
        paste_x < 0
        or paste_y < 0
        or paste_x + fitted_width > CELL_WIDTH
        or paste_y + fitted_height > CELL_HEIGHT
    ):
        raise PlayerCatFeedingAtlasError(
            f"Cannot fit {source_width}x{source_height} source cells into "
            "128x96 while preserving the normalized hip anchor; derived "
            f"size={fitted_width}x{fitted_height}, "
            f"paste=({paste_x},{paste_y})."
        )
    return FitPlan(
        source_cell_size=source_cell_size,
        fitted_size=(fitted_width, fitted_height),
        paste_position=(paste_x, paste_y),
    )


def find_opaque_components(
    image: Image.Image,
) -> list[set[tuple[int, int]]]:
    alpha = image.getchannel("A")
    opaque = {
        (x, y)
        for y in range(image.height)
        for x in range(image.width)
        if alpha.getpixel((x, y)) >= ALPHA_THRESHOLD
    }
    components: list[set[tuple[int, int]]] = []
    while opaque:
        start = opaque.pop()
        component = {start}
        queue = deque([start])
        while queue:
            x, y = queue.popleft()
            for neighbour_y in (y - 1, y, y + 1):
                for neighbour_x in (x - 1, x, x + 1):
                    neighbour = (neighbour_x, neighbour_y)
                    if neighbour == (x, y) or neighbour not in opaque:
                        continue
                    opaque.remove(neighbour)
                    component.add(neighbour)
                    queue.append(neighbour)
        components.append(component)
    return components


def clean_source_cell(
    source_cell: Image.Image,
    logical_index: int,
) -> tuple[Image.Image, int]:
    alpha_min, alpha_max = source_cell.getchannel("A").getextrema()
    if alpha_max == 0:
        raise PlayerCatFeedingAtlasError(
            f"Source cell for logical frame {logical_index:02d} is empty."
        )
    if alpha_min != 0:
        raise PlayerCatFeedingAtlasError(
            f"Source cell for logical frame {logical_index:02d} has no "
            "transparent background; use the approved alpha sheet."
        )

    components = find_opaque_components(source_cell)
    minimum_area = max(
        4,
        round(
            source_cell.width
            * source_cell.height
            * MIN_COMPONENT_AREA_RATIO
        ),
    )
    retained = [
        component
        for component in components
        if len(component) >= minimum_area
    ]
    if not retained:
        raise PlayerCatFeedingAtlasError(
            f"Source cell for logical frame {logical_index:02d} has no "
            f"component at or above the {minimum_area}-pixel artifact "
            "threshold."
        )

    cleaned = Image.new("RGBA", source_cell.size, TRANSPARENT)
    source_pixels = source_cell.load()
    cleaned_pixels = cleaned.load()
    for component in retained:
        for x, y in component:
            red, green, blue, _ = source_pixels[x, y]
            cleaned_pixels[x, y] = (red, green, blue, 255)

    removed_pixels = sum(
        len(component)
        for component in components
        if len(component) < minimum_area
    )
    return cleaned, removed_pixels


def normalize_frame(
    source_cell: Image.Image,
    fit_plan: FitPlan,
    logical_index: int,
) -> Image.Image:
    fitted = source_cell.resize(
        fit_plan.fitted_size,
        resample=Image.Resampling.NEAREST,
    )
    normalized_fitted = Image.new(
        "RGBA",
        fit_plan.fitted_size,
        TRANSPARENT,
    )
    pixels: list[tuple[int, int, int, int]] = []
    for red, green, blue, alpha in fitted.get_flattened_data():
        if alpha >= ALPHA_THRESHOLD:
            pixels.append((red, green, blue, 255))
        else:
            pixels.append(TRANSPARENT)
    normalized_fitted.putdata(pixels)

    frame = Image.new("RGBA", (CELL_WIDTH, CELL_HEIGHT), TRANSPARENT)
    frame.paste(normalized_fitted, fit_plan.paste_position)
    opaque_pixels = sum(
        1
        for value in frame.getchannel("A").get_flattened_data()
        if value == 255
    )
    if opaque_pixels < MIN_OPAQUE_PIXELS:
        raise PlayerCatFeedingAtlasError(
            f"Logical frame {logical_index:02d} contains only "
            f"{opaque_pixels} opaque pixels after normalization; expected "
            f"at least {MIN_OPAQUE_PIXELS}."
        )
    return frame


def extract_frames(
    source: Image.Image,
) -> tuple[list[Image.Image], tuple[FitPlan, ...], int]:
    frames: list[Image.Image] = []
    fit_plans: dict[tuple[int, int], FitPlan] = {}
    removed_artifact_pixels = 0

    for logical_index in range(FRAME_COUNT):
        source_column = logical_index % SOURCE_COLUMNS
        source_row = logical_index // SOURCE_COLUMNS
        left = grid_boundary(source.width, source_column, SOURCE_COLUMNS)
        top = grid_boundary(source.height, source_row, SOURCE_ROWS)
        right = grid_boundary(
            source.width,
            source_column + 1,
            SOURCE_COLUMNS,
        )
        bottom = grid_boundary(
            source.height,
            source_row + 1,
            SOURCE_ROWS,
        )
        left += SOURCE_CELL_INSET
        top += SOURCE_CELL_INSET
        right -= SOURCE_CELL_INSET
        bottom -= SOURCE_CELL_INSET
        if right <= left or bottom <= top:
            raise PlayerCatFeedingAtlasError(
                f"Source cell for logical frame {logical_index:02d} is too "
                f"small for the {SOURCE_CELL_INSET}px separator inset."
            )
        source_cell = source.crop((
            left,
            top,
            right,
            bottom,
        ))
        fit_plan = fit_plans.setdefault(
            source_cell.size,
            make_fit_plan(source_cell.size),
        )
        cleaned, removed = clean_source_cell(
            source_cell,
            logical_index,
        )
        removed_artifact_pixels += removed
        frames.append(
            normalize_frame(cleaned, fit_plan, logical_index)
        )
    ordered_plans = tuple(
        fit_plans[size]
        for size in sorted(fit_plans)
    )
    return frames, ordered_plans, removed_artifact_pixels


def atlas_png_position(logical_index: int) -> tuple[int, int]:
    if logical_index < 0 or logical_index >= FRAME_COUNT:
        raise PlayerCatFeedingAtlasError(
            f"Logical frame index {logical_index} is outside 0..63."
        )
    column = logical_index % SOURCE_COLUMNS
    logical_row = logical_index // SOURCE_COLUMNS
    png_row_from_top = SOURCE_ROWS - 1 - logical_row
    return column * CELL_WIDTH, png_row_from_top * CELL_HEIGHT


def validate_atlas(atlas: Image.Image, frames: list[Image.Image]) -> None:
    if atlas.mode != "RGBA" or atlas.size != (ATLAS_WIDTH, ATLAS_HEIGHT):
        raise PlayerCatFeedingAtlasError(
            f"Atlas must be 1024x768 RGBA, got "
            f"{atlas.width}x{atlas.height} {atlas.mode!r}."
        )
    alpha_values = set(
        atlas.getchannel("A").get_flattened_data()
    )
    invalid_alpha = sorted(alpha_values - {0, 255})
    if invalid_alpha:
        raise PlayerCatFeedingAtlasError(
            "Atlas contains non-binary alpha values: "
            + ", ".join(str(value) for value in invalid_alpha[:8])
        )
    if len(frames) != FRAME_COUNT:
        raise PlayerCatFeedingAtlasError(
            f"Atlas validation expected 64 frames, got {len(frames)}."
        )

    for logical_index, expected in enumerate(frames):
        left, top = atlas_png_position(logical_index)
        actual = atlas.crop((
            left,
            top,
            left + CELL_WIDTH,
            top + CELL_HEIGHT,
        ))
        if actual.tobytes() != expected.tobytes():
            raise PlayerCatFeedingAtlasError(
                f"Runtime cell for logical frame {logical_index:02d} "
                "differs from its normalized source."
            )


def build_atlas(frames: list[Image.Image]) -> Image.Image:
    atlas = Image.new(
        "RGBA",
        (ATLAS_WIDTH, ATLAS_HEIGHT),
        TRANSPARENT,
    )
    for logical_index, frame in enumerate(frames):
        atlas.paste(frame, atlas_png_position(logical_index))
    validate_atlas(atlas, frames)
    return atlas


def validate_output_path(source: Path, output: Path) -> None:
    try:
        if source.resolve() == output.resolve():
            raise PlayerCatFeedingAtlasError(
                "Output path must not overwrite the approved source sheet."
            )
    except OSError as exc:
        raise PlayerCatFeedingAtlasError(
            f"Cannot resolve source/output paths: {exc}"
        ) from exc
    if output.exists() and not output.is_file():
        raise PlayerCatFeedingAtlasError(
            f"Output path exists and is not a file: {output}"
        )


def write_png_atomic(image: Image.Image, destination: Path) -> str:
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        raise PlayerCatFeedingAtlasError(
            f"Cannot create output directory {destination.parent}: {exc}"
        ) from exc

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
            reopened.load()
            if (
                reopened.format != "PNG"
                or reopened.mode != "RGBA"
                or reopened.size != image.size
                or reopened.tobytes() != image.tobytes()
            ):
                raise PlayerCatFeedingAtlasError(
                    "Temporary atlas failed exact RGBA PNG round-trip "
                    "validation."
                )
        digest = hashlib.sha256(temporary.read_bytes()).hexdigest().upper()
        os.replace(temporary, destination)
        temporary = None
        return digest
    except PlayerCatFeedingAtlasError:
        raise
    except OSError as exc:
        raise PlayerCatFeedingAtlasError(
            f"Cannot write atlas atomically to {destination}: {exc}"
        ) from exc
    finally:
        if temporary is not None:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                pass


def run(args: argparse.Namespace) -> None:
    validate_static_contract()
    validate_output_path(args.source, args.output)
    source = load_source(args.source)
    frames, fit_plans, removed_artifact_pixels = extract_frames(source)
    atlas = build_atlas(frames)
    pixel_hash = hashlib.sha256(atlas.tobytes()).hexdigest().upper()

    print(
        f"Validated {args.source} as an 8x8 top-left row-major RGBA sheet "
        f"({source.width}x{source.height})."
    )
    print(
        f"Source separator inset={SOURCE_CELL_INSET}px; removed tiny "
        f"artifact pixels={removed_artifact_pixels}."
    )
    plan_summary = ", ".join(
        f"{plan.source_cell_size[0]}x{plan.source_cell_size[1]}->"
        f"{plan.fitted_size[0]}x{plan.fitted_size[1]}@"
        f"{plan.paste_position}"
        for plan in fit_plans
    )
    print(
        f"Nearest contain plans: {plan_summary}; Unity bottom-origin "
        f"hip={HIP_ANCHOR}."
    )
    print(
        "Runtime layout: 8x8 cells at 128x96; logical frames 0..7 are "
        "in the lower PNG row and logical rows advance upward."
    )
    print(f"Atlas pixel SHA256={pixel_hash}")

    if args.validate_only:
        print("Validation passed; no files were written (--validate-only).")
        return

    file_hash = write_png_atomic(atlas, args.output)
    print(
        f"Wrote {args.output} ({ATLAS_WIDTH}x{ATLAS_HEIGHT}) "
        f"SHA256={file_hash}"
    )


def main() -> int:
    args = parse_args()
    try:
        run(args)
    except PlayerCatFeedingAtlasError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
