"""Causal WC paint and physical fields in the existing registered Bar atlas.

Geometry remains the authority: metre coordinates locate tile joints, splash,
standing water marks, handling and broken coating. The same masks affect
pigment, slope, roughness and cavity; brightness is never converted to height.
"""
from __future__ import annotations

import numpy as np


def polygon_mask(u,v,points):
    """Two authored broken-glaze outlines, never a thresholded repeating field."""
    inside=np.zeros_like(u,dtype=bool)
    distance=np.full_like(u,np.inf)
    for a,b in zip(points,points[1:]+points[:1]):
        dx,dy=b[0]-a[0],b[1]-a[1]
        crossing=((a[1]>v)!=(b[1]>v)) & (u < dx*(v-a[1])/(dy if abs(dy)>1e-9 else 1e-9)+a[0])
        inside ^= crossing
        t=np.clip(((u-a[0])*dx+(v-a[1])*dy)/(dx*dx+dy*dy),0,1)
        distance=np.minimum(distance,np.sqrt((u-a[0]-dx*t)**2+(v-a[1]-dy*t)**2))
    return np.clip(.5+np.where(inside,distance,-distance)/.007,0,1)


def applies(chart):
    return chart["mesh"].startswith("Bar Washroom ") and "Source Hall" not in chart["mesh"]


def masks(u, v, chart):
    name = chart["mesh"]
    axes = chart["physical_axes"]
    coords = {axes[0]: u, axes[1]: v}
    y = coords.get(2, np.zeros_like(u))
    x = coords.get(0, np.zeros_like(u)-3.9)
    z = coords.get(1, np.zeros_like(u)+5.65)
    horizontal = coords.get(1, x) if 2 in axes else x
    floor = "Floor" in name
    wall = "Wall" in name or "Splashback" in name
    tile = "Tiles" in name or "Splashback" in name or floor
    # Dirty water collects at the wall/floor boundary and below the real basin.
    base = np.exp(-np.maximum(y, 0)*7) if wall or "Tiles" in name else np.zeros_like(u)
    if floor:
        edge = np.minimum.reduce((abs(x+5.4), abs(x+2.4), abs(z-4.15), abs(z-7.15)))
        base = np.exp(-edge*11)*.9
        splash = np.exp(-((x+2.66)/.56)**2-((z-5.18)/.67)**2)
        toilet = np.exp(-((x+3.85)/.64)**2-((z-6.35)/.69)**2)*.72
        scuff = np.exp(-((x+4.16)/.30)**2-((z-4.55)/1.05)**2)
        splash = np.maximum(splash, toilet)
    else:
        location = 5.18 if 1 in axes else -3.85
        splash = np.exp(-((horizontal-location)/.48)**2) * np.exp(-((y-.72)/.52)**2) if wall or "Tiles" in name else np.zeros_like(u)
        scuff = np.exp(-((y-.35)/.18)**2)*(.5+.5*np.sin(horizontal*6)**2) if wall else np.zeros_like(u)
    rivulet = np.maximum(0,np.sin(horizontal*41+np.sin(horizontal*8)*1.7)-.58)
    grime = np.clip(base*.73+splash*(.45+rivulet*.55)+scuff*.27,0,1)
    damp = np.clip(splash*.57+base*.30,0,.8)
    grout=np.zeros_like(u);chip=np.zeros_like(u);crack=np.zeros_like(u)
    if tile:
        vertical = z if floor else y
        a = abs((horizontal+.025) % .22-.11)
        b = abs((vertical+.015) % .18-.09)
        grout = np.maximum(np.exp(-((a-.11)/.004)**2),np.exp(-((b-.09)/.004)**2))
        # Local missing glaze and a cracked tile beside the sink; stable metre masks.
        center=5.55 if 1 in axes and not floor else -3.0
        level=5.58 if floor else .61
        first=[(center-.06,level-.035),(center-.035,level-.065),(center+.018,level-.044),
               (center+.045,level-.012),(center+.021,level+.009),(center+.031,level+.048),
               (center-.019,level+.037),(center-.05,level+.018)]
        second=[(center-.39,level+.22),(center-.36,level+.205),(center-.334,level+.224),
                (center-.346,level+.25),(center-.375,level+.244)]
        chip=np.maximum(polygon_mask(horizontal,vertical,first),polygon_mask(horizontal,vertical,second))
        line = horizontal*.31+vertical-.86
        crack = np.exp(-(line/.004)**2)*np.exp(-((horizontal-(5.3 if 1 in axes and not floor else -3.2))/.7)**2)
    hand = np.exp(-((y-1.01)/.16)**2)*np.exp(-((horizontal-.91)/.24)**2) if "Door Leaf" in name else np.zeros_like(u)
    return grime,damp,grout,chip,crack,hand


def fields(u, v, chart, base):
    height,roughness,cavity,pigment=base
    grime,damp,grout,chip,crack,hand=masks(u,v,chart)
    # Relief comes from explicit grout/chips/coating, not the painted stains.
    height=height-grout*.0018-chip*.0011-crack*.0015
    roughness=np.clip(roughness+grime*.10+grout*.17+chip*.17-damp*.22-hand*.16,.21,.99)
    cavity=np.clip(cavity-grout*.33-chip*.20-crack*.32,.45,1.)
    pigment=pigment*(1-grime*.42-grout*.27-chip*.18-crack*.33)*(1+hand*.05)
    name=chart["mesh"]
    if any(token in name for token in ("Paper Roll","Discarded Paper","Sticker Cards")):
        fold=np.exp(-(np.sin(u*41+v*17)/.16)**2)
        height=height+fold*.0006
        roughness=np.full_like(u,.96)
        cavity=np.clip(.98-fold*.08,.86,1.)
        pigment=1-fold*.12-grime*.18
    return height,roughness,cavity,pigment


def colour(rgb,u,v,chart):
    grime,damp,grout,chip,crack,hand=masks(u,v,chart)
    if chart["sheet"] == "Ceramic":
        # The old generic Ceramic sheet has printed-looking repeated oval
        # speckles. These WC surfaces instead get a quiet glaze pigment;
        # their registered damage/grout/water masks remain the authority.
        floor="Floor" in chart["mesh"]
        base=np.array((.63,.615,.565) if floor else (.76,.735,.65))
        broad=np.sin(u*.87+np.sin(v*.49)*1.3)*np.cos(v*1.19-u*.23)
        fine=np.sin(u*317+v*131)*np.cos(v*263-u*109)
        variation=1+broad*.025+fine*.007
        pigment=(1-grime*.42-grout*.27-chip*.18-crack*.33)
        rgb=base*variation[...,None]*pigment[...,None]
    rgb=rgb*(1-grime[...,None]*np.array((.06,.025,.12)))
    # Missing glaze exposes dark ochre body, while grout retains its own pigment.
    rgb=rgb*(1-chip[...,None]*.55)+np.array((.20,.155,.095))*chip[...,None]*.55
    return rgb
