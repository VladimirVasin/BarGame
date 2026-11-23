#!/usr/bin/env python3
"""Bake the shelter's enamel, exposed metal, seat planks and obsolete transport prints."""
import argparse
from io import BytesIO

import numpy as np
from PIL import Image, ImageDraw, ImageFont

import city_surface_authoring as city
from city_last_route_model import CHARTS, SIZE

FONT = city.ROOT / "Assets/Resources/Fonts/Roboto-Regular.ttf"


def transit_print(key, size):
    """Municipal inks and paper on the component face. No valid destination or time."""
    w,h = size
    image = Image.new("RGB",size,(172,160,133))
    pen = ImageDraw.Draw(image)
    teal, wine, ink, ivory = (72,102,98),(136,84,83),(71,74,68),(195,187,159)
    if key == "departures":
        pen.rectangle((0,0,w,64),fill=teal)
        pen.text((22,4),"ОТПРАВЛЕНИЕ",font=ImageFont.truetype(str(FONT),43),fill=ivory)
        pen.rectangle((0,63,w,70),fill=wine)
        for i in range(3):
            y = 82+53*i
            pen.rectangle((17,y,493,y+43),fill=ink)
            # Empty letter tracks, with patches left where individual slugs were lost.
            for j in range(15):
                x=22+j*31
                shade= (94,94,82) if (i,j) in ((1,8),(1,9),(0,13),(2,2)) else (78,81,73)
                pen.rectangle((x,y+4,x+27,y+37),fill=shade)
            pen.line((18,y+42,492,y+42),fill=(132,128,111),width=2)
    elif key == "transfer":
        pen.rectangle((0,0,w,39), fill=teal)
        pen.rectangle((0,41,w,46), fill=wine)
        pen.line([(0,195),(77,195),(143,130),(143,34)],fill=teal,width=14)
        pen.line([(23,256),(23,226),(185,64),(256,64)],fill=wine,width=8)
        for x,y in ((40,195),(91,181),(143,126),(143,64)):
            pen.ellipse((x-8,y-8,x+8,y+8),fill=ink)
            pen.ellipse((x-4,y-4,x+4,y+4),fill=ivory)
        pen.polygon([(182,253),(179,213),(195,208),(184,184),(219,173),(210,161),(256,153),(256,256)],fill=ivory)
    elif key == "bill":
        pen.rectangle((0,0,w,h),fill=wine)
        for offset in (0,65):
            pen.polygon([(15+offset,65),(53+offset,65),(121+offset,126),(51+offset,193),
                         (14+offset,193),(82+offset,126)],fill=ivory)
        pen.rectangle((0,213,w,220),fill=teal)
        pen.polygon([(0,0),(68,0),(61,18),(40,21),(46,33),(14,43),(0,67)],fill=(173,154,130))
    else:
        pen.rectangle((10,12,w-12,57), fill=teal)
        pen.text((17,17),"РЕЙСЫ",font=ImageFont.truetype(str(FONT),30),fill=ivory)
        for x in (17,116,171,238):
            pen.line((x,73,x,242),fill=ink,width=2)
        for y in range(73,247,23):
            pen.line((17,y,238,y),fill=ink,width=2)
        pen.polygon([(0,214),(55,203),(80,216),(125,196),(139,206),(177,187),(188,202),(256,179),(256,256),(0,256)],fill=(184,171,146))
    return np.asarray(image,dtype=np.float32)/255


