#!/usr/bin/env python3
"""Bake the apartment's fitted finishes. RGB is pigment, alpha is dry smoothness."""
import argparse
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
import home_furniture_charts as c

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/"Assets/Resources/Home/Textures/HomeFurnitureFinishes.png"
PALETTE=((136,119,91),(91,105,89),(187,179,150),(103,119,101),
         (130,136,113),(152,145,117),(169,173,160),(148,147,130),
         (169,171,147),(177,177,153),(81,80,67),(118,129,116),
         (127,102,74),(106,119,108),(98,107,91),(131,133,119))


def bake():
    n=512
    atlas=Image.new("RGBA",(n*4,n*4))
    y,x=np.mgrid[0:n,0:n].astype(np.float32)/(n-1)
    edge=np.minimum.reduce((x,y,1-x,1-y))
    for tile,base in enumerate(PALETTE):
        rng=np.random.default_rng(1307+tile)
        def noise(h,w):
            im=Image.fromarray(rng.integers(0,256,(h,w),dtype=np.uint8))
            return np.asarray(im.resize((n,n),Image.Resampling.BICUBIC),dtype=np.float32)/127.5-1
        cloud=noise(16,19)
        grain=noise(487,493)
        stroke=Image.new("L",(n,n)); pen=ImageDraw.Draw(stroke)
        for _ in range(115):
            a,b=rng.integers(5,n-5,size=2)
            pen.line((a,b,a+rng.integers(7,47),b+rng.integers(-6,7)),fill=int(rng.integers(90,205)))
        scratch=np.array(stroke)/255
        value=cloud*5+grain*3
        gloss=78+cloud*8
        accent=np.zeros((n,n,3),np.float32)
        if tile in (c.SEAT,c.BACK,c.PILLOW,c.COVER):
            step=9 if tile in (c.SEAT,c.BACK) else 6
            warp=np.sin((x*n+cloud*.7)*2*np.pi/step)
            weft=np.sin((y*n+cloud)*2*np.pi/(step-1))
            weave=(warp+weft)*2.4+warp*weft*2
            seam=np.exp(-((edge-.028)/.0038)**2)
            stitches=seam*(.5+.5*np.cos((x+y)*n*.94))
            rub=np.exp(-((x-.52)/.37)**2-((y-(.83 if tile==c.SEAT else .5))/.24)**2)
            value+=weave+noise(81,106)*3+rub*(6+cloud*4)-13*seam+27*stitches
            if tile in (c.SEAT,c.BACK):
                stripe=np.cos(x*np.pi*24+.1*cloud)
                value+=4*stripe+3*np.cos(x*np.pi*48)
                accent[...,0]+=3*stripe
            if tile==c.PILLOW:
                ticking=np.cos(x*np.pi*39+cloud*.13)
                value-=6*np.maximum(0,ticking-.30)
                accent[...,0]-=3*rub
            if tile==c.COVER: value+=6*np.cos(y*np.pi*16)
            gloss=39+rub*24+grain*4
        elif tile in (c.PANEL,c.COUNTER,c.JOINERY,c.THRESHOLD):
            fibre=noise(300,10)+.38*np.sin(160*y+4*cloud)
            value+=7*fibre + 6*noise(57,8)
            wear=np.exp(-edge*45)*(.7+.3*cloud)
            if tile==c.THRESHOLD:
                # The walking direction is across the grain, only the middle of the sill is polished.
                wear=np.exp(-((x-.52)/.235)**2)*(1-.35*cloud)
            if tile==c.JOINERY:
                wear*=np.clip(.55+cloud*1.1,0,1)
                accent[...,0]+=19*wear;accent[...,2]-=8*wear
            band=np.exp(-((edge-.014)/.003)**2) if tile in (c.PANEL,c.COUNTER) else 0
            value+=11*wear - 9*band + 7*scratch
            gloss=78+73*wear+6*fibre
        elif tile in (c.SINK,c.HARDWARE,c.ENAMEL,c.LINER,c.RUBBER):
            if tile==c.SINK:
                radius=np.sqrt(((x-.50)*1.4)**2+(y-.5)**2)
                circles=np.sin(radius*500+noise(8,9)*.8)
                value+=4*circles+5*noise(420,14)+12*scratch
                gloss=143+15*circles-40*scratch
            elif tile==c.HARDWARE:
                brushed=noise(450,15)
                value+=10*brushed+13*scratch
                gloss=156+14*brushed-45*scratch
            elif tile==c.RUBBER:
                value+=4*grain+4*np.cos(29*y)
                gloss=49+8*grain
            else:
                chips=np.exp(-edge*66)*np.maximum(0,.45+cloud*.85+grain*.38)
                accent[...,1]-=20*chips;accent[...,2]-=27*chips
                value-=11*chips
                value+=8*scratch
                gloss=130+cloud*12-55*chips-25*scratch
        else:
            rust=np.zeros_like(x)
            for origin in (.085,.28,.71,.89):
                rust+=np.exp(-((x-origin)/(.012+.017*y))**2)*np.exp(-y*3.7)
            if tile==c.SLAB:
                # Anchors on the exposed balcony edges collect runoff; inner slab stays dry.
                rust*=np.exp(-np.minimum(x,1-x)*8)
                value+=9*noise(156,183)+5*grain
            else:
                wear=np.exp(-((y-.50)/.30)**2) if tile==c.CAP else np.exp(-edge*45)
                value+=13*wear + 9*scratch
            accent[...,0]+=22*rust;accent[...,1]-=11*rust;accent[...,2]-=19*rust
            gloss=(51 if tile==c.SLAB else 105)+cloud*11-34*rust
        rgb=np.clip(np.asarray(base)+value[...,None]+accent,0,255)
        rgba=np.dstack((rgb,np.clip(gloss,0,255))).astype(np.uint8)
        atlas.paste(Image.fromarray(rgba),(tile%4*n,tile//4*n))
    return atlas


def household_atlas():
    n=256
    y,x=np.mgrid[0:n,0:n].astype(np.float32)/(n-1)
    atlas=Image.new("RGBA",(4*n,2*n))
    palette=((180,175,148),(78,93,66),(155,145,113),(115,121,103),
             (132,128,105),(166,157,130),(75,65,43),(137,130,111))
    for i,base in enumerate(palette):
        rng=np.random.default_rng(468+i)
        cell=16+i
        im=Image.fromarray(rng.integers(0,256,(17,19),dtype=np.uint8))
        cloud=np.asarray(im.resize((n,n),Image.Resampling.BICUBIC),np.float32)/127.5-1
        grain=rng.normal(0,1,(n,n))
        value=5*cloud+2*grain
        gloss=70+cloud*12
        if cell==c.PLATE:
            radius=np.hypot(x-.5,y-.5)
            ring=np.exp(-((radius-.443)/.009)**2)
            value+=5*np.sin(radius*300)-36*ring
            gloss+=74-24*ring
        elif cell==c.BOTTLE:
            value+=15*np.exp(-((x-.31)/.10)**2)-12*np.exp(-((y-.77)/.09)**2)
            gloss+=92
        elif cell==c.PAPER:
            value+=13*np.exp(-abs(x+.17*y-.53)*66)+2*grain
            gloss-=33
        elif cell==c.SPILL:
            margin=np.hypot((x-.5)*1.05,y-.5)
            value-=20*np.exp(-((margin-.36-.045*cloud)/.024)**2)
            gloss+=24*cloud
        else:
            warp=np.cos(2*np.pi*x*n/5)
            weft=np.sin(2*np.pi*y*n/6)
            value+=3*(warp+weft)+2*warp*weft
            if cell==c.TOWEL: value-=19*np.exp(-((abs(y-.5)-.39)/.017)**2)
            if cell==c.SHIRT: value-=11*np.maximum(0,np.cos(37*x)*np.cos(39*y))
            if cell==c.DUVET: value+=7*np.cos((x+y)*32)+4*np.cos((x-y)*32)
            if cell==c.SHEET: value-=22*np.maximum(0,cloud+.12)
            gloss-=28
        rgb=np.asarray(base)+value[...,None]
        rgba=np.dstack((np.clip(rgb,0,255),np.clip(gloss,0,255))).astype(np.uint8)
        atlas.paste(Image.fromarray(rgba),(i%4*n,i//4*n))
    return atlas


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check",action="store_true")
    check=parser.parse_args().check
    for path,image in ((OUT,bake()),(OUT.with_name("HomeHouseholdFinishes.png"),household_atlas())):
        if check:
            with Image.open(path) as stored:
                if stored.mode!=image.mode or stored.size!=image.size or stored.tobytes()!=image.tobytes():
                    raise SystemExit(f"Stale {path.name}")
        else:
            image.save(path,compress_level=7)
        print(path.name,image.size,image.mode)
