#!/usr/bin/env python3
"""Build the layered eight-direction player atlas from the locked turntable.

The script is intentionally deterministic:

* existing non-transparent pixels in PlayerDirectionalAtlas.png are preserved;
* only the face pixels lost by the original chroma-key pass are restored;
* every visible source pixel is assigned to a body or jointed limb layer;
* the nine neutral layers composite back to the corrected reference frame.

Pillow is the only dependency.
"""

from __future__ import annotations

import argparse
import hashlib
import math
import os
from pathlib import Path
from typing import Iterable, Sequence

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TURNTABLE = (
    ROOT / "ArtSource" / "Player" / "PlayerDirectionalTurntable.png"
)
DEFAULT_REFERENCE = (
    ROOT / "Assets" / "Resources" / "Player" /
    "PlayerDirectionalAtlas.png"
)
DEFAULT_PARTS = (
    ROOT / "Assets" / "Resources" / "Player" /
    "PlayerDirectionalPartsAtlas.png"
)

TURN_TABLE_SHA256 = (
    "EC51D909A4D950C39C9B2309AAAF3BCC8B19CDE171A6E0EE0F8D5EC31FB3F70F"
)

FRAME_WIDTH = 64
FRAME_HEIGHT = 96
DIRECTION_COUNT = 8
PART_COUNT = 9
TRANSPARENT = (0, 0, 0, 0)

BODY = 0
LEFT_UPPER_ARM = 1
LEFT_LOWER_ARM = 2
RIGHT_UPPER_ARM = 3
RIGHT_LOWER_ARM = 4
LEFT_UPPER_LEG = 5
LEFT_LOWER_LEG = 6
RIGHT_UPPER_LEG = 7
RIGHT_LOWER_LEG = 8

# Global source crop, target width and target x. All target images are 84 px
# tall and are placed at y=8, keeping the shared four-pixel foot margin.
SOURCE_SPECS = (
    ((140, 20, 324, 491), 33, 15),
    ((527, 20, 698, 491), 30, 17),
    ((894, 20, 1002, 491), 19, 22),
    ((1216, 20, 1381, 491), 29, 17),
    ((144, 524, 318, 987), 32, 16),
    ((503, 523, 654, 987), 27, 18),
    ((875, 523, 979, 986), 19, 22),
    ((1191, 523, 1356, 987), 30, 17),
)

VISIBLE_FACE_DIRECTIONS = frozenset((0, 1, 2, 6, 7))
EXPECTED_FACE_REPAIRS = (50, 53, 55, 0, 0, 0, 46, 55)

# Coordinates use PNG space (origin at the top-left). Left/right are the two
# stable image-space puppet slots. Authored pixels retain the character's
# physical asymmetry in every view and are never mirrored at runtime.
POSES = (
    # left arm S/E/W, right arm S/E/W, left leg H/K/A, right leg H/K/A
    (((22, 30), (19, 46), (19, 59)),
     ((42, 30), (44, 46), (44, 59)),
     ((28, 56), (27, 75), (25, 87)),
     ((36, 56), (37, 75), (39, 87))),
    (((25, 30), (21, 46), (21, 59)),
     ((39, 30), (43, 46), (43, 59)),
     ((28, 56), (27, 75), (25, 87)),
     ((36, 56), (37, 75), (38, 87))),
    (((32, 30), (32, 46), (33, 59)),
     ((35, 30), (36, 46), (35, 59)),
     ((30, 56), (30, 75), (29, 87)),
     ((34, 56), (35, 75), (35, 87))),
    (((24, 30), (21, 46), (21, 59)),
     ((40, 30), (44, 46), (43, 59)),
     ((28, 56), (27, 75), (25, 87)),
     ((36, 56), (37, 75), (38, 87))),
    (((23, 30), (20, 46), (20, 59)),
     ((41, 30), (44, 46), (44, 59)),
     ((28, 56), (27, 75), (24, 87)),
     ((36, 56), (37, 75), (39, 87))),
    (((25, 30), (22, 46), (22, 59)),
     ((39, 30), (42, 46), (42, 59)),
     ((28, 56), (27, 75), (25, 87)),
     ((36, 56), (37, 75), (38, 87))),
    (((29, 30), (29, 46), (29, 59)),
     ((32, 30), (32, 46), (31, 59)),
     ((30, 56), (29, 75), (28, 87)),
     ((34, 56), (34, 75), (35, 87))),
    (((24, 30), (20, 46), (21, 59)),
     ((40, 30), (43, 46), (43, 59)),
     ((28, 56), (27, 75), (25, 87)),
     ((36, 56), (37, 75), (39, 87))),
)

