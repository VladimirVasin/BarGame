#!/usr/bin/env python3
"""Bake fitted pawnshop finishes. Physical dimensions below are in metres."""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/Resources/Pawnshop/Textures"


def bake(family, size, width, depth, seed):
    rng = np.random.default_rng(seed)
    v, u = np.mgrid[0:size,0:size].astype(np.float32)
    u = (u + .5) / size
    v = 1 - (v + .5) / size
    x, z = u*width, v*depth
    def noise(rows, cols):
        coarse = Image.fromarray(rng.integers(0,256,(rows,cols),dtype=np.uint8))
        return np.asarray(coarse.resize((size,size),Image.Resampling.BICUBIC),dtype=np.float32)/127.5-1
    grain = noise(size,size)
    cloud = noise(17,19)
    if family == "Glass":
        # The profile protects a narrow dusty border. The large centre is clear.
        edge = np.minimum.reduce([u,1-u,v,1-v])
        dust = np.exp(-edge*(78+14*cloud))*(.76+.18*cloud+.06*grain)
        rgb = np.array((164,186,178))+dust[...,None]*np.array((49,26,13))
        return Image.fromarray(np.dstack((rgb,57+183*dust)).clip(0,255).astype('uint8')), None
    if family == "Floor":
        x,z = x-3.85,z-4.35
        pitch=np.minimum.reduce([abs(x+1.83),abs(x-.12),abs(x-2.05)])
        pitch=np.minimum(pitch,np.where(x < -1.83,abs(z+.62),10))
        pitch=np.minimum(pitch,np.where((x>.12)&(x<2.05),abs(z-1.14),10))
        seam=np.exp(-pitch*pitch/.000014)
        lane=np.exp(-((x-.13*np.sin(z*1.6))/.57)**2)*np.clip((.4-z)*2,0,1)
        fleck = noise(size//3,size//3)
        pigment = 4*grain+8*fleck+3*cloud + 4*lane - 28*seam
        pigment += np.select([x<-1.83,x<.12,x<2.05],[2,-1,3],default=-2)
        height = .00012*fleck-.00075*seam
        roughness = .79-.15*lane+.04*grain+.06*seam
        color = (169,169,148)
    elif family == "Counter":
        fibre=noise(270,25)+.44*np.sin(z*610+4*cloud)
        wear=np.exp(-((z-.025)/.055)**2)*(.55+.45*np.exp(-((x-2.74)/.78)**2))
        pigment=8*fibre+4*cloud+2*grain+24*wear
        height=.00012*fibre
        roughness=.62-.18*wear+.034*fibre
        color=(127,101,72)
    else:
        # Fine drawing marks run with the transfer direction. Individual short
        # scratches accumulate where objects slide across the shallow trough.
        brushed = noise(17,size) if family=="Tray" else noise(size,25)
        abrasion=Image.new('L',(size,size))
        pen=ImageDraw.Draw(abrasion)
        for _ in range(280 if family=="Tray" else 75):
            cx = rng.normal(.5,.18)*size
            cy = rng.uniform(.04,.96)*size
            pen.line((cx,cy,cx+rng.normal(0,.014)*size,cy+rng.uniform(.012,.12)*size),
                     fill=int(rng.integers(55,185)))
        marks=np.asarray(abrasion,dtype=np.float32)/255
        wear=np.exp(-((u-.5)/.30)**4)*np.exp(-((v-.48)/.46)**4) if family=="Tray" else 0
        pigment=5*brushed+2*grain+4*cloud+12*wear+18*marks
        height=.000024*brushed-.00010*marks
        roughness=.65-.14*wear+.12*marks+.04*brushed
        color=(134,138,133) if family=="Tray" else (200,198,180)
    rgb=np.clip(np.array(color)+pigment[...,None],0,255).astype('uint8')
    dv,du=np.gradient(height,depth/size,width/size)
    response=np.stack((.5+.5*du,.5-.5*dv,roughness,np.ones_like(u)),axis=-1)
    response=Image.fromarray(np.uint8(np.clip(response,0,1)*255)).resize((size//2,size//2),Image.Resampling.BOX)
    return Image.fromarray(rgb),response


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--validate-only',action='store_true')
    check=parser.parse_args().validate_only
    OUT.mkdir(exist_ok=True)
    for i,(family,size,w,d) in enumerate((('Tray',512,.752,.845),('Counter',1024,5.12,1.30),
                                         ('Glass',256,1.,1.),('Zinc',512,.53,.53),('Floor',2048,7.7,8.7))):
        for channel,image in zip(('Albedo','Response'),bake(family,size,w,d,1873+i)):
            if image is None: continue
            path=OUT/('Pawnshop'+family+channel+'.png')
            if check:
                with Image.open(path) as previous:
                    if previous.size!=image.size or previous.tobytes()!=image.tobytes():
                        raise ValueError('Stale finish: '+path.name)
            else:
                image.save(path)
    print('Pawnshop fitted finishes OK')


if __name__=='__main__':
    main()
