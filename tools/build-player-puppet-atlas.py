#!/usr/bin/env python3
"""Build the layered eight-direction player atlases from the locked turntable.

The script is intentionally deterministic:

* existing non-transparent pixels in PlayerDirectionalAtlas.png are preserved;
* only the head/face pixels lost by the original chroma-key pass are restored;
* every visible source pixel is assigned to a body or jointed limb layer;
* the nine neutral layers composite back to the corrected reference frame.
* facial variants preserve the complete body layer and only recolor explicit,
  direction-specific eye, brow and mouth pixel whitelists.

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
DEFAULT_EXPRESSIONS = (
    ROOT / "Assets" / "Resources" / "Player" /
    "PlayerDirectionalBodyExpressionsAtlas.png"
)

TURN_TABLE_SHA256 = (
    "EC51D909A4D950C39C9B2309AAAF3BCC8B19CDE171A6E0EE0F8D5EC31FB3F70F"
)

FRAME_WIDTH = 64
FRAME_HEIGHT = 96
DIRECTION_COUNT = 8
PART_COUNT = 9
EXPRESSION_COUNT = 5
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

NEUTRAL_EXPRESSION = 0
HALF_BLINK_EXPRESSION = 1
CLOSED_BLINK_EXPRESSION = 2
WATCHFUL_EXPRESSION = 3
TENSE_EXPRESSION = 4

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

HEAD_REPAIR_DIRECTIONS = frozenset((0, 1, 2, 3, 5, 6, 7))
EXPRESSION_FACE_DIRECTIONS = frozenset((0, 1, 2, 6, 7))
EXPECTED_SCAN_REPAIRS = (50, 53, 55, 13, 0, 13, 46, 55)
EXPECTED_EXPLICIT_REPAIRS = (0, 12, 0, 0, 0, 1, 0, 12)

# Extra turntable-authored head pixels outside the original y=12..24 facial
# scan. The front diagonals need their neck/jaw pixels, FrontLeft needs two
# hair pixels and BackLeft needs one dark inner-head pixel outside the
# skin-row span.
TURNED_HEAD_REPAIR_PIXELS = (
    (),
    (
        (31, 25), (32, 25), (33, 25),
        (30, 26), (31, 26), (32, 26),
        (30, 27), (31, 27),
        (28, 28), (29, 28), (30, 28), (31, 28),
    ),
    (),
    (),
    (),
    ((34, 16),),
    (),
    (
        (27, 16), (27, 17),
        (30, 25), (31, 25),
        (31, 26), (32, 26),
        (31, 27), (32, 27), (33, 27), (34, 27),
        (33, 28), (34, 28),
    ),
)

# Coordinates use local frame PNG space (origin at the top-left). Each tuple
# is destination x/y followed by an authored nearby skin sample x/y. The
# lists are intentionally direction-specific: the source artwork is
# asymmetric and no expression frame is ever mirrored.
BLINK_PIXELS = (
    (
        (28, 18, 28, 19),
        (29, 18, 29, 19),
        (30, 18, 30, 19),
        (33, 18, 33, 19),
        (34, 18, 34, 19),
        (35, 18, 35, 19),
    ),
    (
        (28, 18, 28, 19),
        (29, 18, 29, 19),
        (30, 18, 30, 19),
        (33, 18, 33, 19),
        (34, 18, 34, 19),
        (35, 18, 35, 19),
    ),
    (
        (29, 17, 29, 18),
        (30, 17, 29, 18),
        (30, 18, 29, 18),
        (31, 18, 31, 19),
    ),
    (),
    (),
    (),
    (
        (35, 17, 35, 18),
        (36, 17, 36, 18),
        (37, 17, 37, 18),
        (36, 18, 36, 19),
    ),
    (
        (29, 18, 29, 19),
        (30, 18, 31, 18),
        (36, 18, 36, 19),
        (37, 18, 37, 19),
    ),
)

# A fully closed eye retains a short, high-contrast lower lid crease.
CLOSED_LID_PIXELS = (
    ((29, 19, 29, 18), (34, 19, 34, 18)),
    ((29, 19, 29, 18), (34, 19, 34, 18)),
    ((30, 19, 30, 17),),
    (),
    (),
    (),
    ((36, 18, 36, 17),),
    ((30, 19, 30, 18), (37, 19, 37, 18)),
)

# Watchful darkens the pupils and lifts two authored brow pixels.
WATCHFUL_EYE_DARKEN_PIXELS = (
    ((28, 18), (29, 18), (33, 18), (34, 18)),
    ((28, 18), (29, 18), (33, 18), (34, 18)),
    ((30, 17), (30, 18)),
    (),
    (),
    (),
    ((36, 17), (37, 17)),
    ((30, 18), (37, 18)),
)

WATCHFUL_BROW_LIFT_PIXELS = (
    ((28, 17, 28, 19), (34, 17, 34, 19)),
    ((28, 17, 28, 19), (34, 17, 34, 19)),
    ((29, 16, 29, 18), (31, 16, 31, 18)),
    (),
    (),
    (),
    ((35, 16, 35, 18), (37, 16, 37, 18)),
    ((30, 17, 30, 19), (37, 17, 37, 19)),
)

# Tense narrows the eyes and adds compact brow/mouth contrast without
# changing the head silhouette.
TENSE_EYE_SOFTEN_PIXELS = (
    (
        (28, 18, 28, 19),
        (29, 18, 29, 19),
        (33, 18, 33, 19),
        (34, 18, 34, 19),
    ),
    (
        (28, 18, 28, 19),
        (29, 18, 29, 19),
        (33, 18, 33, 19),
        (34, 18, 34, 19),
    ),
    ((30, 17, 29, 18), (30, 18, 29, 18)),
    (),
    (),
    (),
    ((36, 17, 36, 18), (37, 17, 37, 18)),
    ((30, 18, 31, 18), (37, 18, 37, 19)),
)

TENSE_BROW_DARKEN_PIXELS = (
    ((28, 17), (29, 17), (33, 17), (34, 17)),
    ((27, 17), (28, 17), (33, 17), (34, 17)),
    ((29, 16), (30, 16)),
    (),
    (),
    (),
    ((36, 16), (37, 16)),
    ((30, 17), (31, 17), (36, 17), (37, 17)),
)

TENSE_MOUTH_DARKEN_PIXELS = (
    ((30, 22), (31, 22), (32, 22)),
    ((29, 22), (30, 22), (31, 22)),
    ((29, 22), (30, 22)),
    (),
    (),
    (),
    ((34, 22), (35, 22)),
    ((32, 22), (33, 22), (34, 22)),
)

# Half-open bounds around all facial pixels the expression builder is
# allowed to touch. Rear views deliberately have empty masks.
FACE_EDIT_BOUNDS = (
    (27, 16, 36, 24),
    (26, 16, 36, 24),
    (27, 15, 33, 24),
    (0, 0, 0, 0),
    (0, 0, 0, 0),
    (0, 0, 0, 0),
    (31, 15, 39, 24),
    (28, 16, 40, 24),
)

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
    parser.add_argument(
        "--expressions",
        type=Path,
        default=DEFAULT_EXPRESSIONS,
    )
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
    scan_counts: list[int] = []
    explicit_counts: list[int] = []

    for direction, (source_box, target_width, target_x) in enumerate(
        SOURCE_SPECS
    ):
        tile = turntable.crop(source_box).resize(
            (target_width, 84),
            Image.Resampling.NEAREST,
        ).convert("RGB")

        if direction not in HEAD_REPAIR_DIRECTIONS:
            counts.append(0)
            scan_counts.append(0)
            explicit_counts.append(0)
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

        scan_counts.append(repaired)
        explicit_repaired = 0
        for local_x, atlas_y in TURNED_HEAD_REPAIR_PIXELS[direction]:
            tile_x = local_x - target_x
            tile_y = atlas_y - 8
            if (
                tile_x < 0
                or tile_x >= target_width
                or tile_y < 0
                or tile_y >= 84
            ):
                raise RuntimeError(
                    f"Turned-head repair {(local_x, atlas_y)} is "
                    f"outside direction {direction}'s source tile."
                )

            atlas_x = direction * FRAME_WIDTH + local_x
            if corrected.getpixel((atlas_x, atlas_y))[3] != 0:
                continue

            red, green, blue = tile_pixels[tile_x, tile_y]
            distance_from_key = max(
                abs(red - 255),
                abs(green),
                abs(blue - 255),
            )
            if distance_from_key <= 32:
                raise RuntimeError(
                    f"Turned-head repair {(local_x, atlas_y)} in "
                    f"direction {direction} points at the chroma key."
                )

            corrected.putpixel(
                (atlas_x, atlas_y),
                (red, green, blue, 255),
            )
            changed_indices.add(
                atlas_y * corrected.width + atlas_x
            )
            explicit_repaired += 1

        explicit_counts.append(explicit_repaired)
        counts.append(repaired + explicit_repaired)

    counts_tuple = tuple(counts)
    for label, actual_counts, expected_counts in (
        ("scan", scan_counts, EXPECTED_SCAN_REPAIRS),
        ("explicit", explicit_counts, EXPECTED_EXPLICIT_REPAIRS),
    ):
        for direction, (actual, expected) in enumerate(zip(
            actual_counts,
            expected_counts,
        )):
            if actual not in (0, expected):
                raise RuntimeError(
                    f"Unexpected {label} head repair count for direction "
                    f"{direction}: {actual}; expected {expected} or 0 "
                    "for an already-corrected frame."
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


def blend_rgb_toward(
    source: tuple[int, int, int, int],
    target: tuple[int, int, int, int],
    numerator: int,
    denominator: int,
) -> tuple[int, int, int, int]:
    if source[3] != 255 or target[3] != 255:
        raise RuntimeError(
            "Blink pixels and their skin samples must be opaque."
        )

    return (
        (
            source[0] * (denominator - numerator)
            + target[0] * numerator
            + denominator // 2
        ) // denominator,
        (
            source[1] * (denominator - numerator)
            + target[1] * numerator
            + denominator // 2
        ) // denominator,
        (
            source[2] * (denominator - numerator)
            + target[2] * numerator
            + denominator // 2
        ) // denominator,
        255,
    )


def build_blink_variant(
    neutral_body: Image.Image,
    direction: int,
    numerator: int,
    denominator: int,
) -> Image.Image:
    variant = neutral_body.copy().convert("RGBA")
    x_min, y_min, x_max, y_max = FACE_EDIT_BOUNDS[direction]

    for x, y, skin_x, skin_y in BLINK_PIXELS[direction]:
        if not (x_min <= x < x_max and y_min <= y < y_max):
            raise RuntimeError(
                f"Direction {direction} blink pixel {(x, y)} is outside "
                "its explicit face edit mask."
            )

        source = neutral_body.getpixel((x, y))
        skin = neutral_body.getpixel((skin_x, skin_y))
        variant.putpixel(
            (x, y),
            blend_rgb_toward(
                source,
                skin,
                numerator,
                denominator,
            ),
        )

    return variant


def darken_rgb_half(
    source: tuple[int, int, int, int],
) -> tuple[int, int, int, int]:
    if source[3] != 255:
        raise RuntimeError(
            "Facial contrast pixels must be opaque."
        )

    return (
        (source[0] + 1) // 2,
        (source[1] + 1) // 2,
        (source[2] + 1) // 2,
        255,
    )


def validate_face_edit(
    direction: int,
    x: int,
    y: int,
    label: str,
) -> None:
    x_min, y_min, x_max, y_max = FACE_EDIT_BOUNDS[direction]
    if not (x_min <= x < x_max and y_min <= y < y_max):
        raise RuntimeError(
            f"Direction {direction} {label} pixel {(x, y)} is outside "
            "its explicit face edit mask."
        )


def build_closed_blink_variant(
    neutral_body: Image.Image,
    direction: int,
) -> Image.Image:
    variant = build_blink_variant(
        neutral_body,
        direction,
        1,
        1,
    )
    for x, y, dark_x, dark_y in CLOSED_LID_PIXELS[direction]:
        validate_face_edit(direction, x, y, "closed-lid")
        dark = neutral_body.getpixel((dark_x, dark_y))
        if dark[3] != 255:
            raise RuntimeError(
                "Closed-lid source pixels must be opaque."
            )

        variant.putpixel((x, y), dark)

    return variant


def build_watchful_variant(
    neutral_body: Image.Image,
    direction: int,
) -> Image.Image:
    variant = neutral_body.copy().convert("RGBA")

    for x, y in WATCHFUL_EYE_DARKEN_PIXELS[direction]:
        validate_face_edit(direction, x, y, "watchful-eye")
        variant.putpixel(
            (x, y),
            darken_rgb_half(neutral_body.getpixel((x, y))),
        )

    for x, y, skin_x, skin_y in (
        WATCHFUL_BROW_LIFT_PIXELS[direction]
    ):
        validate_face_edit(direction, x, y, "watchful-brow")
        variant.putpixel(
            (x, y),
            blend_rgb_toward(
                neutral_body.getpixel((x, y)),
                neutral_body.getpixel((skin_x, skin_y)),
                1,
                2,
            ),
        )

    return variant


def build_tense_variant(
    neutral_body: Image.Image,
    direction: int,
) -> Image.Image:
    variant = neutral_body.copy().convert("RGBA")

    for x, y, skin_x, skin_y in (
        TENSE_EYE_SOFTEN_PIXELS[direction]
    ):
        validate_face_edit(direction, x, y, "tense-eye")
        variant.putpixel(
            (x, y),
            blend_rgb_toward(
                neutral_body.getpixel((x, y)),
                neutral_body.getpixel((skin_x, skin_y)),
                3,
                4,
            ),
        )

    contrast_pixels = (
        TENSE_BROW_DARKEN_PIXELS[direction] +
        TENSE_MOUTH_DARKEN_PIXELS[direction]
    )
    for x, y in contrast_pixels:
        validate_face_edit(direction, x, y, "tense-contrast")
        variant.putpixel(
            (x, y),
            darken_rgb_half(neutral_body.getpixel((x, y))),
        )

    return variant


def get_expected_expression_changes(
    direction: int,
    expression: int,
) -> set[tuple[int, int]]:
    if expression == HALF_BLINK_EXPRESSION:
        return {
            (x, y)
            for x, y, _, _ in BLINK_PIXELS[direction]
        }
    if expression == CLOSED_BLINK_EXPRESSION:
        return {
            (x, y)
            for x, y, _, _ in (
                BLINK_PIXELS[direction] +
                CLOSED_LID_PIXELS[direction]
            )
        }
    if expression == WATCHFUL_EXPRESSION:
        return set(WATCHFUL_EYE_DARKEN_PIXELS[direction]) | {
            (x, y)
            for x, y, _, _ in (
                WATCHFUL_BROW_LIFT_PIXELS[direction]
            )
        }
    if expression == TENSE_EXPRESSION:
        return {
            (x, y)
            for x, y, _, _ in (
                TENSE_EYE_SOFTEN_PIXELS[direction]
            )
        } | set(TENSE_BROW_DARKEN_PIXELS[direction]) | set(
            TENSE_MOUTH_DARKEN_PIXELS[direction]
        )

    return set()


def assert_expression_contract(
    variants_by_direction: Sequence[Sequence[Image.Image]],
) -> None:
    for direction, variants in enumerate(variants_by_direction):
        neutral = variants[NEUTRAL_EXPRESSION]
        neutral_pixels = list(neutral.get_flattened_data())

        for expression in (
            HALF_BLINK_EXPRESSION,
            CLOSED_BLINK_EXPRESSION,
            WATCHFUL_EXPRESSION,
            TENSE_EXPRESSION,
        ):
            variant = variants[expression]
            changed = set()

            for y in range(FRAME_HEIGHT):
                for x in range(FRAME_WIDTH):
                    original = neutral.getpixel((x, y))
                    facial = variant.getpixel((x, y))
                    if original[3] != facial[3]:
                        raise RuntimeError(
                            f"Expression {expression}, direction "
                            f"{direction} changed alpha at {(x, y)}."
                        )
                    if original != facial:
                        changed.add((x, y))

            expected_changes = get_expected_expression_changes(
                direction,
                expression,
            )
            if changed != expected_changes:
                raise RuntimeError(
                    f"Expression {expression}, direction {direction} "
                    f"changed {changed}; expected {expected_changes}."
                )

            if direction not in EXPRESSION_FACE_DIRECTIONS:
                if list(variant.get_flattened_data()) != neutral_pixels:
                    raise RuntimeError(
                        f"Rear expression direction {direction} differs "
                        "from its neutral body."
                    )
            elif not changed:
                raise RuntimeError(
                    f"Visible direction {direction}, expression "
                    f"{expression} has no facial changes."
                )

        if direction in EXPRESSION_FACE_DIRECTIONS:
            flattened_variants = {
                variant.tobytes()
                for variant in variants
            }
            if len(flattened_variants) != EXPRESSION_COUNT:
                raise RuntimeError(
                    f"Direction {direction} facial states are not "
                    "pairwise distinct."
                )


def build_body_expressions_atlas(
    parts_atlas: Image.Image,
) -> Image.Image:
    if parts_atlas.size != (
        FRAME_WIDTH * DIRECTION_COUNT,
        FRAME_HEIGHT * PART_COUNT,
    ):
        raise RuntimeError(
            "Parts atlas has an unexpected layout while building "
            "expressions."
        )

    body_png_y = (PART_COUNT - 1 - BODY) * FRAME_HEIGHT
    variants_by_direction = []
    for direction in range(DIRECTION_COUNT):
        neutral = parts_atlas.crop((
            direction * FRAME_WIDTH,
            body_png_y,
            (direction + 1) * FRAME_WIDTH,
            body_png_y + FRAME_HEIGHT,
        )).convert("RGBA")
        half_blink = build_blink_variant(
            neutral,
            direction,
            3,
            4,
        )
        closed_blink = build_closed_blink_variant(
            neutral,
            direction,
        )
        watchful = build_watchful_variant(neutral, direction)
        tense = build_tense_variant(neutral, direction)
        variants_by_direction.append((
            neutral,
            half_blink,
            closed_blink,
            watchful,
            tense,
        ))

    assert_expression_contract(variants_by_direction)

    atlas = Image.new(
        "RGBA",
        (
            FRAME_WIDTH * DIRECTION_COUNT,
            FRAME_HEIGHT * EXPRESSION_COUNT,
        ),
        TRANSPARENT,
    )
    for direction, variants in enumerate(variants_by_direction):
        for expression, frame in enumerate(variants):
            # Expression zero occupies texture y=0 in Unity, matching the
            # existing part-index convention.
            atlas_y = (
                EXPRESSION_COUNT - 1 - expression
            ) * FRAME_HEIGHT
            atlas.alpha_composite(
                frame,
                (direction * FRAME_WIDTH, atlas_y),
            )

    return atlas


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
    expressions = build_body_expressions_atlas(parts)
    write_png_atomic(corrected, args.reference)
    write_png_atomic(parts, args.parts)
    write_png_atomic(expressions, args.expressions)

    print(f"face repairs: {repairs} (total {sum(repairs)})")
    for direction, direction_counts in enumerate(counts):
        print(f"direction {direction}: {direction_counts}")
    print(f"reference: {args.reference}")
    print(f"parts: {args.parts}")
    print(f"expressions: {args.expressions}")


if __name__ == "__main__":
    main()
