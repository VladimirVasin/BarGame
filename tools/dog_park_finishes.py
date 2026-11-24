"""Rest-pose coat charts, bench timber, fence metal and metre-mapped dog yard soil."""
import numpy as np
from laundry_finishes import png

CELL, PAD = 256, 6
# The first eleven slots follow the generator's coat palette; slot 7 is nose/eye leather.
PIGMENTS = ((98,72,54),(155,112,73),(194,161,112),(209,193,155),
            (111,111,101),(74,75,69),(61,62,59),(46,41,36),
            (169,128,91),(106,86,71),(178,174,153),(86,103,92),
            (135,113,87),(152,128,96),(105,94,78),(133,134,118))


def field(rng, shape, rows, cols):
    """Continuous low-frequency changes across the chart, independent of pixel grit."""
    h, w = shape
    grid = rng.normal(size=(rows+1, cols+1))
    y, x = np.mgrid[0:h,0:w]
    y, x = y*rows/h, x*cols/w
    iy, ix = y.astype(int), x.astype(int)
    fy, fx = y-iy, x-ix
    fy, fx = fy*fy*(3-2*fy), fx*fx*(3-2*fx)
    return ((1-fy)*((1-fx)*grid[iy,ix]+fx*grid[iy,ix+1])
            + fy*((1-fx)*grid[iy+1,ix]+fx*grid[iy+1,ix+1]))


def charts():
    albedo = np.empty((4*CELL,4*CELL,3),np.uint8)
    response = np.empty((4*CELL,4*CELL,4),np.uint8)
    n = CELL-2*PAD
    v,u = np.mgrid[0:n,0:n]/(n-1)
    for i, rgb in enumerate(PIGMENTS):
        rng = np.random.default_rng(11963+i)
        grain = rng.normal(size=u.shape)
        cloud = field(rng,u.shape,6,10)
        value = 2*cloud+1.5*grain
        rough = .77
        relief = .018*grain
        if i <= 10 and i != 7:
            # Hair lies from nape to rump. Longer locks on the grey dog's cheek and flank.
            flow = 5*np.sin(3*u+4*v)+.8*cloud
            shaggy = i in (4,5,10)
            fibres = np.cos(v*(330 if shaggy else 520)+flow*3+8*np.sin(u*8+v*12))
            value += (5 if shaggy else 3)*fibres+1.8*field(rng,u.shape,80,16)
            if i in (1,4,8):
                saddle = np.exp(-((u-.46)/.39)**4-((v-.23-.09*np.cos(u*7))/.39)**4)
                value += 8-32*saddle+6*np.clip(cloud,-1,1)
            rough = .88 if shaggy else .78
            relief += fibres*.09
        elif i == 7:
            rough = .37
            value += grain*2.2
        elif i in (12,13):
            curl = .013*np.sin(u*11)+.021*np.sin(u*5+v*9)
            ring = np.hypot((u-.23)*1.2,v+.35) if i==13 else v+curl
            grain_line = np.sin(ring*(170 if i==13 else 248)+.48*cloud)
            wear = np.exp(-((v-.77)/.072)**2) if i==12 else 0
            value += grain_line*6+wear*13
            rough = .77-.15*wear
            relief += grain_line*.09
        elif i in (11,14,15):
            abrasion = np.clip(cloud-.6,0,1)
            value += abrasion*(10 if i==11 else 5)
            rough = (.58 if i==11 else .80 if i==14 else .43)-.09*abrasion
            relief += .10*cloud
        dy,dx = np.gradient(relief)
        slope = np.stack((.5-dx,.5+dy,rough+np.clip(cloud,-1,1)*.025,
                          np.clip(.97+.012*grain,0,1)),axis=-1)
        color = np.clip(np.array(rgb)+value[:,:,None],0,255).astype(np.uint8)
        row,col = divmod(i,4)
        area = np.s_[row*CELL:(row+1)*CELL,col*CELL:(col+1)*CELL]
        for target,tile in ((albedo,color),(response,np.uint8(255*np.clip(slope,0,1)))):
            target[area] = np.pad(tile,((PAD,PAD),(PAD,PAD),(0,0)),mode="edge")
    return albedo, response


def maps():
    albedo,response = charts()
    rng = np.random.default_rng(42109)
    n = 1024
    row,col = np.mgrid[0:n,0:n]
    x,z = (col+.5)*8/n-4,5-(row+.5)*10/n
    broad = field(rng,x.shape,14,10)
    grit = rng.normal(size=x.shape)
    crumbs = field(rng,x.shape,190,160)
    # A worn curved walk from the gap between the posts to the front of the bench.
    line = -.09-2.45*((5-z)/9.15).clip(0,1)**1.48
    lane = np.exp(-((x-line)/(.50+.11*np.cos(z*.9)))**2)*np.clip((z+4.6)*1.6,0,1)
    hem = np.maximum(abs(x)-3.45, abs(z)-4.45).clip(0,1)
    flecks = np.clip(crumbs-1,0,2)*(1-lane*.55)
    value = 5*broad+4.5*crumbs+grit*(3.4-1.2*lane)+9*lane+8*flecks
    # Exposed soil darkens into the surrounding moist earth, especially at the mouth.
    edge = np.minimum(4-abs(x),5-abs(z))
    value -= 16*np.exp(-np.maximum(edge,0)/.21)
    soil = np.clip(np.array((112,99,79))+value[:,:,None]+hem[:,:,None]*np.array((-6,1,-4)),0,255)
    terrain = np.stack((.30+.45*lane,.51+.085*crumbs+.035*grit+.08*flecks,.12+.10*hem-.07*lane),axis=-1)
    return {"DogParkPalette":png(albedo),"DogParkFinishResponse":png(response),
            "DogParkSoil":png(soil.astype(np.uint8)),"DogParkSoilResponse":png(np.uint8(255*np.clip(terrain,0,1)))}


def uv(slot,u,v):
    row,col = divmod(slot,4)
    def inset(t):
        return (PAD+.5+max(0,min(1,t))*(CELL-2*PAD-1))/CELL
    return ((col+inset(u))/4,1-(row+inset(v))/4)
