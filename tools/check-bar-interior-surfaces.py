"""Read the exported pub FBX and prove its registered node UV0/UV1 contract.

Run through tools/run-blender.py in validation-only mode. No asset is changed.
"""
from __future__ import annotations

import json
from pathlib import Path
import sys

import bpy

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from bar_interior_unique_surfaces import digest, rounded


def main():
    contract = json.loads((ROOT / "ArtSource/Bar/bar-interior-surfaces.json").read_text(encoding="utf8"))
    registered = {part["mesh"]: part for part in contract["parts"] if part["unique_surface"]}
    bpy.ops.import_scene.fbx(filepath=str(ROOT / "Assets/Bar/Models/BarInterior3D.fbx"), use_anim=False)
    nodes = {obj.name: obj for obj in bpy.context.scene.objects if obj.type == "MESH"}
    for name, part in registered.items():
        if name not in nodes:
            raise RuntimeError("Exported bar lost a registered object/node: " + name)
        mesh = nodes[name].data
        if len(mesh.uv_layers) != 2:
            raise RuntimeError("Exported bar requires unique UV0 and metre UV1: " + name)
        for index, key in ((0, "uv0_signature"), (1, "uv1_signature")):
            actual = digest(rounded([tuple(loop.uv) for loop in mesh.uv_layers[index].data]))
            if actual != part[key]:
                raise RuntimeError(f"Exported bar changed registered UV{index}: {name}")
    print(f"BAR EXPORTED SURFACES OK: {len(registered)} exact object/node names; unique UV0 and original metre UV1 retained.", flush=True)


if __name__ == "__main__":
    main()
