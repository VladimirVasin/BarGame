"""Offline ImageGen material sampling on the retained zombie/porter UV charts.

Only pigment changes. Original face landmarks, UV rectangles, atlas dimensions,
rigs, damage geometry and the living NPC atlas remain authoritative.
"""
from __future__ import annotations

import numpy as np


def _tile(name, width, height, variant=0):
    from combat_horror_surfaces import load_master_tiles
    source = load_master_tiles()[name]
    # Insets exclude the sheet's cell boundaries. A crop, unlike stochastic
    # noise, preserves the authored material's connected stains and fibres.
    margin = max(2, min(source.shape[:2]) // 32)
    source = source[margin:-margin, margin:-margin]
    h, w = source.shape[:2]
    left, right = .04 + variant * .035, .96 - variant * .025
    bottom, top = .035 + variant * .02, .965 - variant * .025
    if width < w and height < h:
        # Exact separable area integration before native-atlas minification.
        # Point-sampling the photographic pores at 3:1 produced alternating
        # bright/dark texels which the retained Point importer magnified.
        def coverage(count, size, low, high):
            edges = np.linspace(low * count, high * count, size + 1)
            starts = np.arange(count)
            weights = np.maximum(0., np.minimum(edges[1:, None], starts + 1.) -
                                 np.maximum(edges[:-1, None], starts))
            return weights / weights.sum(axis=1, keepdims=True)
        rows = np.tensordot(coverage(h, height, bottom, top), source, axes=(1, 0))
        return np.einsum('hyc,wy->hwc', rows, coverage(w, width, left, right))
    u = np.linspace(left, right, width)
    v = np.linspace(bottom, top, height)
    x, y = np.meshgrid(u * (w - 1), v * (h - 1))
    ix, iy = np.floor(x).astype(int), np.floor(y).astype(int)
    nx, ny = np.minimum(ix + 1, w - 1), np.minimum(iy + 1, h - 1)
    dx, dy = (x - ix)[..., None], (y - iy)[..., None]
    return ((source[iy, ix] * (1 - dx) + source[iy, nx] * dx) * (1 - dy) +
            (source[ny, ix] * (1 - dx) + source[ny, nx] * dx) * dy)


def _pigment(name, width, height, target, gain, variant=0):
    source = _tile(name, width, height, variant)
    if name == 'skin':
        # Only the body's low-density flesh chart needs an atlas-space
        # lowpass. Connected veins/mottling survive; subtexel pore noise does
        # not become a new checker pattern on the very long swept neck.
        offsets = np.arange(-3, 4)
        kernel = np.exp(-offsets ** 2 / (2. * 1.1 ** 2))
        kernel /= kernel.sum()
        for axis in (0, 1):
            padding = [(0, 0), (0, 0), (0, 0)]
            padding[axis] = (3, 3)
            padded = np.pad(source, padding, mode='edge')
            source = sum(weight * np.take(padded, np.arange(source.shape[axis]) + index, axis=axis)
                         for index, weight in enumerate(kernel))
    return np.clip(np.asarray(target) + (source - source.mean(axis=(0, 1))) * gain, .025, .89)


def _array(canvas):
    return np.frombuffer(canvas.pixels, dtype=np.uint8).reshape(canvas.height, canvas.width, 4).copy() / 255.


def _finish(canvas, pixels):
    pixels[..., 3] = 1.
    canvas.pixels[:] = np.clip(pixels * 255 + .5, 0, 255).astype(np.uint8).tobytes()
    return canvas.png_bytes()


def _face(pixels, variant):
    # Preserve painted eyes, mouth and expression. The material field follows
    # their pigment rather than adding a second face to the existing one.
    field = _tile('skin', 128, 128, variant)
    luma = field @ np.array((.299, .587, .114))
    variation = np.clip(1. + (luma - luma.mean()) * 1.6, .57, 1.34)
    old = pixels[384:512, 384:512, :3]
    pixels[384:512, 384:512, :3] = np.clip(old * variation[..., None], .025, .88)


def decay(canvas, variant):
    pixels = _array(canvas)
    cloth = _tile('cloth', 512, 512, variant)
    light = cloth @ np.array((.299, .587, .114))
    wear = np.clip(1. + (light - light.mean()) * 2.5, .42, 1.30)
    pixels[..., :3] *= wear[..., None]
    glass_targets = ((.40, .48, .31), (.48, .37, .22), (.34, .45, .35))
    glass = _pigment('glass', 256, 256, glass_targets[variant], 1.05, variant)
    # Cloudy residue and abrasions are pigment; actual reflected light still
    # belongs to the existing opaque glass material and its source lights.
    pixels[:256, :256, :3] = glass
    pixels[240:256, 240:256, :3] = (.075, .094, .055)
    skin_targets = ((.59, .56, .49), (.56, .55, .51), (.60, .56, .51))
    pixels[:128, 256:384, :3] = _pigment('skin', 128, 128, skin_targets[variant], .85, variant)
    # The retained tiny skin palette is still sampled by a few original hand
    # surfaces. It must stay a constant patch to avoid face/clothing leakage.
    pixels[:12, 500:512, :3] = skin_targets[variant]
    _face(pixels, variant)
    return _finish(canvas, pixels)


def porter(canvas, variant):
    pixels = _array(canvas)
    cloth = _tile('cloth', 384, 256, variant)
    light = cloth @ np.array((.299, .587, .114))
    wear = np.clip(1. + (light - light.mean()) * 2.45, .43, 1.32)
    pixels[:256, :384, :3] *= wear[..., None]
    _face(pixels, variant)
    return _finish(canvas, pixels)


def crate(canvas):
    pixels = _array(canvas)
    pixels[:192, :256, :3] = _pigment('wood', 256, 192, (.29, .31, .25), 1.02)
    pixels[192:, :256, :3] = _pigment('residue', 256, 64, (.22, .24, .22), .71)
    # The fused flesh and fish retain the eye/belly/fin chart. Moisture is
    # confined to those existing materials, without painted wounds or blood.
    flesh = _pigment('fascia', 128, 128, (.39, .42, .37), .82)
    pixels[128:256, 256:384, :3] = flesh
    skin = _tile('skin', 256, 128)
    luma = skin @ np.array((.299, .587, .114))
    fish = np.clip(1. + (luma - luma.mean()) * .95, .68, 1.21)
    pixels[:128, 256:512, :3] *= fish[..., None]
    return _finish(canvas, pixels)
