#!/usr/bin/env python3
"""Build the runtime stairwell-cat atlas from the approved source sheet."""

from collections import deque
from pathlib import Path

from PIL import Image


COLUMNS = 8
IDLE_ROWS = 3
ROWS = 4
SOURCE_ROW_ORDER = (2, 1, 0)
FRAME_SIZE = 64
BOTTOM_MARGIN = 4
ALPHA_THRESHOLD = 96

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = (
    REPOSITORY_ROOT
    / "ArtSource"
    / "Stairwell"
    / "Cat"
    / "StairwellCatSource-alpha.png"
)
GROOMING_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "ArtSource"
    / "Stairwell"
    / "Cat"
    / "StairwellCatGroomingSource-alpha.png"
)
OUTPUT_PATH = (
    REPOSITORY_ROOT
    / "Assets"
    / "Resources"
    / "Stairwell"
    / "Cat"
    / "StairwellCatAtlas.png"
)


def _grid_boundary(length: int, index: int, count: int) -> int:
    return round(length * index / count)


def _opaque_bounds(image: Image.Image) -> tuple[int, int, int, int]:
    alpha = image.getchannel("A")
    binary = alpha.point(
        lambda value: 255 if value >= ALPHA_THRESHOLD else 0
    )
    bounds = binary.getbbox()
    if bounds is None:
        raise ValueError("A source cell contains no opaque cat pixels.")
    return bounds


