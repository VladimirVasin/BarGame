#!/usr/bin/env python3
"""Build the City south portal in metres. Local +Y leads into the mountain."""
import argparse
import json
import math
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import interior_kit as kit
from river_bridge_mesh import Part

STONE, PALE, FOOT, LINER, MORTAR, CONCRETE, IRON, RUT = range(8)


class TunnelPart(Part):
    def atlas_uv(self, cell, s, t, point, axis):
        if cell != RUT:
            # Offset each block's cut within its finish while retaining a mip gutter.
            s = .69 * s + .28 * ((self.solids * 7) % 17) / 16
            t = .75 * t + .22 * ((self.solids * 5) % 13) / 12
        return super().atlas_uv(cell, s, t, point, axis)


def wedge(part, a, b, inner, outer, front, back, finish, bevel):
    radius = (inner + outer) * .5
    vertices, faces = kit.joint_box((0, (front + back) * .5, radius),
                                    ((b-a)*radius, back-front, outer-inner), bevel)
    points = []
    for across, depth, r in vertices:
        angle = (a+b)*.5 + across/radius
        points.append((r*math.cos(angle), depth, 1.5+r*math.sin(angle)))
    part.append((points, [tuple(reversed(face)) for face in faces]), finish)


def portal():
    p = TunnelPart('Portal')
    count = 17
    for i in range(count):
        a, b = i*math.pi/count, (i+1)*math.pi/count
        wedge(p, a, b, 4.025, 5.13, -.55, .62, MORTAR, .009)
        key = i == count//2
        wedge(p, a+.002, b-.002, 3.995, 5.22 if key else 5.15,
              -.805 if key else -.708-.008*(i%3), .64, PALE if i%4==0 else STONE, .025)
    for side in (-1, 1):
        p.box((side*4.575, .03, .75), (1.10, 1.17, 1.5), MORTAR, .014)
        for bottom, top, width, cell in [(-.10,.30,1.22,FOOT),(.313,.81,1.14,FOOT),
                                          (.826,1.32,1.14,STONE),(1.335,1.50,1.22,PALE)]:
            p.box((side*(4+width/2), -.046, (bottom+top)/2),
                  (width, 1.415, top-bottom), cell, .022)
    return p


def barrel():
    # One 3 m course module, centered longitudinally; joints alternate at half pitch.
    p = TunnelPart('Barrel')
    for row in range(3):
        y = row - 1
        angles = [0] + [math.pi*(i + (.5 if row == 1 else 0))/12 for i in
                         range(0 if row == 1 else 1, 12)] + [math.pi]
        for a,b in zip(angles, angles[1:]):
            wedge(p,a+.0017,b-.0017,3.962,4.16,y-.493,y+.493,LINER,.016)
        for side in (-1,1):
            for k in range(3):
                p.box((side*4.025,y,(k+.5)*.50),(.125,.985,.485),FOOT if k==0 else LINER,.012)
    return p


def rib():
    p = TunnelPart('Rib')
    for i in range(15):
        wedge(p, i*math.pi/15+.0015, (i+1)*math.pi/15-.0015,
              3.87,4.15,-.12,.12,PALE if i==7 else STONE,.015)
    for side in (-1,1):
        for low,high in [(0,.48),(.49,.98),(.99,1.5)]:
            p.box((side*3.96,0,(low+high)*.5),(.18,.24,high-low),FOOT if low==0 else STONE,.014)
    return p


def bedding():
    # Continuous bed seen between blocks; the same low-poly skin owns barrel collision.
    p = TunnelPart('Bedding')
    arc=[(4.025,0)] + [(4.025*math.cos(i*math.pi/24),1.5+4.025*math.sin(i*math.pi/24))
                         for i in range(25)] + [(-4.025,0)]
    vertices=[(x,y,z) for x,z in arc for y in (-1.5,1.5)]
    p.append((vertices,[(2*i,2*i+2,2*i+3,2*i+1) for i in range(len(arc)-1)]),MORTAR)
    return p


