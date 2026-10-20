#!/usr/bin/env python3
"""Publish measured, compressed village-owned maps; validate without changing files."""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import sys
import uuid

import numpy as np
from PIL import Image

import village_surface_authoring as art

ROOT, OUTPUT = art.ROOT, art.OUTPUT
PREFIX = "Village/Textures/Surfaces/"
MANIFEST = OUTPUT/"VillageSurfaceTextures.json"


def bc7_bytes(width,height=None,mips=True):
    height=height or width
    total=0
    while True:
        total+=((width+3)//4)*((height+3)//4)*16
        if not mips or width==height==1:
            return total
        width=max(1,width//2); height=max(1,height//2)


def meta(path,size,linear,*,mips=True,clamp=False,pot=False):
    sidecar=Path(str(path)+".meta")
    template=ROOT/"Assets/Resources/MothersHouse/Textures/MothersHouseSurfaceAtlas.png.meta"
    data=(sidecar if sidecar.exists() else template).read_text(encoding="utf8")
    guid=re.search(r"(?m)^guid: (\w+)$",data)[1] if sidecar.exists() else uuid.uuid5(
        uuid.NAMESPACE_URL,"bar-promenade/"+path.relative_to(ROOT).as_posix()).hex
    data=re.sub(r"(?m)^guid: \w+$","guid: "+guid,data)
    for key,value in (("maxTextureSize",size),("enableMipMap",int(mips)),("sRGBTexture",int(not linear)),
                      ("linearTexture",int(linear)),("isReadable",0),("streamingMipmaps",0),
                      ("filterMode",2 if mips else 1),("wrapU",int(clamp)),("wrapV",int(clamp)),("wrapW",int(clamp)),
                      ("textureCompression",2),("alphaUsage",int(linear)),("alphaIsTransparency",0),
                      ("aniso",4),("ignoreMipmapLimit",1),("crunchedCompression",0),("nPOTScale",2 if pot else 0)):
        data=re.sub(r"(?m)^(\s*"+key+r":) -?\d+$",lambda m:m[1]+" "+str(value),data)
    blocks=data.split("  - serializedVersion:")
    for i in range(1,len(blocks)):
        if "buildTarget: Standalone" in blocks[i]:
            blocks[i]=re.sub(r"(?m)^(\s*textureFormat:) -?\d+$",r"\g<1> 25",blocks[i])
            blocks[i]=re.sub(r"(?m)^(\s*overridden:) \d+$",r"\g<1> 1",blocks[i])
    return "\n".join(l.rstrip() for l in "  - serializedVersion:".join(blocks).splitlines())+"\n"


def write_or_check(path,data,check):
    if check:
        if not path.exists() or path.read_bytes()!=data:
            raise ValueError("Stale village output: "+str(path))
    else:
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_bytes(data)


def publish(path,data,size,linear,check,*,mips=True,clamp=False,pot=False):
    write_or_check(path,data,check)
    expected=meta(path,size,linear,mips=mips,clamp=clamp,pot=pot)
    sidecar=Path(str(path)+".meta")
    if check:
        actual="\n".join(l.rstrip() for l in sidecar.read_text(encoding="utf8").splitlines())+"\n"
        if actual!=expected:
            raise ValueError("Village map import profile drifted: "+str(path))
    else:
        sidecar.write_text(expected,encoding="utf8",newline="\n")
    return hashlib.sha256(data).hexdigest()


def png(values):
    buffer=io.BytesIO()
    # NumPy authoring rows ascend in physical V, while PNG rows descend from
    # the top. Unity's UV origin is bottom-left. Unique Blender-written PNGs
    # bypass this function and are copied without a second transport flip.
    Image.fromarray(np.uint8(np.clip(values[::-1]*255+.5,0,255))).save(buffer,format="PNG",compress_level=9)
    return buffer.getvalue()


def render(family,size,response=False,ground=False):
    pitch=art.spec(family)[2]
    result=np.empty((size,size,4 if response or ground else 3),np.float32)
    # Bounded row batches keep 2K generation well below an atlas-sized working set.
    for start in range(0,size,128):
        stop=min(size,start+128)
        u,v=np.meshgrid((np.arange(size,dtype=np.float32)+.5)*pitch/size,
                        (np.arange(start,stop,dtype=np.float32)+.5)*pitch/size)
        if ground:
            values=art.ground_sample(family,u,v,pitch/size)
        else:
            colour,physical=art.sample(family,u,v,pixel_metres=pitch/size)
            values=physical if response else colour
        result[start:stop]=values
    if not response and not ground and not art.spec(family)[4]:
        # Measured neutral mean preserves every plot's current authored colour.
        colour=art.linear(result)
        measured=float(np.mean(np.sum(colour*np.asarray((.2126,.7152,.0722)),axis=-1)))
        result=art.gamma(np.clip(colour*(.58/measured),0,1))
    if response:
        # Physical range and finite checks use actual packed data.
        if not np.isfinite(result).all() or result[...,:2].min()<0 or result[...,:2].max()>1:
            raise ValueError("Invalid physical village response: "+family)
    return result


def check_physics():
    # Independent central differences verify metres/signs, including cross-axis grain.
    u,v=np.meshgrid(np.linspace(.13,.83,13),np.linspace(.17,.91,11))
    epsilon=.00005
    for family,_,_,_,_ in art.SPECS:
        h,r=art.structure(family,u,v,.002)
        dx=(art.structure(family,u+epsilon,v,.002)[0]-art.structure(family,u-epsilon,v,.002)[0])/(2*epsilon)
        dy=(art.structure(family,u,v+epsilon,.002)[0]-art.structure(family,u,v-epsilon,.002)[0])/(2*epsilon)
        if max(float(np.max(abs(dx-(r[...,0]*2-1)))),float(np.max(abs(dy-(r[...,1]*2-1)))))>.001:
            raise ValueError("Village physical derivatives drifted: "+family)
    # Read back actual encoded PNG rows, independent of the writing formula.
    # An asymmetric ramp catches V reflection even on nearly flat materials.
    ramp=np.zeros((5,7,3),np.float32)
    ramp[...,0]=np.arange(7)[None,:]/6
    ramp[...,1]=np.arange(5)[:,None]/4
    decoded=np.asarray(Image.open(io.BytesIO(png(ramp))))/255
    if not np.allclose(decoded[4,0],ramp[0,0],atol=1/255) or not np.allclose(decoded[0,6],ramp[4,6],atol=1/255):
        raise ValueError("Village PNG transport does not retain physical U/V orientation")


def check_packed_slopes(family,raw,size):
    rgba=np.asarray(Image.open(io.BytesIO(raw)).convert("RGBA"),np.float32)/255
    pitch=art.spec(family)[2]
    i=np.array((size//7,size//3,size*5//7))
    j=np.array((size//5,size*2//5,size*4//5))
    u=(i+.5)*pitch/size; v=(j+.5)*pitch/size
    epsilon=.00005
    dx=(art.structure(family,u+epsilon,v,pitch/size)[0]-art.structure(family,u-epsilon,v,pitch/size)[0])/(2*epsilon)
    dy=(art.structure(family,u,v+epsilon,pitch/size)[0]-art.structure(family,u,v-epsilon,pitch/size)[0])/(2*epsilon)
    actual=rgba[size-1-j,i,:2]*2-1
    if np.max(abs(actual-np.stack((dx,dy),axis=-1)))>1/255+.0005:
        raise ValueError("Packed village physical slope orientation drifted: "+family)


def build(check):
    art.sources(); check_physics()
    rows=[]; estimated=0
    for family,_,pitch,_,full_colour in art.SPECS:
        # Enamel is nearly flat; reserve its spare pixels for the native photo
        # aspect, whose 1024x683 downsample cannot retain block compression.
        size=512 if family=="Enamel" else art.size_for(family); response_size=size//2
        names={"albedo":family+"Albedo","response":family+"Response","ground_response":family+"GroundResponse"}
        row={"family":family,"size":size,"response_size":response_size,"meters_per_tile":pitch,
             "full_color":full_colour,"ground_response":"","ground_response_sha256":""}
        for kind,resolution,linear in (("albedo",size,False),("response",response_size,True)):
            values=render(family,resolution,response=linear)
            if not linear:
                row["mean_linear_luminance"]=round(float(np.mean(np.sum(art.linear(values)*np.asarray((.2126,.7152,.0722)),axis=-1))),8)
            data=png(values); name=names[kind]
            if linear:
                check_packed_slopes(family,data,resolution)
            row[kind]=PREFIX+name
            row[kind+"_sha256"]=publish(OUTPUT/(name+".png"),data,resolution,linear,check)
            estimated+=bc7_bytes(resolution)
        if family in art.GROUND:
            name=names["ground_response"]
            row["ground_response"]=PREFIX+name
            row["ground_response_sha256"]=publish(OUTPUT/(name+".png"),png(render(family,512,ground=True)),512,True,check)
            estimated+=bc7_bytes(512)
        rows.append(row)
        print("Village surface:",family,flush=True)
    extras=[]
    for name,size,linear in (("VillageUniqueAlbedo",4096,False),("VillageUniqueResponse",2048,True)):
        path=ROOT/("ArtSource/Village/Surfaces/Atlases/"+name+".source.png")
        if not path.is_file():
            raise ValueError("Bake the shared village unique atlas first: "+str(path))
        with Image.open(path) as image:
            if image.size!=(size,size):
                raise ValueError("Wrong unique map size")
        checksum=publish(OUTPUT/(name+".png"),path.read_bytes(),size,linear,check,clamp=True)
        estimated+=bc7_bytes(size)
        extras.append({"resource":PREFIX+name,"size":size,"mipmaps":True,"sRGB":not linear,"sha256":checksum})
    # These existing assets retain every pixel and UV; only their PC compression
    # changes. Junctions deliberately keep their no-mipmap, guarded-tile contract.
    retained=(("Village/Textures/Junctions/VillageJunctionAtlas",2048,False,False),
              ("Village/Textures/Junctions/VillageJunctionResponseAtlas",2048,True,False),
              ("Village/Textures/LodgePictures",1024,False,True),
              ("Village/Textures/LodgeGroupPhotograph",2048,False,True))
    for resource,size,linear,mips in retained:
        path=ROOT/("Assets/Resources/"+resource+".png")
        pot=resource.endswith("LodgeGroupPhotograph")
        checksum=publish(path,path.read_bytes(),size,linear,check,mips=mips,clamp=True,pot=pot)
        with Image.open(path) as image:
            width,height=image.size
            if pot:
                # Unity needs POT import dimensions for this PC compression;
                # normalized photo UVs retain the original image content/aspect.
                width=1<<(width-1).bit_length();height=1<<(height-1).bit_length()
            scale=min(1,size/max(width,height)); width=round(width*scale); height=round(height*scale)
        estimated+=bc7_bytes(width,height,mips)
        extras.append({"resource":resource,"size":size,"mipmaps":mips,"sRGB":not linear,"sha256":checksum})
    if estimated>art.BUDGET:
        raise ValueError(f"Village environment maps exceed96MiB: {estimated}")
    result={"design_id":art.DESIGN,"generator_version":art.VERSION,"gpu_budget_bytes":art.BUDGET,
            "estimated_gpu_bytes":estimated,"channels":["signed_physical_slope_u","signed_physical_slope_v","roughness","cavity"],
            "ground_channels":["smoothness_multiplier","centred_height","cavity_tendency","opaque"],
            "source_provenance":art.provenance(),"sheets":rows,"additional_maps":extras}
    write_or_check(MANIFEST,(json.dumps(result,indent=2)+"\n").encode(),check)
    meta_path=Path(str(MANIFEST)+".meta")
    if not meta_path.exists() and not check:
        meta_path.write_text("fileFormatVersion: 2\nguid: "+uuid.uuid5(uuid.NAMESPACE_URL,MANIFEST.relative_to(ROOT).as_posix()).hex+"\n",encoding="utf8")
    print(f"VILLAGE SURFACES OK: physical gradients, authored maps, import profiles; calculatedBC7 {estimated} <= {art.BUDGET}")


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only",action="store_true")
    build(parser.parse_args().validate_only)
