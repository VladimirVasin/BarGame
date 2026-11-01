"""Offline material sampling shared by the existing authored monster atlases.

The ImageGen master is an albedo source, never a runtime texture. Its nine flat
swatches are rebaked into the retained UV0, including the existing response map.
"""
from __future__ import annotations

from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE_IMAGE = ROOT / 'ArtSource/Combat/Textures/MonsterHorrorSurface.source.png'
PROFILE = 'monster_horror_materials_v1'
TILE_NAMES = ('skin', 'glass', 'wood', 'cloth', 'plaster', 'chitin',
              'emulsion', 'fascia', 'residue')
FAMILIES = {
    'Crane': (('skin', 'chitin', 'fascia', 'residue'),
              ((.48, .46, .39), (.31, .33, .27), (.34, .27, .22), (.22, .24, .22))),
    'Room': (('cloth', 'plaster', 'skin', 'wood'),
             ((.25, .265, .225), (.56, .53, .44), (.52, .49, .44), (.25, .205, .155))),
    'Imprint': (('plaster', 'glass', 'skin', 'residue'),
                ((.60, .605, .565), (.095, .105, .102), (.48, .47, .435), (.30, .32, .29))),
    'LongExposure': (('skin', 'emulsion', 'residue', 'emulsion'),
                     ((.40, .365, .335), (.315, .34, .335), (.155, .175, .17), (.26, .28, .27))),
}
_tiles = None
_means = {}


def load_master_tiles():
    global _tiles
    if _tiles is not None:
        return _tiles
    image = bpy.data.images.load(str(SOURCE_IMAGE), check_existing=True)
    image.colorspace_settings.name = 'Non-Color'
    width, height = image.size
    if width < 768 or width != height or width % 3:
        raise ValueError('Monster material master requires nine equal square swatches')
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(height, width, 4)[..., :3]
    step = width // 3
    # Blender stores the bottom image row first; the authored sheet is named
    # from its top-left. Trim a few texels to exclude any cell-edge transition.
    _tiles = {}
    for index, name in enumerate(TILE_NAMES):
        row, column = divmod(index, 3)
        top = (2 - row) * step
        _tiles[name] = pixels[top + 5:top + step - 5,
                             column * step + 5:(column + 1) * step - 5].copy()
    return _tiles


def source_pixels(family):
    names, targets = FAMILIES[family]
    tiles = load_master_tiles()
    # Keep the existing material palette and gameplay midtones. The prior
    # .28-.58 source gains discarded most authored texture; these preserve its
    # broad mottling while still leaving material colour in charge.
    return tuple(np.clip((tiles[name] - tiles[name].mean(axis=(0, 1))) * .98 + target, .018, .88)
                 for name, target in zip(names, targets))


def sample(image, uv):
    uv = 1 - np.abs(np.mod(uv, 2) - 1)
    xy = uv * np.array((image.shape[1] - 3, image.shape[0] - 3)) + 1
    a = np.floor(xy).astype(int)
    b = xy - a
    return ((image[a[..., 1], a[..., 0]] * (1 - b[..., 0, None]) +
             image[a[..., 1], a[..., 0] + 1] * b[..., 0, None]) * (1 - b[..., 1, None]) +
            (image[a[..., 1] + 1, a[..., 0]] * (1 - b[..., 0, None]) +
             image[a[..., 1] + 1, a[..., 0] + 1] * b[..., 0, None]) * b[..., 1, None])


def projected(points, normals, material, sources, scale):
    weights = np.abs(normals) ** 5
    weights = weights / np.maximum(weights.sum(axis=-1, keepdims=True), 1e-9)
    projections = (points[..., (1, 2)], points[..., (0, 2)], points[..., (0, 1)])
    indices = np.broadcast_to(material, points.shape[:-1])
    result = np.zeros_like(points)
    for index in np.unique(indices):
        colour = sum(sample(sources[index], uv * scale + (.173, .311)) * weights[..., axis, None]
                     for axis, uv in enumerate(projections))
        result[indices == index] = colour[indices == index]
    return result


def variation(points, normals, material, sources):
    colour = projected(points, normals, material, sources, 1.35)
    indices = np.broadcast_to(material, points.shape[:-1])
    key = tuple(id(source) for source in sources)
    cached = _means.get(key)
    if cached is None:
        means = np.array([source.mean(axis=(0, 1)) for source in sources])
        # Retain the arrays with their means so Python cannot recycle their
        # identities between the finite four-family bake stages.
        _means[key] = (sources, means)
    else:
        means = cached[1]
    return colour - means[indices]


def relief(points, normals, material, sources, height):
    # The same sampled fissures/pitting drive albedo, roughness and tangent
    # slopes. This is a registered physical response, not painted illumination.
    delta = variation(points, normals, material, sources)
    luminance = delta @ np.array((.2126, .7152, .0722))
    return height * .70 + luminance * .005


def finish(points, normals, material, sources, rgb, height, roughness, cavity):
    delta = variation(points, normals, material, sources)
    luminance = delta @ np.array((.2126, .7152, .0722))
    rgb = np.clip(rgb + delta * .42, .012, .91)
    roughness = np.clip(roughness + luminance * .55, .16, .98)
    cavity = np.clip(cavity + np.minimum(luminance, 0) * .22, .74, 1.)
    return rgb, height * .70 + luminance * .005, roughness, cavity


def decorate(payload):
    payload['surface_profile'] = PROFILE
    payload['texture_source'] = str(SOURCE_IMAGE.relative_to(ROOT))
    return payload
