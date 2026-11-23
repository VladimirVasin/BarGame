#!/usr/bin/env python3
"""Bake the factory's formed fittings and its metre-space washable surfaces."""
from io import BytesIO
import argparse

import numpy as np
from PIL import Image

import city_surface_authoring as city
from city_cannery_finishes import CHARTS, SIZE

# Pigments and specular response belong to each construction material.
RECIPES = {
    "precast": ("Concrete", (151,145,127), 0,.18),
    "jade": ("PaintedMetal",(78,113,104),.12,.38),
    "zinc": ("Galvanized",(157,161,147),.62,.43),
    "cream": ("Enamel",(188,180,147),.09,.38),
    "cast": ("Rust",(82,80,68),.33,.22),
    "brushed": ("Galvanized",(161,170,161),.58,.47),
    "tin": ("Galvanized",(180,180,157),.69,.45),
    "deck": ("PaintedMetal",(104,115,105),.38,.32),
    "dial": ("Enamel",(216,206,170),.04,.27),
    "kraft": ("Paper",(160,127,91),0,.16),
    "cut": ("Paper",(131,104,72),0,.12),
    "rubber": ("Rubber",(69,73,66),0,.22),
    "wood": ("WeatheredWood",(146,111,75),0,.23),
    "end": ("WeatheredWood",(129,101,73),0,.15),
    "cloth": ("Cloth",(116,137,126),0,.15),
    "tile": ("Enamel",(182,190,173),.02,.41),
    "resin": ("Concrete",(136,144,130),0,.37),
}


def field(key, size):
    w,h=size
    y,x=(np.indices((h,w),dtype=np.float32)+.5)/np.asarray((h,w))[:,None,None]
    family,tint,metal,gloss=RECIPES[key]
    pigment=np.asarray(tint)/255
    rgb,_=city.sample(family,x*2,y*2,tint=pigment,pixel_metres=2/w)
    grain=np.random.default_rng(752+tuple(RECIPES).index(key)).normal(0,.009,(h,w))
    edge=np.minimum.reduce((x,1-x,y,1-y))
    tau=np.pi*2
    if key=="dial":
        dx,dy=x-.5,.5-y
        radius=np.hypot(dx,dy)
        angle=np.arctan2(dx,dy)
        division=np.pi/24
        mark=np.rint(angle/division)
        major=(mark.astype(int)%4)==0
        tick=(abs(angle-mark*division)<np.where(major,.021,.012)) & (abs(mark)<=18)
        ink=tick & (radius<.45) & (radius>np.where(major,.31,.376))
        rgb[ink]=(.20,.22,.19)
        rgb[(mark==16)&ink]=(.48,.28,.21)
    elif key in ("wood","end"):
        curve=(y+.014*np.sin(tau*x)+.075*np.exp(-((x-.72)/.11)**2-((y-.47)/.17)**2)
               if key=="wood" else np.hypot((x-.56)*.94,(y-1.2)*.64))
        rgb+=(.043*np.sin(115*curve)+.019*np.sin(272*curve))[...,None]
    elif key in ("jade","cream","zinc","cast"):
        rub=np.clip((.035-edge+grain*.64+.007*np.cos(29*x+41*y))*18,0,.65)
        rgb=rgb*(1-rub[...,None])+np.array((.52,.51,.43))*rub[...,None]
        metal=metal+.14*rub
        gloss=gloss-.11*rub
    elif key in ("brushed","tin"):
        brush=np.sin(tau*(y*96+.2*np.sin(tau*x)))+.7*np.cos(tau*y*167)
        # Cold-rolled steel has fine grinding lines. Keep mill-scale mottling
        # below the contrast of the highlights on bent sheet and rolled rims.
        rgb=.16*rgb+.84*pigment+.009*brush[...,None]
        grain*=.42
        gloss=gloss+.037*brush
    elif key in ("deck","rubber","cloth"):
        if key=="deck":
            relief=np.maximum(0,np.cos(tau*(x*21+y*17)))**13 * (np.cos(tau*(x*21-y*17))>-.1)
        else:
            relief=np.sin(tau*x*49)*np.cos(tau*y*41)
        rgb+=(.065 if key=="deck" else .015)*relief[...,None]
        gloss=gloss+.10*relief
    elif key in ("kraft","cut"):
        if key=="cut":
            rgb+=(.08*np.cos(tau*(x*42+.65*np.sin(tau*y))))[...,None]
        else:
            rgb+=(.013*np.cos(tau*y*65))[...,None]
    elif key=="tile":
        # Five real glazed tiles per 1.5 m, with dull mineral grout.
        dx=np.abs((x*5+.5)%1-.5)
        dy=np.abs((y*5+.5)%1-.5)
        joint=np.clip((.035-np.minimum(dx,dy))*43,0,1)
        variation=.018*np.cos(tau*(np.floor(x*5)*2+np.floor(y*5))/5)
        rgb=(rgb+variation[...,None])*(1-joint[...,None])+np.array((.44,.46,.42))*joint[...,None]
        gloss=gloss*(1-.77*joint)
    elif key=="resin":
        aggregate=np.clip((np.abs(grain)-.014)*5,0,.17)
        rgb-=aggregate[...,None]
        gloss=gloss-aggregate*.65
    packed=np.zeros((h,w,4),np.uint8)
    packed[:,:,0]=np.rint(255*np.clip(metal+grain*.2,0,1))
    packed[:,:,3]=np.rint(255*np.clip(gloss+grain,0,1))
    return Image.fromarray(np.uint8(np.rint(255*np.clip(rgb+grain[...,None],0,1)))),Image.fromarray(packed)


def sheets():
    albedo,mask=Image.new("RGB",SIZE),Image.new("RGBA",SIZE)
    for i,key in enumerate(CHARTS):
        colour,response=field(key,(502,246))
        for sheet,img in ((albedo,colour),(mask,response)):
            # Duplicate edge texels into a five-pixel extrusion for mip filtering.
            tile=np.pad(np.asarray(img),((5,5),(5,5),(0,0)),mode="edge")
            sheet.paste(Image.fromarray(tile),(i%4*512,i//4*256))
    yield "CanneryFittings",(albedo,mask)
    for stem,key in (("CanneryWorkSteel","brushed"),("CanneryGlaze","tile"),("CanneryResin","resin")):
        yield stem,field(key,(512,512))


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check",action="store_true")
    args=parser.parse_args()
    for stem,pair in sheets():
        for suffix,img in zip(("Albedo","Mask"),pair):
            path=city.OUTPUT/(stem+suffix+".png")
            output=BytesIO()
            img.save(output,format="PNG",compress_level=9)
            if args.check:
                if output.getvalue()!=path.read_bytes():
                    raise SystemExit("Rebake "+path.name)
            else:
                path.write_bytes(output.getvalue())
