#!/usr/bin/env python3
"""Glazed sawtooth roof and a cutaway inspection bay for one City fabrication hall."""
import argparse
import hashlib
import json
import math
import runpy
import struct
import sys
import zlib
from collections import defaultdict
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))

from bar_parts import signed_volume, to_source, u_box, u_rotated_about
from city_building_parts import Geometry, shed_roof
from city_industrial_parts import industrial_prototype

ASSETS = ROOT / 'Assets/Resources/City/MetalWorkshop'
SOURCE = ROOT / 'ArtSource/City/MetalWorkshop'


def primary_shell(root):
    # Retain every authored entrance, vestibule and through-passage surface.
    catalog = runpy.run_path(str(ROOT / 'tools/build-city-buildings-3d-model.py'))
    spec = industrial_prototype(4, inspection_bay=True)
    part = next(p for p in spec.parts if p.role == 'FacadePrimary')
    mesh = bpy.data.meshes.new('Cut wall')
    mesh.from_pydata(part.geometry.vertices, [], part.geometry.faces)
    mesh.update()
    obj = bpy.data.objects.new('Wall', mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = root
    polygons = [tuple(p.vertices) for p in mesh.polygons]
    coords = [tuple(v.co) for v in mesh.vertices]
    geom = Geometry(tuple(coords), tuple(polygons), tuple(0 for _ in polygons))
    uv = mesh.uv_layers.new(name='UVMap')
    for loop, value in zip(uv.data, catalog['side_atlas_uv0_values'](spec, geom)):
        loop.uv = value
    tree = BVHTree.FromPolygons(coords, polygons)
    for depth in (-.8, .5, 1.8):
        hit = tree.ray_cast(Vector((-8.5, depth, 1.9)), Vector((1, 0, 0)))[0]
        assert hit is not None and abs(hit.x + 4.18) < .012, (depth, hit)
    for z in (.85, 3.0):
        assert abs(tree.ray_cast(Vector((-8.5, .5, z)), Vector((1, 0, 0)))[0].x+7.75) < .01
    assert tree.ray_cast(Vector((-2.82, .50, 4.75)), Vector((0, 0, 1)))[0] is None
    assert tree.ray_cast(Vector((0, -6.2, 2.0)), Vector((0, 1, 0)))[0] is None


def fittings():
    parts = defaultdict(list)

    def box(role, pos, size, bevel=.012, rotation=(0, 0, 0)):
        solid = u_box(pos, size, min(bevel, min(size)*.22))
        parts[role].append(u_rotated_about(solid, rotation, pos) if any(rotation) else solid)

    def rod(role, start, end, radius, sides=8):
        a, b = Vector(start), Vector(end)
        axis = (b-a).normalized()
        tangent = axis.cross(Vector((0, 0, 1)) if abs(axis.z) < .85 else Vector((1, 0, 0))).normalized()
        bitangent = axis.cross(tangent)
        pts = [tuple(v+radius*(tangent*math.cos(i*math.tau/sides)+bitangent*math.sin(i*math.tau/sides)))
               for v in (a, b) for i in range(sides)]
        faces = [tuple(reversed(range(sides))), tuple(range(sides, 2*sides))]
        faces.extend((i, (i+1) % sides, sides+(i+1) % sides, sides+i) for i in range(sides))
        parts[role].append((pts, faces))
    # The finished sheet tops follow the existing three serrations precisely.
    width = 15.5/3
    slope = .9/width
    angle = math.degrees(math.atan(slope))
    for i in range(3):
        lo = -7.75+i*width
        hi = lo+width
        box('Roof', ((lo+hi)/2, 7.30, 0), (math.hypot(width, .9)-.035, .075, 11.47), .012, (0, 0, angle))
        for z in (-5.3, -4.15, -3., -1.85, -.70, .45, 1.60, 2.75, 3.90, 5.05):
            box('Roof', ((lo+hi)/2, 7.348, z), (math.hypot(width, .9)-.08, .042, .042), .006, (0, 0, angle))
        # A stiffened triangular end cheek closes each roof bay.
        for z in (-5.71, 5.71):
            g = shed_roof(((lo+hi)/2, z), width, .065, 6.6, 7.8, True)
            parts['Roof'].append(([(x, h, d) for x, d, h in g.vertices],
                                 [tuple(reversed(f)) for f in g.faces]))
        pane_width = 11.42 / 8
        for pane in range(8):
            center = -5.71 + (pane + .5) * pane_width
            role = 'RepairGlass' if (i, pane) == (1, 5) else 'WireGlass'
            box(role, (hi-.065, 7.30, center), (.018, .79, pane_width-.025), .002)
        for y in (6.89, 7.72):
            box('Frames', (hi-.028, y, 0), (.09, .065, 11.48))
        for j in range(9):
            box('Frames', (hi-.025, 7.30, -5.71+j*11.42/8), (.10, .80, .055), .008)
        # Collect runoff at the low edge. Downpipes discharge at the solid
        # rear wall, clear of the front staff vestibule and central passage.
        for dx, h in ((0, .026), (-.095, .07), (.095, .07)):
            box('Frames', (lo+.15+dx, 6.894+h*.5, 0), (.20 if dx == 0 else .019, h, 11.48), .003)
        x = lo+.15
        rod('Frames', (x, 6.89, -5.68), (x, 6.53, -5.83), .043)
        rod('Frames', (x, 6.53, -5.83), (x, .32, -5.83), .043)
        rod('Frames', (x, .32, -5.83), (x, .20, -5.94), .043)
        for y in (1.30, 3.70, 5.90):
            box('Frames', (x, y, -5.79), (.115, .052, .082), .005)
    # A real window recess with unequal lights; the hoist is behind the broad pane.
    box('ClearGlass', (-7.615, 1.98, .50), (.012, 1.72, 3.11), .002)
    for y in (1.11, 2.85):
        box('Frames', (-7.635, y, .50), (.12, .085, 3.20))
    for z in (-1.065, .0, 2.065):
        box('Frames', (-7.635, 1.98, z), (.12, 1.71, .064))
    box('Frames', (-7.70, 1.073, .5), (.23, .04, 3.28), .006)
    box('Floor', (-5.86, .13, .50), (3.35, .055, 4.69))
    box('BackWall', (-4.208, 2.36, .5), (.055, 4.42, 4.65))
    # Truss, chain block and individual oval links. Alternating planes keep
    # neighbouring welded links visibly threaded instead of a single dark rod.
    for y in (3.0, 3.37):
        box('Steel', (-5.87, y, .98), (3.34, .07, .115))
    for j in range(6):
        x = -7.53+j*.55
        rod('Steel', (x, 3.0 if j % 2 == 0 else 3.37, .98), (x+.55, 3.37 if j % 2 == 0 else 3.0, .98), .034)
    box('Hoist', (-5.98, 2.89, .98), (.22, .28, .21), .034)

    def chain(x, z, top, count):
        for j in range(count):
            y = top-j*.059
            pts = []
            for k in range(8):
                a = math.tau*k/8
                short = .026*math.cos(a)
                pts.append((x+short*(.8 if j % 2 else .4), y+.045 *
                           math.sin(a), z+short*(.6 if j % 2 else -.92)))
            for k in range(8):
                rod('Chain', pts[k-1], pts[k], .006, 5)
    chain(-5.98, .98, 2.74, 15)
    chain(-5.90, .89, 2.74, 15)
    chain(-5.83, .87, 2.74, 15)
    rod('Chain', (-5.90, 1.91, .89), (-5.83, 1.91, .87), .012)
    hook = [(-5.98, 1.88, .98), (-5.98, 1.81, .98), (-5.99, 1.76, 1.01),
            (-5.99, 1.74, 1.08), (-5.98, 1.78, 1.13), (-5.97, 1.84, 1.12)]
    for a, b in zip(hook, hook[1:]):
        rod('Hoist', a, b, .019)
    # Timber chock supports the same length of channel held in the vice.
    for x in (-6.48, -5.72):
        for z in (-.69, 1.78):
            box('Steel', (x, .57, z), (.07, .83, .07))
        box('Steel', (x, .46, .54), (.06, .065, 2.49))
    box('Timber', (-6.10, 1.03, .55), (.92, .12, 2.65))
    box('Hoist', (-6.10, 1.16, -.45), (.26, .14, .31))
    for x in (-6.21, -6.055):
        box('Hoist', (x, 1.26, -.45), (.085, .15, .22))
    rod('Chain', (-6.38, 1.18, -.45), (-6.08, 1.18, -.45), .028)
    rod('Chain', (-6.37, 1.10, -.45), (-6.37, 1.27, -.45), .012)
    box('Timber', (-6.13, 1.15, 1.34), (.33, .12, .18))
    box('Stock', (-6.132, 1.221, .45), (.073, .028, 2.15), .004)
    box('Stock', (-6.16, 1.26, .45), (.018, .071, 2.15), .004)
    return parts


def write_parts(parts, root):
    pitches = {'Roof': 4.0, 'ClearGlass': 1., 'WireGlass': .72, 'RepairGlass': .72,
               'Timber': 1.6, 'Frames': .8, 'Floor': 2., 'BackWall': 2.}
    for role, solids in parts.items():
        verts, faces = [], []
        for solid in solids:
            assert signed_volume(solid) > 0, role
            points, polygons = to_source(solid)
            off = len(verts)
            verts.extend(points)
            faces.extend(tuple(off+i for i in f) for f in polygons)
        mesh = bpy.data.meshes.new(role)
        mesh.from_pydata(verts, [], faces)
        mesh.update()
        uv = mesh.uv_layers.new(name='UVMap')
        pitch = pitches.get(role, 1.8)
        for p in mesh.polygons:
            major = max(range(3), key=lambda axis: abs(p.normal[axis]))
            a, b = [i for i in range(3) if i != major]
            for loop in p.loop_indices:
                v = mesh.vertices[mesh.loops[loop].vertex_index].co
                uv.data[loop].uv = (v[a]/pitch, v[b]/pitch)
        obj = bpy.data.objects.new(role, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root


def wire_glass():
    side = 128
    pixels = bytearray()
    for y in range(side):
        pixels.append(0)
        for x in range(side):
            wire = min(min(k % 12, 12-k % 12) for k in ((x+y)*12/16, (x-y)*12/16))
            # A .72 m tile contains eight diamonds across; filtered mips average the wire.
            strength = max(0, 1-wire/.9)
            grit = .5+.5*math.sin(x*math.tau/side*2)*math.cos(y*math.tau/side*3)
            v = round(215+15*grit-91*strength)
            pixels.extend((v, min(v+4, 255), v-2, round(164+77*strength)))

    def chunk(name, data): return struct.pack('>I', len(data)) + \
        name+data+struct.pack('>I', zlib.crc32(name+data))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('>IIBBBBB', side, side, 8, 6, 0, 0, 0))+chunk(b'IDAT', zlib.compress(bytes(pixels), 9))+chunk(b'IEND', b'')


def main():
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument('--validate-only', action='store_true')
    validate = args.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []).validate_only
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = bpy.data.objects.new('MetalWorkshop', None)
    bpy.context.collection.objects.link(root)
    primary_shell(root)
    parts = fittings()
    write_parts(parts, root)
    meshes = sorted(((o.name, tuple(tuple(round(v, 6) for v in vertex.co) for vertex in o.data.vertices),
                    tuple(tuple(p.vertices) for p in o.data.polygons)) for o in root.children), key=lambda v: v[0])
    manifest = dict(unit='metre', base='industrial-prototype-04', window_center=[-7.75, 1.98, .50],
                    window_clear=[3.2, 1.80], room_depth=3.37, roof_teeth=3,
                    mesh_sha256=hashlib.sha256(json.dumps(meshes, separators=(',', ':')).encode()).hexdigest(),
                    wire_sha256=hashlib.sha256(wire_glass()).hexdigest())
    data = json.dumps(manifest, indent=2)+'\n'
    if validate:
        assert (ASSETS/'MetalWorkshop.json').read_text() == data
        assert (ASSETS/'WiredGlass.png').read_bytes() == wire_glass()
        return
    SOURCE.mkdir(parents=True, exist_ok=True)
    ASSETS.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=str(ASSETS/'MetalWorkshop.fbx'), use_selection=True, object_types={'EMPTY', 'MESH'},
                             axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', bake_space_transform=False,
                             add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE')
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'MetalWorkshop.blend'), compress=True)
    (ASSETS/'MetalWorkshop.json').write_text(data)
    (ASSETS/'WiredGlass.png').write_bytes(wire_glass())


if __name__ == '__main__':
    main()
