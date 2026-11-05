#!/usr/bin/env python3
"""Publish the physical response for the full-wall BusShelterPaintedMetal pigment.

The four painted sheets occupy one 4.25 × 2.45 metre chart. Joint and fixing
centres were read from the original 1655 × 950 ImageGen pigment in top-down
pixel coordinates. Their relief is authored in metres, independently of paint
colour. The packaged albedo's Description field contains the generation prompt.
"""
from pathlib import Path
import runpy

import numpy as np

import city_surface_authoring as city


def wall_response():
    width, height = 768, 440
    panel_width, panel_height = 4.25, 2.45
    x, y = np.meshgrid(
        (np.arange(width) + .5) * panel_width / width,
        (np.arange(height) + .5) * panel_height / height)
    _, response = city.structure("PaintedMetal", x, y, max(panel_width / width, panel_height / height))
    relief = np.zeros_like(x)
    for join, fastener_x in ((409.5, 421), (830, 836), (1236.5, 1248)):
        delta = x - join / 1655 * panel_width
        channel = np.exp(-.5 * (delta / .005) ** 2)
        relief += -.002 * channel + .0014 * np.exp(-.5 * ((delta - .011) / .007) ** 2)
        response[..., 3] -= .12 * channel
        for fastener_y in (101, 445, 787):
            dx = x - fastener_x / 1655 * panel_width
            dy = y - (1 - fastener_y / 950) * panel_height
            cap = np.exp(-.5 * (dx ** 2 + dy ** 2) / .006 ** 2)
            relief += .0024 * cap
            response[..., 2] -= .09 * cap
    slope_y, slope_x = np.gradient(relief, panel_height / height, panel_width / width)
    response[..., 0] += slope_x * .5
    response[..., 1] += slope_y * .5
    # Drier dust trapped at the foot of the pressings scatters the specular lobe.
    response[..., 2] += .09 * np.exp(-y / .11)
    return response


if __name__ == "__main__":
    # Keep BC7, colour space, mipmaps and upright PNG transport identical to
    # the existing City sheet publisher, including the stable Unity GUID.
    publisher = runpy.run_path(str(Path(__file__).with_name("build-village-surface-textures.py")))
    base = city.OUTPUT / "BusShelterPaintedMetalAlbedo.png"
    if not base.is_file():
        raise FileNotFoundError(base)
    Path(str(base) + ".meta").write_text(publisher["meta"](base, 2048, False, clamp=True), encoding="utf-8")
    packed = city.OUTPUT / "BusShelterPaintedMetalResponse.png"
    publisher["publish"](packed, publisher["png"](wall_response()), 1024, True, False, clamp=True)