def fittings():
    cheek = TunnelPart('Cheek')
    cheek.box((0,0,-.02),(1,1,.96),CONCRETE,.024)
    cheek.box((0,0,.48),(1,1,.04),PALE,.014)
    for side in (-1,1):
        for y in (-.34,.34):
            cheek.box((side*.498,y,-.12),(.008,.023,.027),MORTAR,.004)
    post = TunnelPart('Post')
    for x in (-.41,.41):
        post.box((x,0,0),(.18,.86,.984),IRON,.018)
    post.box((0,0,0),(.70,.18,.984),IRON,.012)
    for h in (-.478,.478):
        post.box((0,0,h),(1,1,.044),IRON,.014)
    beam = TunnelPart('Beam')
    for h in (-.42,.42):
        beam.box((0,0,h),(1,1,.16),IRON,.014)
    beam.box((0,0,0),(.18,.996,.72),IRON,.012)
    housing = TunnelPart('Housing')
    housing.box((0,-.055,0),(.91,.89,.89),IRON,.055)
    housing.box((0,.355,0),(1,.18,1),IRON,.028)
    for x in (-.44,.44):
        housing.box((x,.44,0),(.09,.12,.91),IRON,.012)
    grate = TunnelPart('Grate')
    # A real slotted casting set in a shallow dark bed, with seats at both ends.
    grate.box((0,0,-.019),(1.115,.54,.027),MORTAR,.007)
    for y in (-.245,.245):
        grate.box((0,y,.009),(1.115,.058,.052),IRON,.007)
    for x in (-.526,.526):
        grate.box((x,0,.009),(.052,.448,.052),IRON,.007)
    for i in range(12):
        grate.box(((i-5.5)*.084,0,.007),(.037,.444,.047),IRON,.005)
    rut = TunnelPart('Rut')
    # Open top paint is intersected by the ground partitioner with the live slope.
    rows = []
    for i in range(9):
        y=i/8-.5
        half=.5 if i in (0,8) else .46+.035*math.sin(2.7*i)
        rows.extend([(fraction*half,y,0) for fraction in (-1,-.68,0,.68,1)])
    faces=[(5*i+j,5*i+j+1,5*i+j+6,5*i+j+5) for i in range(8) for j in range(4)]
    rut.append((rows,faces),RUT)
    return [cheek,post,beam,housing,grate,rut]


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = bpy.data.objects.new('SouthTunnel',None)
    bpy.context.collection.objects.link(root)
    for name, pos in [('AxisForward',(0,1,0)),('AxisUp',(0,0,1))]:
        obj=bpy.data.objects.new(name,None)
        bpy.context.collection.objects.link(obj)
        obj.parent=root
        obj.location=pos
    records=[]
    for i,p in enumerate([portal(),barrel(),bedding(),rib(),*fittings()]):
        records.append(p.emit(root))
        bpy.data.objects[p.name].location=(i*12,0,0)
    return records


if __name__ == '__main__':
    args=argparse.ArgumentParser(description=__doc__)
    args.add_argument('--validate-only',action='store_true')
    options=args.parse_args(sys.argv[sys.argv.index('--')+1:])
    records=build()
    manifest=json.dumps(records,indent=2)+'\n'
    out=ROOT/'Assets/Resources/City'
    assert sum(p['triangles'] for p in records)<12000
    if options.validate_only:
        assert manifest==(out/'SouthTunnel.json').read_text()
    else:
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/City/Blender/SouthTunnel.blend'))
        bpy.ops.export_scene.fbx(filepath=str(out/'SouthTunnel.fbx'),object_types={'MESH','EMPTY'},
            axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',mesh_smooth_type='FACE',
            add_leaf_bones=False,bake_anim=False,use_triangles=True)
        (out/'SouthTunnel.json').write_text(manifest)
    assert records==build()
