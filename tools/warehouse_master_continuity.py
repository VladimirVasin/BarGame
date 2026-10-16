"""Measure the baked painting across adjoining warehouse gallery modules.

This check samples the actual atlas as the GPU does: decode each colour texel
from sRGB before interpolation. Its coordinates come from the same finite
planner export and packed chart transforms as the model; changing the picture's
resolution cannot hide a per-module field reset.
"""
from __future__ import annotations

import math

import numpy as np


TILE_SIZE = 3.
SAMPLES_PER_EDGE = 101
COLOUR_MEAN_LIMIT = .02
RESPONSE_MEAN_LIMIT = .025


def _platforms(layout):
    templates = layout.get("templates", ())
    if not templates:
        raise RuntimeError("Warehouse continuity requires the exported planner templates")
    platforms = None
    for template in templates:
        current = []
        for module in template["modules"]:
            if module["kind"] != "Platform":
                continue
            position = np.asarray(module["position"], dtype=np.float64)
            yaw = float(module["yaw"])
            if position.shape != (3,) or not np.isfinite(position).all() or not math.isfinite(yaw):
                raise RuntimeError("Warehouse continuity requires finite Platform transforms")
            if abs(yaw) > 1e-6:
                raise RuntimeError("Warehouse continuity must explicitly support rotated Platform modules")
            if min(abs(position[1] - height) for height in (3.2, 6.4)) > 1e-5:
                raise RuntimeError("Warehouse continuity found an unsupported gallery level")
            current.append(position)
        if not current:
            raise RuntimeError("Warehouse continuity found no Platform placements")
        current = np.asarray(current)
        if platforms is None:
            platforms = current
        elif current.shape != platforms.shape or not np.allclose(current, platforms, atol=1e-6, rtol=0):
            raise RuntimeError("Warehouse Platform ordinals must retain their transforms across templates")
    return platforms


def _top_charts(charts, count):
    rows = charts["charts"] if isinstance(charts, dict) else charts
    lookup = {tuple(row["key"]): row for row in rows}
    result = []
    for ordinal in range(count):
        root = "Platform" + ("~" + str(ordinal) if ordinal else "")
        key = (root, "Steel", 2, 1, "regular")
        if key not in lookup:
            raise RuntimeError("Warehouse continuity is missing the top chart for " + root)
        chart = lookup[key]
        for field in ("local_origin_m", "atlas_origin_px", "texels_per_metre"):
            if field not in chart:
                raise RuntimeError("Warehouse continuity requires packed chart field " + field)
        if float(chart["texels_per_metre"]) <= 0:
            raise RuntimeError("Warehouse continuity requires positive physical chart density")
        result.append(chart)
    return result


def _sample(data, size, chart, local_xz, colour):
    # Packed origins are expressed in the colour master's pixel coordinates;
    # the registered response uses the same normalized chart at half resolution.
    origin = np.asarray(chart["atlas_origin_px"], dtype=np.float64)
    local_origin = np.asarray(chart["local_origin_m"], dtype=np.float64)
    density = float(chart["texels_per_metre"])
    master_size = float(chart.get("atlas_size", 4096))
    pixel = (origin + (local_xz - local_origin) * density) * (size / master_size) - .5
    lower = np.floor(pixel).astype(np.int64)
    weight = pixel - lower
    if np.any(lower < 0) or np.any(lower + 1 >= size):
        raise RuntimeError("Warehouse seam sample escaped its bounded atlas")
    x, y = lower[:, 0], lower[:, 1]
    wx, wy = weight[:, 0, None], weight[:, 1, None]
    taps = np.stack((data[y, x], data[y, x + 1],
                     data[y + 1, x], data[y + 1, x + 1]))
    if colour:
        rgb = taps[..., :3]
        taps[..., :3] = np.where(rgb <= .04045, rgb / 12.92,
                                ((rgb + .055) / 1.055) ** 2.4)
    return ((taps[0] * (1 - wx) + taps[1] * wx) * (1 - wy) +
            (taps[2] * (1 - wx) + taps[3] * wx) * wy)


def _stats(values):
    values = np.asarray(values, dtype=np.float64)
    return {"mean": round(float(np.mean(values)), 7),
            "rms": round(float(np.sqrt(np.mean(values ** 2))), 7),
            "max": round(float(np.max(values)), 7)}


