"""The apartment furniture atlas, in four rows from the top of the PNG."""
SEAT, BACK, PILLOW, COVER, PANEL, COUNTER, SINK, HARDWARE = range(8)
ENAMEL, LINER, RUBBER, JOINERY, THRESHOLD, CAP, POST, SLAB = range(8,16)

PLATE, BOTTLE, TOWEL, SHIRT, DUVET, PAPER, SPILL, SHEET = range(16,24)

def finish_for(name):
    if name.startswith(("Day","Home Day1")):
        if "Stacked Plate" in name or "Kitchen Plate" in name: return PLATE
        if "Table Bottle" in name: return BOTTLE
        if "Folded Towel" in name: return TOWEL
        if "Sofa Foot Linen" in name: return SHIRT
        if "Duvet" in name: return DUVET
        if "Dirty Sheet" in name: return SHEET
        if "Flattened Paper" in name: return PAPER
        if "Spill" in name or "Floor Tracked Grime" in name: return SPILL
    fixed={"Home Bed Frame":BACK, "Home Bed Mattress":SEAT, "Home Pillow":PILLOW,
           "Home Bed Crooked Blanket":COVER, "Home Sink":SINK,
           "Home Balcony Deck":SLAB, "Home Balcony Threshold":THRESHOLD,
           "Home Balcony Folding Wood":JOINERY, "Home Balcony Folding Handle":HARDWARE}
    if name in fixed: return fixed[name]
    if name.startswith("Home Sleeping Sofa"): return BACK
    if name.startswith("Home Refrigerator "):
        if "Door Gasket" in name: return RUBBER
        if "Rail" in name: return HARDWARE
        if "Liner" in name or ("Shelf" in name and "Stain" not in name): return LINER
        if "Cabinet" in name or name.endswith("Door Enamel"): return ENAMEL
    if name.startswith("Home Balcony "):
        if name.endswith("Rail Cap"): return CAP
        if name.endswith(" Post"): return POST
    if name.startswith("Home Kitchen "):
        if name.startswith("Home Kitchen Top "): return COUNTER
        if name.endswith("Handle") or " Pull " in name or "Faucet" in name or name.endswith("Sink Drain"): return HARDWARE
        if "Cabinet Door" in name or "Drawer Front" in name or "Counter " in name or "Upper " in name: return PANEL
    return None


def uv(cell, point, axis, bounds):
    lo,hi=bounds
    axes=((1,2),(0,2),(0,1))[axis]
    span=[b-a for a,b in zip(lo,hi)]
    if cell in (BACK,HARDWARE,CAP,RUBBER,JOINERY) and span[axes[1]]>span[axes[0]]:
        axes=axes[::-1]
    if cell==THRESHOLD and axis==2: axes=(1,0)
    u,v=[max(0,min(1,(point[a]-lo[a])/max(.00001,span[a]))) for a in axes]
    rows=2 if cell>=16 else 4
    cell%=16
    gutter=4/(256 if rows==2 else 512)
    return ((cell%4+gutter+(1-2*gutter)*u)/4,
            (rows-1-cell//4+gutter+(1-2*gutter)*v)/rows)
