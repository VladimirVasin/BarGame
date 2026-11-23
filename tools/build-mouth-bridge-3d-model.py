#!/usr/bin/env python3
"""Coursed granite, ashlar abutments and waterside stairs for the river mouth."""
import argparse
import json
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
from river_bridge_mesh import Part

OUT = ROOT / "Assets/Resources/City"
STONE, GREY, RUNOFF, WASH, CAP, TREAD, BED, REPAIR = range(8)
SPAN = 10.96


class Masonry(Part):
    runoff_x = None

    def atlas_uv(self, cell, s, t, point, axis):
        if cell == TREAD and axis == 2:
            # Across the whole flight, through the joints between individual stones.
            s = .5 + point.x / 3.02
        elif cell == RUNOFF and axis == 1 and self.runoff_x is not None:
            s = max(.015, min(.985, .5 + (point.x-self.runoff_x)*.39))
        elif cell in (STONE, GREY, BED, REPAIR):
            s = .70*s + .28*((self.solids*7) % 13)/12
            t = .78*t + .20*((self.solids*5) % 11)/10
        return super().atlas_uv(cell, s, t, point, axis)


def joints(length, pitch, phase):
    cuts = [-length/2]
    x = cuts[0] + pitch*(phase or 1)
    while x < length/2 - .35:
        cuts.append(x)
        x += pitch
    cuts.append(length/2)
    return cuts


def course(part, length, depth, bottom, height, pitch, phase, *, lateral=0, runoff=(), cap=False):
    cuts = joints(length, pitch, phase)
    for i, (lo, hi) in enumerate(zip(cuts, cuts[1:])):
        part.runoff_x = next((j for j in runoff if lo+.08 < j < hi-.08), None)
        finish = RUNOFF if part.runoff_x is not None else (GREY if i % 4 == 1 else STONE)
        part.box(((lo+hi)/2, lateral, bottom+height/2), (hi-lo-.012, depth, height),
                 finish, .016 if cap else .012, top=CAP if cap else None)
    part.runoff_x = None


def span():
    p = Masonry("StoneSpan")
    # The recessed core fills the mortar reveals and carries the soffit between the faces.
    p.box((0,0,-.505), (SPAN-.12,7.42,.85), BED, .025)
    for side in (-1, 1):
        for row in range(3):
            top = row == 2
            pitch = (1.78, 1.66, 1.84)[row]
            phase = (.45, .83, .31)[row]
            runoff = joints(SPAN, 1.84, .31)[2:-1:3] if row == 1 else ()
            course(p, SPAN, .36 if top else .32, -.94+row*.286, .285, pitch, phase,
                   lateral=side*(3.96-(.18 if top else .17)), runoff=runoff)
    return p


def parapet():
    p = Masonry("Parapet")
    length = 10.8
    p.box((0,0,.48), (length-.05,.275,.94), BED)
    for row, phase in enumerate((.42, .88, .32)):
        course(p, length, .335 if row == 0 else .31, row*.312, .311, 1.46, phase,
               runoff=joints(length, 1.68, .62)[1:-1:2] if row == 2 else ())
    course(p, length, .435, .936, .133, 1.68, .62, cap=True)
    return p


def pier_course(name, width, length, rows, finish):
    p = Masonry(name)
    for row in range(rows):
        cuts = joints(length, 1.64, .5 if row % 2 else 1)
        for i, (lo, hi) in enumerate(zip(cuts, cuts[1:])):
            y = (lo+hi)/2
            depth = hi-lo-.009
            block = finish if finish != STONE or (i+row) % 4 else GREY
            if name != "Abutment" and (i == 0 or i == len(cuts)-2):
                # One dressed cutwater stone, with a sloping cheek on each side.
                sign = -1 if i == 0 else 1
                edge = y + sign*depth*.5
                vertices, faces = kit.joint_box((0,y+sign*.18,(row+.5)/rows),
                                                (width,depth+.36,1/rows-.002), .030)
                nose = [(x*(1-.57*max(0,sign*(v-edge))/.36), v, z) for x,v,z in vertices]
                p.append((nose, faces), block)
            else:
                p.box((0,y,(row+.5)/rows), (width,depth,1/rows-.002), block, .027)
    return p


def stairs(name, cuts):
    p = Masonry(name)
    for lo, hi in zip(cuts, cuts[1:]):
        p.box(((lo+hi)/2,0,-.095), (hi-lo-.006,.42,.19), GREY, .012, top=TREAD)
    return p


def landing():
    p = Masonry("Landing")
    for i in range(5):
        cuts = joints(3, 1.27, .52 if i % 2 else .98)
        for j, (lo, hi) in enumerate(zip(cuts, cuts[1:])):
            finish = REPAIR if i == 2 and j == 1 else (GREY if (i+j)%4 == 0 else STONE)
            p.box(((lo+hi)/2,-2+i,-.12), (hi-lo-.008,.991,.24), finish, .014)
    return p


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = bpy.data.objects.new("MouthBridge", None)
    bpy.context.collection.objects.link(root)
    parts = (span(), parapet(), pier_course("PierBase", 1.65, 8.18, 2, GREY),
             pier_course("TideCourse", 1.49, 8.02, 1, WASH),
             pier_course("PierShaft", 1.13, 7.81, 2, STONE),
             pier_course("BearingCap", 1.38, 7.99, 1, GREY),
             pier_course("Abutment", 1.04, 8.04, 3, STONE),
             stairs("TreadA", (-1.5,-.47,.72,1.5)), stairs("TreadB", (-1.5,-.91,.26,1.5)),
             landing())
    records = []
    for i, part in enumerate(parts):
        records.append(part.emit(root))
        # Keep all prototypes separately editable in the source contact sheet.
        bpy.data.objects[part.name].location = ((i%3)*13, -(i//3)*10, 0)
    assert sum(r['triangles'] for r in records) < 22000
    return dict(span_m=SPAN, parts=records)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    data = build()
    manifest = OUT / 'MouthBridge.json'
    if args.validate_only:
        assert json.loads(manifest.read_text()) == json.loads(json.dumps(data))
    else:
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/City/Blender/MouthBridge.blend'))
        bpy.ops.export_scene.fbx(filepath=str(OUT/'MouthBridge.fbx'), object_types={'MESH','EMPTY'},
            axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE',
            add_leaf_bones=False, bake_anim=False)
        manifest.write_text(json.dumps(data, indent=2)+'\n')
    assert build() == data
    print("Verified river mouth masonry, staggered courses and deck kit.")
