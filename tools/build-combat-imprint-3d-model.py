"""Deterministic closed negative human casts and ImageGen-derived surfaces.

Use run-blender.py with staged --output-dir/--source-dir/--preview-dir. Forehead,
nose, lips, chest and palms recede into real closed source-bound surfaces.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import imprint_geometry as geometry

SIZE, RESPONSE_SIZE = 2048, 1024
OUT = ROOT / 'Assets/Resources/Combat'
SOURCE = ROOT / 'ArtSource/CombatImprint'
SOURCE_IMAGE = SOURCE / 'Textures/ImprintSurface.source.png'
HIDDEN = ('GEO_Ear', 'GEO_EarFold', 'GEO_FaceSurface')


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / filename)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


helpers = module('imprint_surface_bake_helpers', 'build-combat-crane-surfaces.py')
zombie = module('imprint_retained_worker', 'build-combat-zombie-3d-model.py')


def digest(value):
    return hashlib.sha256(value).hexdigest()


def source_pixels():
    image = bpy.data.images.load(str(SOURCE_IMAGE), check_existing=True)
    image.colorspace_settings.name = 'Non-Color'
    array = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(array)
    array = array.reshape(image.size[1], image.size[0], 4)[..., :3]
    h, w = array.shape[:2]
    regions = (array[h // 2:, :w // 2], array[h // 2:, w // 2:],
               array[:h // 2, :w // 2], array[:h // 2, w // 2:])
    # Midtones deliberately survive the warehouse's existing chest lamp.
    targets = ((.60, .605, .565), (.095, .105, .102), (.48, .47, .435), (.30, .32, .29))
    gains = (.43, .32, .38, .40)
    return tuple(np.clip((a - a.mean(axis=(0, 1))) * gain + target, .015, .82)
                 for a, gain, target in zip(regions, gains, targets))


def field(points, normals, role, name, sources, height_only=False):
    x, y, z = (points[..., i] for i in range(3))
    grain = np.sin(x * 173 + y * 89 + z * 113) * np.sin(z * 83 - y * 131)
    pores = np.sin(z * 231 + x * 17) * np.sin(x * 205 + y * 41)
    skin_fold = np.sin(z * 89 + x * 31 + y * 83)
    if role == 'chalk':
        height = .00055 * pores + .00035 * grain
        roughness, cavity, index = .94 + grain * .018, .97, 0
    elif role == 'glass':
        height = .0007 * grain + .0008 * np.maximum(0., np.sin(x * 77 + z * 89 + y * 41)) ** 11
        roughness, cavity, index = .23 + grain * .04, .94 + .025 * grain, 1
    else:
        height = .0009 * skin_fold + .0004 * grain
        roughness, cavity, index = .71 + grain * .025, .96, 2
    if height_only:
        return height
    projection = (points[..., (1, 2)], points[..., (0, 2)], points[..., (0, 1)])
    weights = np.abs(normals) ** 5
    weights /= max(float(weights.sum()), 1e-9)
    rgb = sum(helpers.sample(sources[index], uv * (4.6 if role == 'chalk' else 3.4) + (.13, .27)) * weights[axis]
              for axis, uv in enumerate(projection))
    if role == 'chalk':
        # Sparse mineral inclusions stay subordinate to the actual concave form.
        worn = np.maximum(0., np.sin(x * 31 - z * 29 + y * 43)) ** 12
        dark = helpers.sample(sources[3], points[..., (0, 2)] * 2.2)
        rgb = rgb * (1 - worn[..., None] * .10) + dark * worn[..., None] * .10
    return np.clip(rgb, 0, 1), height, roughness + np.zeros_like(x), cavity + np.zeros_like(x)


def paint(objects, sources, size, response=False):
    channels = 4 if response else 3
    canvas = np.empty((size, size, channels), dtype=np.float32)
    canvas[:] = (.5, .5, .9, .95) if response else (.16, .15, .12)
    occupied = np.zeros((size, size), dtype=bool)
    for obj in objects:
        mesh = obj.data
        mesh.calc_loop_triangles()
        vertices = np.array([obj.matrix_world @ v.co for v in mesh.vertices])
        roles = list(obj['imprint_surface_roles'])
        for tri in mesh.loop_triangles:
            uv = np.array([mesh.uv_layers[0].data[i].uv[:] for i in tri.loops])
            points = vertices[list(tri.vertices)]
            normal = np.cross(points[1] - points[0], points[2] - points[0])
            length = np.linalg.norm(normal)
            determinant = np.linalg.det(uv[1:] - uv[0])
            if abs(determinant) < 1e-12 or length < 1e-12:
                continue
            normal /= length
            low = np.maximum(np.floor(uv.min(axis=0) * size - .5).astype(int), 0)
            high = np.minimum(np.ceil(uv.max(axis=0) * size + .5).astype(int), size - 1)
            xx, yy = np.meshgrid(np.arange(low[0], high[0] + 1), np.arange(low[1], high[1] + 1))
            pixel = np.stack((xx, yy), axis=-1)
            bary = ((pixel + .5) / size - uv[0]) @ np.linalg.inv(uv[1:] - uv[0])
            inside = (bary[..., 0] >= -1e-6) & (bary[..., 1] >= -1e-6) & (bary.sum(axis=-1) <= 1 + 1e-6)
            if not inside.any():
                continue
            position = points[0] + bary @ (points[1:] - points[0])
            role = roles[tri.material_index]
            rgb, height, roughness, cavity = field(position, normal, role, obj.name, sources)
            if response:
                basis = np.linalg.solve(uv[1:] - uv[0], points[1:] - points[0])
                basis /= np.maximum(np.linalg.norm(basis, axis=1, keepdims=True), 1e-9)
                slopes = []
                for direction in basis:
                    plus = field(position + direction * .0007, normal, role, obj.name, sources, True)
                    minus = field(position - direction * .0007, normal, role, obj.name, sources, True)
                    slopes.append(np.clip((plus - minus) / .0014 * .07, -.55, .55))
                output = np.stack((.5 + slopes[0] * .5, .5 + slopes[1] * .5, roughness, cavity), axis=-1)
            else:
                output = rgb
            target = np.s_[low[1]:high[1] + 1, low[0]:high[0] + 1]
            canvas[target][inside] = output[inside]
            occupied[target][inside] = True
    for _ in range(8 if response else 16):
        fresh = occupied.copy()
        for axis, shift in ((0, -1), (0, 1), (1, -1), (1, 1)):
            mask = np.roll(occupied, shift, axis) & ~fresh
            canvas[mask] = np.roll(canvas, shift, axis)[mask]
            fresh |= mask
        occupied = fresh
    return helpers.png(canvas)


def materials(objects, output):
    result = {}
    atlas = bpy.data.images.load(str(output / 'ImprintBodyAtlas.png'), check_existing=False)
    atlas.colorspace_settings.name = 'sRGB'
    atlas.pack()
    atlas.filepath = '//../../Assets/Resources/Combat/ImprintBodyAtlas.png'
    response = bpy.data.images.load(str(output / 'ImprintMaterialResponse.png'), check_existing=False)
    response.colorspace_settings.name = 'Non-Color'
    response.pack()
    response.filepath = '//../../Assets/Resources/Combat/ImprintMaterialResponse.png'
    for role in ('chalk', 'glass', 'flesh'):
        material = bpy.data.materials.new('Imprint ' + role)
        material.use_nodes = True
        nodes, links = material.node_tree.nodes, material.node_tree.links
        shader = nodes.get('Principled BSDF')
        texture = nodes.new('ShaderNodeTexImage')
        texture.image = atlas
        links.new(texture.outputs['Color'], shader.inputs['Base Color'])
        aux = nodes.new('ShaderNodeTexImage')
        aux.image = response
        separate = nodes.new('ShaderNodeSeparateColor')
        links.new(aux.outputs['Color'], separate.inputs['Color'])
        links.new(separate.outputs['Blue'], shader.inputs['Roughness'])
        shader.inputs['Metallic'].default_value = .08 if role == 'glass' else 0.
        result[role] = material
    for obj in objects:
        indices = [p.material_index for p in obj.data.polygons]
        obj.data.materials.clear()
        for role in obj['imprint_surface_roles']:
            obj.data.materials.append(result[role])
        for poly, index in zip(obj.data.polygons, indices):
            poly.material_index = index
    return result


def measure_bounds(obj):
    points = np.array([obj.matrix_world @ v.co for v in obj.data.vertices])[:, (0, 2, 1)]
    return dict(bounds_min=[round(float(v), 7) for v in points.min(axis=0)],
                bounds_max=[round(float(v), 7) for v in points.max(axis=0)])


def validate_negative(objects, contract):
    from collections import Counter
    from mathutils.bvhtree import BVHTree
    by_name = {obj.name: obj for obj in objects}
    trees = {}
    for name, obj in by_name.items():
        if name.startswith(HIDDEN):
            continue
        faces = [list(p.vertices) for p in obj.data.polygons]
        points = [obj.matrix_world @ v.co for v in obj.data.vertices]
        edges = Counter(tuple(sorted((p[i], p[(i + 1) % len(p)]))) for p in faces for i in range(len(p)))
        if any(count != 2 for count in edges.values()) or geometry.volume(points, faces) <= 0:
            raise ValueError('Imprint cast is not a closed outward volume: ' + name)
        trees[name] = BVHTree.FromPolygons(points, faces)
    for probe in contract['negative_probes']:
        hit, normal, index, distance = trees[probe['source_renderer']].ray_cast(
            Vector(probe['origin']), Vector(probe['direction']), .5)
        if hit is None or distance < probe['minimum_clear_distance_m']:
            raise ValueError('Imprint negative form is flat or convex: ' + probe['name'] + ':' + str(distance))
        probe['measured_clear_distance_m'] = round(distance, 7)


def build(output, source_dir, preview_dir=None, publish=True):
    builder = zombie.load_builder(retain_meshes=True)
    rig = builder.result.rig
    builder._reset_pose()
    bpy.context.view_layer.update()
    negative = geometry.sculpt(builder)
    objects = sorted((o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('GEO_')), key=lambda o: o.name)
    validate_negative(objects, negative)
    # Slots must exist before UV/baking so Blender retains the torso face roles.
    for obj in objects:
        indices = [p.material_index for p in obj.data.polygons]
        obj.data.materials.clear()
        for role in obj['imprint_surface_roles']:
            obj.data.materials.append(bpy.data.materials.get('Imprint placeholder ' + role) or bpy.data.materials.new('Imprint placeholder ' + role))
        for polygon, index in zip(obj.data.polygons, indices):
            polygon.material_index = index
    # Allocate most UV space to the negative face and chest, while keeping every
    # finger unique and the source channel available to source-bound damage.
    priority = [bpy.data.objects['GEO_Torso'], bpy.data.objects['GEO_Head'], bpy.data.objects['GEO_Neck']]
    rest = [o for o in objects if o not in priority]
    coordinates = {o.name: [list(o.matrix_world @ v.co) for v in o.data.vertices] for o in objects}
    helpers.unwrap(priority, coordinates, (.012, .39, .976, .598))
    helpers.unwrap(rest, coordinates, (.012, .012, .976, .360))
    print('IMPRINT CLOSED NEGATIVE FACE, CHEST, PALMS AND UNIQUE UV0 / SOURCE UV1 OK', flush=True)
    sources = source_pixels()
    colour = paint(objects, sources, SIZE)
    print('IMPRINT ALBEDO BAKED', flush=True)
    response = paint(objects, sources, RESPONSE_SIZE, True)
    print('IMPRINT REGISTERED RESPONSE BAKED', flush=True)
    payload = dict(design_id='imprint_body_surface_v1', generator=Path(__file__).name, rig='NpcHumanV2',
        geometry_owner='independent_sculpt', bounds_space='unity_metres', retained_rest_skeleton=True,
        uv_channel=0, source_uv_channel=1, source_sha256=digest(SOURCE_IMAGE.read_bytes()),
        source_geometry_sha256=digest((ROOT / 'ArtSource/VillageLife/StationWorker.blend').read_bytes()),
        signature=helpers.geometry_signature(objects), imprint_negative_form=negative,
        meshes=[dict(name=o.name, visible=not o.name.startswith(HIDDEN), vertices=len(o.data.vertices),
                     triangles=sum(len(p.vertices) - 2 for p in o.data.polygons),
                     surfaces=list(o['imprint_surface_roles']), **measure_bounds(o)) for o in objects],
        hardware=[], passive_meshes=[],
        textures=[dict(name='ImprintBodyAtlas', size=SIZE, sRGB=True, sha256=digest(colour)),
                  dict(name='ImprintMaterialResponse', size=RESPONSE_SIZE, sRGB=False, sha256=digest(response))],
        estimated_gpu_bytes=sum(sum(max(1, (s >> m) + 3 >> 2) ** 2 * 16 for m in range(int(math.log2(s)) + 1))
                                for s in (SIZE, RESPONSE_SIZE)))
    if payload['estimated_gpu_bytes'] > 7 * 1024 * 1024:
        raise ValueError('Imprint surface bank exceeds 7 MiB')
    if not publish:
        return payload
    output.mkdir(parents=True, exist_ok=True)
    source_dir.mkdir(parents=True, exist_ok=True)
    (output / 'ImprintBodyAtlas.png').write_bytes(colour)
    (output / 'ImprintMaterialResponse.png').write_bytes(response)
    (output / 'ImprintBody.json').write_text(json.dumps(payload, indent=2) + '\n', encoding='utf8')
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(output / 'ImprintBody.fbx'), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
        bake_anim=False, use_custom_props=True, use_mesh_modifiers=False,
        use_armature_deform_only=False, path_mode='STRIP', mesh_smooth_type='FACE')
    selected = set(bpy.context.selected_objects)
    for obj in list(bpy.data.objects):
        if obj not in selected and obj.type in ('MESH', 'CAMERA', 'LIGHT'):
            bpy.data.objects.remove(obj, do_unlink=True)
    materials(objects, output)
    for obj in objects:
        obj.hide_render = obj.name.startswith(HIDDEN)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(source_dir / 'ImprintBody.blend'))
    if preview_dir:
        preview(objects, rig, builder, preview_dir)
    return payload


def preview(objects, rig, builder, directory, attack_only=False):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1200
    scene.render.resolution_percentage = 100
    scene.world.color = (.025, .025, .025)
    camera_data = bpy.data.cameras.new('Imprint inspection camera')
    camera = bpy.data.objects.new('Imprint inspection camera', camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    lamps = []
    for pos, power, size in (((.8, -2.2, 2.1), 220, 1.5), ((-1.8, 1.2, 2.5), 120, 2.)):
        data = bpy.data.lights.new('Imprint review light', 'AREA')
        data.energy = power
        data.shape = 'DISK'
        data.size = size
        obj = bpy.data.objects.new('Imprint review light', data)
        scene.collection.objects.link(obj)
        obj.location = pos
        obj.rotation_euler = (Vector((0, 0, 1.1)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
        lamps.append(obj)
    # Existing ordinary idle poses drive this same unchanged bind skeleton.
    ordinary = module('imprint_ordinary_preview', 'build-combat-ordinary-zombie-3d-model.py')
    builder.footwork_support_targets = {side: rig.data.bones['foot.' + side].head_local.copy()
                                        for side in ('L', 'R')}
    ordinary.pose(builder, 'ZombieOrdinaryIdle', .4)
    bpy.context.view_layer.update()
    head = rig.pose.bones['head']
    skin = rig.matrix_world @ head.matrix @ head.bone.matrix_local.inverted()
    cast_rotation = Matrix.Rotation(math.radians(geometry.HEAD_CAST_PITCH_DEGREES), 3, 'X')
    cast_pivot = head.bone.head_local
    cast_center = cast_pivot + cast_rotation @ (Vector((0., -.025, geometry.HEAD_Z)) - cast_pivot)
    centre = skin @ cast_center
    front = (skin.to_3x3() @ cast_rotation @ Vector((0., -1., 0.))).normalized()
    right = (skin.to_3x3() @ Vector((1., 0., 0.))).normalized()
    up = (skin.to_3x3() @ cast_rotation @ Vector((0., 0., 1.))).normalized()
    chest = rig.pose.bones['chest']
    chest_skin = rig.matrix_world @ chest.matrix @ chest.bone.matrix_local.inverted()
    chest_centre = chest_skin @ Vector((0., -.065, 1.255))
    chest_front = (chest_skin.to_3x3() @ Vector((0., -1., 0.))).normalized()
    chest_right = (chest_skin.to_3x3() @ Vector((1., 0., 0.))).normalized()
    for lamp, position in zip(lamps, (centre + front * 1.4 + right * .85 + up * .50,
                                      centre - front * 1.3 - right * 1.1 + up * .8)):
        lamp.location = position
        lamp.rotation_euler = (centre - position).to_track_quat('-Z', 'Y').to_euler()
    directory.mkdir(parents=True, exist_ok=True)
    views = () if attack_only else (
        ('ImprintFront', (.5, -3.2, 1.75), (0, 0, 1.10), 55),
        ('ImprintProfile', (2., -2.6, 1.65), (0., 0., 1.05), 55),
        ('ImprintFace', centre + front * .78 + up * .015, centre, 65),
        ('ImprintChest', chest_centre + chest_front * .90 + chest_right * .12, chest_centre, 65))
    for name, position, target, focal in views:
        if name == 'ImprintChest':
            lamps[0].location = chest_centre + chest_front * .58 + chest_right * 1.10 + up * .20
            lamps[0].rotation_euler = (chest_centre - lamps[0].location).to_track_quat('-Z', 'Y').to_euler()
            lamps[0].data.size = .70
        camera.location = position
        camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera_data.type = 'PERSP'
        camera_data.lens = focal
        scene.render.filepath = str(directory / (name + '.png'))
        bpy.ops.render.render(write_still=True)
    ordinary.pose(builder, 'ZombieOrdinaryAttackRight', .72)
    bpy.context.view_layer.update()
    chest_skin = rig.matrix_world @ chest.matrix @ chest.bone.matrix_local.inverted()
    target = chest_skin @ Vector((0., -.025, 1.245))
    forward = (chest_skin.to_3x3() @ Vector((0., -1., 0.))).normalized()
    lateral = (chest_skin.to_3x3() @ Vector((1., 0., 0.))).normalized()
    camera.location = target + forward * 1.10 + lateral * .52
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data.lens = 55
    scene.render.filepath = str(directory / 'ImprintAttack.png')
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, default=OUT)
    parser.add_argument('--source-dir', type=Path, default=SOURCE)
    parser.add_argument('--preview-dir', type=Path)
    parser.add_argument('--validate-only', action='store_true')
    parser.add_argument('--preview-only', action='store_true')
    parser.add_argument('--attack-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    if args.preview_only:
        if args.preview_dir is None:
            raise ValueError('Imprint preview-only requires --preview-dir')
        builder = zombie.load_builder(retain_meshes=True)
        bpy.ops.wm.open_mainfile(filepath=str(args.source_dir / 'ImprintBody.blend'))
        rig = bpy.data.objects['RIG_Player']
        builder.result.rig = rig
        builder.result.root = rig.parent
        builder.points = builder.create_pose_points()
        builder._reset_pose()
        objects = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('GEO_')]
        preview(objects, rig, builder, args.preview_dir, args.attack_only)
        print('IMPRINT AUTHORED POSE PREVIEW OK', flush=True)
        return
    measured = build(args.output_dir.resolve(), args.source_dir.resolve(),
                     args.preview_dir.resolve() if args.preview_dir else None, not args.validate_only)
    if args.validate_only:
        expected = json.loads((args.output_dir / 'ImprintBody.json').read_text(encoding='utf8'))
        if measured != expected:
            raise ValueError('Imprint body differs from deterministic source')
        for row in measured['textures']:
            if digest((args.output_dir / (row['name'] + '.png')).read_bytes()) != row['sha256']:
                raise ValueError('Imprint map differs: ' + row['name'])
    print('IMPRINT CLOSED NEGATIVE GEOMETRY, SURFACES AND BUDGET OK ' + measured['signature'], flush=True)


if __name__ == '__main__':
    main()
