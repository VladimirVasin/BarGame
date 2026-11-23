#!/usr/bin/env python3
"""Bake eight padded horizontal grain/stone charts for the park footbridge."""
import argparse
from io import BytesIO

import numpy as np
from PIL import Image

import city_surface_authoring as city

SIZE = 1024
HEIGHT = SIZE // 8


def bake():
    albedo = Image.new('RGB', (SIZE, SIZE))
    response = Image.new('RGBA', (SIZE, SIZE))
    normals = Image.new('RGB', (SIZE, SIZE))
    y, x = np.indices((HEIGHT, SIZE), dtype=np.float32)
    u, v = (x+.5)/SIZE, (y+.5)/HEIGHT
    rng = np.random.default_rng(117)
    # Four deck cuts, structural timber, rubbed handrail, sawn ends and granite.
    colours = [(106,90,73), (115,100,82), (101,91,76), (89,78,66),
               (119,99,77), (106,93,77), (118,117,108), (109,97,78)]
    for row, tint in enumerate(colours):
        mineral = row == 6
        base = np.array(tint)/255
        # The source timber runs vertically in its pigment sheet. Along-board U
        # therefore samples the donor's Y axis before introducing the sawn grain.
        donor_x = u*.96 if mineral else v*.26+.19*row
        donor_y = v*.32 if mineral else u*2.14+.23*row
        grain, _ = city.sample('Stone' if mineral else 'WeatheredWood', donor_x, donor_y,
                               tint=base, pixel_metres=2.14/SIZE)
        noise = rng.normal(0, .009, u.shape)
        knot_x = (.22,.74,.34,.62,.82,.18,.5,.38)[row]
        knot = np.exp(-((u-knot_x)/.050)**2-((v-.53)/.28)**2)
        fibres = v + .18*knot + .033*np.sin(7*u+row) + .010*np.sin(19*u+2.2*row)
        knots = rng.uniform(-1, 1, 201)
        fibre_tone = np.interp(fibres, np.linspace(-.25,1.4,len(knots)), knots)
        ring = np.hypot((u-.38)*1.35,(v+.42)*.80) if row==5 else fibres
        fine = .017*np.sin(100*ring)+.009*np.cos(247*ring+.5*np.sin(29*u))
        value = .55*grain+.45*base if mineral else .33*grain+.67*base
        if mineral:
            relief = .00019*np.sin(31*u+9*v)*np.cos(17*v+47*u) + .00008*noise
            gloss = .31 + noise*1.8
            value += noise[...,None]
        else:
            # A crossing abrades the middle of each transverse plank. Hands contact
            # the upper rail face continuously, with untouched arrises either side.
            wear = np.exp(-((u-.5)/.25)**4) if row in (0,1,2,7) else np.exp(-((v-.50)/.35)**4) if row == 4 else 0.
            earlywood = .018*np.maximum(0.,np.cos(50*ring+row))**14
            long_fibre = .018*fibre_tone if row != 5 else 0
            value += (noise*.66 + fine + long_fibre - earlywood - .038*knot + .033*wear)[...,None]
            relief = (.00022*np.sin(100*ring) + .00011*np.cos(247*ring))*(1-.45*wear)
            gloss = .35 + .31*wear - .04*np.sin(100*ring) + noise*1.2
        pixels = np.rint(np.clip(value,0,1)*255).astype('uint8')
        mask = np.zeros((HEIGHT,SIZE,4),dtype='uint8')
        mask[:,:,3] = np.rint(np.clip(gloss,0,1)*255)
        dy, dx = np.gradient(relief)
        normal = np.stack((-dx*SIZE/2.86, dy*HEIGHT/.24, np.ones_like(dx)),axis=-1)
        normal /= np.linalg.norm(normal,axis=-1,keepdims=True)
        packed_normal = np.rint((normal*.5+.5)*255).astype('uint8')
        for dest, src in ((albedo,pixels),(response,mask),(normals,packed_normal)):
            dest.paste(Image.fromarray(src), (0,row*HEIGHT))
    return albedo,response,normals


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    for suffix, img in zip(('Albedo','Mask','Normal'), bake()):
        output = city.OUTPUT / ('ParkFootbridge'+suffix+'.png')
        data = BytesIO()
        img.save(data, 'PNG', compress_level=9)
        if args.check:
            if output.read_bytes() != data.getvalue():
                raise ValueError(f'{output.name} differs from the measured bridge recipe')
        else:
            output.write_bytes(data.getvalue())
