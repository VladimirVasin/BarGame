#!/usr/bin/env python3
"""Bake the four Blender UV charts of the waterworks into a URP metallic atlas."""
import argparse
from io import BytesIO

import numpy as np
from PIL import Image

import city_surface_authoring as city


def bake():
    size = 256
    v, u = (np.indices((size, size), dtype=np.float32) + .5) / size
    radius = np.hypot(u - .5, v - .5)
    rng = np.random.default_rng(260423)
    colour = np.empty((2 * size, 2 * size, 3), dtype=np.uint8)
    mask = np.zeros((2 * size, 2 * size, 4), dtype=np.uint8)
    # Order follows the UV quadrants in city_waterworks_model.build.
    for cell, (pigment, tint, metallic, gloss) in enumerate((
            ('PaintedMetal', (79, 91, 80), .13, .34), ('Galvanized', (138, 141, 128), .72, .43),
            ('Enamel', (118, 72, 56), .10, .41), ('Rust', (73, 71, 62), .34, .24))):
        rgb, _ = city.sample(pigment, u, v, tint=np.asarray(tint) / 255, pixel_metres=1/size)
        grain = rng.normal(0, .014, radius.shape)
        if cell == 2:
            # Exposed metal follows the hand contact on the outer crown of the rim.
            angle = np.arctan2(v - .5, u - .5)
            edge = .419 + .012 * np.sin(5*angle) + .010 * np.cos(9*angle)
            wear = np.clip((radius - edge + grain*.30) / .034, 0, 1)
            rgb = rgb * (1-wear[..., None]) + np.array([126, 122, 103])/255 * wear[..., None]
            metallic += wear*.63
            gloss += wear*.14
        elif cell == 3:
            grain -= np.maximum(0, -grain-.014)*1.8
            # The damp collector is visible between the grate bars.
            receiver = v < .185
            rgb[receiver] *= .39
            metallic = np.where(receiver, .08, metallic)
            gloss = np.where(receiver, .15, gloss)
        y, x = divmod(cell, 2)
        tile = np.s_[y*size:(y+1)*size, x*size:(x+1)*size]
        colour[tile] = np.rint(255*np.clip(rgb + grain[..., None], 0, 1))
        mask[tile][..., 0] = np.rint(255*np.clip(metallic + grain, 0, 1))
        mask[tile][..., 3] = np.rint(255*np.clip(gloss + grain*1.3, 0, 1))
    return {'Albedo': colour[::-1], 'Mask': mask[::-1]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    for suffix, pixels in bake().items():
        path = city.OUTPUT / f'WaterworksMetal{suffix}.png'
        data = BytesIO()
        Image.fromarray(pixels).save(data, format='PNG', compress_level=9)
        if args.check:
            if data.getvalue() != path.read_bytes():
                raise SystemExit(f'Rebake {path.name}')
        else:
            path.write_bytes(data.getvalue())


if __name__ == '__main__':
    main()
