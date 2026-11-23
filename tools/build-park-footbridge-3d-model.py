#!/usr/bin/env python3
"""Authored park crossing in metres. Blender X spans the river and Z is up."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit

SPAN, WIDTH = 10.96, 2.8
BAYS = 6
OUT = ROOT / "Assets/Resources/City"
SOURCE = ROOT / "ArtSource/City/Blender/ParkFootbridge.blend"


class Joinery:
    """One draw mesh, retaining per-face grain, cut ends and independent solid edges."""
    def __init__(self, name):
        self.name = name
        self.vertices, self.faces, self.uv = [], [], []
        self.pieces = 0

    def member(self, a, b, breadth, height, bevel, finish, *, top=None):
        a, b = Vector(a), Vector(b)
        axis = (b-a).normalized()
        up = Vector((0, 0, 1)) if abs(axis.z) < .99 else Vector((0, 1, 0))
        cross = up.cross(axis).normalized()
        up = axis.cross(cross)
        length = (b-a).length
        centre = (a+b)*.5
        sizes = (length, breadth, height)
        vertices, faces = kit.joint_box((0, 0, 0), sizes, bevel)
        offset = len(self.vertices)
        self.vertices.extend(tuple(centre + axis*x + cross*y + up*z) for x,y,z in vertices)
        for face in faces:
            pts = [Vector(vertices[i]) for i in face]
            normal = (pts[1]-pts[0]).cross(pts[2]-pts[0]).normalized()
            side = max(range(3), key=lambda i: abs(normal[i]))
            cut = side == 0 and finish < 6
            row = 5 if cut else (top if top is not None and side == 2 and normal.z > .6 else finish)
            along, across = ((1,2) if side == 0 else (0,2) if side == 1 else (0,1))
            for pt in pts:
                u, v = pt[along]/sizes[along]+.5, pt[across]/sizes[across]+.5
                if cut:
                    # End rings are circular in metres, including thin plank ends.
                    u = .17 + .62*u
                    v = .18 + .60*v
                elif row in (0,1,2,7):
                    window = .82 + .15*((self.pieces*5)%11)/10
                    u = u*window + (1-window)*((self.pieces*11)%19)/18
                    if self.pieces % 2: u = 1-u
                    v = .52*v + (self.pieces*13 % 19)/43
                elif row != 6:
                    sample = min(.94, length/2.95)
                    u = sample*u + (1-sample)*((self.pieces*7 % 17)/16)
                self.uv.append((.006 + .988*u, 1-(row + .055 + .89*v)/8))
            self.faces.append(tuple(offset+i for i in face))
        self.pieces += 1

    def emit(self, root):
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        uv = mesh.uv_layers.new(name="Finish")
        assert len(uv.data) == len(self.uv)
        for loop, coord in zip(uv.data, self.uv):
            loop.uv = coord
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
        mesh.calc_loop_triangles()
        fingerprint = json.dumps((self.vertices, self.faces, self.uv), separators=(',', ':'))
        return dict(name=self.name, solids=self.pieces, triangles=len(mesh.loop_triangles),
                    sha256=hashlib.sha256(fingerprint.encode()).hexdigest())


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = bpy.data.objects.new("ParkFootbridge", None)
    bpy.context.collection.objects.link(root)
    deck = Joinery("Timber Deck Planks")
    frame = Joinery("Timber Bridge Structure")
    masonry = Joinery("Stone Bearings")
    rng = random.Random(771)
    start, end = -SPAN/2+.30, SPAN/2-.30
    widths = [rng.uniform(.186, .253) for _ in range(47)]
    scale = (end-start)/sum(widths)
    x = start
    for i, width in enumerate(widths):
        pitch = width*scale
        mid = x+pitch/2
        deck.member((mid,-1.43,-.025), (mid,1.43,-.025), pitch-.006, .110, .0045, 3, top=(0,1,2,7)[i%4])
        x += pitch
    assert abs(x-end) < 1e-6
    # Each sill carries the rail feet and the ends of the long stringers.
    for sign in (-1,1):
        edge = sign*(SPAN/2-.15)
        for k in range(3):
            lo = -1.43+k*2.86/3+.0025
            hi = -1.43+(k+1)*2.86/3-.0025
            masonry.member((edge,lo,-.136),(edge,hi,-.136),.294,.32,.010,6)
        masonry.member((sign*(SPAN/2-.48),-1.55,-.566),
                       (sign*(SPAN/2-.48),1.55,-.566),.91,.205,.018,6)
    for y in (-1.17,0,1.17):
        frame.member((-SPAN/2+.26,y,-.275),(SPAN/2-.26,y,-.275),.22,.390,.012,3)
    posts = [-SPAN/2+.07 + i*(SPAN-.14)/BAYS for i in range(BAYS+1)]
    for x in posts[1:-1]:
        frame.member((x,-1.40,-.560),(x,1.40,-.560),.195,.18,.012,3)
    for side in (-1,1):
        y = side*1.30
        for x in posts:
            frame.member((x,y,-.073),(x,y,.996),.139,.139,.006,3)
        for i, (a,b) in enumerate(zip(posts, posts[1:])):
            # Rails meet above a tenoned post, leaving a visible butt joint.
            frame.member((a+(.002 if i else -.061),y,1.052),
                         (b-(.002 if i < BAYS-1 else -.061),y,1.052),.15,.116,.016,3,top=4)
            frame.member((a+.069,y,.532),(b-.069,y,.532),.082,.115,.007,3)
            if i in (0,BAYS-1):
                foot, shoulder = (a+.08,b-.08) if i == 0 else (b-.08,a+.08)
                frame.member((foot,y,-.025),(shoulder,y,.475),.071,.107,.007,3)
        # Paired knee braces pick up the outside beams over the masonry seats.
        for sign in (-1,1):
            frame.member((sign*(SPAN/2-.45),side*1.17,-.91),
                         (sign*(SPAN/2-1.64),side*1.17,-.44),.174,.19,.011,3)
            masonry.member((sign*(SPAN/2-.35),side*1.17,-.91),
                           (sign*(SPAN/2-.62),side*1.17,-.91),.39,.28,.016,6)
    meshes = [part.emit(root) for part in (deck,frame,masonry)]
    low, high = kit.bounds((deck.vertices, deck.faces))
    assert deck.pieces == len(widths) and abs(high[2]-.03) < 1e-6 and abs(low[1]+1.43) < 1e-6
    assert max(item["triangles"] for item in meshes) < 12000
    return dict(span_m=SPAN, deck_width_m=WIDTH, deck_top_m=.03, railing_bays=BAYS, meshes=meshes)


def main():
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument('--validate-only', action='store_true')
    options = args.parse_args(sys.argv[sys.argv.index('--')+1:])
    data = build()
    manifest = OUT / 'ParkFootbridge.json'
    if options.validate_only:
        assert json.loads(manifest.read_text()) == data, "Footbridge manifest is stale"
    else:
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
        bpy.ops.export_scene.fbx(filepath=str(OUT/'ParkFootbridge.fbx'), object_types={'MESH','EMPTY'},
            axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=False)
        manifest.write_text(json.dumps(data, indent=2)+'\n')
    assert build() == data, "Footbridge generation changed between passes"
    print("Park footbridge:", data, flush=True)


if __name__ == '__main__':
    main()
