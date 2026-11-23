#!/usr/bin/env python3
"""Metric plate girders, bearing shoes and masonry for the southern river crossing."""
import argparse
import json
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from river_bridge_mesh import Part as AtlasPart

OUT = ROOT / "Assets/Resources/City"
SPAN = 10.96
# Atlas cells: paint, flange, splice, rubbed rail, granite, damp granite, concrete, exposed bolt.
PAINT, FLANGE, SPLICE, GRIP, STONE, DAMP, CONCRETE, IRON = range(8)


class Part(AtlasPart):
    def atlas_uv(self, cell, s, t, point, axis):
        if cell in (STONE, DAMP, CONCRETE):
            s = s*.74 + .24*((self.solids*7) % 17)/16
            t = t*.79 + .19*((self.solids*3) % 11)/10
        return super().atlas_uv(cell, s, t, point, axis)


def bridge_span():
    p = Part("PlateGirders")
    # Four rolled longitudinal girders sit directly between the road slab and eight shoes.
    for y in (-3.69, -1.24, 1.24, 3.69):
        p.box((0,y,-.32), (SPAN,.36,.075), FLANGE)
        p.box((0,y,-.67), (SPAN,.075,.626), PAINT)
        p.box((0,y,-1.02), (SPAN,.43,.075), FLANGE)
        for x in (-SPAN*.30, 0, SPAN*.30):
            for face in (-1,1):
                p.box((x,y+face*.085,-.67), (.034,.101,.625), FLANGE, .004)
        for x in (-SPAN*.30, SPAN*.30):
            p.box((x,y,-1.09), (.57,.49,.065), IRON)
            p.box((x,y,-1.15), (.43,.36,.055), PAINT)
            p.box((x,y,-1.21), (.65,.55,.065), SPLICE)
        if abs(y) > 3:
            outward = 1 if y > 0 else -1
            for x in (-1.70,1.70):
                p.box((x,y+outward*.052,-.67), (.39,.03,.52), SPLICE)
                for dx in (-.124,.124):
                    for z in (-.855,-.67,-.485):
                        # Small square-headed fasteners with eased corners.
                        p.box((x+dx,y+outward*.077,z), (.046,.024,.046), IRON, .008)
    for x in (-SPAN*.42,-SPAN*.21,0,SPAN*.21,SPAN*.42):
        for lo,hi in ((-3.65,-1.28),(-1.20,1.20),(1.28,3.65)):
            y = (lo+hi)*.5
            p.box((x,y,-.51), (.074,hi-lo,.30), PAINT)
            p.box((x,y,-.675), (.17,hi-lo,.035), FLANGE, .004)
    # Visible deck edge and soffit above the top flanges. The asphalt has its own owner.
    for i in range(6):
        length = SPAN/6
        p.box((-SPAN/2+(i+.5)*length,0,-.18125), (length-.009,7.94,.2025), CONCRETE, .012)
    return p


def kit_parts():
    pier = Part("Pier")
    pier.box((0,0,.43), (.78,7.21,.86), CONCRETE, .035)
    pier.box((0,0,.93), (.96,7.96,.14), DAMP, .019)
    a, b = Part("TreadA"), Part("TreadB")
    for part, seam in ((a,-.34),(b,.40)):
        for lo, hi in ((-1.5,seam-.003),(seam+.003,1.5)):
            part.box(((lo+hi)/2,0,-.09), (hi-lo,.32,.18), STONE, .009, top=DAMP if lo > 0 else STONE)
    floor = Part("Landing")
    for row in range(6):
        cuts = (-1.5,-.72,.27,1.5) if row%2 else (-1.5,-.32,.72,1.5)
        for col,(lo,hi) in enumerate(zip(cuts,cuts[1:])):
            floor.box(((lo+hi)/2,-2.5+(row+.5)*5/6,-.12),
                      (hi-lo-.007,5/6-.009,.24), DAMP if col == 2 else STONE, .012)
    rail = Part("Rail")
    rail.box((0,0,0), (2,.115,.072), PAINT, .018, top=GRIP)
    infill = Part("Infill")
    infill.box((0,0,0), (2,.047,.054), PAINT, .006)
    post = Part("Stanchion")
    post.box((0,0,.033), (.17,.17,.066), SPLICE)
    post.box((0,0,.526), (.088,.084,.92), PAINT)
    post.box((0,0,.993), (.116,.106,.026), FLANGE, .004)
    for x in (-.059,.059):
        for y in (-.058,.058):
            post.box((x,y,.068), (.026,.026,.025), IRON, .005)
    return (bridge_span(),pier,a,b,floor,rail,infill,post)


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = bpy.data.objects.new("WorksBridge", None)
    bpy.context.collection.objects.link(root)
    parts = kit_parts()
    data = [part.emit(root) for part in parts]
    assert abs(data[0]['bounds'][0][2] + 1.2425) < .0001
    assert sum(p['triangles'] for p in data) < 20000
    return dict(span_m=SPAN, width_m=8, bearing_level_m=-1.2425, parts=data)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    data = build()
    manifest = OUT / 'WorksBridge.json'
    if args.validate_only:
        assert json.loads(manifest.read_text()) == json.loads(json.dumps(data))
    else:
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/City/Blender/WorksBridge.blend'))
        bpy.ops.export_scene.fbx(filepath=str(OUT/'WorksBridge.fbx'), object_types={'MESH','EMPTY'},
            axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL',
            mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)
        manifest.write_text(json.dumps(data, indent=2)+'\n')
    assert build() == data
    print("Verified Works bridge bearings and deterministic spans, stairs, slabs and rails.")
