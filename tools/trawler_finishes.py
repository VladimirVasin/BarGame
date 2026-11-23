"""Metre-registered paint and surface response for the fishing vessel's shell, deck and lids."""
import struct
import zlib

import numpy as np


# Bottom-up chart limits. Padding separates the surfaces down the mip chain.
CHARTS = ((0, 272), (272, 544), (544, 864), (864, 944), (944, 1024))
WIDTH, HEIGHT = 2048, 1024
PAD = 4


def chart_uv(chart, across, up):
    low, high = CHARTS[chart]
    return ((PAD + across * (WIDTH - 2 * PAD)) / WIDTH,
            (low + PAD + up * (high - low - 2 * PAD)) / HEIGHT)


def vessel_uv(role, position, normal):
    # Blender X/Y/Z are vessel beam/length/height. Ends and narrow edge faces
    # use their own projection to retain area on caps and bevels.
    x, z, y = position
    axis = max(range(3), key=lambda i: abs(normal[i]))
    if role == "ShellPlates":
        along = (z + 10.2) / 20.4 if axis != 1 else .48 + x / 20.4
        level = (y + 1.65) / 4.3 if axis != 2 else .055 + x / 65
        return chart_uv(int(normal.x < 0), along, level)
    if role == "Deck":
        along = (z + 10.2) / 20.4 if axis != 1 else .48 + x / 20.4
        transverse = (x + 2.95) / 5.9 if axis == 2 else (y + .1) / 5.9
        return chart_uv(2, along, transverse)
    if role == "CabinPaint":
        along = (z+9.5)/3.7 if axis != 1 else (x+1.68)/3.36
        up = (y-1.53)/2.72 if axis != 2 else .4+x/9
        return chart_uv(4, along, up)
    along = (x + 4.76) / 4.86 if axis != 0 else .48 + z / 4.86
    across = (z + 1.08) / 2.16 if axis == 2 else .48 + y / 2.16
    return chart_uv(3, along, across)


def png(pixels):
    h, w, _ = pixels.shape
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    rows = b"".join(b"\0" + row.tobytes() for row in pixels[::-1])
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w,h,8,2,0,0,0))
            + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def bake(steel):
    pigment = np.empty((HEIGHT, WIDTH, 3), dtype=np.uint8)
    response = np.empty_like(pigment)
    rng = np.random.default_rng(76211)
    for chart, (low, high) in enumerate(CHARTS):
        u = np.clip((np.arange(WIDTH)[None,:] + .5-PAD)/(WIDTH-2*PAD), 0, 1)
        v = np.clip((np.arange(high-low)[:,None] + .5-PAD)/(high-low-2*PAD), 0, 1)
        z = u*20.4-10.2
        speck = rng.normal(0, 1, (high-low,WIDTH))
        # Local pigment from the port's original steel. Large seams and wear are in ship metres.
        grain = steel[(np.arange(high-low)[:,None]+chart*113)%steel.shape[0],
                      np.arange(WIDTH)[None,:]%steel.shape[1]]
        grain = np.clip((grain-.5)*1.35, -.35, .35)
        if chart < 2:
            y = v*4.3-1.65
            strake = np.floor((y+.15)/.74)
            joint = np.abs(((z + (strake%2)*1.54 + chart*.67 + 1.55) % 3.10) - 1.55)
            horizontal = np.abs(((y+.15+.37) % .74)-.37)
            weld = np.exp(-(np.minimum(horizontal,joint)/.021)**2)
            plate = np.sin(np.floor((z+(strake%2)*1.54+chart*.67+1.55)/3.10)*8.32 + strake*3.72)
            water = np.clip((.33 + .027*np.sin(z*2.2+chart)+grain*.15 - y)/.18,0,1)
            top = np.clip((y-1.25-.12*np.sin(z*.30))/.42,0,1)
            drip = np.zeros_like(speck)
            for outlet in (-5.7,-2.4,2.4,5.7):
                length = .42+.32*rng.random()
                for offset in (-.115,.105):
                    distance = 1.30-y
                    width = .032+.021*np.clip(distance,0,1)
                    drip += np.exp(-((z-outlet-offset-.012*np.sin(9*y+chart))/width)**2) * np.clip(1-distance/length,0,1) * (distance>0)
            tint = np.array([89,113,105]) + (plate*3.2 + 18*grain + speck*1.4 + 4*weld)[...,None]
            tint += top[...,None]*np.array([14,13,9]) - water[...,None]*np.array([35,40,32])
            tint += np.clip(drip,0,1)[...,None]*np.array([17,-13,-21])
            gloss = .75-.24*drip+.15*water-.12*weld
            height = .49+.36*weld+.22*grain
        elif chart == 2:
            x = v*5.9-2.95
            lane = np.exp(-((abs(x)-2.42-.04*np.sin(z*2.1))/.24)**2)*np.clip((8.15-abs(z))/.95,0,1)
            foot = lane*(.73+.27*np.cos(z*9.4+x*2)**2)
            grit = .5+.5*np.sin(z*145+x*29)*np.sin(x*135-z*23)
            seam = np.exp(-(np.minimum(abs((z+1.11)%2.22-1.11),abs((x+.64)%1.28-.64))/.018)**2)
            tint = np.array([88,98,91])+(grain*24+speck*1.9+grit*3-10*seam)[...,None] + foot[...,None]*np.array([26,23,19])
            gloss = .35+.35*foot-.09*seam+.13*grain
            height = .48+.18*grit*(1-.8*foot)-.25*seam
        elif chart == 3:
            edge = np.minimum(np.minimum(u,1-u)*4.86,np.minimum(v,1-v)*2.16)
            rubbed = np.exp(-((edge-.09)/.04)**2)*(.7+.3*np.sin(u*63+v*20))
            tint = np.array([91,111,101])+(grain*29+speck*1.5+12*rubbed)[...,None]
            gloss = .57+.24*rubbed+.20*grain
            height = .5+.21*grain-.08*rubbed
        else:
            # Pale marine enamel, darker at the deck coaming and below the window seals.
            margin = np.clip((.22-v)/.20,0,1)
            streak = np.zeros_like(speck)
            for start in (.18,.27,.72,.82):
                streak += np.exp(-((u-start)/.010)**2)*np.clip((v-.20)/.24,0,1)*(v<.44)
            tint = np.array([161,163,143])+(grain*13+speck*.95)[...,None]-margin[...,None]*np.array([34,34,29])
            tint += streak[...,None]*np.array([-5,-9,-11])
            gloss = .66-.16*margin+.11*grain
            height = .50+.12*grain
        pigment[low:high] = np.rint(np.clip(tint,0,255)).astype(np.uint8)
        response[low:high] = np.rint(255*np.clip(np.broadcast_arrays(gloss+0*speck,height+0*speck,.03+0*speck),0,1)).astype(np.uint8).transpose(1,2,0)
    return png(pigment), png(response)


if __name__ == "__main__":
    import argparse
    from pathlib import Path
    from PIL import Image

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    port = Path(__file__).resolve().parents[1]/"Assets/Resources/City/Port"
    with Image.open(port/"Textures/PortPaintedSteelAlbedo.png") as donor:
        steel = np.asarray(donor.convert("L"),dtype=float)/255
    for name, data in zip(("TrawlerAlbedo.png","TrawlerResponse.png"),bake(steel)):
        path = port/name
        if args.check:
            if path.read_bytes() != data:
                raise ValueError(f"Stale trawler surface: {path}")
        else:
            path.write_bytes(data)