def _keep_largest_opaque_component(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    width, height = image.size
    opaque = {
        (x, y)
        for y in range(height)
        for x in range(width)
        if alpha.getpixel((x, y)) >= ALPHA_THRESHOLD
    }
    largest: set[tuple[int, int]] = set()
    while opaque:
        start = opaque.pop()
        component = {start}
        queue = deque([start])
        while queue:
            x, y = queue.popleft()
            for neighbour in (
                (x - 1, y),
                (x + 1, y),
                (x, y - 1),
                (x, y + 1),
            ):
                if neighbour not in opaque:
                    continue
                opaque.remove(neighbour)
                component.add(neighbour)
                queue.append(neighbour)

        if len(component) > len(largest):
            largest = component

    if not largest:
        raise ValueError("A source cell contains no connected cat pixels.")

    cleaned = image.copy()
    cleaned_alpha = Image.new("L", image.size, 0)
    cleaned_alpha_pixels = cleaned_alpha.load()
    source_alpha_pixels = alpha.load()
    for x, y in largest:
        cleaned_alpha_pixels[x, y] = source_alpha_pixels[x, y]
    cleaned.putalpha(cleaned_alpha)
    return cleaned


def _body_center_x(
    image: Image.Image,
    bounds: tuple[int, int, int, int],
) -> float:
    left, top, right, bottom = bounds
    height = bottom - top
    sample_top = top + round(height * 0.18)
    sample_bottom = top + round(height * 0.78)
    alpha = image.getchannel("A")
    samples: list[int] = []
    for y in range(sample_top, sample_bottom):
        for x in range(left, right):
            if alpha.getpixel((x, y)) >= ALPHA_THRESHOLD:
                samples.append(x)

    if not samples:
        return (left + right) * 0.5

    samples.sort()
    return float(samples[len(samples) // 2])


def _extract_source_cells(
    source: Image.Image,
) -> list[tuple[Image.Image, tuple[int, int, int, int], float]]:
    cells = []
    for source_row in SOURCE_ROW_ORDER:
        y_min = _grid_boundary(source.height, source_row, IDLE_ROWS)
        y_max = _grid_boundary(
            source.height,
            source_row + 1,
            IDLE_ROWS,
        )
        for column in range(COLUMNS):
            x_min = _grid_boundary(source.width, column, COLUMNS)
            x_max = _grid_boundary(source.width, column + 1, COLUMNS)
            cell = _keep_largest_opaque_component(
                source.crop((x_min, y_min, x_max, y_max))
            )
            bounds = _opaque_bounds(cell)
            cells.append((cell, bounds, _body_center_x(cell, bounds)))
    return cells


def _extract_grooming_cells(
    source: Image.Image,
) -> list[tuple[Image.Image, tuple[int, int, int, int], float]]:
    cells = []
    for column in range(COLUMNS):
        x_min = _grid_boundary(source.width, column, COLUMNS)
        x_max = _grid_boundary(
            source.width,
            column + 1,
            COLUMNS,
        )
        cell = _keep_largest_opaque_component(
            source.crop((x_min, 0, x_max, source.height))
        )
        bounds = _opaque_bounds(cell)
        cells.append((cell, bounds, _body_center_x(cell, bounds)))
    return cells


def _normalize_cell(
    source: Image.Image,
    bounds: tuple[int, int, int, int],
    body_center_x: float,
    scale: float,
) -> Image.Image:
    left, top, right, bottom = bounds
    cropped = source.crop(bounds)
    alpha = cropped.getchannel("A").point(
        lambda value: 255 if value >= ALPHA_THRESHOLD else 0
    )
    cropped.putalpha(alpha)

    width = max(1, round(cropped.width * scale))
    height = max(1, round(cropped.height * scale))
    resized = cropped.resize(
        (width, height),
        resample=Image.Resampling.NEAREST,
    )

    frame = Image.new(
        "RGBA",
        (FRAME_SIZE, FRAME_SIZE),
        (0, 0, 0, 0),
    )
    body_center_in_crop = (body_center_x - left) * scale
    x = round((FRAME_SIZE * 0.5) - body_center_in_crop)
    horizontal_margin = 2
    x = max(
        horizontal_margin,
        min(
            x,
            FRAME_SIZE - horizontal_margin - width,
        ),
    )
    target_bottom = FRAME_SIZE - 1 - BOTTOM_MARGIN
    y = target_bottom - height + 1
    frame.alpha_composite(resized, (x, y))
    return frame


def build() -> None:
    if not SOURCE_PATH.is_file():
        raise FileNotFoundError(
            f"Missing stairwell-cat source: {SOURCE_PATH}"
        )
    if not GROOMING_SOURCE_PATH.is_file():
        raise FileNotFoundError(
            "Missing stairwell-cat grooming source: "
            f"{GROOMING_SOURCE_PATH}"
        )

    source = Image.open(SOURCE_PATH).convert("RGBA")
    grooming_source = Image.open(
        GROOMING_SOURCE_PATH
    ).convert("RGBA")
    cells = (
        _extract_source_cells(source) +
        _extract_grooming_cells(grooming_source)
    )
    maximum_height = max(
        bounds[3] - bounds[1] for _, bounds, _ in cells
    )
    maximum_width = max(
        bounds[2] - bounds[0] for _, bounds, _ in cells
    )
    scale = min(
        (FRAME_SIZE - BOTTOM_MARGIN - 4) / maximum_height,
        (FRAME_SIZE - 4) / maximum_width,
    )

    atlas = Image.new(
        "RGBA",
        (COLUMNS * FRAME_SIZE, ROWS * FRAME_SIZE),
        (0, 0, 0, 0),
    )
    for index, (cell, bounds, body_center_x) in enumerate(cells):
        frame = _normalize_cell(
            cell,
            bounds,
            body_center_x,
            scale,
        )
        column = index % COLUMNS
        row = index // COLUMNS
        atlas.alpha_composite(
            frame,
            (column * FRAME_SIZE, row * FRAME_SIZE),
        )

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(OUTPUT_PATH, optimize=True)
    opaque_pixels = sum(
        1
        for alpha in atlas.getchannel("A").get_flattened_data()
        if alpha > 0
    )
    print(
        f"Wrote {OUTPUT_PATH.relative_to(REPOSITORY_ROOT)} "
        f"({atlas.width}x{atlas.height}, "
        f"{opaque_pixels} opaque pixels)."
    )


if __name__ == "__main__":
    build()
