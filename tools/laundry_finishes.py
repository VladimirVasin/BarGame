"""Metric charts for the laundry kit. RG slopes, B roughness and A cavity use City Surface."""
import math
import struct
import zlib

import numpy as np

CELL, PAD = 256, 5
# RGB pigment, roughness and chart span in metres. Fabrics share the rest-pose weave direction.
SURFACES = {
    "towel": ((185,178,153),.88,(.54,.36)),
    "linen": ((149,157,149),.86,(.55,.42)),
    "blue": ((117,134,133),.84,(.50,.40)),
    "canvas": ((160,145,119),.88,(.48,.38)),
    "wood": ((130,108,84),.73,(1.70,1.20)),
    "end": ((147,121,91),.80,(.50,.30)),
    "enamel": ((169,177,161),.50,(1.12,1.32)),
    "panel": ((141,154,146),.52,(1.12,.52)),
    "paint": ((86,110,103),.63,(.90,.62)),
    "steel": ((128,137,134),.42,(.62,.38)),
    "dark": ((74,84,82),.62,(.67,.50)),
    "grip": ((154,156,135),.39,(.16,.14)),
    "rubber": ((60,65,62),.81,(.36,.32)),
    "drum": ((136,145,141),.41,(.54,.48)),
    "zinc": ((146,153,145),.53,(.94,.38)),
    "cartwood": ((119,106,86),.73,(.92,.75)),
}
NAMES = tuple(SURFACES)


def png(data):
    def chunk(tag,block):
        return struct.pack(">I",len(block))+tag+block+struct.pack(">I",zlib.crc32(tag+block))
    h,w,c=data.shape
    return (b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">2I5B",w,h,8,2 if c==3 else 6,0,0,0))
            +chunk(b"IDAT",zlib.compress(b"".join(b"\0"+line.tobytes() for line in data),9))+chunk(b"IEND",b""))


def maps():
    albedo=np.zeros((4*CELL,4*CELL,3),np.uint8)
    response=np.zeros((4*CELL,4*CELL,4),np.uint8)
    n=CELL-2*PAD
    v,u=np.mgrid[0:n,0:n]/(n-1)
    for i,(key,(rgb,rough,span)) in enumerate(SURFACES.items()):
        rng=np.random.default_rng(6417+i)
        noise=rng.normal(0,1,u.shape)
        variation=noise*1.6+1.8*np.sin(12*u+np.cos(9*v))
        relief=np.zeros_like(u)
        roughness=rough+noise*.012
        cavity=np.zeros_like(u)
        if key in ("towel","linen","blue","canvas"):
            warp=np.sin(2*math.pi*(89*u+.10*np.sin(25*v)))
            weft=np.sin(2*math.pi*(62*v+.09*np.sin(27*u)))
            weave=(warp+weft)*.31+warp*weft*.37
            variation+=8.5*weave
            relief+=weave*.00028
            if key=="towel":
                border=sum(np.exp(-((v-k)/width)**4) for k,width in ((.07,.011),(.11,.004),(.89,.004),(.93,.011)))
                variation-=border*35
                stitch=np.cos(2*math.pi*u*62)*np.exp(-((v-.05)/.005)**2)
                relief+=.00025*stitch
        elif key in ("wood","cartwood","end"):
            phase=96*v+3*np.sin(6*u+v)+1.8*np.sin(17*u-2*v)
            if key=="end": phase=112*np.hypot(u-.26,.67*(v+.12))
            grain=np.sin(phase)+.4*np.cos(2.9*phase)
            wear=np.exp(-((v-.73)/.20)**2-((u-.48)/.31)**2)
            variation+=4.6*grain+8*wear
            relief+=.00032*grain
            roughness-=.15*wear
        elif key=="drum":
            px=((u*28+(np.floor(v*24)%2)*.5)%1)-.5
            py=(v*24%1)-.5
            hole=np.exp(-((px*px+py*py)/.054)**2)
            variation-=65*hole
            relief-=.0007*hole
            cavity=hole*.24
        elif key=="grip":
            touch=np.exp(-((u-.5)/.36)**4)
            variation+=10*touch+1.8*np.cos(98*u)
            roughness-=.13*touch
        elif key=="rubber":
            variation+=1.1*np.cos(129*v)+noise
        else:
            brushing=np.sin(188*v+.7*np.sin(9*u))
            variation+=brushing*(2.3 if key in ("steel","zinc") else .6)
            relief+=brushing*.00004
            if key in ("enamel","panel"):
                lower=np.exp(-((u-.18)/.15)**2-((v-.82)/.14)**2)
                variation-=3*lower
        dv,du=np.gradient(relief)
        tile=np.uint8(np.clip(np.array(rgb)+variation[:,:,None],0,255))
        channels=np.stack((.5+du*(n-1)/span[0]*.5,.5-dv*(n-1)/span[1]*.5,roughness,1-cavity),axis=-1)
        r,c=divmod(i,4)
        for atlas,data in ((albedo,tile),(response,np.uint8(np.clip(channels,0,1)*255))):
            atlas[r*CELL:(r+1)*CELL,c*CELL:(c+1)*CELL]=np.pad(data,((PAD,PAD),(PAD,PAD),(0,0)),mode="edge")

    h,w=768,1024
    row,col=np.mgrid[0:h,0:w]
    x=(col+.5)/w*8.6-4.3
    z=(1-(row+.5)/h)*6.6-3.3
    rng=np.random.default_rng(96051)
    grit=rng.normal(0,1,(h,w))
    lane=np.exp(-((z-(1.14-.066*x))/.40)**2)*np.exp(-((x+.18)/2.72)**6)
    stone=2.7*np.sin(3*x+np.sin(4*z))+2*np.sin(7*z-.8*x)+grit*(3.5-lane)
    seams=np.zeros_like(x)
    for axis,positions in ((x,(-1.55,1.47)),(z,(-.64,1.71))):
        for position in positions: seams=np.maximum(seams,np.exp(-((axis-position)/.009)**2))
    floor=np.clip(np.array((121,124,112))+(stone+9*lane-18*seams)[:,:,None],0,255).astype(np.uint8)
    floor_response=np.stack((.40+.38*lane,.54+grit*.032-.23*seams,.10+.66*seams),axis=-1)
    return {"LaundryFittingsAlbedo":png(albedo),"LaundryFittingsResponse":png(response),
            "LaundryFloorAlbedo":png(floor),"LaundryFloorResponse":png(np.uint8(np.clip(floor_response,0,1)*255))}


def chart_uv(key,u,v):
    row,col=divmod(NAMES.index(key),4)
    u,v=(max(0,min(1,q)) for q in (u,v))
    return ((CELL*col+PAD+.5+u*(CELL-2*PAD-1))/(4*CELL),
            1-(CELL*row+PAD+.5+v*(CELL-2*PAD-1))/(4*CELL))