BODY_CENTERS = (32, 32, 32, 32, 32, 32, 31, 32)
BODY_HALF_WIDTHS = (12.0, 10.5, 6.5, 10.5, 12.0, 10.5, 6.5, 10.5)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--turntable", type=Path, default=DEFAULT_TURNTABLE)
    parser.add_argument("--reference", type=Path, default=DEFAULT_REFERENCE)
    parser.add_argument("--parts", type=Path, default=DEFAULT_PARTS)
    return parser.parse_args()


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def write_png_atomic(image: Image.Image, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.name + ".tmp")
    image.save(temporary, format="PNG", optimize=False)
    os.replace(temporary, destination)


def restore_face_pixels(
    turntable: Image.Image,
    reference: Image.Image,
) -> tuple[Image.Image, tuple[int, ...]]:
    corrected = reference.copy().convert("RGBA")
    before = list(corrected.get_flattened_data())
    changed_indices: set[int] = set()
    counts: list[int] = []

    for direction, (source_box, target_width, target_x) in enumerate(
        SOURCE_SPECS
    ):
        tile = turntable.crop(source_box).resize(
            (target_width, 84),
            Image.Resampling.NEAREST,
        ).convert("RGB")

        if direction not in VISIBLE_FACE_DIRECTIONS:
            counts.append(0)
            continue

        tile_pixels = tile.load()
        skin_rows: list[list[int]] = [[] for _ in range(84)]
        for y in range(84):
            atlas_y = y + 8
            if atlas_y < 12 or atlas_y > 24:
                continue

            for x in range(target_width):
                red, green, blue = tile_pixels[x, y]
                if (
                    red >= 50
                    and green >= 25
                    and blue >= 20
                    and red >= green + 5
                    and green >= blue - 12
                    and red >= blue + 8
                ):
                    skin_rows[y].append(x)

        repaired = 0
        for y, skin_xs in enumerate(skin_rows):
            if not skin_xs:
                continue

            for x in range(min(skin_xs), max(skin_xs) + 1):
                red, green, blue = tile_pixels[x, y]
                distance_from_key = max(
                    abs(red - 255),
                    abs(green),
                    abs(blue - 255),
                )
                dominance = min(red, blue) - green
                keylike = distance_from_key <= 32 or dominance >= 16
                t = max(
                    0.0,
                    min(1.0, (distance_from_key - 48) / 48.0),
                )
                soft_alpha = 255.0 * t * t * (3.0 - 2.0 * t)
                dominance_ratio = max(
                    0.0,
                    min(1.0, dominance / max(1, 255 - green)),
                )
                dominance_alpha = 255.0 * (1.0 - dominance_ratio)
                subject = (
                    not keylike
                    or min(soft_alpha, dominance_alpha) >= 128.0
                )

                atlas_x = direction * FRAME_WIDTH + target_x + x
                atlas_y = y + 8
                if not subject or corrected.getpixel(
                    (atlas_x, atlas_y)
                )[3] != 0:
                    continue

                corrected.putpixel(
                    (atlas_x, atlas_y),
                    (red, green, blue, 255),
                )
                changed_indices.add(
                    atlas_y * corrected.width + atlas_x
                )
                repaired += 1

        counts.append(repaired)

    counts_tuple = tuple(counts)
    if counts_tuple not in (
        EXPECTED_FACE_REPAIRS,
        (0,) * DIRECTION_COUNT,
    ):
        raise RuntimeError(
            "Unexpected face repair counts: "
            f"{counts_tuple}; expected {EXPECTED_FACE_REPAIRS} or an "
            "already-corrected atlas."
        )

    after = list(corrected.get_flattened_data())
    for index, (old_pixel, new_pixel) in enumerate(zip(before, after)):
        if index in changed_indices:
            if old_pixel[3] != 0 or new_pixel[3] != 255:
                raise RuntimeError("Face repair changed an invalid pixel.")
            continue
        if old_pixel != new_pixel:
            raise RuntimeError(
                "Face repair modified a pixel outside the repair mask."
            )

    return corrected, counts_tuple


