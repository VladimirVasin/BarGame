"""Room-space wear for the fitted timber, upholstery and metalwork in the pub."""
import math
import numpy as np


def applies(name):
    return name.startswith(("Counter ", "Bar Counter", "Bar Stool ", "Bar Drink Service Stool",
        "Beer Tap ", "Tap ", "Backbar Shelf", "Backbar Shelves", "Backbar Mirror Frames",
        "Booth ", "Snug Backing ", "Social Pub Table "))


def fields(u, v, chart, original):
    name, sheet = chart["mesh"], chart["sheet"]
    axes = chart["physical_axes"]
    lo = chart["origin_m"]
    w,h = chart["extent_m"]
    U,V = (u-lo[0])/max(w,.0001),(v-lo[1])/max(h,.0001)
    top = axes == [0,1]
    height,rough,cavity,pigment = original
    grain = np.sin(v*165+2*np.sin(u*5)+np.sin(v*29+u*2))
    if sheet in ("PolishedWood","DarkWood"):
        # The return is a separate board, with its grain running towards the aisle.
        along,across=(v,u) if name=="Counter Return Top" or (axes!=[0,1] and axes[1]==2 and "Panels" in name) else (u,v)
        grain=np.sin(150*across+3*np.sin(along*1.8+across*9)+.8*np.sin(along*8+across*17))
        grain+=.45*np.sin(across*312+1.7*np.sin(along*3+across*14))
        polish=np.zeros_like(u)
        edge=np.minimum.reduce([U*w,(1-U)*w,V*h,(1-V)*h])
        lip=np.exp(-np.square(edge/.014))
        if name in ("Counter Top","Counter Return Top") and top:
            elbow=np.exp(-((v-1.36)/.105)**2)*(.55+.45*np.cos((u+.75)*math.tau/.90))
            wipe=np.exp(-((u+.18)/.68)**2-((v-2.16)/.21)**2)
            polish=np.maximum(elbow,wipe) if name=="Counter Top" else .4*lip
        elif "Stool" in name and top:
            polish=np.exp(-((U-.53)/.39)**2-((V-.52)/.39)**2)
        elif "Table" in name and top:
            polish=np.exp(-((V-.53)/.35)**2-((U-.5)/.42)**2)
        elif name=="Backbar Shelves" and top:
            polish=np.exp(-((v-3.391)/.074)**2)
        pigment=pigment*(.92+.107*grain+.14*polish+.09*lip)
        rough=np.clip(rough-.19*polish-.055*lip+.025*grain,.28,.97)
        height+=.00034*grain
    elif sheet=="WornLeather":
        # Work in the physical width of each upholstered panel. Narrow side charts
        # carry the edging, while seating faces gain broader abrasion from the body.
        seat="Cushion" in name
        face=(top and seat) or (axes==[0,2] and not seat)
        rub=np.exp(-((U-.51)/.40)**2-((V-.56)/.34)**2) if face else 0.
        dx=np.minimum(U,1-U)*w
        dy=np.minimum(V,1-V)*h
        edge=np.minimum(dx,dy)
        stitch=np.exp(-((edge-.024)/.004)**2)*(.46+.54*np.cos((u+v)*223))
        folds=np.sin(53*u+34*v+2*np.sin(v*19))*np.exp(-edge/.08)
        nap=np.sin(u*205+2*np.sin(v*41))*np.cos(v*182)
        pigment=pigment*(1.04+.31*rub+.24*np.exp(-edge/.019)+.31*stitch+.052*folds+.026*nap)
        height+=.00040*folds+.00018*nap-.00027*stitch
        rough=np.clip(rough-.17*rub-.065*np.exp(-edge/.018),.58,.995)
    elif sheet in ("PaintedMetal","AgedBrass","Ceramic"):
        if "Foot Rail" in name:
            along = v if name.startswith("Counter Return") and top else u
            boot=.5+.5*np.cos((along+.73)*math.tau/.90)
            polish=.20+.74*boot**2
        elif "Tap Handle" in name:
            polish=np.exp(-((v-.21)/.075)**2) if 2 in axes else .65
        elif "Shelf Brackets" in name or "Drip Pan" in name:
            polish=.12
        elif "Tap" in name:
            polish=.65 if "Grate" in name else .37
        else:
            polish=.35+.18*np.sin(6*u+4*v)
        patina=(.5+.5*np.sin(16*u+1.8*np.sin(11*v))*np.sin(v*23+u*4))*(1-polish)
        brush=np.sin(v*180+np.sin(u*7))*.5+.5*np.sin(133*v+8*u)
        pigment=pigment*(.93+.19*polish-.23*patina+.022*brush)
        rough=np.clip(rough-.16*polish+.08*patina,.25,.96)
        cavity=np.clip(cavity-.045*patina,.84,1)
        height+=.00009*brush
    return height,rough,cavity,pigment
