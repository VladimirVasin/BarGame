#!/usr/bin/env python3
"""Retain unchanged seated/head/skull FBXs only after their ordinary rebuild proofs.

Run through run-blender.py with all three output directories staged. Only source
provenance may change: the regular generators compare complete rebuilt contracts,
and this refresh additionally compares retained FBX skin and rest-bone frames.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys

import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
sys.path.insert(0, str(TOOLS))
from asset_pipeline import workspace_temporary_directory


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def module(name):
    spec = importlib.util.spec_from_file_location(name, TOOLS / (name + ".py"))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def capture(names=None):
    """Source-weight records use world metres; bone tails are FBX-generated."""
    bpy.context.view_layer.update()
    objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and
               (names is None or obj.name in names)]
    arm = next((modifier.object for obj in objects for modifier in obj.modifiers
                if modifier.type == "ARMATURE"), None)
    bones = {}
    if arm:
        for bone in arm.data.bones:
            matrix = arm.matrix_world @ bone.matrix_local
            bones[bone.name] = {"parent": bone.parent.name if bone.parent else None,
                "head": tuple(matrix.translation),
                "axes": [tuple(matrix.to_3x3().col[axis].normalized()) for axis in range(3)]}
    meshes = {}
    for obj in objects:
        groups = {group.index: group.name for group in obj.vertex_groups}
        meshes[obj.name] = {"vertices": [tuple(obj.matrix_world @ vertex.co) for vertex in obj.data.vertices],
            "weights": [{groups[group.group]: group.weight for group in vertex.groups if group.weight > 1e-7}
                        for vertex in obj.data.vertices],
            "shapes": {key.name: [tuple(obj.matrix_world @ point.co) for point in key.data]
                       for key in list(obj.data.shape_keys.key_blocks)[1:]} if obj.data.shape_keys else {}}
    return {"bones": bones, "meshes": meshes}


def verify_export(path, expected):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    actual = capture()
    if set(actual["meshes"]) != set(expected["meshes"]) or set(actual["bones"]) != set(expected["bones"]):
        raise RuntimeError("Retained FBX mesh/bone palette changed: " + str(path))
    for name, wanted in expected["bones"].items():
        found = actual["bones"][name]
        if found["parent"] != wanted["parent"] or (Vector(found["head"]) - Vector(wanted["head"])).length > 2e-5 or \
                any((Vector(a) - Vector(b)).length > 2e-4 for a, b in zip(found["axes"], wanted["axes"])):
            raise RuntimeError("Retained FBX rest-bone frame changed: " + path.name + "/" + name)
    for name, wanted in expected["meshes"].items():
        found = actual["meshes"][name]
        if len(found["vertices"]) != len(wanted["vertices"]) or len(found["shapes"]) != len(wanted["shapes"]):
            raise RuntimeError("Retained FBX vertex/shape palette changed: " + path.name + "/" + name)
        shapes = {}
        for shape, points in wanted["shapes"].items():
            matches = [key for key in found["shapes"] if key == shape or key.endswith("." + shape)]
            if len(matches) != 1:
                raise RuntimeError("Retained FBX corrective changed: " + path.name + "/" + name + "/" + shape)
            shapes[shape] = found["shapes"][matches[0]]
        tree = KDTree(len(found["vertices"]))
        for index, point in enumerate(found["vertices"]):
            tree.insert(Vector(point), index)
        tree.balance()
        used = set()
        for index, point in enumerate(wanted["vertices"]):
            skin = wanted["weights"][index]
            candidates = []
            for _, target, distance in tree.find_range(Vector(point), 2e-5):
                row = found["weights"][target]
                if target not in used and row.keys() == skin.keys() and \
                        all(abs(row[bone] - weight) < 2e-5 for bone, weight in skin.items()) and \
                        all((Vector(points[index]) - Vector(shapes[shape][target])).length < 2e-5
                            for shape, points in wanted["shapes"].items()):
                    candidates.append((distance, target))
            if not candidates:
                raise RuntimeError("Retained FBX skin/shape point changed: " + path.name + "/" + name + "/" + str(index))
            used.add(min(candidates)[1])
    print("RETAINED FBX SKIN AND REST RIG OK", path.name, len(actual["meshes"]), flush=True)


def ordinary_validation(builder, arguments):
    previous = sys.argv
    try:
        sys.argv = [str(builder.__file__), "--", "--validate-only", *arguments]
        builder.main()
    finally:
        sys.argv = previous


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seated-output-dir", type=Path, required=True)
    parser.add_argument("--seated-source-dir", type=Path, required=True)
    parser.add_argument("--gore-output-dir", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    seated = module("build-home-toilet-seated-3d-model")
    gore = module("build-combat-gore-3d-model")
    skull = module("build-combat-skull-3d-model")
    seated_path = ROOT / "Assets/Resources/HomeToiletSeated/HomeToiletSeated.json"
    seated_source_path = ROOT / "ArtSource/HomeToiletSeated/home-toilet-seated-3d-model.json"
    gore_path = ROOT / "Assets/Resources/CombatGore/CombatGore3D.json"
    skull_path = ROOT / "Assets/Resources/CombatGore/CombatSkull3D.json"
    originals = {path: path.read_bytes() for path in (seated_path, seated_source_path, gore_path, skull_path)}
    if json.loads(originals[seated_path]) != json.loads(originals[seated_source_path]):
        raise RuntimeError("Seated resource/source manifests already disagree")
    sources = [seated.HERO_SOURCE, *(ROOT / source for source in gore.SOURCES.values())]
    source_hashes = {path: digest(path) for path in sources}
    seated_candidate = json.loads(originals[seated_path])
    seated_candidate["source_hero_sha256"] = source_hashes[seated.HERO_SOURCE]
    payload = deepcopy(seated_candidate)
    del payload["signature"]
    payload["report"].pop("fbx_round_trip_vertices_shapes_metres_anchors")
    seated_candidate["signature"] = hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()
    gore_candidate = json.loads(originals[gore_path])
    for kind, source in gore.SOURCES.items():
        gore_candidate["sources"][kind]["sha256"] = source_hashes[ROOT / source]
    gore_bytes = (json.dumps(gore_candidate, indent=2) + "\n").encode()
    skull_candidate = json.loads(originals[skull_path])
    for kind, source in gore.SOURCES.items():
        skull_candidate["models"][kind]["source_sha256"] = source_hashes[ROOT / source]
        skull_candidate["models"][kind]["head_contract_sha256"] = hashlib.sha256(gore_bytes).hexdigest()

    with workspace_temporary_directory("derived-provenance-") as proof:
        proof = Path(proof)
        seated_out = proof / "Assets/Resources/HomeToiletSeated"
        gore_out = proof / "Assets/Resources/CombatGore"
        seated_out.mkdir(parents=True)
        gore_out.mkdir(parents=True)
        for source in gore.SOURCES.values():
            target = proof / source
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / source, target)
        shutil.copytree(seated_path.parent / "Models", seated_out / "Models")
        for name in set(gore_candidate["files"]) | set(skull_candidate["files"]):
            shutil.copy2(gore_path.parent / name, gore_out / name)
        (seated_out / seated_path.name).write_text(json.dumps(seated_candidate, indent=2) + "\n", encoding="utf-8")
        (gore_out / gore_path.name).write_bytes(gore_bytes)
        (gore_out / skull_path.name).write_text(json.dumps(skull_candidate, indent=2) + "\n", encoding="utf-8")

        seated_records = {}
        original_round_trip = seated.round_trip
        def round_trip(expected):
            seated_records.update(capture(set(expected["SeatedLowerBody"]["meshes"])))
            original_round_trip(expected)
        seated.round_trip = round_trip
        ordinary_validation(seated, ["--resource-dir", str(seated_out)])
        verify_export(seated_out / "Models/SeatedLowerBody.fbx", seated_records)

        def validate_head(builder):
            original_build = builder.build
            records = {}
            def build(kind, out, publish=False):
                result = original_build(kind, out, publish)
                records[kind] = capture()
                return result
            builder.build = build
            ordinary_validation(builder, ["--output-dir", str(gore_out)])
            prefix = "Head" if builder is gore else "Skull"
            for kind, record in records.items():
                verify_export(gore_out / (prefix + kind + ".fbx"), record)
        validate_head(gore)
        skull.ROOT = skull.body.ROOT = proof
        validate_head(skull)

        if any(digest(path) != expected for path, expected in source_hashes.items()) or \
                any(path.read_bytes() != expected for path, expected in originals.items()):
            raise RuntimeError("Sources or published manifests changed during the retention proof")
        for directory in (args.seated_output_dir, args.seated_source_dir, args.gore_output_dir):
            directory.mkdir(parents=True, exist_ok=True)
        shutil.copy2(seated_out / seated_path.name, args.seated_output_dir / seated_path.name)
        shutil.copy2(seated_out / seated_path.name, args.seated_source_dir / "home-toilet-seated-3d-model.json")
        shutil.copy2(gore_out / gore_path.name, args.gore_output_dir / gore_path.name)
        shutil.copy2(gore_out / skull_path.name, args.gore_output_dir / skull_path.name)
        print("DERIVED PROVENANCE REFRESH PROVED: only source hashes/signatures changed; five FBXs retained", flush=True)


if __name__ == "__main__":
    main()
