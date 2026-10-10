#!/usr/bin/env python3
"""Compare published NPC FBX parts to their evaluated source and manifest."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
FAMILIES = (
    ("Assets/Pedestrians", "ArtSource/Pedestrians/Blender"),
    ("Assets/Vehicles/Drivers/Models", "ArtSource/Vehicles/Drivers/Blender"),
    ("Assets/Bar/Bartender/Models", "ArtSource/Bar/Bartender/Blender"),
    ("Assets/Supermarket/Cashier/Models", "ArtSource/Supermarket/Cashier/Blender"),
    ("Assets/Resources/VillageLife", "ArtSource/VillageLife"),
    ("Assets/Resources/City/Cannery/Receiver", "ArtSource/City/CanneryReceiver"),
)


def triangles(mesh):
    return sum(len(polygon.vertices) - 2 for polygon in mesh.polygons)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", action="append", default=[], help="Optional exact model stem")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    errors = []
    checked = 0
    for asset_folder, source_folder in FAMILIES:
        for path in sorted((ROOT / asset_folder).rglob("*.json")):
            payload = json.loads(path.read_text(encoding="utf-8"))
            if not payload.get("joint_surfaces") or not path.with_suffix(".fbx").is_file():
                continue
            if args.model and path.stem not in args.model:
                continue
            checked += 1
            names = {part["name"] for part in payload["parts"]}
            bpy.ops.wm.open_mainfile(filepath=str(ROOT / source_folder / (path.stem + ".blend")))
            depsgraph = bpy.context.evaluated_depsgraph_get()
            source = {}
            for name in names:
                obj = bpy.data.objects[name]
                evaluated = obj.evaluated_get(depsgraph)
                mesh = evaluated.to_mesh()
                source[name] = triangles(mesh)
                evaluated.to_mesh_clear()
            if sum(source.values()) != payload["triangle_count"]:
                errors.append(f"{path.stem}: evaluated source differs from manifest triangles")
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.fbx(filepath=str(path.with_suffix(".fbx")), use_anim=False)
            exported = {obj.name: triangles(obj.data) for obj in bpy.data.objects if obj.type == "MESH"}
            if set(exported) != names:
                errors.append(f"{path.stem}: exported part names differ: {sorted(set(exported) ^ names)}")
            differences = [(name, source[name], exported.get(name))
                           for name in sorted(names) if source[name] != exported.get(name)]
            if differences:
                errors.append(f"{path.stem}: evaluated/exported triangles differ {differences}")
            for seam in payload["joint_surfaces"]["seams"]:
                key = seam.get("corrective_shape")
                for name in set(seam["renderers"]):
                    obj = bpy.data.objects.get(name)
                    if key and (obj is None or not obj.data.shape_keys or key not in obj.data.shape_keys.key_blocks):
                        errors.append(f"{path.stem}: {name} exported corrective missing {key}")
            print(f"NPC EXPORT {path.stem}: source={sum(source.values())}, FBX={sum(exported.values())}, manifest={payload['triangle_count']}", flush=True)
    if checked == 0:
        raise RuntimeError("No matching NPC models were checked")
    if errors:
        raise RuntimeError("NPC export geometry failed:\n" + "\n".join(errors))
    print(f"NPC export geometry passed: {checked} models, exact parts, evaluated triangles and corrective keys", flush=True)


if __name__ == "__main__":
    main()
