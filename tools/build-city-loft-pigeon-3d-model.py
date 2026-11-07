#!/usr/bin/env python3
"""One articulated rock pigeon with six fixed plumages in a shared palette strip.

Unity metres: toes on Y=0, bill towards +Z. The spread wing is the export/rest
pose. Runtime folds the entire layered wing against the flank at its shoulder.
"""
import argparse
import importlib.util
import json
import math
import random
import wave
from pathlib import Path
import struct
import sys
import zlib

sys.dont_write_bytecode = True
import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('pigeon_boat_geometry', ROOT / 'tools/build-city-port-3d-model.py')
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)
base = port.base
MODEL_DIR = ROOT / 'Assets/Resources/City/LoftPigeons'
NAME = 'LoftPigeon'
# Breast, mantle, coverts, flights, wing bars, rump, neck green/violet, tail,
# toes, claws, ivory cere, beak, iris, pupil, localized cheek/wing feather patch.
SWATCHES = ('858f98', '697784', '939da5', '4b545e', '383d43', 'bcc0bc', '495d59', '5c5361',
            '666d73', 'ad746f', '5e5551', 'd1cabb', '525052', 'ab8552', '28292b', '7f8892')
COLORS = [tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)) + (1,) for h in SWATCHES]


def blob(g, center, radii, pigment, pitch=0, sides=12, rings=7):
    vertices, faces = [], []
    angle = math.radians(pitch)
    for row in range(rings + 1):
        lat = math.pi * (row + .04) / (rings + .08)
        for k in range(sides):
            theta = k * math.tau / sides
            x = radii[0] * math.sin(lat) * math.cos(theta)
            y = radii[1] * math.cos(lat)
            z = radii[2] * math.sin(lat) * math.sin(theta)
            vertices.append((center[0] + x, center[1] + y * math.cos(angle) - z * math.sin(angle),
                             center[2] + y * math.sin(angle) + z * math.cos(angle)))
    faces.extend((tuple(range(sides)), tuple(range(rings * sides, (rings + 1) * sides))))
    for r in range(rings):
        for k in range(sides):
            a, b = r * sides + k, r * sides + (k + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    g.add(vertices, faces, COLORS[pigment])


def quill(g, start, end, half_width, pigment, camber=.003):
    """Closed tapered feather with broad tip notch and a narrow raised shaft."""
    a, b = Vector(start), Vector(end)
    side = (b - a).cross(Vector((0, 1, 0))).normalized()
    normal = Vector((0, 1, 0))
    center = a.lerp(b, .56)
    ring = [a, a.lerp(b, .34) + side * half_width, a.lerp(b, .84) + side * half_width * .64,
            b, a.lerp(b, .79) - side * half_width * .53, a.lerp(b, .32) - side * half_width * .8]
    verts = [tuple(p) for p in ring + [center + normal * camber, center - normal * camber]]
    faces = [(6, i, (i + 1) % 6) for i in range(6)] + [(7, (i + 1) % 6, i) for i in range(6)]
    g.add(verts, faces, COLORS[pigment])


def joint(name, center, geo, parent, mat):
    obj = geo.object(name, None, mat)
    origin = Vector(base.source(center))
    obj.data.transform(Matrix.Translation(-origin))
    obj.parent = parent
    obj.matrix_world = Matrix.Translation(origin)
    for poly, color in zip(obj.data.polygons, geo.colors):
        for loop in poly.loop_indices:
            obj.data.uv_layers.active.data[loop].uv = ((COLORS.index(color) + .5) / len(COLORS), .5 / 8)
    bpy.context.view_layer.update()
    return obj


def build():
    base.reset()
    root = base.empty(NAME)
    mat = base.material('Domestic pigeon pigment')
    g = base.Geometry()
    blob(g, (0, .113, -.008), (.058, .072, .105), 0, -22)
    blob(g, (0, .15, -.030), (.052, .039, .077), 1, -16)
    blob(g, (0, .127, -.096), (.041, .023, .041), 5)
    body = joint('Breast', (0, .108, 0), g, root, mat)
    g = base.Geometry()
    blob(g, (0, .177, .048), (.041, .055, .039), 6, -19)
    blob(g, (0, .18, .065), (.035, .037, .033), 7, -8)
    blob(g, (0, .214, .064), (.037, .035, .042), 1)
    for s in (-1, 1):
        blob(g, (.0305*s, .209, .073), (.008, .011, .018), 15 if s == 1 else 1, -17)
        blob(g, (.032*s, .222, .080), (.0051, .006, .006), 13, sides=10, rings=5)
        blob(g, (.036*s, .222, .081), (.0025, .0034, .0033), 14, sides=8, rings=4)
    g.rod((0, .212, .095), (0, .207, .128), .010, COLORS[12], 8, .001)
    for s in (-1, 1):
        blob(g, (s * .0044, .219, .103), (.006, .005, .009), 11, 11, 8, 4)
    joint('NeckHead', (0, .151, .036), g, body, mat)
    for s, side in ((-1, 'Left'), (1, 'Right')):
        g = base.Geometry()
        # Long primaries project from a tapering hand. Short secondary tips
        # round the inner trailing edge; two dark crossbars traverse the coverts.
        for i in range(10):
            t = i / 9
            start = (s*(.089 + .043*t), .155 - .001*i, .027 - .061*t)
            end = (s*(.304 - .132*t - .022*t*t), .147 - .001*i, -.043 - .071*t)
            quill(g, start, end, .011, 3 if i % 3 != 1 else 1)
        for i in range(7):
            t = i / 6
            quill(g, (s*(.064+.088*t), .158+.002*t, .031-.008*t),
                  (s*(.101+.131*t), .152+.002*t, -.095+.039*t), .020, 2)
        blob(g, (s*.113, .159, -.004), (.086, .013, .039), 2)
        for i in range(2):
            quill(g, (s*.071, .1715, -.004-i*.018), (s*(.177-i*.008), .166, -.034-i*.014), .007, 4)
        quill(g, (s*.08, .1715, .005), (s*.167, .169, -.008), .012, 15 if s == 1 else 2)
        joint(side+'Wing', (s*.039, .159, .026), g, body, mat)
        g = base.Geometry()
        hip = (s*.021, .084, .006)
        ankle = (s*.020, .039, -.008)
        ball = (s*.023, .005, .006)
        g.rod(hip, ankle, .0075, COLORS[0], 7, .005)
        g.rod(ankle, ball, .0037, COLORS[9], 7, .0028)
        for dx, dz in ((-.015, .018), (.000, .028), (.014, .019), (0, -.018)):
            bend = (ball[0]+dx*.7, .003, ball[2]+dz*.72)
            tip = (ball[0]+dx, .001, ball[2]+dz)
            g.rod(ball, bend, .0026, COLORS[9], 6, .0019)
            g.rod(bend, tip, .0019, COLORS[10], 6, .0006)
        joint(side+'Foot', hip, g, root, mat)
        g = base.Geometry()
        for i in range(5):
            t = (i+.5)/5
            quill(g, (s*(.002+.016*t), .125+.004*t, -.084),
                  (s*(.003+.039*t), .097+.006*t, -.185+.012*t), .012, 8 if i%2 else 3)
        joint(side+'Tail', (s*.006, .125, -.083), g, body, mat)
    summary = port.describe(root)
    assert len([o for o in root.children_recursive if o.type == 'MESH']) == 8
    assert .59 < summary['bounds_max'][0] - summary['bounds_min'][0] < .64
    assert -.002 < summary['bounds_min'][1] < .002 and .24 < summary['bounds_max'][1] < .27
    assert .30 < summary['bounds_max'][2]-summary['bounds_min'][2] < .34
    assert summary['triangles'] < 3900
    return root, {'design_id': 'domestic_rock_pigeon', 'coordinates': 'metres, spread wings, +Z bill, Y0 toes',
                  'plumages': ['blue bar', 'pale bar', 'warm grey', 'slate grey', 'sooty', 'pied cheek and covert'],
                  'model': summary}


def palette(path):
    # One row per stable bird. PNG scanlines run top-down, Unity UV rows bottom-up.
    rows = []
    for i in reversed(range(8)):
        row = bytearray([0])
        for k, swatch in enumerate(COLORS):
            factor = (1, 1.09, .93, .87, .55, 1.03, 1, 1)[i] if k < 9 or k == 15 else 1
            rgb = [min(255, round(255*c*factor)) for c in swatch[:3]]
            if i == 2 and k < 9:
                rgb[0] += 6
            if i == 5 and k == 15:
                rgb = [220, 218, 202]
            row.extend(rgb)
        rows.append(row)
    def chunk(tag, data):
        return struct.pack('!I',len(data))+tag+data+struct.pack('!I',zlib.crc32(tag+data))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',16,8,8,2,0,0,0))+
                     chunk(b'IDAT',zlib.compress(b''.join(rows),9))+chunk(b'IEND',b''))


