"""Unique atlas charts for the near checkpoint kit, in the generator's Unity metres."""
from math import floor
from interior_kit import solid_bounds

GROUPS = {'Booth', 'Barrier', 'Gate', 'Fence', 'RepairedFence', 'Shelter',
          'CanopyLamp', 'Bench', 'UtilityCabinet', 'Lamp'}
PLASTER, RENDER, PLINTH, FOOTING, ROOF, ZINC, ENAMEL, IRON = range(8)
STEEL, GLASS, STRIPE, TIMBER, DOOR, MORTAR, SEAL, REPAIR = range(8, 16)


def chart(group, role, point, normal_axis, bounds):
    lo, hi = bounds
    x,y,z = point
    size = [b-a for a,b in zip(lo,hi)]
    mid = [(a+b)*.5 for a,b in zip(lo,hi)]
    axes = ((2,1),(0,2),(0,1))[normal_axis]
    s,t = [(point[i]-lo[i])/max(size[i],.00001) for i in axes]
    if role == 'Wall':
        side = abs(mid[0]) > 1.15 or (normal_axis == 0 and abs(x)>1.30)
        cell = PLASTER if side else RENDER
        s,t = ((z/3.40+.5) if side else (x/3.00+.5)), (y-.16)/2.66
    elif role == 'Glass':
        cell = GLASS
    elif role == 'Paint':
        cell = STRIPE
        s,t = z/6.28+.5, (y-1.07)/.24
    elif role == 'Roof':
        cell = ROOF
    elif role == 'Zinc':
        cell = ZINC
    elif role == 'Bright':
        cell = STEEL
    elif role == 'Stone':
        cell = PLINTH if group == 'Booth' and hi[1] < .70 and max(size)<2.5 else FOOTING
    elif role == 'Dark':
        cell = MORTAR if group=='Booth' and hi[1]<.55 else SEAL
    elif role == 'Wood':
        cell = TIMBER
    elif group == 'Booth' and mid[2] < -1.45 and size[0]>.55 and size[1]>.45:
        cell = DOOR
    elif group == 'RepairedFence' and abs(mid[2])<.72:
        cell = REPAIR
    elif group in ('Barrier','Fence','RepairedFence','Gate'):
        cell = IRON
    else:
        cell = ENAMEL
    if cell not in (PLASTER,RENDER,STRIPE,GLASS,DOOR):
        # A metre-long rail samples along its length, a bolt uses a small piece
        # of the same sheet. Components get different origins within their tile.
        if cell in (TIMBER,ROOF) and size[axes[1]]>size[axes[0]]:
            axes=axes[::-1]
            s,t=t,s
        span_s=min(.95,max(.09,size[axes[0]]/2.6))
        span_t=min(.93,max(.09,size[axes[1]]/1.7))
        seed=mid[0]*.37+mid[1]*.53+mid[2]*.69
        s = s*span_s+(1-span_s)*(seed-floor(seed))
        t = t*span_t+(1-span_t)*((seed*3.31)%1)
    inset = .014
    s = inset+(1-2*inset)*min(1,max(0,s))
    t = inset+(1-2*inset)*min(1,max(0,t))
    return ((cell%4+s)/4, (3-cell//4+t)/4)
