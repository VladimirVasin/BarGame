#!/usr/bin/env python3
"""One 1.78 x 1.72 m slab at the turn into Lower Court, with converging shoe wear."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 384
ROOT = Path(__file__).resolve().parents[1]
rng = np.random.default_rng(1125)
grain = rng.normal(0, 3.0, (SIZE, SIZE))
def cloudy(grid, strength):
    values = Image.fromarray(rng.integers(32, 224, (grid, grid), dtype=np.uint8))
    return (np.asarray(values.resize((SIZE, SIZE), Image.Resampling.BICUBIC), dtype=float) - 128) * strength
tone = 118 + cloudy(7, .08) + cloudy(47, .055) + grain
# Dirt collects along the outside of the turn. A few partial sole and heel
# edges survive between the two traffic streams, at actual adult shoe scale.
wear = Image.new("L", (SIZE, SIZE))
for px, py, angle, strength in ((78,70,-32,100),(128,145,-38,84),(156,193,-52,116),
                               (208,214,-61,92),(278,248,-79,82),(321,290,-72,65),
                               (290,137,75,72),(205,162,58,87),(117,210,34,73)):
    shoe = Image.new("L", (60,80))
    draw = ImageDraw.Draw(shoe)
    draw.ellipse((19,12,39,44), fill=strength)
    draw.rounded_rectangle((21,49,37,62),radius=4,fill=int(strength*.8))
    for y in range(17,44,6):
        draw.line((18,y,40,y+4), fill=10,width=2)
    wear.paste(shoe.rotate(angle, Image.Resampling.BICUBIC), (px-30,py-40),
               shoe.rotate(angle, Image.Resampling.BICUBIC))
tone -= np.asarray(wear.filter(ImageFilter.GaussianBlur(.75))) * .31
pigment = np.stack((tone*.98, tone, tone*.965), axis=-1)
Image.fromarray(np.uint8(np.clip(pigment, 0, 255))).save(ROOT/"Assets/Resources/CityTerraces/CourtShoeTurn.png")