def distance_to_segment(
    point: tuple[float, float],
    start: tuple[float, float],
    end: tuple[float, float],
) -> float:
    px, py = point
    ax, ay = start
    bx, by = end
    delta_x = bx - ax
    delta_y = by - ay
    denominator = delta_x * delta_x + delta_y * delta_y
    if denominator <= 0.0001:
        return math.hypot(px - ax, py - ay)

    t = (
        (px - ax) * delta_x + (py - ay) * delta_y
    ) / denominator
    t = max(0.0, min(1.0, t))
    nearest_x = ax + t * delta_x
    nearest_y = ay + t * delta_y
    return math.hypot(px - nearest_x, py - nearest_y)


def body_score(
    direction: int,
    x: int,
    y: int,
) -> float:
    center = BODY_CENTERS[direction]
    half_width = BODY_HALF_WIDTHS[direction]
    head_radius_x = 9.5 if direction not in (2, 6) else 7.0
    head = math.sqrt(
        ((x - center) / head_radius_x) ** 2
        + ((y - 18.0) / 12.0) ** 2
    )
    torso = distance_to_segment(
        (x, y),
        (center, 29.0),
        (center, 57.0),
    ) / half_width
    pelvis = distance_to_segment(
        (x, y),
        (center - half_width * 0.55, 58.0),
        (center + half_width * 0.55, 58.0),
    ) / 6.0
    return min(head, torso, pelvis)


def classify_pixel(
    direction: int,
    x: int,
    y: int,
) -> int:
    if y <= 27:
        return BODY

    left_arm, right_arm, left_leg, right_leg = POSES[direction]

    # Hands and wrapped forearms can overlap the coat in diagonal/profile
    # frames. Give the closest lower-arm capsule ownership before evaluating
    # the torso so those pixels do not remain baked into the body layer.
    if 42 <= y <= 64:
        lower_arms = (
            (
                distance_to_segment(
                    (x, y), left_arm[1], left_arm[2]
                ),
                LEFT_LOWER_ARM,
            ),
            (
                distance_to_segment(
                    (x, y), right_arm[1], right_arm[2]
                ),
                RIGHT_LOWER_ARM,
            ),
        )
        closest_lower_arm = min(lower_arms, key=lambda item: item[0])
        if closest_lower_arm[0] <= 5.8:
            return closest_lower_arm[1]

    # In the two strict profiles both arms project onto the torso. Extract the
    # visible capsule and later duplicate it behind the near limb at rest.
    if direction in (2, 6) and 28 <= y <= 48:
        upper_arms = (
            (
                distance_to_segment(
                    (x, y), left_arm[0], left_arm[1]
                ),
                LEFT_UPPER_ARM,
            ),
            (
                distance_to_segment(
                    (x, y), right_arm[0], right_arm[1]
                ),
                RIGHT_UPPER_ARM,
            ),
        )
        closest_upper_arm = min(upper_arms, key=lambda item: item[0])
        if closest_upper_arm[0] <= 5.2:
            return closest_upper_arm[1]

    candidates: list[tuple[float, int]] = []
    if y <= 64:
        arm_bias = 0.82 if direction in (2, 6) else 0.92
        candidates.extend((
            (
                distance_to_segment(
                    (x, y), left_arm[0], left_arm[1]
                ) / 5.2 * arm_bias,
                LEFT_UPPER_ARM,
            ),
            (
                distance_to_segment(
                    (x, y), left_arm[1], left_arm[2]
                ) / 4.8 * arm_bias,
                LEFT_LOWER_ARM,
            ),
            (
                distance_to_segment(
                    (x, y), right_arm[0], right_arm[1]
                ) / 5.2 * arm_bias,
                RIGHT_UPPER_ARM,
            ),
            (
                distance_to_segment(
                    (x, y), right_arm[1], right_arm[2]
                ) / 4.8 * arm_bias,
                RIGHT_LOWER_ARM,
            ),
        ))

    if y >= 52:
        candidates.extend((
            (
                distance_to_segment(
                    (x, y), left_leg[0], left_leg[1]
                ) / 6.5,
                LEFT_UPPER_LEG,
            ),
            (
                distance_to_segment(
                    (x, y), left_leg[1], left_leg[2]
                ) / 5.8,
                LEFT_LOWER_LEG,
            ),
            (
                distance_to_segment(
                    (x, y), right_leg[0], right_leg[1]
                ) / 6.5,
                RIGHT_UPPER_LEG,
            ),
            (
                distance_to_segment(
                    (x, y), right_leg[1], right_leg[2]
                ) / 5.8,
                RIGHT_LOWER_LEG,
            ),
        ))

    if y <= 64:
        candidates.append((body_score(direction, x, y), BODY))

    if not candidates:
        return BODY
    return min(candidates, key=lambda item: (item[0], item[1]))[1]


