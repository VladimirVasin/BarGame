"""Rebake monster albedo/response into existing UVs without exporting geometry.

Run via tools/run-blender.py. --validate-only independently rebakes and compares
published PNG bytes, source provenance and geometry/UV/rig protection hashes.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

import bpy
import numpy as np

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import combat_horror_surfaces as horror

FILES = {
    'Crane': 'build-combat-crane-surfaces.py',
    'Room': 'build-combat-room-3d-model.py',
    'Imprint': 'build-combat-imprint-3d-model.py',
    'LongExposure': 'build-combat-long-exposure-3d-model.py',
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def module(family):
    spec = importlib.util.spec_from_file_location('refresh_' + family, ROOT / 'tools' / FILES[family])
    result = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = result
    spec.loader.exec_module(result)
    return result


def protected_files(family):
    # Every published FBX associated with this body remains byte-for-byte
    # unchanged, including source-bound damage and action banks.
    paths = [ROOT / 'Assets/Resources/Combat' / (family + 'Body.fbx')]
    for folder in ('CombatGore', 'CombatBlood'):
        paths.extend((ROOT / 'Assets/Resources' / folder).glob('*' + family + '*.fbx'))
    return {str(path.relative_to(ROOT)): digest(path.read_bytes()) for path in paths}


def refresh(family, output, validate_only):
    bank = module(family)
    native = ROOT / 'Assets/Resources/Combat'
    manifest_path = native / (family + 'Body.json')
    expected = json.loads(manifest_path.read_text())
    before_files = protected_files(family)
    source = ROOT / ('ArtSource/Combat' + family) / (family + 'Body.blend')
    bpy.ops.wm.open_mainfile(filepath=str(source))
    anatomical = sorted((bpy.data.objects[row['name']] for row in expected['meshes']), key=lambda item: item.name)
    static_names = {row['name'] for row in expected.get('hardware', [])}
    static_names.update(row['name'] for row in expected.get('passive_meshes', []))
    objects = anatomical + sorted((bpy.data.objects[name] for name in static_names), key=lambda item: item.name)
    helper = bank if family == 'Crane' else bank.helpers
    signature = helper.geometry_signature(objects)
    if signature != expected['signature']:
        raise ValueError(f'{family} editable geometry/UVs no longer match its locked manifest')
    rig = bpy.data.objects['RIG_Player']
    bind = [(bone.name, [value for row in bone.matrix_local for value in row]) for bone in rig.data.bones]
    if family == 'Crane':
        bank.BONES = {bone.name: (np.array(rig.matrix_world @ bone.head_local),
                                  np.array(rig.matrix_world @ bone.tail_local)) for bone in rig.data.bones}
    sources = bank.source_pixels()
    colour = bank.paint(objects, sources, bank.SIZE)
    print(f'{family} HORROR ALBEDO REBAKED', flush=True)
    response = bank.paint(objects, sources, bank.RESPONSE_SIZE, True)
    print(f'{family} REGISTERED RESPONSE REBAKED', flush=True)
    measured = dict(expected)
    measured['source_sha256'] = digest(horror.SOURCE_IMAGE.read_bytes())
    horror.decorate(measured)
    for row, data in zip(measured['textures'], (colour, response)):
        row['sha256'] = digest(data)
    if helper.geometry_signature(objects) != signature or bind != [
            (bone.name, [value for row in bone.matrix_local for value in row]) for bone in rig.data.bones]:
        raise ValueError('Texture rebake changed a geometric or rig contract')
    if before_files != protected_files(family):
        raise ValueError('Texture rebake modified published FBX files')
    if validate_only:
        actual = json.loads((output / manifest_path.name).read_text())
        if measured != actual:
            raise ValueError(f'{family} texture source/profile/manifest drifted')
        for row, data in zip(measured['textures'], (colour, response)):
            if (output / (row['name'] + '.png')).read_bytes() != data:
                raise ValueError('Non-deterministic texture: ' + row['name'])
    else:
        output.mkdir(parents=True, exist_ok=True)
        (output / (family + 'BodyAtlas.png')).write_bytes(colour)
        (output / (family + 'MaterialResponse.png')).write_bytes(response)
        (output / manifest_path.name).write_text(json.dumps(measured, indent=2) + '\n')
        # Update packed editable material sources, retaining exact native
        # coordinates/indices/UVs/bones/actions and exported FBX bytes.
        if family == 'Crane':
            bank.source_material(objects, output)
        else:
            bank.materials(objects, output)
        if helper.geometry_signature(objects) != signature:
            raise ValueError('Material refresh modified source mesh data')
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(source))
        if family in ('Imprint', 'LongExposure'):
            path = ROOT / 'Assets/Resources/CombatGore' / ('Combat' + family + 'Damage3D.json')
            damage = json.loads(path.read_text())
            if damage['source']['sha256'] != before_files['Assets/Resources/Combat/' + family + 'Body.fbx']:
                raise ValueError('Existing damage bank has a different geometry source')
            damage['source_manifest_sha256'] = digest((output / manifest_path.name).read_bytes())
            path.write_text(json.dumps(damage, indent=2) + '\n')
    print(f'{family} HORROR TEXTURES / LOCKED FBX UV RIG OK {signature}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, default=ROOT / 'Assets/Resources/Combat')
    parser.add_argument('--families', nargs='+', choices=FILES, default=list(FILES))
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    for family in args.families:
        refresh(family, args.output_dir.resolve(), args.validate_only)


if __name__ == '__main__':
    main()
