#!/usr/bin/env python3
"""Bake stone, joint runoff, the waterline and locally worn granite contact faces."""
import argparse
from io import BytesIO

import numpy as np
from PIL import Image

import city_surface_authoring as city


def bake():
    w, h = 512, 256
    y, x = np.indices((h, w), dtype=np.float32)
    u, v = (x+.5)/w, (y+.5)/h
    sheets = [Image.new(mode, (2*w, 4*h)) for mode in ('RGB', 'RGBA', 'RGB')]
    colours = ((132,127,112), (112,118,111), (128,126,110), (99,108,96),
               (155,151,131), (139,134,119), (99,98,87), (143,139,124))
    rng = np.random.default_rng(1982)
    for cell, rgb in enumerate(colours):
        tint = np.array(rgb)/255
        pigment, _ = city.sample('Stone', u*1.65+.23*cell, v*.71, tint=tint, pixel_metres=1.65/w)
        flecks = rng.normal(0,.015,u.shape)
        drift = np.sin(13*u + 3*np.sin(4*v) + .6*cell)
        grains = .0045*np.sin(315*u+65*v)*np.sin(142*u-220*v) + flecks
        relief = .0016*drift + .00018*np.sin(128*u+7*v) + .030*grains
        value = .54*pigment + .46*tint + (.014*drift+grains)[...,None]
        gloss = np.full(u.shape, .38)
        if cell == 2:
            centre = .5+.012*np.sin(9*v)+.012*v
            stain = np.exp(-((u-centre)/(.027+.035*v))**2) * np.exp(-1.5*v)
            value -= .130*stain[...,None]
            gloss += .13*stain
        elif cell == 3:
            band = np.clip((v-.24+.056*drift)*3.1,0,1)
            value *= 1-.35*band[...,None]
            gloss += .26*band
        elif cell in (4,5):
            # Hands follow the long coping crown; feet follow the centreline across separate treads.
            axis = v if cell == 4 else u
            worn = np.exp(-((axis-.50)/(.28 if cell == 4 else .26))**4)
            value += (.045*worn)[...,None]
            relief *= 1-.78*worn
            gloss += (.40 if cell == 4 else .27)*worn
        dx = np.gradient(relief, axis=1)*w/1.65
        dy = np.gradient(relief, axis=0)*h/.71
        normal = np.stack((-dx, dy, np.ones_like(dx)), axis=-1)
        normal /= np.linalg.norm(normal,axis=-1,keepdims=True)
        mask = np.zeros((h,w,4),dtype=np.uint8)
        mask[:,:,3] = np.rint(np.clip(gloss+flecks,0,1)*255)
        for sheet, pixels in zip(sheets,(np.uint8(np.clip(value,0,1)*255+.5),mask,
                                          np.uint8((normal*.5+.5)*255+.5))):
            sheet.paste(Image.fromarray(pixels), ((cell%2)*w, (cell//2)*h))
    return sheets


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    for suffix, sheet in zip(('Albedo','Mask','Normal'),bake()):
        path = city.OUTPUT / ('MouthBridge'+suffix+'.png')
        data = BytesIO()
        sheet.save(data, format='PNG', compress_level=9)
        if args.check:
            if data.getvalue() != path.read_bytes():
                raise ValueError('Rebake '+path.name)
        else:
            path.write_bytes(data.getvalue())
