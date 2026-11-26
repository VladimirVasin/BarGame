"""Physical response of coated wood, painted/bare metal and rubber at real contact sites."""
import argparse
from pathlib import Path
from typing import NamedTuple

import numpy as np

OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/WorldFinishes"


class Finish(NamedTuple):
    # Absolute Lit smoothness at rest, rubbed and exposed; metallic of film and substrate.
    intact: float
    rubbed: float
    exposed: float
    coat_metal: float
    base_metal: float


WOOD = Finish(.27, .58, .14, 0., 0.)
PAINTED_STEEL = Finish(.24, .44, .66, .04, .84)
BARE_METAL = Finish(.40, .65, .46, .73, .76)
RUBBER = Finish(.095, .32, .12, 0., 0.)


def response(finish, contact, chip=0.):
    contact, chip = np.clip(contact, 0, 1), np.clip(chip, 0, 1)
    coated = finish.intact + (finish.rubbed - finish.intact) * contact
    return (coated + (finish.exposed - coated) * chip,
            finish.coat_metal + (finish.base_metal - finish.coat_metal) * chip)


def patch(u, v, x, y, width, height):
    return np.exp(-((u-x)/width)**2 - ((v-y)/height)**2)


def chart(name, width, height):
    u, v = np.meshgrid((np.arange(width)+.5)/width, 1-(np.arange(height)+.5)/height)
    chip = np.zeros_like(u)
    grain = np.sin(40*u + 2*np.sin(22*v)) * np.sin(183*v + .8*np.sin(12*u))
    if name == "RailWood":
        contact = np.clip((v-.39)/.51, 0, 1) * (.63+.37*np.sin(5.8*u+.5)**2)
        chip = np.clip((patch(u,v,.24,.86,.047,.11) + patch(u,v,.73,.94,.031,.075)
                        + .12*grain-.43)*2.6, 0, 1)
        finish = WOOD
    elif name == "RailSupports":
        contact = np.clip((v-.55)*1.9, 0, 1)
        # Paint breaks at the edges of the steel saddle, around its contact with the cap.
        chip = np.clip((v-.88+.048*np.sin(57*u)+.021*grain)*9, 0, 1)
        finish = PAINTED_STEEL
    elif name == "BusGrip":
        # V is height in a three-metre cabin chart. Wear follows a hand's reach.
        contact = np.exp(-((3*v-1.39)/.36)**4) * (.76+.24*np.sin(18*u)**2)
        chip = contact * np.clip((np.cos(3*v*27+1.4*np.sin(u*43))-.48)*1.8, 0, 1)
        finish = PAINTED_STEEL
    else:
        # The floor's top UVs encode physical source X/Y; both caps have their own lip.
        x, y = (u-.5)*2.56, (v-.5)*8.
        contact = np.zeros_like(u)
        from city_bus_surfaces import STEP_SECTION, STEP_SPANS
        for start, end in STEP_SPANS:
            for distance, _, depth, _ in STEP_SECTION:
                wear = patch(x,y,-distance-.28*depth,(start+end)*.5,.38*depth,.31*(end-start))
                contact = np.maximum(contact, wear)
        finish = RUBBER
    smooth, metal = response(finish, contact, chip)
    # R metallic, G visible substrate, B relative pigment lift (0.5 neutral), A smoothness.
    pigment = .5 + .11*contact + (.035*grain if finish == WOOD else 0.)
    return np.rint(np.stack(np.broadcast_arrays(metal, chip, pigment, smooth), axis=-1)*255).astype('uint8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    for name, w, h in [('RailWood',256,64), ('RailSupports',128,64), ('BusGrip',128,128), ('BusStep',256,512)]:
        from atlas_kit import PixelCanvas
        image = PixelCanvas(w, h)
        image.pixels[:] = chart(name,w,h).tobytes()
        output = image.png_bytes()
        path = OUTPUT / (name+'.png')
        if args.check:
            if path.read_bytes() != output:
                raise ValueError('Stale finish chart: '+str(path))
        else:
            OUTPUT.mkdir(parents=True, exist_ok=True)
            path.write_bytes(output)


if __name__ == '__main__':
    main()
