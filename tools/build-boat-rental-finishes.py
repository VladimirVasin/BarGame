#!/usr/bin/env python3
"""Print the municipal rental sign and bake the station's shared coastal finishes."""
import argparse
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
CITY = ROOT/'Assets/Resources/City/Surfaces'
W,H = 512,256
COLOURS = [(176,169,141),(138,133,111),(131,116,87),(115,97,72),
           (96,119,104),(100,124,132),(148,130,101),(66,63,51),
           (151,140,112),(74,76,68),(146,143,122),(55,61,54)]


def print_sign():
    # Compose real type in square pixels at the board's installed aspect ratio.
    w,h=1690,320
    out=Image.new('RGB',(w,h),(211,204,169))
    draw=ImageDraw.Draw(out)
    draw.rectangle((18,18,w-19,h-19),outline=(109,107,83),width=4)
    font=ImageFont.truetype(str(ROOT/'Assets/Resources/Fonts/Roboto-Regular.ttf'),213)
    label='ПРОКАТ ЛОДОК'
    box=draw.textbbox((0,0),label,font=font,stroke_width=2)
    draw.text(((w-box[2]-box[0])/2,(h-box[3]-box[1])/2),label,font=font,
              fill=(55,73,64),stroke_width=2,stroke_fill=(55,73,64))
    pixels=np.array(out.resize((4*W,H),Image.Resampling.LANCZOS),dtype=float)
    y,x=np.indices((H,4*W));rng=np.random.default_rng(411)
    edge=np.minimum.reduce([y,H-1-y,x,4*W-1-x])
    worn=(edge<3+2*np.sin(.085*x)+1.6*np.cos(.063*x+.72*y))
    pixels[worn]=np.array((115,105,80))
    pixels+=rng.normal(0,1.6,(H,4*W,1))
    return Image.fromarray(np.uint8(np.clip(pixels,0,255)))


def bake():
    colour=Image.new('RGB',(4*W,4*H))
    mask=Image.new('RGBA',colour.size,(0,0,0,110))
    normal=Image.new('RGB',colour.size,(128,128,255))
    y,x=np.indices((H,W));u=(x+.5)/W;v=(y+.5)/H
    rng=np.random.default_rng(1703)
    for i,tint in enumerate(COLOURS):
        stone=i in (0,1,10)
        donor='Concrete' if stone else ('Hull' if i in (4,5) else 'Plank')
        with Image.open(CITY/('CitySeacoast'+donor+'Albedo.png')) as original:
            source=np.asarray(original.convert('L').resize((W,H),Image.Resampling.BOX))/255.
        donor_detail=source-source.mean()
        grain=np.sin(143*v+2.5*np.sin(u*9+v*11))*.014+np.sin(446*v+4*np.sin(10*u))*.009
        fibre=grain+rng.normal(0,.010,(H,W))+donor_detail*.22
        broad=np.sin(14*u+7*v)+.68*np.sin(39*v-5*u)+.49*np.cos(27*u+91*v)
        finish=np.zeros((H,W,3))+np.array(tint)/255
        if stone:
            fibre=donor_detail*.42+rng.normal(0,.017,(H,W))
            if i==1:
                chips=np.clip((broad*.28+.9*v-.60)*4,0,1)
                finish-=chips[...,None]*np.array((.14,.13,.096))
            elif i==10:
                fibre+=np.maximum(0,v-.69)*-.10
        elif i in (4,5,6):
            flakes=np.clip((broad+.33*np.sin(460*v+6*u)-.73)*2.5,0,1)
            raw=np.array((148,133,105))/255
            finish=finish*(1-flakes[...,None])+raw*flakes[...,None]
        elif i==3:
            fibre=.015*np.sin(96*np.hypot(u-.38,(v-.49)*.66))+donor_detail*.14
        elif i==8:
            wear=np.exp(-((u-.5)/.24)**4)
            finish+=.047*wear[...,None]
            fibre*=1-.53*wear
        elif i==9:
            fibre*=.14
        elif i==11:
            finish+=.038*np.clip(.34-v,0,1)[...,None]
        roughness=(.36 if stone else .43)+fibre*2
        if i==8: roughness+=.22*wear
        if i in (7,11): roughness+=.18 if i==7 else .36
        hfield=fibre*(.0036 if stone else .0050)
        dx=np.gradient(hfield,axis=1)*W/1.7;dy=np.gradient(hfield,axis=0)*H/.34
        n=np.stack((-dx,dy,np.ones_like(dx)),axis=-1);n/=np.linalg.norm(n,axis=-1,keepdims=True)
        m=np.zeros((H,W,4),np.uint8);m[:,:,3]=np.uint8(255*np.clip(roughness,0,1))
        at=((i%4)*W,(i//4)*H)
        colour.paste(Image.fromarray(np.uint8(255*np.clip(finish+fibre[...,None],0,1))),at)
        mask.paste(Image.fromarray(m),at)
        normal.paste(Image.fromarray(np.uint8(255*(.5+.5*n))),at)
    colour.paste(print_sign(),(0,3*H))
    return colour,mask,normal


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--check',action='store_true')
    check=parser.parse_args().check
    for image,suffix in zip(bake(),('Albedo','Mask','Normal')):
        path=CITY/('BoatRental'+suffix+'.png')
        data=BytesIO();image.save(data,format='PNG',compress_level=9)
        if check: assert path.read_bytes()==data.getvalue(),path
        else: path.write_bytes(data.getvalue())
