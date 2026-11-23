#!/usr/bin/env python3
"""Bake the checkpoint's UV charts against measured City pigments, in linear response space."""
import argparse
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image
import east_checkpoint_uv as kit

ROOT = Path(__file__).resolve().parents[1]
SURFACES = ROOT / 'Assets/Resources/City/Surfaces'
N = 512
# RGB pigment, donor detail, bare-metal fraction, smoothness.
FINISHES = [
    ((157,153,125),'ChurchPlaster',0,.23), ((159,154,124),'ChurchPlaster',0,.23),
    ((124,121,103),'CityFringeConcrete',0,.27), ((138,135,122),'CityFringeConcrete',0,.29),
    ((102,116,107),'PortRoofMetal',.29,.53), ((151,154,141),'BuildingIndustrialMetal',.58,.51),
    ((101,118,103),'CityParkPaintedMetal',.12,.43), ((80,87,80),'BuildingIndustrialMetal',.40,.40),
    ((158,155,137),'BuildingIndustrialMetal',.66,.62), ((71,90,84),'BuildingIndustrialMetal',0,.38),
    ((190,182,148),'BuildingIndustrialMetal',.06,.46), ((138,119,88),'ChurchWood',0,.34),
    ((107,121,105),'CityParkPaintedMetal',.12,.43), ((89,88,76),'CityFringeConcrete',0,.22),
    ((57,62,55),'BuildingIndustrialMetal',.07,.24), ((119,124,106),'CityParkPaintedMetal',.24,.44)]


def field(rng, width, height):
    pixels=rng.integers(0,256,(height,width),dtype=np.uint8)
    return np.asarray(Image.fromarray(pixels).resize((N,N),Image.Resampling.BICUBIC),float)/255-.5


def bake():
    pigment=Image.new('RGB',(N*4,N*4))
    normal=Image.new('RGB',pigment.size)
    mask=Image.new('RGBA',pigment.size)
    y,x=(np.indices((N,N))+.5)/N
    for i,(tint,donor,metal,smooth) in enumerate(FINISHES):
        rng=np.random.default_rng(4710+i)
        with Image.open(SURFACES/(donor+'Albedo.png')) as im:
            grain=np.asarray(im.convert('L').resize((N,N),Image.Resampling.LANCZOS),float)/255
        grain-=grain.mean()
        broad=field(rng,14,14)
        pits=field(rng,185,190)
        fine=rng.normal(0,1,(N,N))
        rgb=np.broadcast_to(tint,(N,N,3)).astype(float).copy()
        height=.34*grain+.041*pits+.005*fine
        smoothness=np.full_like(x,smooth)
        steel=np.full_like(x,metal)
        if i in (kit.PLASTER,kit.RENDER):
            splash=np.clip((y-.69+broad*.078)*3.25,0,1)
            rgb-=splash[...,None]*[34,31,20]
            if i==kit.PLASTER:
                # Sill ends are at +/-1.015 m along a 3.40 m wall. Runoff starts below them.
                below=np.clip((y-.58)*18,0,1)*np.clip((.99-y)*4,0,1)
                trails=sum(np.exp(-((x-c-.006*broad)/w)**2) for c,w in
                           ((.203,.022),(.232,.009),(.797,.031),(.769,.011)))
                rgb-= (trails*below)[...,None]*[39,38,27]
            height+=.052*pits
        elif i in (kit.PLINTH,kit.FOOTING,kit.MORTAR):
            gravel=np.maximum(0,-fine-1.25)
            rgb+=(32*broad-7*gravel)[...,None]
            height-=gravel*.038
        elif i == kit.GLASS:
            drips=field(rng,115,3)
            rim=np.minimum.reduce([x,1-x,y,1-y])
            dirt=np.clip((.10-rim+broad*.035)*7,0,1)*(.70+drips*.5)
            rgb+=(dirt*39+drips*10+broad*12)[...,None]
            smoothness-=.15*dirt
            height=.001*fine
        elif i == kit.TIMBER:
            fibres=np.sin(2*np.pi*(y*101+np.sin(9*x)*.7+3*broad))
            rgb+=(fibres*6+21*broad)[...,None]
            height+=fibres*.028
        else:
            if i==kit.STRIPE:
                # Muted oxide bands are paint on the boom, continuous round its edges.
                band=((x*8.5+.17*y)%1)<.37
                rgb[band]=[126,95,77]
            wear=np.clip((broad+pits*.4+grain*.6-.22)*4,0,1)
            if i in (kit.ENAMEL,kit.DOOR,kit.STRIPE,kit.IRON,kit.REPAIR):
                rgb=rgb*(1-wear[...,None])+np.array([117,99,78])*wear[...,None]
                steel+=wear*.10
                smoothness-=wear*.15
            elif i in (kit.ZINC,kit.ROOF):
                mottling=field(rng,47,40)
                rgb+=(19*mottling+9*np.sin(x*26+broad*3))[...,None]
                steel-=np.maximum(0,broad)*.3
            elif i==kit.STEEL:
                brush=field(rng,4,205)
                rgb+=(brush*16)[...,None]
                height=.006*fine
        rgb+=(41*grain+16*broad+2*fine)[...,None]
        nx=-np.gradient(height,axis=1)*2.7
        ny=np.gradient(height,axis=0)*2.7
        n=np.stack((nx,ny,np.ones_like(x)),axis=-1)
        n/=np.linalg.norm(n,axis=-1,keepdims=True)
        m=np.zeros((N,N,4),np.uint8)
        m[:,:,0]=np.uint8(255*np.clip(steel,0,1))
        m[:,:,3]=np.uint8(255*np.clip(smoothness+grain*.15,0,1))
        at=(i%4*N,i//4*N)
        pigment.paste(Image.fromarray(np.uint8(np.clip(rgb,0,255))),at)
        normal.paste(Image.fromarray(np.uint8((n+1)*127.5)),at)
        mask.paste(Image.fromarray(m),at)
    return pigment,normal,mask


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check',action='store_true')
    check=parser.parse_args().check
    for sheet,suffix in zip(bake(),('Albedo','Normal','Mask')):
        path=SURFACES/('EastCheckpoint'+suffix+'.png')
        encoded=BytesIO()
        sheet.save(encoded,format='PNG',compress_level=8)
        if check:
            assert path.read_bytes()==encoded.getvalue(),path
        else:
            path.write_bytes(encoded.getvalue())
