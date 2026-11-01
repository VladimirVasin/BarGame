"""Finite thick glass fragments from the exact three retained bottle surfaces.

Run through tools/run-blender.py. Exterior triangles and UVs come only from the
published editable GEO_Torso glass submesh; closed inner/cut faces are authored
here. The imported Variant anchors are canonicalized to the real chest bind
frame by CombatZombieBottleFractureAssetSetup, never guessed at runtime.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
OUT = ROOT / 'Assets/Resources/Combat'
SOURCE = ROOT / 'ArtSource/CombatZombie'
NAME = 'ZombieBottleFracture'
CELL_COUNT = 35
THICKNESS = .006
CUT_UV = (.48, .52)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def plain(v):
    return [round(float(x), 8) for x in v]


def unity(v):
    return [round(float(v[i]), 8) for i in (0, 2, 1)]


def key(v):
    return tuple(round(float(c), 8) for c in v)


def mid(a, b):
    return ((a[0] + b[0]) * .5, (a[1] + b[1]) * .5,
            (a[2] + b[2]).normalized())


def subdivide(triangle, depth=0):
    # Uniform edge midpoints keep neighbouring source triangles conforming.
    # Independently adaptive splits would introduce T-junctions in cut walls.
    a, b, c = triangle
    ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
    return [(a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca)]


def extract(variant):
    path = SOURCE / f'ZombieDecayBody{variant}.blend'
    bpy.ops.wm.open_mainfile(filepath=str(path))
    torso, rig = bpy.data.objects['GEO_Torso'], bpy.data.objects['RIG_Player']
    matrix = torso.matrix_world
    normal_matrix = matrix.to_3x3().inverted().transposed()
    mesh = torso.data
    mesh.calc_loop_triangles()
    source_faces = [p for p in mesh.polygons if p.material_index == 1]
    source_indices = {i for p in source_faces for i in p.vertices}
    # The rigid-material contract makes every fragment exactly follow one bone.
    chest_index = torso.vertex_groups['chest'].index
    for index in source_indices:
        groups = [(g.group, g.weight) for g in mesh.vertices[index].groups if g.weight > .00001]
        if len(groups) != 1 or groups[0][0] != chest_index or abs(groups[0][1] - 1.) > .00001:
            raise ValueError('A bottle vertex is not rigidly chest-bound')
    rows = []
    signature = hashlib.sha256()
    surface_area = 0.
    for tri in mesh.loop_triangles:
        if mesh.polygons[tri.polygon_index].material_index != 1:
            continue
        vertices = []
        for vi, li in zip(tri.vertices, tri.loops):
            p = matrix @ mesh.vertices[vi].co
            uv = mesh.uv_layers[0].data[li].uv.copy()
            normal = (normal_matrix @ mesh.vertices[vi].normal).normalized()
            vertices.append((p, uv, normal))
            signature.update(json.dumps((plain(p), plain(uv)), separators=(',', ':')).encode())
        surface_area += (vertices[1][0]-vertices[0][0]).cross(vertices[2][0]-vertices[0][0]).length * .5
        rows.extend(subdivide(tuple(vertices)))
    points = [v[0] for t in rows for v in t]
    zmin, zmax = min(v.z for v in points), max(v.z for v in points)
    # Circumferential bands are lightly staggered. Every outer triangle remains
    # on the exact source plane, including its original UV interpolation.
    ordered = sorted(source_indices)
    rings = [sum((matrix @ mesh.vertices[i].co for i in ordered[j:j+28]), Vector()) / 28
             for j in range(0, len(ordered), 28)]
    def centre_y(z):
        closest = min(rings, key=lambda point: abs(point.z-z))
        return closest.y
    cells = [[] for _ in range(CELL_COUNT)]
    for tri in rows:
        center = sum((v[0] for v in tri), Vector()) / 3
        uv_values = [v[1].x for v in tri]
        if max(uv_values)-min(uv_values) > .000001:
            angle = ((sum(uv_values)/3 - .02)/.44) * math.tau - math.pi
        else:
            angle = math.atan2(center.y - centre_y(center.z), center.x)
        height = (center.z - zmin) / (zmax - zmin)
        ring = max(0, min(4, int(height * 5 + .10 * math.sin(angle * 3 + variant * .31))))
        turn = (angle / math.tau + .5 + .035 * math.sin(height * 9. + ring * 1.7)) % 1.
        sector = min(6, int(turn * 7))
        cells[ring * 7 + sector].append(tri)
    if any(not cell for cell in cells):
        raise ValueError('A finite bottle fracture cell is empty')
    chest = rig.matrix_world @ rig.data.bones['chest'].matrix_local
    return dict(variant=variant, cells=cells, chest=chest,
                source_surface_sha256=signature.hexdigest(), source_area=surface_area,
                bounds_min=[min(v[i] for v in points) for i in range(3)],
                bounds_max=[max(v[i] for v in points) for i in range(3)],
                source_body_sha256=digest((OUT / f'ZombieDecayBody{variant}.fbx').read_bytes()),
                source_editable_sha256=digest(path.read_bytes()))


def closed_cell(triangles):
    # A shard boundary may touch itself at one source vertex. Split its
    # disconnected triangle fans there, so thickening cannot create a
    # nonmanifold four-way wall edge. Loop UVs remain independent at seams.
    owners = {}; positions = {}
    for ti, triangle in enumerate(triangles):
        keys = [key(v[0]) for v in triangle]
        for k in keys: positions.setdefault(k, []).append(ti)
        for a, b in zip(keys, keys[1:] + keys[:1]):
            owners.setdefault(tuple(sorted((a, b))), []).append(ti)
    fans = {}
    for k, incident in positions.items():
        parent = {i:i for i in incident}
        def find(i):
            while parent[i] != i:
                parent[i] = parent[parent[i]]; i = parent[i]
            return i
        for edge, shared in owners.items():
            if k not in edge or len(shared) != 2: continue
            a, b = shared
            if a in parent and b in parent: parent[find(b)] = find(a)
        for i in incident: fans[(k, i)] = find(i)
    outer = []; normals = []; faces = []; indices = {}
    for ti, triangle in enumerate(triangles):
        face = []
        for p, uv, normal in triangle:
            k = (key(p), fans[(key(p), ti)])
            if k not in indices:
                indices[k] = len(outer); outer.append(p.copy()); normals.append(normal.copy())
            face.append(indices[k])
        faces.append((tuple(face), tuple(v[1] for v in triangle)))
    n = len(outer)
    vertices = outer + [p - normal * THICKNESS for p, normal in zip(outer, normals)]
    result = [(face, coords, True) for face, coords in faces]
    result += [(tuple(i+n for i in reversed(face)), tuple(reversed(coords)), False) for face, coords in faces]
    edges = {}
    for face, _ in faces:
        for a, b in zip(face, face[1:] + face[:1]):
            k = tuple(sorted((a, b)))
            if k in edges: edges[k] = None
            else: edges[k] = (a, b)
    for edge in edges.values():
        if edge is None: continue
        a, b = edge
        result.append(((b, a, a+n, b+n), (Vector(CUT_UV),)*4, False))
    counts = {}
    for face, _, _ in result:
        for a, b in zip(face, face[1:] + face[:1]):
            k = tuple(sorted((a, b))); counts[k] = counts.get(k, 0) + 1
    if any(value != 2 for value in counts.values()):
        raise ValueError('Fracture cell is not a closed two-manifold shell')
    volume = sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]])) / 6
                 for f, _, _ in result for i in range(1, len(f)-1))
    if volume < 0:
        result = [(tuple(reversed(f)), tuple(reversed(t)), smooth) for f, t, smooth in result]
        volume = -volume
    if volume <= 1e-8: raise ValueError('Fracture cell has no physical volume')
    center = sum(outer, Vector()) / n
    loop_normals = []
    for face, _, smooth in result:
        if smooth:
            loop_normals.extend(normals[i] for i in face)
        else:
            a,b,c = (vertices[i] for i in face[:3])
            normal = (b-a).cross(c-a).normalized()
            loop_normals.extend([normal] * len(face))
    return vertices, result, center, volume, loop_normals


def object_mesh(name, vertices, faces, center, parent, materials, loop_normals):
    mesh = bpy.data.meshes.new(name + 'Mesh')
    mesh.from_pydata([p-center for p in vertices], [], [f for f, _, _ in faces]); mesh.update()
    obj = bpy.data.objects.new(name, mesh); bpy.context.collection.objects.link(obj)
    obj.location = center; obj.parent = parent; obj.matrix_parent_inverse = parent.matrix_world.inverted()
    for material in materials: mesh.materials.append(material)
    uv = mesh.uv_layers.new(name='UVMap')
    for polygon, (_, coords, smooth) in zip(mesh.polygons, faces):
        polygon.use_smooth = smooth
        for loop, value in zip(polygon.loop_indices, coords): uv.data[loop].uv = value
    mesh.normals_split_custom_set(loop_normals)
    return obj


def build():
    retained = [extract(i) for i in range(3)]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = bpy.data.objects.new(NAME, None); bpy.context.collection.objects.link(root)
    records = []; signature = hashlib.sha256()
    for source in retained:
        variant = source['variant']
        anchor = bpy.data.objects.new('Variant' + str(variant), None)
        bpy.context.collection.objects.link(anchor); anchor.parent = root; anchor.matrix_world = source['chest']
        materials = [bpy.data.materials.new(f'GlassCell_{variant}_{i:02}') for i in range(CELL_COUNT)]
        combined_vertices = []; combined_faces = []; combined_normals = []; cell_records = []
        for i, triangles in enumerate(source['cells']):
            vertices, faces, center, volume, loop_normals = closed_cell(triangles)
            obj = object_mesh(f'V{variant}_Cell_{i:02}', vertices, faces, center, anchor, [materials[i]], loop_normals)
            combined_normals.extend(loop_normals)
            start = len(combined_vertices); combined_vertices.extend(vertices)
            combined_faces.extend((tuple(j+start for j in f), coords, smooth, i) for f, coords, smooth in faces)
            for p in vertices: signature.update(json.dumps(plain(p), separators=(',', ':')).encode())
            for normal in loop_normals: signature.update(json.dumps(plain(normal), separators=(',', ':')).encode())
            for face, coords, smooth in faces:
                signature.update(json.dumps((face, [plain(uv) for uv in coords], smooth), separators=(',', ':')).encode())
            cell_records.append(dict(index=i, name=f'Cell_{i:02}', volume_m3=round(volume, 10),
                source_center=unity(center), vertices=len(vertices), triangles=sum(len(f)-2 for f,_,_ in faces)))
        final_faces = [(f, coords, smooth) for f, coords, smooth, _ in combined_faces]
        intact = object_mesh(f'V{variant}_Intact', combined_vertices, final_faces, Vector(), anchor, materials, combined_normals)
        for p, (_, _, _, index) in zip(intact.data.polygons, combined_faces): p.material_index = index
        records.append(dict(variant=variant, anchor=anchor.name, cell_count=CELL_COUNT,
            bounds_min=unity(source['bounds_min']), bounds_max=unity(source['bounds_max']),
            source_body_sha256=source['source_body_sha256'],
            source_editable_sha256=source['source_editable_sha256'],
            source_surface_sha256=source['source_surface_sha256'],
            source_area_m2=round(source['source_area'], 8), cells=cell_records))
    return dict(design_id='zombie_bottle_fracture_v1', generator=Path(__file__).name,
        coordinate_space='source_body_unity_metres', runtime_anchor='canonical_imported_chest',
        cell_count=CELL_COUNT, wall_thickness_m=THICKNESS, variants=records,
        signature=signature.hexdigest())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, default=OUT)
    parser.add_argument('--source-dir', type=Path, default=SOURCE)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    result = build()
    if args.validate_only:
        if json.loads((args.output_dir/(NAME+'.json')).read_text()) != result:
            raise ValueError('Bottle fracture differs from deterministic retained source')
    else:
        args.output_dir.mkdir(parents=True, exist_ok=True); args.source_dir.mkdir(parents=True, exist_ok=True)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/(NAME+'.blend')))
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.export_scene.fbx(filepath=str(args.output_dir/(NAME+'.fbx')), use_selection=True,
            object_types={'EMPTY','MESH'}, axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
            bake_anim=False, use_custom_props=True, path_mode='STRIP', mesh_smooth_type='FACE')
        (args.output_dir/(NAME+'.json')).write_text(json.dumps(result, indent=2)+'\n')
    print('BOTTLE FRACTURE EXACT SURFACE CLOSED CELLS / RETAINED UV RIG OK '+result['signature'], flush=True)


if __name__ == '__main__': main()
