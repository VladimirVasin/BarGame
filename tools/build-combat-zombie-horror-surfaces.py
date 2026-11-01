"""Rebake zombie/porter pigments only, retaining every published mesh and action.

Run via tools/run-blender.py. --validate-only checks exact deterministic output,
provenance and retained FBX/UV/rig signatures without opening or exporting a rig.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / 'tools'))
from atlas_kit import read_generated_png
from combat_horror_surfaces import SOURCE_IMAGE

OUTPUT = ROOT / 'Assets/Resources/Combat'
RECORD = ROOT / 'ArtSource/CombatZombie/Textures/HorrorSurfaceBake.json'


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / filename)
    value = importlib.util.module_from_spec(spec)
    sys.modules[name] = value
    spec.loader.exec_module(value)
    return value


def digest(data):
    return hashlib.sha256(data).hexdigest()


def retained_files():
    names = ['ZombieCrate.fbx', 'ZombieActions.fbx', 'ZombieDecayActions.fbx']
    names += [f'ZombieDecay{kind}{variant}.fbx' for kind in ('Body', 'Dormant', 'Standing') for variant in range(3)]
    files = {name: digest((OUTPUT / name).read_bytes()) for name in names}
    # The global NPC source and palette are explicitly outside this change.
    for name in ('Assets/Resources/VillageLife/StationWorkerAtlas.png',
                 'ArtSource/VillageLife/StationWorker.blend'):
        files[name] = digest((ROOT / name).read_bytes())
    return files


def expected():
    decay = module('horror_decay_source', 'build-combat-degraded-zombie-3d-model.py')
    porter = module('horror_porter_source', 'build-combat-zombie-3d-model.py')
    images = {f'ZombieDecayBody{i}Atlas.png': decay.atlas(i) for i in range(3)}
    images.update({f'ZombieFace{i + 1}.png': porter.atlas(i) for i in range(3)})
    images['ZombieCrateAtlas.png'] = porter.crate_atlas()
    metadata = {}
    for variant in range(3):
        name = f'ZombieDecayBody{variant}.json'
        payload = json.loads((OUTPUT / name).read_text())
        payload['atlas_sha256'] = digest(images[f'ZombieDecayBody{variant}Atlas.png'])
        metadata[name] = payload
    payload = json.loads((OUTPUT / 'ZombieCrate.json').read_text())
    payload['texture_sha256'] = digest(images['ZombieCrateAtlas.png'])
    metadata['ZombieCrate.json'] = payload
    payload = json.loads((OUTPUT / 'Zombie3D.json').read_text())
    for name, data in images.items():
        if name in payload['assets']:
            payload['assets'][name] = digest(data)
    metadata['Zombie3D.json'] = payload
    return images, metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, default=OUTPUT)
    parser.add_argument('--record', type=Path, default=RECORD)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if not SOURCE_IMAGE.is_file():
        raise ValueError('The authored horror material sheet is required before baking.')
    retained = retained_files()
    images, metadata = expected()
    repeated, repeated_metadata = expected()
    if images != repeated or metadata != repeated_metadata:
        raise ValueError('Zombie horror pigments are not deterministic.')
    provenance = dict(generator=Path(__file__).name, version=1,
        source=str(SOURCE_IMAGE.relative_to(ROOT)), source_sha256=digest(SOURCE_IMAGE.read_bytes()),
        uv_layout='retained 512x512 worker and 512x256 porter charts',
        geometry_rig_actions=retained,
        output_sha256={name: digest(data) for name, data in images.items()},
        material_tiles=['skin', 'glass', 'cloth', 'wood', 'fascia', 'residue'])
    args.output_dir.mkdir(parents=True, exist_ok=True)
    for name, data in images.items():
        path = args.output_dir / name
        if args.validate_only:
            if path.read_bytes() != data:
                raise ValueError('Published texture differs: ' + name)
        else:
            path.write_bytes(data)
        width, height, pixels = read_generated_png(path)
        if (width, height) != ((512, 256) if name == 'ZombieCrateAtlas.png' else (512, 512)):
            raise ValueError('Retained atlas dimensions changed: ' + name)
        if any(pixels[index] != 255 for index in range(3, len(pixels), 4)):
            raise ValueError('Opaque monster pigment gained transparency: ' + name)
    for name, payload in metadata.items():
        path = args.output_dir / name
        if args.validate_only:
            if json.loads(path.read_text()) != payload:
                raise ValueError('Texture hash manifest differs: ' + name)
        else:
            path.write_text(json.dumps(payload, indent=2) + '\n')
    if retained != retained_files():
        raise ValueError('Texture baking changed a published mesh, skeleton or living palette.')
    if args.validate_only:
        if json.loads(args.record.read_text()) != provenance:
            raise ValueError('Monster texture provenance differs.')
    else:
        args.record.parent.mkdir(parents=True, exist_ok=True)
        args.record.write_text(json.dumps(provenance, indent=2) + '\n')
    print('ZOMBIE PORTER HORROR SURFACES / RETAINED UV RIG GEOMETRY OK', flush=True)


if __name__ == '__main__':
    main()
