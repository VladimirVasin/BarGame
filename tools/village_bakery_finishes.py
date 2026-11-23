"""Short-repeat finishes for the abandoned shop, sampled in physical metres."""
import math
import numpy as np
import village_surface_authoring as village

# The small sheets cover a single board or masonry course at close-view density.
# For the chimney V is the height band around the flue rather than a board axis.
PITCH = {"BakeryYardWood": .58, "BakeryTrayWood": .42, "BakeryFlue": 1.8, "BakeryGlass": .92}


def sample(name, u, v, footprint):
    pitch = PITCH[name]
    x, y = u/pitch, v/pitch
    if name == "BakeryGlass":
        # Quiet cloudy glass and vertical dried runs, still completely opaque.
        streak = np.sin(math.tau*(3*x+.025*np.sin(math.tau*y)))**8
        haze = .5+.5*np.cos(math.tau*(x+2*y))
        rgb = np.array((.145,.169,.161))*(.88+.10*haze+.15*streak)[...,None]
        response = np.stack((np.full_like(x,.5),np.full_like(x,.5),
                             .37+.13*streak+.06*haze,np.ones_like(x)),axis=-1)
        return rgb.astype(np.float32),response.astype(np.float32)
    flue = name == "BakeryFlue"
    pine = name == "BakeryTrayWood"
    cell = "Stone" if flue else "Pine" if pine else "WeatheredWood"
    grain = village.pigment(cell,x,y,footprint/pitch)
    value = np.mean(village.linear(grain),axis=-1)
    mean = np.mean(village.linear(village.sources()[cell][-1]))
    value = np.clip(value/mean,.54,1.52)
    # Periodic grain wear and weather checks share one differentiable height
    # field. Response R/G stores physical slopes including the metre scale.
    fx,fy = (11,9) if flue else (24,1) if pine else (15,1)
    a = math.tau*(fx*x+fy*y)+.6*np.sin(math.tau*y)
    amp = .00019 if flue else .00010 if pine else .00027
    amp *= math.exp(-(math.pi*footprint/pitch)**2*(fx*fx+fy*fy))
    du = amp*np.cos(a)*math.tau*fx/pitch
    dv = amp*np.cos(a)*math.tau*(fy+.6*np.cos(math.tau*y))/pitch
    ridge = np.sin(a)
    base = np.array((.49,.438,.365) if flue else (.60,.491,.346) if pine else (.432,.410,.352))
    if flue:
        # A sooty rim dries to uneven fingers below the mouth. Cosine gives a
        # periodic swatch. The masonry UVs only use the ascending half of it.
        band = (.5-.5*np.cos(math.tau*y))**2.5
        deposit = np.clip(band*(.89+.08*np.sin(math.tau*x*3)+.11*(1-value)),0,1)
        colour = base*(1-deposit[...,None]) + np.array((.159,.153,.142))*deposit[...,None]
    else:
        colour = base
    rgb = village.gamma(np.clip(village.linear(colour)*value[...,None]*(1+.048*ridge[...,None]),0,1))
    rough = (.965 if flue else .855 if pine else .946)-.023*ridge
    response = np.stack((.5+.5*du,.5+.5*dv,rough,.96+.033*ridge),axis=-1)
    return rgb.astype(np.float32), response.astype(np.float32)
