#!/usr/bin/env python3
"""Validate and pack the authored stairwell-cat feeding atlas.

The builder consumes one approved 4x4 contact sheet in top-left row-major
logical order.  The source may use a transparent background or the locked
magenta chroma key.  Sixteen binary-alpha 64x64 frames are packed into an 8x2
runtime atlas in the cat library's established top-first row order.

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
    / "Stairwell"
    / "Cat"
    / "Feeding"
    / "StairwellCatFeedingSource-alpha.png"
)
DEFAULT_OUTPUT = (
    ROOT
    / "Assets"
    / "Resources"
    / "Stairwell"
    / "Cat"
    / "StairwellCatFeedingAtlas.png"
)

SOURCE_COLUMNS = 4
SOURCE_ROWS = 4
RUNTIME_COLUMNS = 8
RUNTIME_ROWS = 2
FRAME_COUNT = SOURCE_COLUMNS * SOURCE_ROWS
FRAME_SIZE = 64
ATLAS_SIZE = (
    RUNTIME_COLUMNS * FRAME_SIZE,
    RUNTIME_ROWS * FRAME_SIZE,
)
ALPHA_THRESHOLD = 128
MIN_SOURCE_SIZE = 512
MAX_SOURCE_DIMENSION = 16384
MAX_SOURCE_PIXELS = 67_108_864
MIN_OPAQUE_PIXELS = 16
TRANSPARENT = (0, 0, 0, 0)
SOURCE_CELL_INSET = 3
FRAME_MARGIN = 2
BOTTOM_MARGIN = 4
MIN_COMPONENT_AREA_RATIO = 0.0001

# The raw image-generation sheet may retain this locked chroma family.  An
# already keyed RGBA sheet is also valid.  Foreground art must not use colors
# inside this range; the README keeps that authoring restriction explicit.
MAGENTA_RED_MIN = 200
MAGENTA_BLUE_MIN = 200
MAGENTA_GREEN_MAX = 100
MAGENTA_DOMINANCE_MIN = 100
MAGENTA_RED_BLUE_DELTA_MAX = 64


class CatFeedingAtlasError(RuntimeError):
    """A concise, user-actionable source or output contract failure."""


@dataclass(frozen=True)
class SourceFrame:
    image: Image.Image
    combined_bounds: tuple[int, int, int, int]
    body_center_x: float


def grid_boundary(total: int, index: int, count: int) -> int:
    if index < 0 or index > count:
        raise CatFeedingAtlasError(
            f"Grid boundary {index} is outside 0..{count}."
        )
    # Integer half-up rounding is deterministic and also supports generated
    # sheets such as 1254x1254 that do not divide evenly by four.
    return (index * total + count // 2) // count


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source",
        type=Path,
        default=DEFAULT_SOURCE,
        help=(
            "Approved 4x4 magenta/alpha source sheet "
            f"(default: {DEFAULT_SOURCE})."
        ),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
        help=(
            "Destination for the 512x128 RGBA atlas "
            f"(default: {DEFAULT_OUTPUT})."
        ),
    )
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="Validate and build in memory without writing any file.",
    )
    return parser.parse_args()


def is_magenta_key(red: int, green: int, blue: int) -> bool:
    return (
        red >= MAGENTA_RED_MIN
        and blue >= MAGENTA_BLUE_MIN
        and green <= MAGENTA_GREEN_MAX
        and min(red, blue) - green >= MAGENTA_DOMINANCE_MIN
        and abs(red - blue) <= MAGENTA_RED_BLUE_DELTA_MAX
    )


def load_source(path: Path) -> Image.Image:
    if not path.exists():
        raise CatFeedingAtlasError(
            "Missing approved stairwell-cat feeding source sheet: "
            f"{path}"
        )
    if not path.is_file():
        raise CatFeedingAtlasError(f"Source path is not a file: {path}")

    try:
        with Image.open(path) as opened:
            if opened.format != "PNG" or opened.mode not in ("RGB", "RGBA"):
                raise CatFeedingAtlasError(
                    f"{path.name} must be an RGB or RGBA PNG, got "
                    f"{opened.format or 'unknown'} {opened.mode!r}."
                )
            width, height = opened.size
            if width < MIN_SOURCE_SIZE or height < MIN_SOURCE_SIZE:
                raise CatFeedingAtlasError(
                    f"{path.name} is only {width}x{height}; expected an "
                    f"approved 4x4 sheet of at least "
                    f"{MIN_SOURCE_SIZE}x{MIN_SOURCE_SIZE}."
                )
            if width > MAX_SOURCE_DIMENSION or height > MAX_SOURCE_DIMENSION:
                raise CatFeedingAtlasError(
                    f"{path.name} exceeds the {MAX_SOURCE_DIMENSION}px "
                    "per-axis safety limit."
                )
            if width * height > MAX_SOURCE_PIXELS:
                raise CatFeedingAtlasError(
                    f"{path.name} exceeds the {MAX_SOURCE_PIXELS}-pixel "
                    "safety limit."
                )
            cell_width = width / SOURCE_COLUMNS
            cell_height = height / SOURCE_ROWS
            aspect_delta = abs(cell_width - cell_height) / max(
                cell_width,
                cell_height,
            )
            if aspect_delta > 0.10:
                raise CatFeedingAtlasError(
                    f"{path.name} uses {cell_width}x{cell_height} cells; "
                    "feeding-cat source cells must be square or within 10% "
                    "of square."
                )
            opened.load()
            return opened.convert("RGBA")
    except CatFeedingAtlasError:
        raise
    except OSError as exc:
        raise CatFeedingAtlasError(f"Cannot read {path}: {exc}") from exc


def normalize_source_alpha(source: Image.Image) -> tuple[Image.Image, int]:
    normalized = Image.new("RGBA", source.size, TRANSPARENT)
    pixels: list[tuple[int, int, int, int]] = []
    keyed_pixels = 0
    for red, green, blue, alpha in source.get_flattened_data():
        chroma = is_magenta_key(red, green, blue)
        if alpha < ALPHA_THRESHOLD or chroma:
            pixels.append(TRANSPARENT)
            if chroma and alpha >= ALPHA_THRESHOLD:
                keyed_pixels += 1
        else:
            pixels.append((red, green, blue, 255))
    normalized.putdata(pixels)
    return normalized, keyed_pixels


def find_opaque_components(
    image: Image.Image,
) -> list[set[tuple[int, int]]]:
    alpha = image.getchannel("A")
    opaque = {
        (x, y)
        for y in range(image.height)
        for x in range(image.width)
        if alpha.getpixel((x, y)) == 255
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


def component_bounds(
    component: set[tuple[int, int]],
) -> tuple[int, int, int, int]:
    xs = [point[0] for point in component]
    ys = [point[1] for point in component]
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def body_center_x(
    body_component: set[tuple[int, int]],
) -> float:
    left, top, right, bottom = component_bounds(body_component)
    height = bottom - top
    sample_top = top + round(height * 0.18)
    sample_bottom = top + round(height * 0.72)
    samples = sorted(
        x
        for x, y in body_component
        if sample_top <= y < sample_bottom
    )
    if not samples:
        return (left + right) * 0.5
    return float(samples[len(samples) // 2])


def clean_source_cell(
    cell: Image.Image,
    logical_index: int,
) -> tuple[SourceFrame, int]:
    alpha_min, alpha_max = cell.getchannel("A").getextrema()
    if alpha_max == 0:
        raise CatFeedingAtlasError(
            f"Source cell for logical frame {logical_index:02d} is empty "
            "after alpha/chroma normalization."
        )
    if alpha_min != 0:
        raise CatFeedingAtlasError(
            f"Source cell for logical frame {logical_index:02d} has no "
            "transparent/keyed background."
        )

    components = find_opaque_components(cell)
    minimum_area = max(
        4,
        round(cell.width * cell.height * MIN_COMPONENT_AREA_RATIO),
    )
    retained = [
        component
        for component in components
        if len(component) >= minimum_area
    ]
    if not retained:
        raise CatFeedingAtlasError(
            f"Source cell for logical frame {logical_index:02d} has no "
            f"component at or above the {minimum_area}-pixel artifact "
            "threshold."
        )

    cleaned = Image.new("RGBA", cell.size, TRANSPARENT)
    source_pixels = cell.load()
    cleaned_pixels = cleaned.load()
    for component in retained:
        for x, y in component:
            cleaned_pixels[x, y] = source_pixels[x, y]

    bounds = cleaned.getchannel("A").getbbox()
    if bounds is None:
        raise CatFeedingAtlasError(
            f"Logical frame {logical_index:02d} became empty during "
            "component cleanup."
        )
    body_component = max(retained, key=len)
    removed_pixels = sum(
        len(component)
        for component in components
        if len(component) < minimum_area
    )
    return SourceFrame(
        image=cleaned,
        combined_bounds=bounds,
        body_center_x=body_center_x(body_component),
    ), removed_pixels


def normalize_frame(
    source_frame: SourceFrame,
    scale: float,
    logical_index: int,
) -> Image.Image:
    left, top, right, bottom = source_frame.combined_bounds
    cropped = source_frame.image.crop((left, top, right, bottom))
    fitted_width = max(1, round(cropped.width * scale))
    fitted_height = max(1, round(cropped.height * scale))
    fitted = cropped.resize(
        (fitted_width, fitted_height),
        resample=Image.Resampling.NEAREST,
    )

    frame = Image.new("RGBA", (FRAME_SIZE, FRAME_SIZE), TRANSPARENT)
    body_center_in_crop = (
        source_frame.body_center_x - left
    ) * scale
    paste_x = round((FRAME_SIZE * 0.5) - body_center_in_crop)
    paste_x = max(
        FRAME_MARGIN,
        min(
            paste_x,
            FRAME_SIZE - FRAME_MARGIN - fitted_width,
        ),
    )
    paste_y = FRAME_SIZE - BOTTOM_MARGIN - fitted_height
    if (
        paste_x < 0
        or paste_y < 0
        or paste_x + fitted_width > FRAME_SIZE
        or paste_y + fitted_height > FRAME_SIZE
    ):
        raise CatFeedingAtlasError(
            f"Logical frame {logical_index:02d} cannot fit in 64x64 after "
            f"normalization: size={fitted_width}x{fitted_height}, "
            f"paste=({paste_x},{paste_y})."
        )
    frame.paste(fitted, (paste_x, paste_y))
    opaque_pixels = sum(
        1
        for value in frame.getchannel("A").get_flattened_data()
        if value == 255
    )
    if opaque_pixels < MIN_OPAQUE_PIXELS:
        raise CatFeedingAtlasError(
            f"Logical frame {logical_index:02d} contains only "
            f"{opaque_pixels} opaque pixels after normalization; expected "
            f"at least {MIN_OPAQUE_PIXELS}."
        )
    return frame


def extract_frames(
    source: Image.Image,
) -> tuple[list[Image.Image], int, int, float]:
    normalized, keyed_pixels = normalize_source_alpha(source)
    source_frames: list[SourceFrame] = []
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
            raise CatFeedingAtlasError(
                f"Source cell for logical frame {logical_index:02d} is too "
                f"small for the {SOURCE_CELL_INSET}px separator inset."
            )
        source_frame, removed = clean_source_cell(
            normalized.crop((left, top, right, bottom)),
            logical_index,
        )
        source_frames.append(source_frame)
        removed_artifact_pixels += removed

    maximum_width = max(
        frame.combined_bounds[2] - frame.combined_bounds[0]
        for frame in source_frames
    )
    maximum_height = max(
        frame.combined_bounds[3] - frame.combined_bounds[1]
        for frame in source_frames
    )
    scale = min(
        (FRAME_SIZE - 2 * FRAME_MARGIN) / maximum_width,
        (
            FRAME_SIZE - FRAME_MARGIN - BOTTOM_MARGIN
        ) / maximum_height,
    )
    frames = [
        normalize_frame(source_frame, scale, logical_index)
        for logical_index, source_frame in enumerate(source_frames)
    ]
    return frames, keyed_pixels, removed_artifact_pixels, scale


def atlas_png_position(logical_index: int) -> tuple[int, int]:
    if logical_index < 0 or logical_index >= FRAME_COUNT:
        raise CatFeedingAtlasError(
            f"Logical frame index {logical_index} is outside 0..15."
        )
    column = logical_index % RUNTIME_COLUMNS
    row_from_top = logical_index // RUNTIME_COLUMNS
    return column * FRAME_SIZE, row_from_top * FRAME_SIZE


def validate_atlas(atlas: Image.Image, frames: list[Image.Image]) -> None:
    if atlas.mode != "RGBA" or atlas.size != ATLAS_SIZE:
        raise CatFeedingAtlasError(
            f"Atlas must be 512x128 RGBA, got "
            f"{atlas.width}x{atlas.height} {atlas.mode!r}."
        )
    alpha_values = set(
        atlas.getchannel("A").get_flattened_data()
    )
    invalid_alpha = sorted(alpha_values - {0, 255})
    if invalid_alpha:
        raise CatFeedingAtlasError(
            "Atlas contains non-binary alpha values: "
            + ", ".join(str(value) for value in invalid_alpha[:8])
        )
    if len(frames) != FRAME_COUNT:
        raise CatFeedingAtlasError(
            f"Atlas validation expected 16 frames, got {len(frames)}."
        )

    for logical_index, expected in enumerate(frames):
        left, top = atlas_png_position(logical_index)
        actual = atlas.crop((
            left,
            top,
            left + FRAME_SIZE,
            top + FRAME_SIZE,
        ))
        if actual.tobytes() != expected.tobytes():
            raise CatFeedingAtlasError(
                f"Runtime cell for logical frame {logical_index:02d} "
                "differs from its normalized source."
            )


def build_atlas(frames: list[Image.Image]) -> Image.Image:
    atlas = Image.new("RGBA", ATLAS_SIZE, TRANSPARENT)
    for logical_index, frame in enumerate(frames):
        atlas.paste(frame, atlas_png_position(logical_index))
    validate_atlas(atlas, frames)
    return atlas


def validate_output_path(source: Path, output: Path) -> None:
    try:
        if source.resolve() == output.resolve():
            raise CatFeedingAtlasError(
                "Output path must not overwrite the approved source sheet."
            )
    except OSError as exc:
        raise CatFeedingAtlasError(
            f"Cannot resolve source/output paths: {exc}"
        ) from exc
    if output.exists() and not output.is_file():
        raise CatFeedingAtlasError(
            f"Output path exists and is not a file: {output}"
        )


def write_png_atomic(image: Image.Image, destination: Path) -> str:
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        raise CatFeedingAtlasError(
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
                raise CatFeedingAtlasError(
                    "Temporary atlas failed exact RGBA PNG round-trip "
                    "validation."
                )
        digest = hashlib.sha256(temporary.read_bytes()).hexdigest().upper()
        os.replace(temporary, destination)
        temporary = None
        return digest
    except CatFeedingAtlasError:
        raise
    except OSError as exc:
        raise CatFeedingAtlasError(
            f"Cannot write atlas atomically to {destination}: {exc}"
        ) from exc
    finally:
        if temporary is not None:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                pass


def run(args: argparse.Namespace) -> None:
    if FRAME_COUNT != RUNTIME_COLUMNS * RUNTIME_ROWS:
        raise CatFeedingAtlasError(
            "Internal layout must map all 16 frames into the 8x2 atlas."
        )
    validate_output_path(args.source, args.output)
    source = load_source(args.source)
    frames, keyed_pixels, removed_artifact_pixels, scale = extract_frames(
        source
    )
    atlas = build_atlas(frames)
    pixel_hash = hashlib.sha256(atlas.tobytes()).hexdigest().upper()

    print(
        f"Validated {args.source} as a 4x4 top-left row-major sheet "
        f"({source.width}x{source.height}); keyed opaque magenta pixels="
        f"{keyed_pixels}, removed tiny artifact pixels="
        f"{removed_artifact_pixels}."
    )
    print(
        f"Source separator inset={SOURCE_CELL_INSET}px; shared "
        f"combined-bounds scale={scale:.6f}; retained components "
        "are grounded with the largest component used as the cat-body "
        "horizontal anchor."
    )
    print(
        "Runtime layout: 8x2 cells at 64x64; logical frames 0..7 are "
        "in the upper PNG row and 8..15 are in the lower row (cat "
        "library top-first order)."
    )
    print(f"Atlas pixel SHA256={pixel_hash}")

    if args.validate_only:
        print("Validation passed; no files were written (--validate-only).")
        return

    file_hash = write_png_atomic(atlas, args.output)
    print(
        f"Wrote {args.output} ({ATLAS_SIZE[0]}x{ATLAS_SIZE[1]}) "
        f"SHA256={file_hash}"
    )


def main() -> int:
    args = parse_args()
    try:
        run(args)
    except CatFeedingAtlasError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
