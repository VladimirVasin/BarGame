"""Extract the default male rest surfaces from the original Hero V2 source."""

import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from default_npc_body import SOURCE_PARTS


def load_parts(path):
    with bpy.data.libraries.load(str(path.resolve()), link=False) as (available, loaded):
        if set(SOURCE_PARTS) - set(available.objects):
            raise ValueError(f"Missing neutral anatomy in {path}")
        loaded.objects = list(SOURCE_PARTS)
    return loaded.objects


def rest_surfaces(objects):
    return [(
        tuple(obj.location),
        {bone.name: tuple(map(tuple, bone.matrix_local)) for bone in obj.find_armature().data.bones},
        [tuple(vertex.co) for vertex in obj.data.vertices],
        [tuple(polygon.vertices) for polygon in obj.data.polygons],
        [tuple((obj.vertex_groups[g.group].name, g.weight) for g in vertex.groups)
         for vertex in obj.data.vertices],
    ) for obj in objects]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--source-dir", required=True, type=Path)
    parser.add_argument("--worker-manifest", type=Path)
    parser.add_argument("--model-dir", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    if (args.model_dir is None) != (args.worker_manifest is None):
        parser.error("--model-dir and --worker-manifest are required together")
    objects = load_parts(args.source)
    rig = objects[0].find_armature()
    if rig is None or any(obj.find_armature() != rig for obj in objects):
        raise ValueError("The donor surfaces must share their bind rig")
    original_surfaces = rest_surfaces(objects)
    rig.animation_data_clear()
    for obj in objects:
        obj.animation_data_clear()
    args.source_dir.mkdir(parents=True, exist_ok=True)
    destination = args.source_dir / "DefaultMaleAnatomy.blend"
    bpy.data.libraries.write(str(destination.resolve()), set(objects), fake_user=True, compress=True)
    if rest_surfaces(load_parts(destination)) != original_surfaces:
        raise ValueError("NPC donor changed the source geometry, skin or bind frames")
    if args.worker_manifest:
        manifest = json.loads(args.worker_manifest.read_text(encoding="utf-8"))
        anatomy = manifest["body_shape"]
        if anatomy["source_sha256"] != hashlib.sha256(args.source.read_bytes()).hexdigest():
            raise ValueError("The existing worker manifest does not certify this original source")
        anatomy["source_path"] = "ArtSource/VillageLife/DefaultMaleAnatomy.blend"
        anatomy["source_sha256"] = hashlib.sha256(destination.read_bytes()).hexdigest()
        manifest.pop("signature")
        encoded = json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode()
        manifest["signature"] = hashlib.sha256(encoded).hexdigest()
        args.model_dir.mkdir(parents=True, exist_ok=True)
        (args.model_dir / args.worker_manifest.name).write_text(json.dumps(manifest, indent=2) + "\n",
                                                                encoding="utf-8")
    print("NPC ANATOMY DONOR: reloaded vertices, topology, weights and bind frames match the original", flush=True)


if __name__ == "__main__":
    main()
