#!/usr/bin/env python3
"""Four fixed-metre Ridge parking bays. Shared City materials are bound at composition."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import bar_parts as parts

OUT = ROOT / 'Assets/Resources/City/RidgeParking'
SOURCE = ROOT / 'ArtSource/City/RidgeParking'


def geometry():
    pieces = {'Asphalt': [], 'Curbs': [], 'Markings': [], 'Apron': []}
    def box(group, center, size, bevel):
        pieces[group].append(parts.u_box(center, size, bevel))
    box('Asphalt', (0, -.09, -10.3), (14.5, .18, 20.6), .025)
    # This apron follows the existing sidewalk, then ramps over its roadside lip.
    # Both seams share the exact height; no vertical step at the wheel contact.
    box('Apron', (0, -.09, 1), (14.5, .18, 2), .008)
    # Use a bevelled box deformed by its longitudinal grade, preserving its normals/winding.
    vertices, faces = parts.u_box((0, -.09, 2.45), (14.5, .18, .9), .006)
    rise = .012 + .06  # CityHillsideBuilding.PavementRise, plus the anti-z-fight finish.
    vertices = [(x, y - rise * max(0, min(1, (z - 2) / .9)), z) for x, y, z in vertices]
    pieces['Apron'].append((vertices, faces))
    for x in (-7.16, 7.16):
        for i in range(21):
            a, b = -20.6 + i * 20.45 / 21, -20.6 + (i + 1) * 20.45 / 21
            box('Curbs', (x, .045, (a+b)/2), (.18, .15, b-a-.008), .018)
    for i in range(15):
        a, b = -7.25+i*14.5/15, -7.25+(i+1)*14.5/15
        box('Curbs', ((a+b)/2, .045, -20.51), (b-a-.008, .15, .18), .018)
    for i in range(5):
        box('Markings', ((i-2)*2.9, .0025, -17.3), (.10, .005, 5.8), .001)
    box('Markings', (0, .0025, -20.2), (11.7, .005, .10), .001)
    return pieces


def measure(pieces):
    points = [p for solids in pieces.values() for vertices, _ in solids for p in vertices]
    return dict(bounds_min=[round(min(p[i] for p in points), 6) for i in range(3)],
                bounds_max=[round(max(p[i] for p in points), 6) for i in range(3)],
                triangles=sum(len(face)-2 for solids in pieces.values() for _, faces in solids for face in faces),
                geometry_sha256=hashlib.sha256(json.dumps(pieces, sort_keys=True).encode()).hexdigest())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model-dir', type=Path, default=OUT)
    parser.add_argument('--source-dir', type=Path, default=SOURCE)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    pieces = geometry()
    for solids in pieces.values():
        for solid in solids:
            assert parts.signed_volume(solid) > 0, 'Inside-out parking solid'
            assert parts.signed_volume(parts.to_source(solid)) > 0, 'Axis conversion reversed normals'
    record = measure(pieces)
    assert record == measure(geometry()), 'Non-deterministic geometry'
    assert record['triangles'] < 6000
    manifest = json.dumps(dict(generator='tools/build-ridge-parking-3d-model.py', unit='metre',
        width=14.5, depth=20.6, capacity=4, bay_width=2.9, bay_length=5.8, park_z=-17.3,
        ramp_front=2.9, road_drop=.072, **record), indent=2) + '\n'
    if args.validate_only:
        assert (args.model_dir/'RidgeParking.json').read_text() == manifest
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    root = bpy.data.objects.new('RidgeParking', None)
    bpy.context.collection.objects.link(root)
    for name, solids in pieces.items():
        vertices, faces = [], []
        for points, polygons in solids:
            offset = len(vertices)
            vertices.extend((x, z, y) for x, y, z in points)
            faces.extend(tuple(offset+i for i in reversed(face)) for face in polygons)
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        uv = mesh.uv_layers.new(name='UVMap')
        for poly in mesh.polygons:
            axis = max(range(3), key=lambda i: abs(poly.normal[i]))
            others = [i for i in range(3) if i != axis]
            for loop in poly.loop_indices:
                p = mesh.vertices[mesh.loops[loop].vertex_index].co
                uv.data[loop].uv = (p[others[0]]/2, p[others[1]]/2)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir/'RidgeParking.fbx'), use_selection=True,
        object_types={'MESH','EMPTY'}, axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=False, add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE')
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/'RidgeParking.blend'), compress=True)
    (args.model_dir/'RidgeParking.json').write_text(manifest)
    print('RIDGE PARKING: deterministic bevelled floor, ramp, curbs and four bays passed')


if __name__ == '__main__':
    main()
