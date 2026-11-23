#!/usr/bin/env python3
"""Pack both illustrated bills and the closed-frontage finishes into one URP atlas."""
import argparse
import hashlib
import json
from io import BytesIO

import numpy as np
from PIL import Image, ImageDraw, ImageFont

import city_surface_authoring as city
from city_cinema_model import ATLAS_SIZE, CHARTS

PRINTS = city.ROOT / "ArtSource/City/Cinema"
FONT = city.ROOT / "Assets/Resources/Fonts/Roboto-Regular.ttf"


def bake():
    rng = np.random.default_rng(341997)
    albedo = Image.new("RGB", ATLAS_SIZE, (97, 76, 62))
    response = Image.new("RGBA", ATLAS_SIZE, (15, 0, 0, 55))
    records = json.loads((PRINTS / "generation.json").read_text(encoding="utf-8"))["images"]
    for key, record in zip(("shift", "balcony"), records, strict=True):
        rect = CHARTS[key]
        source = PRINTS / record["file"]
        if hashlib.sha256(source.read_bytes()).hexdigest() != record["sha256"]:
            raise ValueError(f"Cinema print changed: {source.name}")
        with Image.open(source) as src:
            pixels = np.asarray(src.convert("RGB").resize((512, 768), Image.Resampling.LANCZOS), dtype=np.float32)
        # Sun-bleached ink on old opaque paper; each drawn print keeps its own colours.
        faded = pixels*.77 + np.array((188, 170, 140))*.17
        albedo.paste(Image.fromarray(faded.astype(np.uint8)), rect[:2])
        response.paste((0, 0, 0, 41), rect)

    finishes = (
        ("enamel", "PaintedMetal", (128, 65, 53), .11, .30),
        ("zinc", "Galvanized", (133, 130, 109), .54, .34),
        ("iron", "Rust", (67, 65, 58), .43, .23),
        ("wood", "WeatheredWood", (125, 111, 88), .0, .19),
        ("plaster", "Plaster", (159, 146, 119), .0, .15),
        ("stone", "Stone", (116, 113, 95), .0, .24),
        ("glass", "Enamel", (63, 76, 70), .0, .44),
        ("paper", "Paper", (160, 143, 114), .0, .13),
    )
    for key, family, tint, metal, gloss in finishes:
        rect = CHARTS[key]
        height, width = rect[3]-rect[1], rect[2]-rect[0]
        y,x = (np.indices((height,width))+.5) / np.array([height,width])[:,None,None]
        rgb,_ = city.sample(family, x*1.7, y*.85, tint=np.array(tint)/255, pixel_metres=1.7/width)
        grain = rng.normal(0,.011,x.shape)
        variation = grain.copy()
        if key == "wood":
            # The two boards were sawn lengthwise. No cross-grain grid on their broad faces.
            rgb = .12*rgb + .88*np.array(tint)/255
            knot = .042*np.exp(-((x-.63)/.26)**2-((y-.47)/.19)**2)
            rings = y + knot + .006*np.sin(x*7+2*np.pi*y)
            variation += .035*np.sin(2*np.pi*17*rings) + .019*np.sin(2*np.pi*49*rings)
        if key in ("plaster","enamel","zinc","glass"):
            # Narrow runs beneath the upper lip and broader dust at the foot of each panel.
            strand = np.maximum(0,np.cos(x*88+np.sin(x*19)))**15
            variation -= strand*.071*np.exp(-y*(4+2*np.sin(x*14))) + .032*y**4
        if key == "enamel":
            variation -= .029*np.maximum(0,np.sin(x*19+y*7))**8
        albedo.paste(Image.fromarray(np.rint(255*np.clip(rgb + variation[...,None],0,1)).astype(np.uint8)), rect[:2])
        mask = np.zeros((height,width,4),np.uint8)
        mask[:,:,0] = np.rint(255*np.clip(metal+grain,0,1))
        mask[:,:,3] = np.rint(255*np.clip(gloss+variation*.8,0,1))
        response.paste(Image.fromarray(mask), rect[:2])

    labels = {"marquee":"К И Н О", "ticket":"КАССА", "times":"СЕАНСЫ"}
    labels.update({f"letter{i}": letter for i,letter in enumerate("КИНО")})
    for key,label in labels.items():
        rect = CHARTS[key]
        w,h = rect[2]-rect[0], rect[3]-rect[1]
        plate = np.array(albedo.crop(CHARTS["enamel"]).resize((w,h)),dtype=np.float32)
        ink = Image.new("L",(w,h),0)
        draw = ImageDraw.Draw(ink)
        size = h
        while True:
            font = ImageFont.truetype(str(FONT), size)
            left,top,right,bottom = font.getbbox(label)
            if right-left < w*.88 and bottom-top < h*.77: break
            size -= 2
        draw.text(((w-right+left)/2-left,(h-bottom+top)/2-top),label,font=font,fill=255,stroke_width=1,stroke_fill=255)
        coverage = np.asarray(ink,dtype=np.float32)/255
        wear = rng.uniform(0,1,(h,w))
        coverage *= np.where(wear<.07,.35,.95)
        paint = np.array((224,205,157)) + rng.normal(0,4,(h,w,1))
        plate = plate*(1-coverage[...,None]) + paint*coverage[...,None]
        albedo.paste(Image.fromarray(np.clip(plate,0,255).astype(np.uint8)),rect[:2])
        response.paste((28,0,0,71),rect)
    return albedo,response


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for suffix, sheet in zip(("Albedo","Mask"),bake()):
        path = city.OUTPUT / ("ClosedCinema"+suffix+".png")
        stream = BytesIO()
        sheet.save(stream,format="PNG",compress_level=9)
        if args.check:
            if path.read_bytes() != stream.getvalue():
                raise SystemExit(f"Rebake {path.name}")
        else:
            path.write_bytes(stream.getvalue())
