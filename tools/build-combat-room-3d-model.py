"""Deterministic Room Inside body, hollow corridor and ImageGen-derived surfaces.

Use run-blender.py with staged --output-dir/--source-dir/--preview-dir. A real
open tapered passage replaces the chest; runtime retains the anatomical rig.
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
from mathutils import Vector

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import room_inside_geometry as geometry

SIZE, RESPONSE_SIZE = 2048, 1024
OUT = ROOT / 'Assets/Resources/Combat'
SOURCE = ROOT / 'ArtSource/CombatRoom'
SOURCE_IMAGE = SOURCE / 'Textures/RoomSurface.source.png'
HIDDEN = ('GEO_Ear', 'GEO_EarFold', 'GEO_FaceSurface')


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / filename)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


helpers = module('room_surface_bake_helpers', 'build-combat-crane-surfaces.py')
zombie = module('room_retained_worker', 'build-combat-zombie-3d-model.py')


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
    targets = ((.25, .265, .225), (.56, .53, .44), (.52, .49, .44), (.25, .205, .155))
    gains = (.50, .58, .45, .58)
    return tuple(np.clip((a - a.mean(axis=(0, 1))) * gain + target, .015, .82)
                 for a, gain, target in zip(regions, gains, targets))


def field(points, normals, role, name, sources, height_only=False):
    x, y, z = (points[..., i] for i in range(3))
    grain = np.sin(x * 173 + y * 89 + z * 113) * np.sin(z * 83 - y * 131)
    fibers = np.sin(z * 231 + x * 17) * np.sin(x * 205 + y * 41)
    skin_fold = np.sin(z * 89 + x * 31 + y * 83)
    if role == 'cloth':
        height = .0011 * fibers + .0006 * grain
        roughness, cavity, index = .89 + grain * .025, .97, 0
    elif role == 'plaster':
        height = .0014 * grain + .0015 * np.maximum(0., np.sin(x * 29 + z * 37 + y * 11)) ** 7
        roughness, cavity, index = .94 + grain * .025, .92 + .04 * grain, 1
    else:
        height = .0009 * skin_fold + .0004 * grain
        roughness, cavity, index = .71 + grain * .025, .96, 2
    if height_only:
        return height
    projection = (points[..., (1, 2)], points[..., (0, 2)], points[..., (0, 1)])
    weights = np.abs(normals) ** 5
    weights /= max(float(weights.sum()), 1e-9)
    rgb = sum(helpers.sample(sources[index], uv * (4.6 if role == 'cloth' else 3.4) + (.13, .27)) * weights[axis]
              for axis, uv in enumerate(projection))
    if role == 'plaster':
        # The floor uses the fourth source swatch but the same shared response.
        floor = (normals[2] > .65) & (z < 1.19)
        if np.any(floor):
            wood = helpers.sample(sources[3], points[..., (0, 1)] * (5., 2.))
            seams = np.abs(np.mod(x + .024, .048) - .024) > .0232
            wood *= (1. - .35 * seams)[..., None]
            rgb = np.where(floor[..., None], wood, rgb)
        # Deterministic lower-wall dirt is physical wear, not baked illumination.
        rgb *= (1. - .20 * np.maximum(0., 1. - np.abs(z - (1.03 + (y + .182) * .282)) / .035))[..., None]
    elif role == 'cloth' and name == 'GEO_Torso':
        # Worn wool dissolves into plaster at the real doorway's lapel edge.
        border = np.clip(1 - np.abs(np.abs(x) - .165) / .030, 0, 1)
        border *= np.clip((z - 1.0) / .05, 0, 1) * np.clip((1.43 - z) / .05, 0, 1)
        border *= np.clip((-.08 - y) / .08, 0, 1)
        plaster = helpers.sample(sources[1], points[..., (0, 2)] * 4.2)
        rgb = rgb * (1 - border[..., None] * .78) + plaster * border[..., None] * .78
    if name == 'GEO_Head':
        # Nearly featureless, with no printed eyeballs or face-shaped decal.
        seam = np.exp(-((z - 1.564) / .0025) ** 2) * np.exp(-(x / .018) ** 6) * (y < -.055)
        rgb *= (1 - .20 * seam)[..., None]
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
        roles = list(obj['room_surface_roles'])
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
    atlas = bpy.data.images.load(str(output / 'RoomBodyAtlas.png'), check_existing=False)
    atlas.colorspace_settings.name = 'sRGB'
    atlas.pack()
    atlas.filepath = '//../../Assets/Resources/Combat/RoomBodyAtlas.png'
    response = bpy.data.images.load(str(output / 'RoomMaterialResponse.png'), check_existing=False)
    response.colorspace_settings.name = 'Non-Color'
    response.pack()
    response.filepath = '//../../Assets/Resources/Combat/RoomMaterialResponse.png'
    for role in ('cloth', 'plaster', 'flesh'):
        material = bpy.data.materials.new('Room ' + role)
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
        result[role] = material
    for obj in objects:
        indices = [p.material_index for p in obj.data.polygons]
        obj.data.materials.clear()
        for role in obj['room_surface_roles']:
            obj.data.materials.append(result[role])
        for poly, index in zip(obj.data.polygons, indices):
            poly.material_index = index
    return result


def measure_bounds(obj):
    points = np.array([obj.matrix_world @ v.co for v in obj.data.vertices])[:, (0, 2, 1)]
    return dict(bounds_min=[round(float(v), 7) for v in points.min(axis=0)],
                bounds_max=[round(float(v), 7) for v in points.max(axis=0)])


def validate_hollow(torso):
    # Direct mesh-ray checks prevent a later coat face silently sealing the door.
    from mathutils.bvhtree import BVHTree
    matrix = torso.matrix_world
    vertices = [matrix @ v.co for v in torso.data.vertices]
    tree = BVHTree.FromPolygons(vertices, [list(p.vertices) for p in torso.data.polygons])
    for x in (-.012, 0., .012):
        hit, normal, index, distance = tree.ray_cast(Vector((x, -.35, 1.215)), Vector((0, 1, 0)), 1.)
        if hit is not None:
            raise ValueError('Room corridor central sightline is blocked: ' + str(hit))
    for x in (-.12, .12):
        hit, normal, index, distance = tree.ray_cast(Vector((x, -.35, 1.215)), Vector((0, 1, 0)), 1.)
        if hit is None or hit.y < -.19:
            raise ValueError('Room receding frame failed to give physical depth')


def build(output, source_dir, preview_dir=None, publish=True):
    builder = zombie.load_builder(retain_meshes=True)
    rig = builder.result.rig
    builder._reset_pose()
    bpy.context.view_layer.update()
    corridor = geometry.sculpt(builder)
    objects = sorted((o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('GEO_')), key=lambda o: o.name)
    validate_hollow(bpy.data.objects['GEO_Torso'])
    # Slots must exist before UV/baking so Blender retains the torso face roles.
    for obj in objects:
        indices = [p.material_index for p in obj.data.polygons]
        obj.data.materials.clear()
        for role in obj['room_surface_roles']:
            obj.data.materials.append(bpy.data.materials.get('Room placeholder ' + role) or bpy.data.materials.new('Room placeholder ' + role))
        for polygon, index in zip(obj.data.polygons, indices):
            polygon.material_index = index
    # Allocate most UV space to the torso doorway and head, while keeping every
    # finger unique and the source channel available to source-bound damage.
    priority = [bpy.data.objects['GEO_Torso'], bpy.data.objects['GEO_Head'], bpy.data.objects['GEO_Neck']]
    rest = [o for o in objects if o not in priority]
    coordinates = {o.name: [list(o.matrix_world @ v.co) for v in o.data.vertices] for o in objects}
    helpers.unwrap(priority, coordinates, (.012, .39, .976, .598))
    helpers.unwrap(rest, coordinates, (.012, .012, .976, .360))
    print('ROOM HOLLOW CORRIDOR AND UNIQUE UV0 / SOURCE UV1 OK', flush=True)
    sources = source_pixels()
    colour = paint(objects, sources, SIZE)
    print('ROOM ALBEDO BAKED', flush=True)
    response = paint(objects, sources, RESPONSE_SIZE, True)
    print('ROOM REGISTERED RESPONSE BAKED', flush=True)
    payload = dict(design_id='room_body_surface_v1', generator=Path(__file__).name, rig='NpcHumanV2',
        geometry_owner='independent_sculpt', bounds_space='unity_metres', retained_rest_skeleton=True,
        uv_channel=0, source_uv_channel=1, source_sha256=digest(SOURCE_IMAGE.read_bytes()),
        source_geometry_sha256=digest((ROOT / 'ArtSource/VillageLife/StationWorker.blend').read_bytes()),
        signature=helpers.geometry_signature(objects), corridor=corridor,
        meshes=[dict(name=o.name, visible=not o.name.startswith(HIDDEN), vertices=len(o.data.vertices),
                     triangles=sum(len(p.vertices) - 2 for p in o.data.polygons),
                     surfaces=list(o['room_surface_roles']), **measure_bounds(o)) for o in objects],
        hardware=[], passive_meshes=[],
        textures=[dict(name='RoomBodyAtlas', size=SIZE, sRGB=True, sha256=digest(colour)),
                  dict(name='RoomMaterialResponse', size=RESPONSE_SIZE, sRGB=False, sha256=digest(response))],
        estimated_gpu_bytes=sum(sum(max(1, (s >> m) + 3 >> 2) ** 2 * 16 for m in range(int(math.log2(s)) + 1))
                                for s in (SIZE, RESPONSE_SIZE)))
    if payload['estimated_gpu_bytes'] > 7 * 1024 * 1024:
        raise ValueError('Room surface bank exceeds 7 MiB')
    if not publish:
        return payload
    output.mkdir(parents=True, exist_ok=True)
    source_dir.mkdir(parents=True, exist_ok=True)
    (output / 'RoomBodyAtlas.png').write_bytes(colour)
    (output / 'RoomMaterialResponse.png').write_bytes(response)
    (output / 'RoomBody.json').write_text(json.dumps(payload, indent=2) + '\n', encoding='utf8')
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(output / 'RoomBody.fbx'), use_selection=True,
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
    bpy.ops.wm.save_as_mainfile(filepath=str(source_dir / 'RoomBody.blend'))
    if preview_dir:
        preview(objects, rig, builder, preview_dir)
    return payload


def preview(objects, rig, builder, directory):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1200
    scene.render.resolution_percentage = 100
    scene.world.color = (.025, .025, .025)
    camera_data = bpy.data.cameras.new('Room inspection camera')
    camera = bpy.data.objects.new('Room inspection camera', camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    for pos, power, size in (((.8, -2.2, 2.1), 220, 1.5), ((-1.8, 1.2, 2.5), 120, 2.)):
        data = bpy.data.lights.new('Room review light', 'AREA')
        data.energy = power
        data.shape = 'DISK'
        data.size = size
        obj = bpy.data.objects.new('Room review light', data)
        scene.collection.objects.link(obj)
        obj.location = pos
        obj.rotation_euler = (Vector((0, 0, 1.1)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    # Existing ordinary idle poses drive this same unchanged bind skeleton.
    ordinary = module('room_ordinary_preview', 'build-combat-ordinary-zombie-3d-model.py')
    builder.footwork_support_targets = {side: rig.data.bones['foot.' + side].head_local.copy()
                                        for side in ('L', 'R')}
    ordinary.pose(builder, 'ZombieOrdinaryIdle', .4)
    bpy.context.view_layer.update()
    directory.mkdir(parents=True, exist_ok=True)
    for name, position, target, focal in (
        ('RoomFront', (1.5, -3.2, 1.8), (0, 0, 1.10), 55),
        ('RoomProfile', (3., -.30, 1.65), (0, .04, 1.12), 55),
        ('RoomCorridor', (.03, -1.10, 1.235), (0, .20, 1.215), 55)):
        camera.location = position
        camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera_data.type = 'PERSP'
        camera_data.lens = focal
        scene.render.filepath = str(directory / (name + '.png'))
        bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, default=OUT)
    parser.add_argument('--source-dir', type=Path, default=SOURCE)
    parser.add_argument('--preview-dir', type=Path)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    measured = build(args.output_dir.resolve(), args.source_dir.resolve(),
                     args.preview_dir.resolve() if args.preview_dir else None, not args.validate_only)
    if args.validate_only:
        expected = json.loads((args.output_dir / 'RoomBody.json').read_text(encoding='utf8'))
        if measured != expected:
            raise ValueError('Room body differs from deterministic source')
        for row in measured['textures']:
            if digest((args.output_dir / (row['name'] + '.png')).read_bytes()) != row['sha256']:
                raise ValueError('Room map differs: ' + row['name'])
    print('ROOM BODY, HOLLOW CORRIDOR, SURFACES AND BUDGET OK ' + measured['signature'], flush=True)


if __name__ == '__main__':
    main()
