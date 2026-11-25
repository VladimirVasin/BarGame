"""Tangent-space relief for the sheltered and rain-exposed Ridge garage finishes."""

import numpy as np

from atlas_kit import PixelCanvas


def wood_grain(key, u, v):
    if key == "endgrain":
        return 125 * np.hypot(.78 * (u + .23), v - .33) + 2 * np.cos(10 * v)
    return 128 * v + 4 * np.sin(6 * u) + 2 * np.cos(13 * u + 5 * v)


def worktop_scores(u, v):
    rng = np.random.default_rng(63812)
    scores = np.zeros_like(u)
    for _ in range(32):
        x, y = rng.uniform(.08, .92), rng.uniform(.10, .90)
        scores += np.exp(-((u - x) / .032) ** 2 - ((v - y - .39 * (u - x)) / .006) ** 2)
    return scores


def floor_damp(u, v):
    return np.clip((.15 - v + .033 * np.sin(8 * u) + .015 * np.cos(16 * u)) / .11, 0, 1)


def relief_atlas(finishes, grid, tile, gutter):
    """Heights are millimetres. Atlas rows run opposite to tangent V."""
    side = tile - 2 * gutter
    v, u = np.mgrid[0:side, 0:side] / (side - 1)
    atlas = np.empty((1024, 1024, 4), dtype=np.uint8)
    atlas[:] = (128, 128, 255, 255)
    for i, key in enumerate(finishes):
        rng = np.random.default_rng(60209 + i)
        tooth = rng.normal(0, 1.5, u.shape)
        pitch_u, pitch_v = 1550, 1200
        if key in ("concrete", "slab", "floor"):
            grit = rng.normal(0, 2.4, u.shape)
            height = .31 * grit - 1.22 * np.maximum(grit - 3, 0)
            if key == "floor":
                height *= 1 - .58 * floor_damp(u, v)
                pitch_u, pitch_v = 3500, 6000
        elif key in ("timber", "repair", "endgrain", "worktop"):
            phase = wood_grain(key, u, v)
            height = .44 * np.sin(phase) + .17 * np.sin(2.35 * phase)
            pitch_u, pitch_v = 1300, 440
            if key == "worktop":
                height -= 1.20 * worktop_scores(u, v)
        elif key in ("limewash", "dado"):
            height = .24 * tooth + .32 * np.cos(60 * u + 5 * v) + .12 * np.sin(101 * u + 3 * v)
        elif key == "canvas":
            height = .51 * np.cos(123 * v) * np.cos(102 * u)
            pitch_u, pitch_v = 640, 440
        elif key == "case":
            height = .11 * tooth - .65 * np.maximum(0, -tooth - .8)
        elif key in ("iron", "zinc", "brass"):
            height = .10 * np.sin(152 * v + .5 * np.cos(8 * u)) + .06 * tooth
        elif key == "felt":
            height = .47 * tooth
        elif key in ("rubber", "leather"):
            height = .13 * tooth - .15 * np.maximum(0, -tooth - .7)
        elif key == "glass":
            continue
        else:
            # Paint and enamel retain a slight orange peel under a raking lamp.
            height = .09 * tooth
        dy, dx = np.gradient(height, pitch_v / (side - 1), pitch_u / (side - 1))
        normal = np.stack((-dx, dy, np.ones_like(dx)), axis=-1)
        normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
        rgb = np.rint((normal * .5 + .5) * 255).astype(np.uint8)
        row, col = divmod(i, grid)
        atlas[row*tile:(row+1)*tile, col*tile:(col+1)*tile, :3] = np.pad(
            rgb, ((gutter, gutter), (gutter, gutter), (0, 0)), mode="edge")
    canvas = PixelCanvas(1024, 1024)
    canvas.pixels[:] = atlas.tobytes()
    return canvas.png_bytes()