def bake():
    rng = np.random.default_rng(724029)
    albedo = Image.new("RGB",(SIZE,SIZE))
    mask = Image.new("RGBA",(SIZE,SIZE))
    recipes = {
        "departures":("Enamel",(150,146,123),.09,.28), "transfer":("Paper",(168,153,131),0,.16),
        "bill":("Paper",(168,153,131),0,.14), "timetable":("Paper",(168,153,131),0,.13),
        "roof":("Galvanized",(119,139,137),.35,.32), "enamel":("PaintedMetal",(70,104,101),.15,.30),
        "zinc":("Galvanized",(149,151,137),.62,.39), "iron":("Rust",(79,74,65),.36,.22),
        "wood":("WeatheredWood",(141,111,84),0,.22), "end":("WeatheredWood",(115,95,75),0,.17),
        "cloth":("Cloth",(135,88,84),0,.14), "glass":("Enamel",(64,88,78),.04,.59),
        "paper":("Paper",(169,157,131),0,.14),
    }
    for key,(family,tint,metal,gloss) in recipes.items():
        l,t,r,b = CHARTS[key]
        h,w = b-t,r-l
        y,x=(np.indices((h,w),dtype=np.float32)+.5)/np.array([h,w],np.float32)[:,None,None]
        rgb,_=city.sample(family,x*1.7,y*.85,tint=np.array(tint)/255,pixel_metres=1.7/w)
        grain=rng.normal(0,.013,(h,w))
        if key in ("departures","transfer","bill","timetable"):
            printed=transit_print(key,(w,h))
            fibre=rgb.mean(axis=2)-rgb.mean()
            edge=np.minimum.reduce((x,1-x,y,1-y))
            scuff=np.clip(.1 + 5*(.04-edge)+grain*5+.08*np.sin(x*49+y*19),0,.65)
            rgb=printed*(1-scuff[...,None]) + np.array(tint)/255*scuff[...,None]+fibre[...,None]
        elif key in ("wood","end"):
            # A grain line bends around a knot. Crosscuts expose the annual rings.
            knot=.095*np.exp(-((x-.71)/.14)**2-((y-.54)/.27)**2)
            line = (y+knot+.009*np.sin(x*12+y*3) if key=="wood" else
                    np.hypot((x-.72)*.94,(y-1.16)*.59))
            fibre=.040*np.sin(95*line)+.021*np.sin(253*line)+.013*np.sin(461*line)
            wear=np.clip((.24-y)*2,0,.42) if key=="wood" else 0
            rgb=.28*rgb+.72*np.array(tint)/255+fibre[...,None]+np.asarray(wear)[...,None]*.13
            gloss=gloss+wear*.26
        elif key in ("roof","enamel","zinc"):
            drip=np.maximum(0,np.cos(44*x+3*np.sin(x*11)))**15 * np.exp(-3*y)
            edge=np.minimum.reduce((x,1-x,y,1-y))
            exposed=np.clip((.025-edge+grain*.55+.009*np.sin(x*43+y*37))*26,0,.84)
            rust=np.array((127,98,77))/255
            rgb=rgb*(1-exposed[...,None])+rust*exposed[...,None]-drip[...,None]*.075
            metal=metal*(1-exposed*.62)
            gloss=gloss*(1-exposed*.45)
        elif key=="cloth":
            weave = .012*np.sin(2*np.pi*x*33)*np.cos(2*np.pi*y*40)
            band = np.isin(np.floor(x*48).astype(int),[4,6,41,43])
            rgb += (weave+.13*band)[...,None]
        colour=np.rint(255*np.clip(rgb+grain[...,None],0,1)).astype(np.uint8)
        packed=np.zeros((h,w,4),np.uint8)
        packed[:,:,0]=np.rint(255*np.clip(metal+grain*.3,0,1))
        packed[:,:,3]=np.rint(255*np.clip(gloss+grain,0,1))
        albedo.paste(Image.fromarray(colour),(l,t))
        mask.paste(Image.fromarray(packed),(l,t))
    return albedo,mask


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check",action="store_true")
    args=parser.parse_args()
    for suffix,sheet in zip(("Albedo","Mask"),bake()):
        path=city.OUTPUT/("LastRouteIsland"+suffix+".png")
        data=BytesIO()
        sheet.save(data,format="PNG",compress_level=9)
        if args.check:
            if path.read_bytes()!=data.getvalue():
                raise SystemExit("Rebake "+path.name)
        else:
            path.write_bytes(data.getvalue())
