"""Offline village pigments and causal physical material fields in metres.

ImageGen provides pigment only. Analytic structural fields own height slopes,
roughness and cavity independently of image brightness. Both atlas and tiled
publishers sample this one footprint-filtered, periodic material definition.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

import numpy as np
try:
    from PIL import Image
except ImportError:  # Pinned Blender has NumPy; use its raw image loader there.
    Image = None

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/Village/Surfaces/Source"
OUTPUT = ROOT / "Assets/Resources/Village/Textures/Surfaces"
DESIGN = "village_material_surfaces_v1"
VERSION = "1.0.0"
BUDGET = 96 * 1024 * 1024
# Family, pigment cell, physical repeat, roughness, full colour.
SPECS = (
    ("Timber", "WeatheredWood", 1.4, .88, False),
    ("Joinery", "WeatheredWood", 1.4, .86, False),
    ("Stone", "Stone", 2.4, .94, False),
    ("Roof", "Roof", 2.4, .94, False),
    ("Plaster", "Plaster", 2.4, .96, False),
    ("Concrete", "Concrete", 3.0, .94, False),
    ("RustedIron", "Rust", 1.2, .90, False),
    ("PaintedMetal", "PaintedMetal", 1.2, .76, False),
    ("Enamel", "Enamel", 1.2, .63, False),
    ("Linoleum", "Enamel", 1.2, .87, False),
    ("WallPaint", "Plaster", 2.4, .96, False),
    ("Cloth", "Linen", 1.2, .98, False),
    ("Asphalt", "Concrete", 3.5, .96, False),
    ("ForestFloor", "Soil", 5.0, .98, False),
    ("Snow", "Snow", 5.0, .95, False),
    ("Needles", "Needles", 2.5, .98, False),
    ("Bark", "Bark", 2.5, .97, False),
    ("AbandonedWood", "WeatheredWood", 1.4, .94, False),
    ("AbandonedPlaster", "Plaster", 2.4, .98, False),
    ("AbandonedRoof", "Roof", 2.4, .97, False),
    ("WreckRust", "Rust", 1.2, .97, True),
    ("WreckPaint", "PaintedMetal", 1.2, .94, True),
    ("LodgeFloorWood", "WeatheredWood", 2.4, .92, True),
    ("LodgeHatchWood", "Pine", 1.2, .88, True),
    ("LodgePaintedWood", "PaintedWood", .6, .91, True),
    ("LodgeOakWood", "Oak", 1.6, .84, True),
    ("LodgePaleWood", "Pine", 1.6, .91, True),
    ("LodgeDarkWood", "Walnut", 1.25, .85, True),
    ("LodgeRoughWood", "WeatheredWood", 1.8, .94, True),
)
TABLE = {row[0]: row for row in SPECS}
ALIASES = {"Canvas": "Cloth", "Metal": "RustedIron", "LayeredStone": "Stone",
           "Masonry": "Plaster", "BareStone": "Stone", "BarkAndDeadwood": "Bark",
           "WindSnow": "Snow", "PaleEnamel": "Enamel", "ConiferNeedles": "Needles",
           "WoodRough": "LodgeRoughWood", "WoodOak": "LodgeOakWood", "WoodPale": "LodgePaleWood",
           "WoodDark": "LodgeDarkWood", "WoodPainted": "LodgePaintedWood"}
EXTRAS = {"Rubber": ("Rubber", "Rubber", 1.2, .98, False),
          "Paper": ("Paper", "Paper", 1.0, .98, False),
          "Wicker": ("Wicker", "Pine", 1.0, .97, False)}
CELL_NAMES = {"Wood": ("WeatheredWood", "Pine", "Oak", "Walnut"),
              "Mineral": ("Stone", "Plaster", "Concrete", "Roof"),
              "Finish": ("Rust", "PaintedMetal", "Enamel", "PaintedWood"),
              "Nature": ("Snow", "Soil", "Bark", "Needles"),
              "Fabric": ("Linen", "Burgundy", "Paper", "Rubber")}
COLOURS = {"LodgeFloorWood": "655f54", "LodgeHatchWood": "b1a084", "LodgePaintedWood": "969e85",
           "LodgeOakWood": "a18459", "LodgePaleWood": "b5a88c", "LodgeDarkWood": "514335",
           "LodgeRoughWood": "6f6252", "WreckRust": "6b4731", "WreckPaint": "777b6c"}
GROUND = {"Timber", "Plaster", "Stone", "Concrete", "Asphalt", "ForestFloor", "Snow"}
_sources = None


def linear(rgb):
    return np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)


def gamma(rgb):
    return np.where(rgb <= .0031308, rgb * 12.92, 1.055 * np.maximum(0, rgb) ** (1/2.4) - .055)


def canonical(family):
    return ALIASES.get(family, family)


def spec(family):
    family = canonical(family)
    if family not in TABLE and family not in EXTRAS:
        raise ValueError("Unregistered village pigment family: " + family)
    return TABLE.get(family, EXTRAS.get(family))


def size_for(family):
    return 2048 if family in ("Timber", "Stone") else 512 if family in ("Linoleum", "WallPaint") else 1024


def periodic(image):
    # Opposite half-shifts cover each other's discontinuities. No edge strip
    # is cloned flat, and the first derivative stays quiet through the seam.
    for axis in (1, 0):
        n = image.shape[axis]
        phase = (np.arange(n, dtype=np.float32) + .5) / n
        weight = np.sin(np.pi * phase) ** 2
        shape = [1, 1, 1]; shape[axis] = n
        weight = weight.reshape(shape)
        image = image * weight + np.roll(image, n//2, axis=axis) * (1-weight)
    return image


def load_rgb(path):
    if Image is not None:
        return np.asarray(Image.open(path).convert("RGB"),dtype=np.float32)/255
    import bpy
    image=bpy.data.images.load(str(path),check_existing=False)
    image.colorspace_settings.name="Non-Color"
    raw=np.empty(image.size[0]*image.size[1]*4,np.float32)
    image.pixels.foreach_get(raw)
    rgb=raw.reshape(image.size[1],image.size[0],4)[::-1,:,:3].copy()
    bpy.data.images.remove(image)
    return rgb


def resize_pixels(image,size=512):
    # Identical pixel-centre resampling in host Python and pinned Blender.
    h,w=image.shape[:2]
    x=(np.arange(size,dtype=np.float32)+.5)*w/size-.5
    y=(np.arange(size,dtype=np.float32)+.5)*h/size-.5
    ix=np.floor(x).astype(np.int32); iy=np.floor(y).astype(np.int32)
    fx=(x-ix)[None,:,None]; fy=(y-iy)[:,None,None]
    ix=np.clip(ix,0,w-1); iy=np.clip(iy,0,h-1)
    return ((image[iy[:,None],ix[None,:]]*(1-fx)+image[iy[:,None],np.minimum(ix+1,w-1)[None,:]]*fx)*(1-fy)+
            (image[np.minimum(iy+1,h-1)[:,None],ix[None,:]]*(1-fx)+image[np.minimum(iy+1,h-1)[:,None],np.minimum(ix+1,w-1)[None,:]]*fx)*fy)


def sources():
    global _sources
    if _sources is not None:
        return _sources
    manifest = json.loads((SOURCE / "generation.json").read_text(encoding="utf8"))
    result = {}
    for row in manifest["images"]:
        path = ROOT / row["source"]
        if hashlib.sha256(path.read_bytes()).hexdigest() != row["sha256"]:
            raise ValueError("Village pigment source changed: " + str(path))
        rgb = load_rgb(path)
        h, w = rgb.shape[:2]
        for index, name in enumerate(CELL_NAMES[row["name"]]):
            y, x = divmod(index, 2)
            cell = rgb[y*h//2:(y+1)*h//2, x*w//2:(x+1)*w//2]
            # Crop the occasional central grid boundary; it is not an object seam.
            inset = max(2, round(min(cell.shape[:2]) * .015))
            cell = periodic(cell[inset:-inset, inset:-inset])
            # Fixed power-of-two pyramid permits bounded, filtered baking into
            # both large tiling sheets and tiny unique part charts.
            base = resize_pixels(cell)
            levels = []
            while True:
                levels.append(base.astype(np.float32))
                if base.shape[0] == 1:
                    break
                base=(base[::2,::2]+base[1::2,::2]+base[::2,1::2]+base[1::2,1::2])*.25
            result[name] = levels
    _sources = result
    return result


def pigment(name, x, y, footprint):
    levels = sources()[name]
    lod = max(0., min(len(levels)-1., math.log2(max(1., float(footprint) * 512))))
    lo = int(lod); hi = min(lo+1, len(levels)-1)
    def bilinear(a):
        h, w = a.shape[:2]
        px = np.mod(x, 1.) * w - .5; py = np.mod(y, 1.) * h - .5
        ix = np.floor(px).astype(np.int32); iy = np.floor(py).astype(np.int32)
        fx = (px-ix)[...,None]; fy = (py-iy)[...,None]
        return ((a[iy%h,ix%w]*(1-fx)+a[iy%h,(ix+1)%w]*fx)*(1-fy) +
                (a[(iy+1)%h,ix%w]*(1-fx)+a[(iy+1)%h,(ix+1)%w]*fx)*fy)
    a = bilinear(levels[lo])
    return a if hi == lo else a*(1-(lod-lo))+bilinear(levels[hi])*(lod-lo)


def structure(family, u, v, pixel_metres):
    family, _, pitch, rough, _ = spec(family)
    x, y = np.broadcast_arrays(np.asarray(u, dtype=np.float32), np.asarray(v, dtype=np.float32))
    height = np.zeros(x.shape, np.float32); du = height.copy(); dv = height.copy()
    cavity = np.ones(x.shape, np.float32); wear = np.zeros(x.shape, np.float32)
    wood = "Wood" in family or family in ("Timber", "Joinery", "Bark", "Roof", "AbandonedRoof", "Wicker")
    def wave(fx, fy, amplitude, phase=0.):
        nonlocal height, du, dv
        # Integrate a bounded footprint instead of baking skipped fine threads.
        damping = math.exp(-min(80., (math.pi*float(pixel_metres))**2*(fx*fx+fy*fy)))
        a = amplitude*damping
        angle = math.tau*(fx*x+fy*y)+phase
        height += a*np.sin(angle)
        du += a*math.tau*fx*np.cos(angle)
        dv += a*math.tau*fy*np.cos(angle)
    if wood:
        horizontal = family == "Timber"
        if horizontal:
            wave(1/pitch, 27/pitch, .00015, .4); wave(2/pitch, 61/pitch, .000055, 1.8)
        else:
            wave(27/pitch, 1/pitch, .00015, .4); wave(61/pitch, 2/pitch, .000055, 1.8)
        if family in ("Bark", "LodgeRoughWood", "AbandonedWood"):
            wave(7/pitch, 1/pitch, .00065, 1.2)
        if family in ("Roof", "AbandonedRoof"):
            wave(1/pitch, 11/pitch, .0012)
        # Cavity and rubbed fibre ridges share the physical field, never the
        # dark pigment of a knot or an artist-painted shadow.
        cavity = .94 + .05*np.tanh(height/.0005)
        wear = .025*np.tanh(height/.0003)
    elif family in ("Cloth", "Wicker"):
        wave(96/pitch, 0, .000045); wave(0, 104/pitch, .000045, 1.2)
        cavity = .97 + .02*np.tanh(height/.00008)
    elif family in ("Stone", "Plaster", "Concrete", "AbandonedPlaster", "Asphalt", "ForestFloor"):
        amplitude = .000025 if "Plaster" in family else .00013
        wave(17/pitch, 11/pitch, amplitude, .7); wave(-23/pitch, 31/pitch, amplitude*.45, 1.4)
        wave(3/pitch, -4/pitch, amplitude*.8, 2.2)
        cavity = .96+.03*np.tanh(height/max(amplitude,.00001))
    elif family in ("RustedIron", "WreckRust", "WreckPaint", "PaintedMetal"):
        wave(29/pitch, 23/pitch, .000045); wave(-37/pitch, 43/pitch, .000022, .9)
        cavity = .96+.025*np.tanh(height/.00006)
    elif family in ("Snow", "Needles"):
        wave(1/pitch, 3/pitch, .000035)
    elif family in ("Enamel", "Linoleum", "WallPaint", "Rubber", "Paper"):
        wave(3/pitch, 2/pitch, .000002)
    response = np.stack((np.clip(.5+du*.5,0,1), np.clip(.5+dv*.5,0,1),
                         np.clip(np.full(x.shape,rough,np.float32)-wear,.5,.995),
                         np.clip(cavity,.85,1)), axis=-1)
    return height, response


def sample(family, u, v, *, part_name="", tint=None, pixel_metres=.01):
    family, cell, pitch, _, full_colour = spec(family)
    x, y = np.broadcast_arrays(np.asarray(u,dtype=np.float32),np.asarray(v,dtype=np.float32))
    # Sources run grain vertically; facade courses run along their U axis.
    pu, pv = (y/pitch, x/pitch) if family == "Timber" else (x/pitch, y/pitch)
    source = pigment(cell,pu,pv,float(pixel_metres)/pitch)
    physical_height, response = structure(family,x,y,pixel_metres)
    lum = np.sum(linear(source)*np.asarray((.2126,.7152,.0722)),axis=-1)
    mean = float(np.mean(np.sum(linear(sources()[cell][-1])*np.asarray((.2126,.7152,.0722)),axis=-1)))
    detail = np.clip(lum/max(.01,mean),.70,1.30)
    # Low relief also modulates pigment gently so its structural cavity stays
    # registered at each resolution, without baked directional illumination.
    detail *= 1.+np.tanh(physical_height/.0005)*.018
    if full_colour:
        colour = np.array([int(COLOURS[family][i:i+2],16)/255 for i in (0,2,4)],np.float32)
        rgb = gamma(np.clip(linear(colour)*detail[...,None],0,1))
    else:
        rgb = gamma(np.clip(detail[...,None]*np.ones(3,np.float32)*.58,0,1))
        if tint is not None:
            rgb = gamma(np.clip(linear(rgb)*linear(np.asarray(tint[:3],np.float32))/.58,0,1))
    return np.clip(rgb,0,1).astype(np.float32), response.astype(np.float32)


def ground_sample(family, u, v, pixel_metres):
    height, response = structure(family,u,v,pixel_metres)
    return np.stack((np.clip((1-response[...,2])/.1,.2,1.4),
                     np.clip(.5+height/.0015,0,1),1-response[...,3],np.ones_like(height)),axis=-1).clip(0,1)


def provenance():
    payload = json.loads((SOURCE/"generation.json").read_text(encoding="utf8"))
    return {"generator":"image_gen (built-in)","source_manifest_sha256":hashlib.sha256(
        (SOURCE/"generation.json").read_bytes()).hexdigest(), "images":payload["images"]}
