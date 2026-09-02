#!/usr/bin/env python3
"""Pure variant contracts and geometry recipes for the cashier builder."""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class CashierVariant:
    key: str
    generator_version: str
    design_id: str
    display_name: str
    output_stem: str
    total_height: float
    signature_anatomy: tuple[str, ...]
    head_target_pivot: tuple[float, float, float]
    head_scale: tuple[float, float, float]
    head_role: str
    neck_design: str
    neck_segment_count: int
    neck_rest_length: float
    neck_segment_height: float
    neck_max_stretch_ratio: float
    eye_design: str


# Historical Watcher mechanism. These values remain public because the main
# builder owns the pivot objects and the segment metadata/runtime contract.
NECK_BASE = (0.0, -0.015, 1.335)
NECK_SEGMENT_COUNT = 5
NECK_SEGMENT_HEIGHT = 0.11
NECK_REST_LENGTH = NECK_SEGMENT_COUNT * NECK_SEGMENT_HEIGHT
NECK_MAX_STRETCH_RATIO = 32.7
NECK_RADIUS_BOTTOM = 0.075
NECK_RADIUS_TOP = 0.065
NECK_RING_EXTRA_RADIUS = 0.012
NECK_PIVOT_NAMES = tuple(
    f"PIVOT_Neck.{index + 1:02d}" for index in range(NECK_SEGMENT_COUNT)
)


# Ordinary fixed-neck silhouette. The shoulder yoke is combined into the
# existing torso renderer, leaving one short neck mesh and no mechanism parts.
NORMAL_SHOULDER_TOP = 1.405
NORMAL_NECK_BASE = (0.0, -0.010, 1.398)
NORMAL_NECK_TOP = (0.0, -0.025, 1.525)
NORMAL_NECK_REST_LENGTH = sum(
    (NORMAL_NECK_TOP[axis] - NORMAL_NECK_BASE[axis]) ** 2
    for axis in range(3)
) ** 0.5
NORMAL_NECK_RADIUS_BOTTOM = 0.075
NORMAL_NECK_RADIUS_TOP = 0.062


# Every face feature is authored in the old Watcher's absolute source space.
# Scale the whole group about its crown so the skin, eyes and facial details
# stay registered. The target crown also owns exact overall model height.
HEAD_BASE_CENTER = (0.006, -0.028, 1.960)
HEAD_BASE_RADII = (0.085, 0.078, 0.090)
HEAD_SOURCE_PIVOT = (
    HEAD_BASE_CENTER[0],
    HEAD_BASE_CENTER[1],
    HEAD_BASE_CENTER[2] + HEAD_BASE_RADII[2],
)


WATCHER_VARIANT = CashierVariant(
    key="watcher",
    generator_version="2.2.0",
    design_id="watcher_cashier_v1",
    display_name="Watcher Cashier",
    output_stem="SupermarketWatcherCashier3D",
    total_height=2.05,
    signature_anatomy=("stretch_neck", "undersized_head"),
    head_target_pivot=HEAD_SOURCE_PIVOT,
    head_scale=(1.12, 1.12, 1.12),
    head_role="undersized_watcher_head",
    neck_design="segmented_periscope_v1",
    neck_segment_count=NECK_SEGMENT_COUNT,
    neck_rest_length=NECK_REST_LENGTH,
    neck_segment_height=NECK_SEGMENT_HEIGHT,
    neck_max_stretch_ratio=NECK_MAX_STRETCH_RATIO,
    eye_design="wide_watcher_asymmetric",
)


NORMAL_VARIANT = CashierVariant(
    key="normal",
    generator_version="1.0.0",
    design_id="supermarket_cashier_v1",
    display_name="Supermarket Cashier",
    output_stem="SupermarketCashier3D",
    total_height=1.75,
    signature_anatomy=("wide_asymmetric_eyes",),
    head_target_pivot=(
        HEAD_SOURCE_PIVOT[0],
        HEAD_SOURCE_PIVOT[1],
        1.75,
    ),
    head_scale=(1.05, 1.05, 1.28),
    head_role="human_head",
    neck_design="fixed_human_v1",
    neck_segment_count=0,
    neck_rest_length=NORMAL_NECK_REST_LENGTH,
    neck_segment_height=0.0,
    neck_max_stretch_ratio=1.0,
    eye_design="wide_watcher_asymmetric",
)


CASHIER_VARIANTS = {
    NORMAL_VARIANT.key: NORMAL_VARIANT,
    WATCHER_VARIANT.key: WATCHER_VARIANT,
}


def head_point(variant: CashierVariant, point):
    """Map a source-space head position through one variant's crown."""

    return tuple(
        variant.head_target_pivot[axis]
        + (point[axis] - HEAD_SOURCE_PIVOT[axis])
        * variant.head_scale[axis]
        for axis in range(3)
    )


def head_size(variant: CashierVariant, size):
    """Scale a source-space head radius or extent with the whole face."""

    return tuple(
        size[axis] * variant.head_scale[axis] for axis in range(3)
    )


def head_center(variant: CashierVariant):
    return head_point(variant, HEAD_BASE_CENTER)


def head_radii(variant: CashierVariant):
    return head_size(variant, HEAD_BASE_RADII)


def neck_segment_radii(index: int) -> tuple[float, float]:
    span = NECK_RADIUS_BOTTOM - NECK_RADIUS_TOP
    lower = NECK_RADIUS_BOTTOM - span * index / NECK_SEGMENT_COUNT
    upper = NECK_RADIUS_BOTTOM - span * (index + 1) / NECK_SEGMENT_COUNT
    return lower, upper


def make_normal_torso_geometry(geometry, torso):
    """Add a shoulder yoke to the ordinary variant's existing torso mesh."""

    return geometry.combine_geometry(
        torso,
        geometry.make_tapered_box(
            (0, -0.004, 1.275),
            (0, -0.010, NORMAL_SHOULDER_TOP),
            (0.340, 0.198, 0),
            (0.155, 0.135, 0),
        ),
    )


def make_fixed_neck_geometry(geometry):
    """Build the ordinary variant's single rigid, short neck mesh."""

    return geometry.make_frustum_between(
        NORMAL_NECK_BASE,
        NORMAL_NECK_TOP,
        NORMAL_NECK_RADIUS_BOTTOM,
        NORMAL_NECK_RADIUS_TOP,
        12,
    )


def make_collar_geometry(geometry, variant: CashierVariant):
    """Keep the uniform collar aligned to the selected neck base."""

    if variant.key == NORMAL_VARIANT.key:
        axis = NORMAL_NECK_BASE
        bottom = 1.387
        top = 1.421
    else:
        axis = NECK_BASE
        bottom = 1.318
        top = 1.352
    return geometry.make_frustum_between(
        (axis[0], axis[1], bottom),
        (axis[0], axis[1], top),
        0.084,
        0.062,
        12,
    )
