#!/usr/bin/env python3
"""Bake cut stone, damp plinths, formwork, oxide and depressed wheel marks."""
import argparse
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image

SURFACES = Path(__file__).resolve().parents[1]/'Assets/Resources/City/Surfaces'
N = 512
# Pigment, source relief and dry smoothness of each construction finish.
FINISHES = [
    ((155,150,133),'CityMountainRock',.27), ((174,166,143),'CityMountainRock',.24),
    ((106,111,96),'CityMountainRock',.37), ((132,132,117),'CityFringeMasonry',.25),
    ((78,79,69),'CityFringeConcrete',.18), ((148,143,124),'CityFringeConcrete',.30),
    ((83,89,82),'BuildingIndustrialMetal',.44), ((77,69,55),'CityFringeForefield',.32)]


def bake():
    albedo=Image.new('RGB',(2*N,4*N))
    normal=Image.new('RGB',albedo.size)
    mask=Image.new('RGBA',albedo.size)
    y,x=np.indices((N,N))/N
    for i,(tint,donor,smooth) in enumerate(FINISHES):
        rng=np.random.default_rng(7126+i)
        with Image.open(SURFACES/(donor+'Albedo.png')) as surface:
            rock=np.asarray(surface.convert('L').resize((N,N),Image.Resampling.LANCZOS),dtype=float)/255
        rock-=rock.mean()
        noise=rng.normal(0,1,(N,N))
        broad=np.asarray(Image.fromarray(rng.integers(0,256,(14,14),dtype=np.uint8))
                         .resize((N,N),Image.Resampling.BICUBIC),dtype=float)/255-.5
        colour=np.zeros((N,N,3))+np.array(tint)
        height=.35*rock+.014*noise+.04*broad
        metal=np.zeros_like(x)
        smoothness=np.full_like(x,smooth)
        if i in (0,1,2,3):
            tool=np.maximum(0,np.cos((x*116+y*63+broad*.4)*np.pi*2)-.83)
            veins=np.exp(-((np.sin(x*11+y*8+broad*.9))/.12)**2)
            height+=tool*.09+veins*.016
            colour+=(31*broad-7*veins+13*tool)[...,None]
            if i==2:
                damp=np.clip(1.3*y+.31*broad-.28,0,1)
                colour-=damp[...,None]*np.array([24,19,13])
                smoothness+=damp*.19
        elif i==5:
            boards=(y*7+.025*np.sin(x*10))%1
            seam=np.exp(-((boards-.07)/.035)**2)
            pores=np.maximum(0,-noise-1.6)
            height-=seam*.07+pores*.025
            colour+=(np.floor(y*7)%2*3-8*seam+26*broad-8*pores)[...,None]
        elif i==6:
            rust=np.clip((broad+.5*rock-.15)*4,0,1)
            colour+=rust[...,None]*np.array([39,2,-18])
            metal=.44*(1-rust)
            height+=.05*rust
            smoothness+=.14*(1-rust)
        elif i==7:
            # Pressed centre and pushed-up shoulders, repeated along the wheel path.
            lateral=abs(x-.5)*2
            depression=np.exp(-(lateral/.56)**4)
            shoulder=np.exp(-((lateral-.72)/.15)**2)
            tread=np.maximum(0,np.cos(2*np.pi*(y*10+abs(x-.5)*2.8))-.58)*depression
            height+=-.85*depression+.25*shoulder-.08*tread
            aggregate=np.asarray(Image.fromarray(rng.integers(0,256,(180,65),dtype=np.uint8))
                                 .resize((N,N),Image.Resampling.BILINEAR),dtype=float)/255-.5
            colour+=(3*shoulder-7*depression-7*tread+23*broad+70*rock+24*aggregate)[...,None]
            smoothness+=depression*.16
        colour+=(rock*57+noise*2.1)[...,None]
        dx=np.gradient(height,axis=1)*1.75
        dy=np.gradient(height,axis=0)*1.75
        if i==7: dx+=np.gradient(-depression+.32*shoulder,axis=1)*12
        n=np.stack((-dx,dy,np.ones_like(x)),axis=-1)
        n/=np.linalg.norm(n,axis=-1,keepdims=True)
        masks=np.zeros((N,N,4),dtype=np.uint8)
        masks[:,:,0]=np.uint8(255*metal)
        masks[:,:,3]=np.uint8(255*np.clip(smoothness+.13*rock,0,1))
        at=(i%2*N,i//2*N)
        albedo.paste(Image.fromarray(np.uint8(np.clip(colour,0,255))),at)
        normal.paste(Image.fromarray(np.uint8(127.5*(n+1))),at)
        mask.paste(Image.fromarray(masks),at)
    return albedo,normal,mask


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check',action='store_true')
    check=parser.parse_args().check
    for image,suffix in zip(bake(),('Albedo','Normal','Mask')):
        path=SURFACES/('SouthTunnel'+suffix+'.png')
        buf=BytesIO()
        image.save(buf,format='PNG',compress_level=8)
        if check:
            assert buf.getvalue()==path.read_bytes(),path
        else:
            path.write_bytes(buf.getvalue())
