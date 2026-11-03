"""Physical metre-scale slopes, roughness and cavity for the six shop pigments.

Independent geometry fields; colour brightness is never converted to relief.
Maps repeat with their existing pigment pitch and retain the old pigment mean.
"""
from __future__ import annotations
import hashlib
import json
import re
from pathlib import Path
import uuid
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'Assets/Resources/Supermarket/Textures'
SPECS = {'Linoleum': (2.4, .82, .0004), 'WallPaint': (2.6, .94, .0006),
         'Ceiling': (3., .96, .0003), 'ShelfMetal': (1.3, .66, .00018),
         'Counter': (1.2, .67, .00025), 'Cardboard': (.9, .97, .00035)}
SIZE = 512


def build(kind, pitch, roughness, height):
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float64) / SIZE
    wave = np.sin(2*np.pi*(x*7+y*3))*.45 + np.sin(2*np.pi*(x*23-y*11))*.22
    relief = wave*height
    cavity = np.ones_like(x)
    if kind in ('Linoleum', 'Ceiling'):
        edge = np.minimum(np.mod(x*4, 1), 1-np.mod(x*4, 1))
        edge = np.minimum(edge, np.minimum(np.mod(y*4, 1), 1-np.mod(y*4, 1)))
        joint = np.exp(-(edge/.016)**2)
        relief -= joint*(.0008 if kind == 'Linoleum' else .0012)
        cavity -= joint*.16
    elif kind == 'Cardboard':
        relief += np.sin(x*np.pi*2*40)*.00016
    elif kind == 'ShelfMetal':
        relief += np.sin(y*np.pi*2*71)*.00006
    slope_x = (np.roll(relief,-1,1)-np.roll(relief,1,1))/(2*pitch/SIZE)
    slope_y = (np.roll(relief,-1,0)-np.roll(relief,1,0))/(2*pitch/SIZE)
    rgba = np.stack((.5+slope_x*.5, .5+slope_y*.5,
                     roughness+wave*.07, cavity), axis=2)
    return Image.fromarray(np.rint(np.clip(rgba,0,1)*255).astype(np.uint8),'RGBA')


def publish(verify=False):
    rows=[]
    for kind, (pitch, roughness, height) in SPECS.items():
        path=OUTPUT/f'Supermarket{kind}Response.png'
        image=build(kind,pitch,roughness,height)
        if verify:
            with Image.open(path) as current:
                if current.tobytes()!=image.tobytes(): raise RuntimeError('Stale response '+kind)
        else:
            image.save(path,compress_level=9)
            meta=Path(str(path)+'.meta')
            if not meta.exists():
                # A bare GUID meta is interpreted as an obsolete importer by Unity.
                # Start from the valid sibling Texture2D template; the bounded
                # importer owns linear/alpha/BC7 settings on first import.
                template=(OUTPUT/f'Supermarket{kind}Albedo.png.meta').read_text()
                template=re.sub(r'(?m)^guid: [0-9a-f]+$',
                    'guid: '+uuid.uuid5(uuid.NAMESPACE_URL,str(path.relative_to(ROOT))).hex, template)
                meta.write_text(template.replace('sRGBTexture: 1','sRGBTexture: 0'))
        rows.append({'kind':kind,'metres_per_tile':pitch,'size':SIZE,
                     'mean_roughness':roughness,'sha256':hashlib.sha256(image.tobytes()).hexdigest()})
    manifest=ROOT/'ArtSource/Supermarket/supermarket-response.json'
    payload={'design_id':'supermarket_response_v1','channels':['slope_u','slope_v','roughness','cavity'],
             'resident_bc7_mip_bytes':len(rows)*SIZE*SIZE*4//3,'sheets':rows}
    if verify:
        if json.loads(manifest.read_text())!=payload: raise RuntimeError('Stale supermarket response contract')
    else: manifest.write_text(json.dumps(payload,indent=2)+'\n')
    print('Supermarket material response validated' if verify else 'Supermarket material response published')

if __name__=='__main__':
    import sys
    publish('--validate-only' in sys.argv)
