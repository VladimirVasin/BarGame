#!/usr/bin/env python3
"""Bake the southern road bridge's pigment, metal/smoothness and slope atlas."""
import argparse
from io import BytesIO
import numpy as np
from PIL import Image
import city_surface_authoring as city


def bake():
    width, height = 512, 256
    sheets = [Image.new(mode,(1024,1024)) for mode in ('RGB','RGBA','RGB')]
    y, x = np.indices((height,width), dtype=np.float32)
    u, v = (x+.5)/width, (y+.5)/height
    rng = np.random.default_rng(1938)
    recipes = [
        ('PaintedMetal',(73,84,80),.19,.38), ('PaintedMetal',(68,75,72),.28,.43),
        ('PaintedMetal',(83,89,82),.24,.36), ('Galvanized',(108,112,101),.48,.60),
        ('Stone',(135,130,117),0,.37), ('Stone',(116,119,110),0,.43),
        ('Concrete',(113,112,102),0,.29), ('Galvanized',(90,85,75),.64,.50)]
    for index,(family,tint,metal,gloss) in enumerate(recipes):
        base = np.array(tint)/255
        pigment,_ = city.sample(family,u*1.85+.28*index,v*.85,tint=base,pixel_metres=1.85/width)
        noise = rng.normal(0,.009,u.shape)
        streak = np.sin(33*u + .65*np.sin(9*v) + index)
        value = pigment*.63 + base*.37
        relief = .00018*streak + .00012*np.sin(133*u+28*v)
        if index < 3:
            edge = np.minimum.reduce([u,1-u,v,1-v])
            rust = np.clip((.035-edge+.011*streak+noise)*13,0,.65)
            if index == 2:
                for bx in (.18,.82):
                    for by in (.145,.50,.855):
                        ring = np.exp(-((np.hypot((u-bx)*.77,v-by)-.062)/.025)**2)
                        trail = np.exp(-((u-bx)/.020)**2)*np.exp(-np.maximum(0,v-by)*13)*(v>by)
                        rust = np.maximum(rust, .48*ring+.25*trail)
            value = value*(1-rust[...,None])+np.array((.42,.294,.201))*rust[...,None]
            gloss = gloss-.16*rust
            metal = metal*(1-rust)
        elif index == 3:
            grip = np.exp(-((v-.49)/.31)**4) * (.85+.15*np.sin(u*9))
            value += .046*grip[...,None]
            gloss += .25*grip
        elif index in (4,5,6):
            seep = np.exp(-((u-.78-.027*np.sin(4*v))/.10)**2)*(.16+.77*v)
            value -= (.035 if index == 4 else .068)*seep[...,None]
            grit = .006*rng.standard_normal(u.shape)
            value += grit[...,None]
            gloss += .09*seep
            relief += .00023*np.cos(128*u+14*v)*np.sin(71*v+4*u)
        colour = np.uint8(np.clip(value+noise[...,None],0,1)*255+.5)
        dy, dx = np.gradient(relief)
        normal = np.stack((-dx*width/1.85,dy*height/.85,np.ones_like(dx)),axis=-1)
        normal /= np.linalg.norm(normal,axis=-1,keepdims=True)
        normal = np.uint8((normal*.5+.5)*255+.5)
        mask = np.zeros((height,width,4),dtype=np.uint8)
        mask[:,:,0] = np.rint(255*np.clip(metal,0,1))
        mask[:,:,3] = np.rint(255*np.clip(gloss+noise,0,1))
        for sheet, pixels in zip(sheets,(colour,mask,normal)):
            sheet.paste(Image.fromarray(pixels),((index%2)*width,(index//2)*height))
    return sheets


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check',action='store_true')
    args = parser.parse_args()
    for suffix, sheet in zip(('Albedo','Mask','Normal'),bake()):
        path = city.OUTPUT / ('WorksBridge'+suffix+'.png')
        stream = BytesIO()
        sheet.save(stream,format='PNG',compress_level=9)
        if args.check:
            if path.read_bytes() != stream.getvalue(): raise ValueError(f'Rebake {path.name}')
        else:
            path.write_bytes(stream.getvalue())
