#!/usr/bin/env python3
"""Bake the metric paper sleeve, drawn wall and stamped lid into one opaque RGBA atlas."""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image


OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Supermarket/Products/BeerCanFinish.png"


def bake():
    size = 256
    axis = (np.arange(size) + .5) / size
    u, v = np.meshgrid(axis, axis)
    rng = np.random.default_rng(6543)
    grain = rng.uniform(-1, 1, u.shape)

    # Match the sleeve's +/-37 mm radius and its centre at 76 mm.
    # Unequal patches at the grip leave intact paper between the thumb and fingers.
    x, y = (u - .25) / 5., (v - .5) / 16.
    thumb = np.exp(-((x - .012) / .013)**2 - ((y - .004) / .008)**2)
    fingers = np.exp(-((x + .019) / .012)**2 - ((y + .002) / .014)**2)
    contact = np.maximum(thumb, fingers * .82)
    fibres = (grain + np.roll(grain, 1, axis=1) + np.roll(grain, 2, axis=1)) / 3.
    edge = np.exp(-((np.abs(y) - .0223) / .0013)**2)
    pigment = .87 + .09 * fibres + .075 * contact + edge * (.04 + .035 * grain)
    gloss = .16 + .17 * contact + .035 * fibres - .04 * edge

    # The body is drawn vertically. The lid's tool marks are concentric about
    # the can axis. The aperture and tab share that same stamped metal stock.
    drawn = u >= .5
    stroke = np.sin((u - .625) * 3000 + np.sin(v * 11)) * .5 + grain * .5
    pigment = np.where(drawn, .91 + .045 * stroke, pigment)
    gloss = np.where(drawn, .49 + .10 * stroke, gloss)
    radius = np.hypot((u - .875) / 2.7, (v - .5) / 11.)
    tool = np.cos(radius * 9300)
    roll = np.exp(-((radius - .0315) / .0018)**2)
    pigment = np.where(u >= .75, .91 + .027 * tool + .045 * roll + .012 * grain, pigment)
    gloss = np.where(u >= .75, .57 + .07 * tool + .16 * roll + .025 * grain, gloss)
    rgba = np.stack((pigment, pigment, pigment, gloss), axis=-1)
    return np.uint8(np.clip(rgba[::-1], 0, 1) * 255 + .5)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    pixels = bake()
    if args.check:
        with Image.open(OUTPUT) as image:
            if not np.array_equal(np.asarray(image), pixels):
                raise SystemExit("Beer can finish differs from its metric atlas")
    else:
        Image.fromarray(pixels).save(OUTPUT)
