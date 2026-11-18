#!/usr/bin/env python3
"""Refresh source-dependent damage meshes for one character at a time.

Run through tools/run-blender.py. By default the current NPC head is retained.
Use --npc-head for an authored change to its geometry or UVs. --hero refreshes
the corresponding hero assets.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from asset_pipeline import publish_files, workspace_temporary_directory
KIND = "Npc"
SOURCE = "Assets/Resources/VillageLife/StationWorker.fbx"
GORE = ROOT / "Assets/Resources/CombatGore"
BLOOD = ROOT / "Assets/Resources/CombatBlood"


def load_generator(name, kind=KIND, source=SOURCE):
    path = Path(__file__).with_name("build-combat-" + name + "-3d-model.py")
    spec = importlib.util.spec_from_file_location("default_npc_damage_" + name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    sources = module.body.SOURCES if name == "skull" else module.SOURCES
    if sources.get(kind) != source:
        raise RuntimeError(f"The scoped damage refresh lost the {kind} source: {name}")
    return module


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_manifest(folder, name):
    path = folder / name
    result = json.loads(path.read_text(encoding="utf-8"))
    for filename, expected in result["files"].items():
        # Only explicitly listed, local published assets may be read or replaced.
        if Path(filename).name != filename:
            raise RuntimeError("A damage manifest contains a nonlocal filename: " + filename)
        if digest(folder / filename) != expected:
            raise RuntimeError("A published damage asset differs from its manifest: " + filename)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    character = parser.add_mutually_exclusive_group()
    character.add_argument("--hero", action="store_true")
    character.add_argument("--npc-head", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.hero:
        refresh_hero(args.validate_only)
        return
    body = load_generator("body")
    blood = load_generator("blood")
    gore = load_generator("gore")
    previous_body = read_manifest(GORE, "CombatBody3D.json")
    previous_blood = read_manifest(BLOOD, "CombatBlood3D.json")
    previous_gore = read_manifest(GORE, "CombatGore3D.json")
    if previous_body["sources"].get(KIND) != SOURCE or any(
        manifest["sources"][KIND]["path"] != SOURCE for manifest in (previous_blood, previous_gore)
    ):
        raise RuntimeError("The published NPC derivatives no longer use the ordinary-worker source.")

    print("DEFAULT NPC DAMAGE: checking head semantics", flush=True)
    head = gore.build(KIND, GORE, publish=False)
    if head != gore.build(KIND, GORE, publish=False):
        raise RuntimeError("NPC head semantics differ across independent rebuilds.")
    head_changed = head != previous_gore["models"][KIND]
    if head_changed and not args.npc_head:
        raise RuntimeError("NPC head geometry/UV/skin changed unexpectedly; the scoped refresh cannot replace HeadNpc.fbx.")

    # Generators reconstruct from a clean Blender scene each time. Stage exports
    # until both independent semantic reconstructions agree, before replacing assets.
    with workspace_temporary_directory("default-npc-damage-") as staging:
        stage = Path(staging)
        print("DEFAULT NPC DAMAGE: rebuilding body partition", flush=True)
        model = body.build(KIND, stage, publish=not args.validate_only)
        if model != body.build(KIND, stage, publish=False):
            raise RuntimeError("NPC body derivative differs across independent rebuilds.")
        print("DEFAULT NPC DAMAGE: rebuilding wound surfaces", flush=True)
        patches, wounds = blood.wounds(KIND, stage, publish=not args.validate_only)
        if (patches, wounds) != blood.wounds(KIND, stage, publish=False):
            raise RuntimeError("NPC wounds differ across independent rebuilds.")

        next_body = copy.deepcopy(previous_body)
        next_blood = copy.deepcopy(previous_blood)
        next_gore = copy.deepcopy(previous_gore)
        source_hash = digest(ROOT / SOURCE)
        next_body["models"][KIND] = model
        next_body["source_tessellation_tolerances"] = [
            entry for entry in previous_body["source_tessellation_tolerances"] if entry["kind"] != KIND
        ] + [dict(kind=KIND, **entry) for entry in model["source_tessellation"]]
        next_blood["patches"][KIND] = patches
        next_blood["models"]["WoundsNpc"] = wounds
        next_blood["sources"][KIND]["sha256"] = source_hash
        next_gore["sources"][KIND]["sha256"] = source_hash
        publication = []
        manifests = [(GORE, "CombatBody3D.json", next_body), (BLOOD, "CombatBlood3D.json", next_blood),
                     (GORE, "CombatGore3D.json", next_gore)]
        if head_changed and not args.validate_only:
            if head != gore.build(KIND, stage, publish=True):
                raise RuntimeError("NPC head export differs from the measured source.")
            next_gore["models"][KIND] = head
            next_gore["sources"][KIND]["renderers"] = [entry["name"] for entry in head["source_renderers"]]
            next_gore["files"]["HeadNpc.fbx"] = digest(stage / "HeadNpc.fbx")
            publication.append((stage / "HeadNpc.fbx", GORE / "HeadNpc.fbx"))
        if args.npc_head:
            skull = load_generator("skull")
            previous_skull = read_manifest(GORE, "CombatSkull3D.json")
            next_skull = copy.deepcopy(previous_skull)
            skull_model = skull.build(KIND, stage, head_contract=next_gore)
            if skull_model["meshes"] != previous_skull["models"][KIND]["meshes"] and not args.validate_only:
                if skull_model != skull.build(KIND, stage, publish=True, head_contract=next_gore):
                    raise RuntimeError("NPC head dependent export differs from the measured source.")
                next_skull["files"]["SkullNpc.fbx"] = digest(stage / "SkullNpc.fbx")
                publication.append((stage / "SkullNpc.fbx", GORE / "SkullNpc.fbx"))
            manifest_bytes = (json.dumps(next_gore, indent=2) + "\n").encode("utf-8")
            head_hash = hashlib.sha256(manifest_bytes).hexdigest()
            next_skull["models"][KIND] = skull_model
            for entry in next_skull["models"].values():
                entry["head_contract_sha256"] = head_hash
            manifests.append((GORE, "CombatSkull3D.json", next_skull))
        if args.validate_only:
            if (head_changed or next_body != previous_body or next_blood != previous_blood or next_gore != previous_gore
                    or args.npc_head and next_skull != previous_skull):
                raise RuntimeError("NPC damage source/semantic measurements changed; run this tool without --validate-only.")
        else:
            next_body["files"]["BodyNpc.fbx"] = digest(stage / "BodyNpc.fbx")
            next_blood["files"]["WoundsNpc.fbx"] = digest(stage / "WoundsNpc.fbx")
            publication.extend(((stage / "BodyNpc.fbx", GORE / "BodyNpc.fbx"),
                                (stage / "WoundsNpc.fbx", BLOOD / "WoundsNpc.fbx")))
            for folder, filename, manifest in manifests:
                candidate = stage / filename
                candidate.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
                publication.append((candidate, folder / filename))
            publish_files(publication)
    print("DEFAULT NPC DAMAGE SOURCE AND INDEPENDENT SEMANTIC REBUILDS OK", flush=True)


def refresh_hero(validate_only):
    kind = "Hero"
    source = "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx"
    body, blood, gore, skull = (load_generator(name, kind, source)
                                for name in ("body", "blood", "gore", "skull"))
    keys = ((GORE, "CombatBody3D.json"), (BLOOD, "CombatBlood3D.json"),
            (GORE, "CombatGore3D.json"), (GORE, "CombatSkull3D.json"))
    previous = [read_manifest(folder, name) for folder, name in keys]
    next_body, next_blood, next_head, next_skull = copy.deepcopy(previous)
    source_hash = digest(ROOT / source)
    with workspace_temporary_directory("hero-damage-") as stage:
        publish = not validate_only
        print("HERO DERIVATIVES: body and wound surfaces", flush=True)
        model = body.build(kind, stage, publish=publish)
        patches, wounds = blood.wounds(kind, stage, publish=publish)
        if model != body.build(kind, stage) or (patches, wounds) != blood.wounds(kind, stage, publish=False):
            raise RuntimeError("Hero body and wound surfaces differ across rebuilds")
        print("HERO DERIVATIVES: head and skull", flush=True)
        head = gore.build(kind, stage, publish=publish)
        if head != gore.build(kind, stage):
            raise RuntimeError("Hero head differs across rebuilds")
        next_body["models"][kind] = model
        next_body["source_tessellation_tolerances"] = [
            dict(kind=model_kind, **entry) for model_kind, row in next_body["models"].items()
            for entry in row["source_tessellation"]]
        next_blood["patches"][kind] = patches
        next_blood["models"]["WoundsHero"] = wounds
        next_blood["sources"][kind]["sha256"] = source_hash
        next_head["models"][kind] = head
        next_head["sources"][kind] = {"path": source, "sha256": source_hash,
                                      "renderers": [row["name"] for row in head["source_renderers"]]}
        if publish:
            next_head["files"]["HeadHero.fbx"] = digest(stage / "HeadHero.fbx")
        head_path = stage / "CombatGore3D.json"
        head_path.write_text(json.dumps(next_head, indent=2) + "\n", encoding="utf-8")
        skull_model = skull.build(kind, stage, publish, head_contract=next_head)
        if skull_model != skull.build(kind, stage, head_contract=next_head):
            raise RuntimeError("Hero skull differs across rebuilds")
        skull_model["head_contract_sha256"] = digest(head_path)
        next_skull["models"][kind] = skull_model
        # The NPC's skull references the shared head manifest. Its own
        # inputs were retained, so only the shared manifest fingerprint changes.
        next_skull["models"]["Npc"]["head_contract_sha256"] = digest(head_path)
        updated = [next_body, next_blood, next_head, next_skull]
        if validate_only:
            if previous != updated:
                raise RuntimeError("Hero damage assets are stale")
        else:
            files = []
            for manifest, folder, filename in ((next_body, GORE, "BodyHero.fbx"),
                                               (next_blood, BLOOD, "WoundsHero.fbx"),
                                               (next_head, GORE, "HeadHero.fbx"),
                                               (next_skull, GORE, "SkullHero.fbx")):
                manifest["files"][filename] = digest(stage / filename)
                files.append((stage / filename, folder / filename))
            for (folder, name), manifest in zip(keys, updated):
                candidate = stage / name
                candidate.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
                files.append((candidate, folder / name))
            publish_files(files)
    print("HERO DAMAGE DERIVATIVES MATCH THE NEW MODEL", flush=True)


if __name__ == "__main__":
    main()
