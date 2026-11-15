#!/usr/bin/env python3
"""Author the sedan's panel atlas and metre-scale surface response maps."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import numpy as np

sys.dont_write_bytecode = True
from atlas_kit import PixelCanvas
from last_route_car_cabin_atlas import PATCHES, PATCH_GRID

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / "Assets/Vehicles/Textures"
SEED = 470303


def coordinates(size):
    unit = (np.arange(size, dtype=np.float64) + .5) / size
    return np.meshgrid(unit, unit[::-1])


def wave(u, v, size, phase=0.):
    return (np.sin(math.tau * (u * size + np.sin(v * 9.) * .23) + phase)
            * np.cos(math.tau * (v * (size + 3) + np.sin(u * 11.) * .19)))


def rgba(r, g, b, a=1.):
    return np.clip(np.round(np.stack(np.broadcast_arrays(r, g, b, a), axis=-1) * 255), 0, 255).astype(np.uint8)


def normals(height, factor):
    dy, dx = np.gradient(height)
    x = -dx * factor
    y = dy * factor
    inverse = 1. / np.sqrt(x*x + y*y + 1.)
    return rgba(.5 + x * inverse * .5, .5 + y * inverse * .5, .5 + inverse * .5)


def pressure(u, z, x, y, w, h):
    return np.exp(-((u-x)/w)**2 - ((z-y)/h)**2)


def paint_atlas(rng):
    u,v = coordinates(1024)
    grain = rng.normal(0., .23, u.shape) + .35*wave(u,v,74) + .41*wave(u,v,31,1.)
    fade = np.full_like(u,.886) + .009*grain + .008*wave(u,v,17)
    for x,y,width,height,opacity in ((.18,.56,.18,.11,-.041),(.68,.61,.23,.13,.037),
                                     (.38,.155,.16,.085,.032),(.74,.33,.18,.071,-.031),
                                     (.09,.89,.13,.046,-.026),(.80,.90,.14,.042,.035)):
        fade += opacity * pressure(u,v,x,y,width,height)
    oxidation = np.zeros_like(u)
    polish = np.zeros_like(u)
    dirt = np.zeros_like(u)
    for index, base in enumerate((.022,.225)):
        side = (v >= base-.008) & (v <= base+.179)
        z = (v-base)/.107
        streak = .50+.26*wave(u,v,49+index,1.3)+.24*wave(u,v,23,2.)
        dirt += side * (np.exp(-np.maximum(0.,z-.25)*6.)*.24 + streak*.075*np.exp(-((z-.64)/.37)**2))
        wear = np.zeros_like(u)
        for x, z0, width, height in ((.20,.66,.033,.080),(.32,.48,.046,.063),(.55,.412,.070,.062),
                                      (.76,.62,.046,.085),(.84,.46,.031,.082),(.905,.77,.021,.084)):
            shift = .013 if index else -.006
            wear += pressure(u,z,x+shift,z0,width,height)
        chips = rng.normal(0.,.19,u.shape) + .17*wave(u,v,51,index) + .13*wave(u,v,81,2.)
        field = wear * (.67+.24*chips+.10*grain)
        oxidation += side * np.clip((field-.35)*1.31,0,.83)
        polish += side * pressure(u,z,.563,.918,.030,.045)*.21
    top = (v>.453)&(v<.768)
    dirt += top * (.013*wave(u,v,15,2.) + .029*np.cos(math.tau*u*1.44)**2)
    nose_or_tail = v>.796
    dirt += nose_or_tail * (.14*np.exp(-((v-.842)/.032)**2) + .038*wave(u,v,24,2.))
    fade -= dirt
    primer = np.clip(oxidation * 2.5, 0, 1)
    base = rgba(fade*(1-primer)+(.59+grain*.038)*primer,
                fade*(1-primer)+(.348+grain*.038)*primer,
                fade*(1-primer)+(.198+grain*.026)*primer)
    response = rgba(oxidation*.22, 0., 0., np.clip(.30-dirt*.37-oxidation*.23+polish+grain*.06,.08,.51))
    height = .067*grain + .24*oxidation - .14*primer + .055*wave(u,v,15)
    return {"PaintAlbedo":base,"PaintNormal":normals(height, 2.8),"PaintResponse":response}


def rubber_surface(rng):
    u,v = coordinates(512)
    grain = rng.normal(0., .13, u.shape) + .31*wave(u,v,91) + .46*wave(u,v,38,1.)
    mottle=.50+.32*wave(u,v,8)+.15*wave(u,v,17,1.)
    rubber = .77+.085*(mottle-.45)+.055*grain + .025*wave(u,v,24)
    dust = np.clip(.16 + .30*mottle,.09,.44)
    return {
        "RubberAlbedo":rgba(rubber,rubber,rubber*.985),
        "RubberNormal":normals(.215*grain+.063*wave(u,v,23),2.25),
        "RubberResponse":rgba(0.,0.,0.,.42+.56*dust+.095*grain),
    }


def cabin_surfaces(rng):
    size = 256
    u, v = coordinates(size)
    patches = {
        "dash_top": ((.31,.335,.338), "pebble", .26),
        "fascia": ((.259,.286,.282), "stipple", .41),
        "driver_door": ((.397,.349,.282), "vinyl", .43),
        "passenger_door": ((.43,.382,.314), "vinyl", .40),
        "armrest": ((.207,.227,.225), "pebble", .49),
        "driver_cloth": ((.354,.392,.343), "weave", .13),
        "passenger_cloth": ((.393,.414,.350), "weave", .15),
        "rear_cloth": ((.367,.397,.354), "weave", .13),
        "headliner": ((.546,.551,.505), "felt", .13),
        "footwell": ((.262,.244,.213), "carpet", .10),
        "glovebox_flock": ((.259,.298,.318), "felt", .12),
        "glovebox_lid": ((.31,.351,.348), "vinyl", .36),
        "bakelite": ((.21,.23,.232), "stipple", .55),
        "vent_grille": ((.173,.191,.189), "stipple", .29),
        "vinyl_binding": ((.26,.269,.232), "vinyl", .38),
        "cabin_liner": ((.332,.328,.290), "pebble", .24),
    }
    maps = {name: np.empty((size*PATCH_GRID, size*PATCH_GRID, 4), dtype=np.uint8)
            for name in ("InteriorAlbedo", "InteriorNormal", "InteriorResponse")}
    for index, name in enumerate(PATCHES):
        rgb, surface, gloss = patches[name]
        fibre = rng.normal(0.,1.,u.shape)
        wear = pressure(u,v, .39+.19*rng.random(), .43+.24*rng.random(), .27, .21)
        stain = pressure(u,v,.23,.65,.11,.21)-pressure(u,v,.79,.19,.13,.22)
        low = wave(u,v,6, index*.83)
        if surface == "weave":
            coarse = .55*np.sin(math.tau*u*86)*np.cos(math.tau*v*111)
            stripe = .043*np.cos(math.tau*u*17) + .024*np.cos(math.tau*v*14)
            fleck = coarse + .41*fibre
            variation = stripe + .025*fleck + .027*wear + .015*stain
        elif surface == "carpet":
            fleck = np.maximum(fibre, .4*wave(u,v,112))
            variation = .051*fleck - .038*wear + .016*low
        elif surface == "felt":
            fleck = .62*fibre + .37*wave(u,v,87)
            variation = .040*fleck + .028*stain + .011*low
        elif surface == "vinyl":
            fleck = .42*fibre + .60*wave(u,v,77,index)
            seam = np.exp(-((v-.15)/.021)**2) - .64*np.exp(-((v-.18)/.019)**2)
            variation = .024*fleck + .054*wear + .039*seam + .018*stain
        else:
            fleck = (.38*fibre + .53*wave(u,v,93,index)) if surface == "pebble" else .65*fibre
            variation = .028*fleck + .032*wear + .012*low + .016*stain
        tile = {
            "InteriorAlbedo": rgba(*(channel + variation for channel in rgb)),
            "InteriorNormal": normals(.10*fleck + variation*.43, 1.75),
            "InteriorResponse": rgba(0.,0.,0.,np.clip(gloss + .055*wear - .024*fleck, .06, .65)),
        }
        x = size*(index % PATCH_GRID)
        y = size*(PATCH_GRID - 1 - index//PATCH_GRID)
        for channel, pixels in tile.items():
            maps[channel][y:y+size,x:x+size] = pixels
    return maps


def glass_surface():
    u,v = coordinates(256)
    edge=np.minimum(np.minimum(u,1-u),np.minimum(v,1-v))
    dirt=np.exp(-edge*19.)*(.68+.25*wave(u,v,18,.5))
    bottom=np.exp(-v*14.)*(.58+.24*wave(u,v,28,2.))
    a=.053+.17*dirt+.12*bottom
    return {"GlassAlbedo":rgba(.72+.19*dirt,.78+.10*dirt,.79+.053*dirt,a)}


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--output-dir",type=Path,default=DEFAULT_OUTPUT)
    parser.add_argument("--validate-only",action="store_true")
    argv = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else sys.argv[1:]
    args=parser.parse_args(argv)
    rng=np.random.default_rng(SEED)
    maps=paint_atlas(rng) | rubber_surface(rng) | glass_surface() | cabin_surfaces(rng)
    records={}
    for name, image in maps.items():
        height,width,_=image.shape
        canvas=PixelCanvas(width,height)
        canvas.pixels[:]=image.tobytes()
        payload=canvas.png_bytes()
        path=args.output_dir / ("LastRouteCar"+name+".png")
        records[path.name]={"size":width,"sha256":hashlib.sha256(payload).hexdigest()}
        if args.validate_only:
            if path.read_bytes()!=payload:
                raise RuntimeError(f"Texture differs from authoring source: {path}")
        else:
            args.output_dir.mkdir(parents=True,exist_ok=True)
            path.write_bytes(payload)
    # Channel spread guards against flat wear masks and inverted roughness.
    if np.ptp(maps["PaintResponse"][:,:,3]) < 75 or np.ptp(maps["PaintNormal"][:,:,0]) < 30:
        raise RuntimeError("The sedan lost its painted-surface response")
    if maps["GlassAlbedo"][128,128,3] > 20 or maps["GlassAlbedo"][253,128,3] < 40:
        raise RuntimeError("The windscreen must be clear through its centre")
    print(json.dumps(records,sort_keys=True))


if __name__ == "__main__":
    main()