def nearest_body_pixel(
    frame: Image.Image,
    labels: Sequence[int],
    x: int,
    y: int,
) -> tuple[int, int, int, int]:
    for radius in range(1, 18):
        candidates = (
            (x - radius, y),
            (x + radius, y),
            (x, y - radius),
            (x, y + radius),
        )
        for sample_x, sample_y in candidates:
            if (
                sample_x < 0
                or sample_x >= FRAME_WIDTH
                or sample_y < 0
                or sample_y >= FRAME_HEIGHT
            ):
                continue
            index = sample_y * FRAME_WIDTH + sample_x
            pixel = frame.getpixel((sample_x, sample_y))
            looks_like_exposed_skin = (
                y >= 38 and sum(pixel[:3]) >= 250
            )
            if (
                labels[index] == BODY
                and pixel[3] == 255
                and not looks_like_exposed_skin
            ):
                return pixel

    preferred_y = 41 if y < 56 else 58
    best: tuple[float, tuple[int, int, int, int]] | None = None
    for sample_y in range(28, 64):
        for sample_x in range(FRAME_WIDTH):
            index = sample_y * FRAME_WIDTH + sample_x
            pixel = frame.getpixel((sample_x, sample_y))
            if (
                labels[index] != BODY
                or pixel[3] != 255
                or sum(pixel[:3]) >= 250
            ):
                continue
            distance = (
                abs(sample_x - x)
                + abs(sample_y - preferred_y) * 1.5
            )
            if best is None or distance < best[0]:
                best = (distance, pixel)
    if best is not None:
        return best[1]
    return frame.getpixel((x, y))


def copy_original_pixels(
    source: Image.Image,
    target: Image.Image,
    coordinates: Iterable[tuple[int, int]],
) -> None:
    for x, y in coordinates:
        pixel = source.getpixel((x, y))
        if pixel[3] == 255:
            target.putpixel((x, y), pixel)


def build_part_layers(
    frame: Image.Image,
    direction: int,
) -> list[Image.Image]:
    frame = frame.convert("RGBA")
    labels = [BODY] * (FRAME_WIDTH * FRAME_HEIGHT)
    layers = [
        Image.new("RGBA", (FRAME_WIDTH, FRAME_HEIGHT), TRANSPARENT)
        for _ in range(PART_COUNT)
    ]

    for y in range(FRAME_HEIGHT):
        for x in range(FRAME_WIDTH):
            pixel = frame.getpixel((x, y))
            if pixel[3] == 0:
                continue
            part = classify_pixel(direction, x, y)
            labels[y * FRAME_WIDTH + x] = part
            layers[part].putpixel((x, y), pixel)

    # Paint a restrained torso/pelvis underlay beneath moving joints. The
    # original limb pixels remain on top, so the neutral composite is exact.
    center = BODY_CENTERS[direction]
    half_width = BODY_HALF_WIDTHS[direction]
    for y in range(28, 64):
        core_width = half_width * (0.72 if y < 56 else 0.62)
        for x in range(FRAME_WIDTH):
            pixel = frame.getpixel((x, y))
            if (
                pixel[3] == 0
                or abs(x - center) > core_width
                or labels[y * FRAME_WIDTH + x] == BODY
            ):
                continue
            layers[BODY].putpixel(
                (x, y),
                nearest_body_pixel(frame, labels, x, y),
            )

    left_arm, right_arm, left_leg, right_leg = POSES[direction]
    joints = (
        (left_arm[1], LEFT_UPPER_ARM, LEFT_LOWER_ARM),
        (right_arm[1], RIGHT_UPPER_ARM, RIGHT_LOWER_ARM),
        (left_leg[1], LEFT_UPPER_LEG, LEFT_LOWER_LEG),
        (right_leg[1], RIGHT_UPPER_LEG, RIGHT_LOWER_LEG),
    )
    for joint, upper_part, lower_part in joints:
        joint_pixels = []
        for y in range(FRAME_HEIGHT):
            for x in range(FRAME_WIDTH):
                if distance_to_segment((x, y), joint, joint) <= 2.25:
                    joint_pixels.append((x, y))
        copy_original_pixels(frame, layers[upper_part], joint_pixels)
        copy_original_pixels(frame, layers[lower_part], joint_pixels)

    # A strict side view contains only one flattened silhouette. Give both
    # hidden and visible joints the same authored source pixels at rest; they
    # overlap exactly when idle and separate only during the gait.
    if direction in (2, 6):
        for first, second in (
            (LEFT_UPPER_ARM, RIGHT_UPPER_ARM),
            (LEFT_LOWER_ARM, RIGHT_LOWER_ARM),
            (LEFT_UPPER_LEG, RIGHT_UPPER_LEG),
            (LEFT_LOWER_LEG, RIGHT_LOWER_LEG),
        ):
            coordinates = []
            for y in range(FRAME_HEIGHT):
                for x in range(FRAME_WIDTH):
                    if (
                        layers[first].getpixel((x, y))[3] == 255
                        or layers[second].getpixel((x, y))[3] == 255
                    ):
                        coordinates.append((x, y))
            copy_original_pixels(frame, layers[first], coordinates)
            copy_original_pixels(frame, layers[second], coordinates)

    return layers


