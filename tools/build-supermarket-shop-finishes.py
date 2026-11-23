#!/usr/bin/env python3
"""Author the registered shop floor and fixture coatings in albedo + URP response."""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import supermarket_floor_rolls as rolls
import supermarket_shop_charts as charts

DEST = Path(__file__).resolve().parents[1] / "Assets/Resources/Supermarket/Textures"


def noise(rng, shape, grid):
    raw = Image.fromarray(rng.integers(0, 256, size=grid, dtype=np.uint8))
    return np.asarray(raw.resize(shape[::-1], Image.Resampling.BICUBIC), dtype=np.float32)/127.5-1


def maps(base, variation, height, roughness, cavity, tint=None):
    rgb = np.asarray(base, dtype=np.float32) + variation[..., None]
    if tint is not None:
        rgb += tint
    dy, dx = np.gradient(height)
    response = np.stack((128-dx*20, 128+dy*20, roughness, cavity), axis=-1)
    return tuple(Image.fromarray(np.clip(v, 0, 255).astype(np.uint8)) for v in (rgb, response))


def path_wear(x, z, points, radius):
    dist = np.full_like(x+z, np.inf)
    for (ax, az), (bx, bz) in zip(points, points[1:]):
        dx, dz = bx-ax, bz-az
        t = np.clip(((x-ax)*dx+(z-az)*dz)/(dx*dx+dz*dz), 0, 1)
        dist = np.minimum(dist, (x-ax-t*dx)**2+(z-az-t*dz)**2)
    return np.exp(-dist/(radius*radius))


def floor_maps(rng):
    shape = (2048, 2048)
    x = ((np.arange(shape[1], dtype=np.float32)[None, :]+.5)/shape[1]-.5)*16
    z = (.5-(np.arange(shape[0], dtype=np.float32)[:, None]+.5)/shape[0])*11
    base = noise(rng, shape, (54, 83))
    fleck = noise(rng, shape, (890, 670))
    streak = noise(rng, shape, (460, 1200))
    wear = path_wear(x,z,[(0,-5.5),(0,-3.6),(2.8,-3.35),(4.8,-2.35)],.64)
    wear = np.maximum(wear, .72*path_wear(x,z,[(0,-3.8),(-1.22,-2.4),(-1.35,2.8),(-.4,4.05)],.56))
    col = np.searchsorted(rolls.EDGES[1:-1], x)
    shade = np.array([0,4,-2,3,0,-2,4,1,-3])[col]
    weld = np.min([abs(x-edge) for edge in rolls.EDGES[1:-1]], axis=0) + np.zeros_like(z)
    for lane, cut in rolls.CROSS_CUTS.items():
        weld = np.minimum(weld, np.where(col == lane, abs(z-cut), 100))
    weld = np.exp(-(weld/.0065)**2)
    wear *= .90+.10*base
    # Short pigmented inclusions and wavy veins are part of the sheet, visible
    # across a room. Grit alone disappears into a flat field in distant mip levels.
    mottling = noise(rng, shape, (135, 188))
    veins = np.exp(-((mottling + .47*base)/.19)**2)
    inclusions = np.maximum(abs(streak)-.43, 0) * np.sign(fleck)
    marbling = 5*base + 7*fleck + 5*streak + 20*inclusions + 8*mottling - 8*veins
    return maps((113,119,103), marbling+1.5*shade+9*wear-25*weld,
                .23*fleck+.75*weld, 187+7*base-54*wear-12*weld, 249-14*weld)


def fixture_maps(rng):
    n = 512
    atlases = (Image.new("RGB",(4*n,2*n)), Image.new("RGBA",(4*n,2*n)))
    y, x = np.mgrid[0:n,0:n].astype(np.float32)/(n-1)
    edge = np.minimum.reduce([x,1-x,y,1-y])
    palette = [(110,139,130),(75,98,98),(149,155,147),(95,111,89),
               (148,148,117),(105,84,65),(58,61,55),(48,65,65)]
    for cell, base in enumerate(palette):
        shape = y.shape
        cloud = noise(rng,shape,(18,22))
        grain = noise(rng,shape,(370,370))
        scratches = Image.new("L",(n,n))
        pen = ImageDraw.Draw(scratches)
        for _ in range(110 if cell == charts.STEEL else 65):
            a,b = rng.integers(6,n-6,size=2)
            length = rng.integers(4,66)
            end = (a+rng.integers(-3,4), b+length) if cell == charts.DECK else (a+length,b+rng.integers(-3,4))
            pen.line((a,b,*end),fill=int(rng.integers(50,205)))
        mark = np.asarray(scratches,dtype=np.float32)/255
        edge_wear = np.exp(-edge*65)*np.maximum(0,.5+.9*cloud+.65*grain)
        value = 6*cloud + 3*grain
        height = .17*grain
        rough = 157 + 6*cloud
        cavity = 250 + 2*cloud
        if cell == charts.LINER:
            ribs = np.clip((np.cos(x*np.pi*22)-.55)*2.6,0,1)
            frost = np.clip((y-.77)*3 + (.13-np.minimum(x,1-x))*2 + cloud*.21+grain*.11,0,.95)
            value += 9*ribs+65*frost
            height += .65*ribs + frost*grain*1.8
            rough += 43*frost
            cavity -= 5*(1-ribs)
        elif cell == charts.STEEL:
            brush = noise(rng,shape,(500,12))
            value += 9*brush+11*mark
            height += .5*brush-.7*mark
            rough += 13*brush-42*mark-32
        elif cell == charts.BELT:
            ribs = np.sin(y*n*.72)*np.sin(x*n*.19)
            value += 5*ribs+5*mark
            height += .64*ribs
            rough += 38-26*mark
        elif cell == charts.GLASS:
            value += 8*mark+3*edge_wear
            height *= .1
            rough = 47+7*cloud+75*mark
        else:
            value += 19*edge_wear+9*mark
            height -= .40*edge_wear+.42*mark
            rough += 11*grain-34*edge_wear-27*mark
        pair = maps(base,value,height,rough,cavity)
        for atlas, tile in zip(atlases,pair):
            atlas.paste(tile, ((cell%4)*n,(cell//4)*n))
    return atlases


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for name, generate, seed in (("FloorRolls",floor_maps,723),("Fittings",fixture_maps,941)):
        for channel, image in zip(("Albedo","Response"),generate(np.random.default_rng(seed))):
            file = DEST / f"Supermarket{name}{channel}.png"
            if args.check:
                with Image.open(file) as existing:
                    if existing.mode != image.mode or existing.size != image.size or existing.tobytes() != image.tobytes():
                        raise SystemExit(f"Stale {file.name}")
            else:
                image.save(file, compress_level=7)
            print(file.name, image.size, image.mode)
