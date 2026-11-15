#!/usr/bin/env python3
"""Bake the midibus' panel atlas and shared metre-scale surface maps."""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import sys

import numpy as np

sys.dont_write_bytecode = True
from atlas_kit import PixelCanvas
from city_bus_surfaces import PAINT_END, PAINT_ROOF, PAINT_SIDE

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets/Vehicles/Textures"


def coordinates(size):
    c = (np.arange(size, dtype=np.float64) + .5) / size
    return np.meshgrid(c, c[::-1])


def wave(u, v, x, y, phase=0.):
    return np.sin(2*np.pi*(u*x+phase)) * np.cos(2*np.pi*v*y)


def pressure(u, v, x, y, width, height):
    return np.exp(-((u-x)/width)**2-((v-y)/height)**2)


def rgba(r, g, b, a=1.):
    return np.clip(np.rint(np.stack(np.broadcast_arrays(r, g, b, a), axis=-1)*255),
                   0, 255).astype(np.uint8)


def finish(rgb, height, smoothness, metallic=0., tile=False, scale=2.):
    if tile:
        dx = (np.roll(height,-1,1)-np.roll(height,1,1))*.5
        dy = (np.roll(height,-1,0)-np.roll(height,1,0))*.5
    else:
        dy, dx = np.gradient(height)
    x, y = -dx*scale, dy*scale
    length = np.sqrt(1+x*x+y*y)
    return {"Albedo": rgba(*rgb), "Normal": rgba(.5+.5*x/length,.5+.5*y/length,.5+.5/length),
            "Response": rgba(metallic, 0., 0., np.clip(smoothness,.06,.83))}


def paint(rng):
    u,v = coordinates(1024)
    grain = .36*wave(u,v,110,83) + .38*wave(u,v,52,39,.34) + rng.normal(0.,.24,u.shape)
    sx = (u-.026)/.948
    mottle = .55+.29*wave(u,v,11,79,.20)+.14*wave(u,v,43,43,.42)
    stains = np.zeros_like(u)
    oxide = np.zeros_like(u)
    scuffs = np.zeros_like(u)
    repair = np.zeros_like(u)
    for i, origin in enumerate(PAINT_SIDE):
        z = (v-origin)/.156*2.95
        band = (v > origin-.020) & (v < origin+.167)
        stains += band*(.21*np.exp(-np.maximum(0.,z-.34)*4.1)
            + mottle*.035*np.exp(-((z-.98)/.70)**2)
            + .018*wave(u,v,61,11,i*.19)*np.exp(-((z-1.58)/.73)**2))
        for wheel in (.232,.768):
            stains += band*(.22*pressure(sx,z,wheel+.058,.54,.085,.38)
                            + .090*pressure(sx,z,wheel,1.04,.047,.18))
        for x,h,w in ((.142,.55,.042),(.316,.83,.023),(.558,.51,.051),(.831,.63,.046),(.939,.66,.026)):
            wear = pressure(sx,z,x+i*.014,h,w,.063)
            oxide += band*np.clip(wear*(.68 + .34*grain)-.38,0,.6)
        scuffs += band*pressure(sx,z,.21 if i else .821,1.424,.055,.022)
        # A broad, low contrast repair follows one replaceable panel.
        mask = (sx > .432+i*.085) & (sx < .577+i*.085) & (z > .50) & (z < 1.397)
        repair += mask*.075
    for origin in PAINT_END:
        z = (v-origin)/.151*2.95
        stains += ((z > -.3)&(z < 3.15))*(.23*np.exp(-np.maximum(0.,z-.37)*3.5)
            + .026*mottle*np.exp(-((z-1.52)/.70)**2))
    stains += (v > PAINT_ROOF-.018)*(.029+.018*wave(u,v,7,13))
    enamel = .96 + repair + .018*grain - stains + .028*scuffs
    flake = np.clip(oxide*1.5,0,1)
    rgb = (enamel*(1-flake)+.73*flake, enamel*(1-flake)+.48*flake,
           enamel*(1-flake)+.307*flake)
    return finish(rgb,.076*grain+.14*oxide+.034*wave(u,v,19,16),
                  .39-.54*stains-.32*oxide+.072*scuffs+.025*grain,.04+.13*flake,scale=2.1)


def repeat_surfaces(rng):
    u,v = coordinates(512)
    detail = .28*wave(u,v,71,63) + .38*wave(u,v,35,29,.35) + rng.normal(0.,.19,u.shape)
    grime = .5 + .23*wave(u,v,3,5) + .16*wave(u,v,8,7,.26)
    streak = wave(u,v,4,94,.69)
    n = .89 - grime*.09 + .038*detail
    metal = finish((n,n*.995,n*.972),.075*detail+.063*streak,.40+.13*grime+.034*streak,
                   metallic=.42+.24*grime,tile=True)
    panel = .92 + .027*detail - .14*grime
    interior = finish((panel,panel*.99,panel*.964),.13*detail,.34+.05*detail,tile=True)
    weave = .6*wave(u,v,85,95) + .4*wave(v,u,45,60,.12)
    buckle = (np.cos(2*np.pi*v*2.)*.5+.5)**12
    vinyl = .82 + .14*grime+.043*weave-.051*buckle
    seat = finish((vinyl,vinyl*.996,vinyl*.968),
                  .079*weave+.046*detail-.053*buckle,.29+.12*grime+.06*weave,tile=True)
    carbon = .87-.20*grime+.034*detail
    rubber = finish((carbon,carbon*.998,carbon*.967),.073*detail,.13+.054*grime,tile=True)
    # Staggered moulded studs rather than scratches on passenger floor rubber.
    col = (u*20+.5*(np.floor(v*28)%2))%1-.5
    row = (v*28)%1-.5
    studs = np.maximum(0.,1-(col/.32)**2-(row/.35)**2)**.68
    floor = .88-.17*grime+.062*studs
    floor_maps = finish((floor,floor*.989,floor*.96),.085*detail+.12*studs,
                        .18+.057*grime+.065*studs,tile=True,scale=3.2)
    glass = .96-.10*grime+.027*streak
    return {"Metal":metal, "Interior":interior, "Seat":seat, "Rubber":rubber, "Floor":floor_maps,
            "Glass":{"Albedo":rgba(glass,glass,glass*.978)}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_TEXTURE_DIR)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    rng = np.random.default_rng(260813)
    families = {"Paint":paint(rng), **repeat_surfaces(rng)}
    if not args.verify:
        args.output_dir.mkdir(parents=True,exist_ok=True)
    digest = hashlib.sha256()
    for family,maps in families.items():
        for kind,data in maps.items():
            canvas = PixelCanvas(data.shape[1],data.shape[0])
            canvas.pixels[:] = data.tobytes()
            content = canvas.png_bytes()
            path = args.output_dir / f"CityBus{family}{kind}.png"
            if args.verify:
                if path.read_bytes() != content:
                    raise RuntimeError(f"Non-deterministic bus map: {path}")
            else:
                path.write_bytes(content)
            digest.update(content)
    print(f"CITY BUS SURFACE CONTRACT OK {digest.hexdigest()}")


if __name__ == "__main__":
    main()