def validate_platform_edges(data, size, channel, charts, layout, *, enforce=True,
                            mean_limit=None):
    """Return bounded actual-pixel seam metrics; reject conspicuous colour jumps.

    ``data`` is the undistorted, dilated RGBA bake or PNG normalized to 0..1.
    Colour RGB holds serialized sRGB values; its four taps are decoded before
    interpolation to compare linear RGB with the production GPU semantics.
    For response, only B roughness and A cavity are comparable physical scalars;
    its RG slopes belong to each chart's derivative frame and are not compared.
    ``enforce=False`` retains diagnostics for measuring an older asset bank.
    """
    if channel not in ("colour", "response"):
        raise ValueError("Warehouse continuity channel must be colour or response")
    if data is None or data.shape != (size, size, 4) or not np.isfinite(data).all():
        raise RuntimeError("Warehouse continuity requires a finite RGBA bake of its declared size")
    platforms = _platforms(layout)
    tops = _top_charts(charts, len(platforms))
    colour = channel == "colour"
    channels = slice(0, 3) if channel == "colour" else slice(2, 4)
    limit = float(mean_limit if mean_limit is not None else
                  COLOUR_MEAN_LIMIT if channel == "colour" else RESPONSE_MEAN_LIMIT)
    if not math.isfinite(limit) or limit <= 0:
        raise ValueError("Warehouse continuity requires a positive finite mean limit")
    along = np.linspace(-TILE_SIZE * .5 + .15, TILE_SIZE * .5 - .15, SAMPLES_PER_EDGE)
    differences, inset_differences, interior_variation, edge_rows = [], [], [], []
    level_counts = {}
    for first, first_position in enumerate(platforms):
        for second in range(first + 1, len(platforms)):
            second_position = platforms[second]
            delta = second_position - first_position
            if abs(delta[1]) > 1e-5:
                continue
            horizontal = delta[[0, 2]]
            axis = int(np.argmax(np.abs(horizontal)))
            other_axis = 1 - axis
            if abs(abs(horizontal[axis]) - TILE_SIZE) > 1e-5 or abs(horizontal[other_axis]) > 1e-5:
                continue
            direction = 1 if horizontal[axis] > 0 else -1
            edge = (first_position[[0, 2]] + second_position[[0, 2]]) * .5
            points = np.repeat(edge[None, :], SAMPLES_PER_EDGE, axis=0)
            points[:, other_axis] += along
            first_local = points - first_position[[0, 2]]
            second_local = points - second_position[[0, 2]]
            a = _sample(data, size, tops[first], first_local, colour)[:, channels]
            b = _sample(data, size, tops[second], second_local, colour)[:, channels]
            jump = np.abs(a - b)
            differences.append(jump)
            # Half a native texel inside each module avoids exact boundary
            # dilation while retaining the same broad rust and damp field.
            first_metre = float(tops[first].get("atlas_size", 4096)) / (size * tops[first]["texels_per_metre"])
            second_metre = float(tops[second].get("atlas_size", 4096)) / (size * tops[second]["texels_per_metre"])
            first_local[:, axis] -= direction * first_metre * .5
            second_local[:, axis] += direction * second_metre * .5
            inset_a = _sample(data, size, tops[first], first_local, colour)[:, channels]
            inset_b = _sample(data, size, tops[second], second_local, colour)[:, channels]
            inset_jump = np.abs(inset_a - inset_b)
            inset_differences.append(inset_jump)
            for local, chart, sign, metres in ((first_local, tops[first], -direction, first_metre),
                                               (second_local, tops[second], direction, second_metre)):
                local[:, axis] += sign * metres
                inner_a = _sample(data, size, chart, local, colour)[:, channels]
                local[:, axis] += sign * metres
                inner_b = _sample(data, size, chart, local, colour)[:, channels]
                interior_variation.append(np.abs(inner_a - inner_b))
            level = str(round(float(first_position[1]), 1))
            level_counts[level] = level_counts.get(level, 0) + 1
            edge_rows.append({"first": first, "second": second, "level_m": float(first_position[1]),
                              "axis": "X" if axis == 0 else "Z",
                              "exact_edge": _stats(jump), "half_texel_inset": _stats(inset_jump)})
    if not edge_rows or set(level_counts) != {"3.2", "6.4"}:
        raise RuntimeError("Warehouse continuity must cover adjoining tiles on both gallery storeys")
    worst = max(edge_rows, key=lambda row: row["exact_edge"]["mean"])
    summary = {"channel": channel, "comparison": "linear RGB" if channel == "colour" else "roughness/cavity",
               "platform_count": len(platforms), "adjacent_edge_count": len(edge_rows),
               "samples_per_edge": SAMPLES_PER_EDGE, "levels": level_counts,
               "exact_edge": _stats(np.stack(differences)),
               "half_texel_inset": _stats(np.stack(inset_differences)),
               "native_texel_interior": _stats(np.stack(interior_variation)),
               "mean_limit": limit, "worst_edge": worst}
    # A global average can conceal the user's failure: one rusty module next
    # to one clean module.  Every shared boundary must meet the mean limit.
    if enforce and worst["exact_edge"]["mean"] > limit:
        raise RuntimeError(f"Warehouse {channel} painting resets at Platform "
                           f"{worst['first']}/{worst['second']} ({worst['axis']}): "
                           f"mean seam {worst['exact_edge']['mean']:.5f} > {limit:.5f}")
    return summary
