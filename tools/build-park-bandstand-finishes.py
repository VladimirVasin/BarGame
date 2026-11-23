#!/usr/bin/env python3
"""Bake the park joinery and pendant finishes from the city pigment library."""
import argparse
from io import BytesIO

import numpy as np
from PIL import Image

import city_surface_authoring as city
from park_bandstand_finishes import CHARTS, SIZE, TILE


def sheets():
    colours = Image.new("RGB", (SIZE, SIZE))
    masks = Image.new("RGBA", (SIZE, SIZE))
    recipes = (
        ("WeatheredWood",(113,101,81),0,.19), ("WeatheredWood",(103,98,83),0,.16),
        ("WeatheredWood",(90,78,65),0,.15), ("WeatheredWood",(87,89,72),0,.20),
        ("WeatheredWood",(134,109,82),0,.23), ("WeatheredWood",(95,76,60),0,.33),
        ("WeatheredWood",(168,151,119),0,.27), ("WeatheredWood",(78,77,63),0,.27),
        ("Concrete",(115,113,100),0,.13), ("PaintedMetal",(70,86,77),.30,.25),
        ("Enamel",(68,83,74),.22,.32), ("Enamel",(167,157,128),.15,.36),
        ("Enamel",(174,169,147),0,.34), ("Galvanized",(126,102,69),.65,.31),
        ("Rust",(78,76,66),.43,.20), ("Rubber",(60,62,53),0,.16),
    )
    y,x = (np.indices((TILE,TILE), dtype=np.float32)+.5)/TILE
    rng = np.random.default_rng(1967)
    for i, (key,(family,tint,metal,gloss)) in enumerate(zip(CHARTS, recipes, strict=True)):
        base = np.asarray(tint)/255
        rgb, _ = city.sample(family, x*1.6, y*.6, tint=base, pixel_metres=1.6/TILE)
        noise = rng.normal(0,.012,x.shape)
        if i < 8:
            knot = .09*np.exp(-((x-.38-.08*(i%4))/.115)**2 - ((y-.50)/.3)**2)
            grain = y+knot+.017*np.sin(x*8+i) if key != "cut" else np.hypot(x*.8-.62,y*.57+.14)
            fibre = .021*np.cos(108*grain)+.013*np.sin(253*grain)+.006*np.cos(456*grain)
            rgb = .6*rgb+.4*base+fibre[...,None]
            if key in ("bench","rim"):
                contact = np.exp(-((x-.5)/.36)**4-((y-.48)/.38)**2)
                rgb += .047*contact[...,None]
                gloss += .21*contact
            elif key.startswith("deck"):
                rgb -= (.038*np.maximum(0.,np.cos(32*x+.6*np.sin(9*y)))**18 * np.exp(-y*3))[...,None]
        elif key in ("roof","enamel","lining","iron"):
            edge = np.minimum.reduce((x,1-x,y,1-y))
            chips = np.clip((.025-edge+noise*.9+.009*np.cos(x*48+y*34))*22,0,.7)
            rgb = rgb*(1-chips[...,None])+np.array((.44,.36,.28))*chips[...,None]
            gloss *= 1-.44*chips
        colour = np.uint8(np.rint(255*np.clip(rgb+noise[...,None],0,1)))
        packed = np.zeros((TILE,TILE,4), np.uint8)
        packed[:,:,0] = np.rint(255*metal)
        packed[:,:,3] = np.rint(255*np.clip(gloss+noise,0,1))
        origin = ((i%4)*TILE,(i//4)*TILE)
        colours.paste(Image.fromarray(colour), origin)
        masks.paste(Image.fromarray(packed), origin)
    return colours,masks


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for suffix, sheet in zip(("Albedo","Mask"), sheets()):
        path = city.OUTPUT / ("ParkBandstand"+suffix+".png")
        stream = BytesIO()
        sheet.save(stream, format="PNG", compress_level=9)
        if args.check:
            if path.read_bytes() != stream.getvalue():
                raise SystemExit("Rebake "+path.name)
        else:
            path.write_bytes(stream.getvalue())
