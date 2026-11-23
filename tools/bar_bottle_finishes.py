"""Bake five reusable, unbranded glass, closure and paper finishes for the shelf and hand meshes."""
import argparse
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image

from bar_bottles import STYLES

OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Bar/Textures"
SIZE = (1024, 512)


def bake():
    width,height = SIZE
    y,x = np.mgrid[:height,:width]
    across,up = (x+.5)/width*len(STYLES), (1.-(y+.5)/height)*3
    style,finish = across.astype(int), up.astype(int)
    u,v = np.clip(((across%1)-.034)/.932,0,1), np.clip(((up%1)-.023)/.954,0,1)
    rng = np.random.default_rng(117)
    noise = rng.normal(0.,1.,u.shape)
    def ridge(distance, radius):
        return np.exp(-np.square(distance/radius))

    # Pigment stays neutral on the glass. The district/service tint supplies its actual colour.
    seam = ridge(np.sin((u-.245)*2*np.pi),.038)
    heel = ridge(v-.045,.017) + .42*ridge(v-.091,.013)
    ripple = np.sin(2*np.pi*(u*11+.16*np.sin(v*8)+style*.13))
    glass = .80+.053*ripple+.09*heel+.046*seam+.008*noise
    colour = np.stack((glass, glass*1.014, glass*1.005),axis=-1)
    relief = .23*seam+.37*heel+.023*ripple
    rough = .35+.08*(1.+ripple)/2 + .10*heel
    # Rolled rim and vertical crimps on the closures, with dull oxide in the grooves.
    crimp = .5+.5*np.cos(2*np.pi*u*(22+style*2))
    lip = ridge(v-.21,.058)+ridge(v-.84,.047)
    cap = .64+.17*crimp+.14*lip+.017*noise
    cap_colour = cap[...,None] * np.array([(1.,.98,.91),(.76,.78,.71),(.66,.36,.32),
                                            (.75,.78,.76),(.66,.49,.32)])[style]
    colour = np.where((finish==1)[...,None],cap_colour,colour)
    relief = np.where(finish==1,.15*crimp+.29*lip,relief)
    rough = np.where(finish==1,.42+.22*(1-crimp),rough)

    # Two impressions on opposite faces, printed in a single faded ink on cheap paper.
    # The wide stripe, lozenge and narrow ruled panel distinguish the stock at counter distance.
    face = (u*2+.5)%1-.5
    edge = np.abs(face)
    panel = ridge(v-.12,.012) + ridge(v-.86,.015) + .4*ridge(v-.81,.009)
    narrow = np.abs(face)*2 + np.abs(v-.53)*1.55
    diamond = ridge(narrow-.36,.028)
    stripe = ((v>.38)&(v<.49)&(edge<.32)).astype(float)
    oval = ridge(np.sqrt((face/.23)**2+((v-.53)/.22)**2)-1.,.085)
    mark = np.choose(style,[oval, stripe+.36*diamond, diamond, stripe, oval+.4*diamond])
    ghost = sum(ridge(v-h,.011) for h in (.23,.28,.66))*(edge<.18)
    ink = np.clip(np.maximum(panel,.82*mark)+.44*ghost,0,1)*np.clip(.90+.08*noise,.25,1.)
    fibre = .014*noise+.011*np.sin(x*.61+np.sin(y*.11))
    stock = np.array([(.82,.85,.79),(.86,.75,.54),(.86,.80,.69),
                      (.79,.83,.82),(.84,.72,.55)])[style]
    dye = np.array([(.34,.45,.42),(.44,.27,.15),(.42,.22,.22),
                    (.28,.39,.45),(.45,.31,.19)])[style]
    paper = stock*(1-ink[...,None])+dye*ink[...,None]+fibre[...,None]
    colour = np.where((finish==2)[...,None],paper,colour)
    relief = np.where(finish==2,fibre*.48-.025*ink,relief)
    rough = np.where(finish==2,.83+.06*noise*.18-.07*ink,rough)
    dv,du = np.gradient(relief)
    response = np.stack((.5-du*.42,.5+dv*.42,rough,.98-.04*np.abs(relief)),axis=-1)
    return {"Albedo":colour,"Response":response}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only",action="store_true")
    args=parser.parse_args()
    for kind,data in bake().items():
        path = OUTPUT/f"BarBottleFinishes{kind}.png"
        png = BytesIO()
        Image.fromarray((np.clip(data,0,1)*255+.5).astype(np.uint8)).save(png,format="PNG",compress_level=9)
        encoded=png.getvalue()
        if args.validate_only:
            if path.read_bytes()!=encoded: raise RuntimeError("Stale bar bottle finish: "+str(path))
        elif not path.exists() or path.read_bytes()!=encoded:
            path.write_bytes(encoded)

if __name__ == "__main__":
    main()