def sounds(directory):
    rate = 22050
    rng = random.Random(48129)
    for name, duration in (('PigeonCoo', 1.36), ('PigeonWing', .57)):
        samples = []
        phase = previous = smooth = 0.
        for i in range(round(rate * duration)):
            t = i / rate
            noise = rng.uniform(-1., 1.)
            if name == 'PigeonCoo':
                # The low hollow two-note coo ends on a shorter back syllable.
                amp = 0.
                frequency = 395.
                for onset, length, pitch, gain in ((.02,.30,410.,.68),(.35,.57,362.,1.),(.97,.30,326.,.65)):
                    u = (t - onset) / length
                    if 0 < u < 1:
                        amp = gain * math.sin(math.pi * u) ** 1.45
                        frequency = pitch * (1.035 - .11 * u + .008 * math.sin(25.0*t))
                        break
                phase += math.tau * frequency / rate
                value = amp * (.71 * math.sin(phase) + .16 * math.sin(2*phase+.3) + .06 * math.sin(3*phase))
            else:
                smooth += .24 * (noise - smooth)
                band = smooth - previous
                previous += .045 * (smooth - previous)
                amp = 0.
                for onset in (.015,.151,.279,.405):
                    elapsed = t - onset
                    if 0 < elapsed < .13:
                        amp += (1-math.exp(-elapsed*120))*math.exp(-elapsed*32)
                value = amp * (1.6*band+.06*math.sin(2*math.pi*(145+18*math.sin(30*t))*t))
            samples.append(max(-32767,min(32767,round(24500*value))))
        with wave.open(str(directory/(name+'.wav')), 'wb') as stream:
            stream.setparams((1,2,rate,len(samples),'NONE','not compressed'))
            stream.writeframes(struct.pack('<%dh'%len(samples), *samples))

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model-dir', type=Path, default=MODEL_DIR)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    root, report = build()
    manifest = args.model_dir/(NAME+'.json')
    if args.validate_only:
        assert json.loads(manifest.read_text()) == report
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        base.export(root, args.model_dir/(NAME+'.fbx'))
        manifest.write_text(json.dumps(report, indent=2)+'\n')
        palette(args.model_dir/'PigeonPlumages.png')
        sounds(args.model_dir)
    assert build()[1] == report, 'Pigeon geometry changed on rebuild'
    print('LOFT PIGEON art contract OK', report['model']['triangles'], 'triangles')


if __name__ == '__main__':
    main()