def assert_neutral_composite(
    reference: Image.Image,
    layers_by_direction: Sequence[Sequence[Image.Image]],
) -> None:
    for direction, layers in enumerate(layers_by_direction):
        expected = reference.crop((
            direction * FRAME_WIDTH,
            0,
            (direction + 1) * FRAME_WIDTH,
            FRAME_HEIGHT,
        )).convert("RGBA")
        actual = Image.new(
            "RGBA",
            (FRAME_WIDTH, FRAME_HEIGHT),
            TRANSPARENT,
        )
        for layer in layers:
            actual.alpha_composite(layer)
        if (
            list(actual.get_flattened_data())
            != list(expected.get_flattened_data())
        ):
            raise RuntimeError(
                f"Neutral part composite differs in direction {direction}."
            )


def build_parts_atlas(
    corrected_reference: Image.Image,
) -> tuple[Image.Image, tuple[tuple[int, ...], ...]]:
    layers_by_direction = []
    counts_by_direction = []
    for direction in range(DIRECTION_COUNT):
        frame = corrected_reference.crop((
            direction * FRAME_WIDTH,
            0,
            (direction + 1) * FRAME_WIDTH,
            FRAME_HEIGHT,
        ))
        layers = build_part_layers(frame, direction)
        layers_by_direction.append(layers)
        counts = tuple(
            sum(
                1
                for pixel in layer.get_flattened_data()
                if pixel[3] == 255
            )
            for layer in layers
        )
        if any(count == 0 for count in counts):
            raise RuntimeError(
                f"Direction {direction} has an empty part: {counts}."
            )
        counts_by_direction.append(counts)

    assert_neutral_composite(corrected_reference, layers_by_direction)

    atlas = Image.new(
        "RGBA",
        (
            FRAME_WIDTH * DIRECTION_COUNT,
            FRAME_HEIGHT * PART_COUNT,
        ),
        TRANSPARENT,
    )
    for direction, layers in enumerate(layers_by_direction):
        for part, layer in enumerate(layers):
            # PNG coordinates grow down. Part zero is stored in the bottom row
            # so its Unity Sprite.Create rect begins at texture y=0.
            atlas_y = (PART_COUNT - 1 - part) * FRAME_HEIGHT
            atlas.alpha_composite(
                layer,
                (direction * FRAME_WIDTH, atlas_y),
            )

    return atlas, tuple(counts_by_direction)


def main() -> None:
    args = parse_args()
    if sha256(args.turntable) != TURN_TABLE_SHA256:
        raise RuntimeError(
            "The locked player turntable does not match its expected SHA256."
        )

    turntable = Image.open(args.turntable).convert("RGB")
    reference = Image.open(args.reference).convert("RGBA")
    if reference.size != (
        FRAME_WIDTH * DIRECTION_COUNT,
        FRAME_HEIGHT,
    ):
        raise RuntimeError(
            f"Reference atlas must be 512x96, got {reference.size}."
        )

    corrected, repairs = restore_face_pixels(turntable, reference)
    parts, counts = build_parts_atlas(corrected)
    write_png_atomic(corrected, args.reference)
    write_png_atomic(parts, args.parts)

    print(f"face repairs: {repairs} (total {sum(repairs)})")
    for direction, direction_counts in enumerate(counts):
        print(f"direction {direction}: {direction_counts}")
    print(f"reference: {args.reference}")
    print(f"parts: {args.parts}")


if __name__ == "__main__":
    main()
