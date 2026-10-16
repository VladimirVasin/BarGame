"""Warehouse-local UV authoring and unlit colour bake; no shared exporter changes.

UV0 is one unique master atlas with offline-authored placement variants.
UV1 always retains the original object-anchored metre projection. Collider
meshes and authored cutout decals are deliberately excluded from atlas packing.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SIZE = 4096
BAKE_MARGIN = 2
PACK_MARGIN = 4 / SIZE
VERSION = "2.2.0"
FAMILIES = {"WarehouseMasterAtlas": ()}  # Layout is owned by warehouse_master_atlas.
TILED = ()
UV_IMPORTANCE = {}
ROLE_SOURCE = {"Concrete": "Concrete", "ConcreteDark": "Concrete", "Steel": "Paint",
    "WornSteel": "Steel", "Rust": "Rust", "Timber": "Timber", "Cardboard": "Cardboard", "Rubber": "Rubber"}
ROLE_TINT = {"Concrete": (.84, .88, .83), "ConcreteDark": (.57, .63, .58), "Steel": (.72, .77, .70),
    "WornSteel": (.88, .88, .82), "Rust": (.90, .83, .72), "Timber": (.80, .77, .68),
    "Cardboard": (.88, .82, .72), "Rubber": (.70, .73, .69), "Glass": (.22, .30, .28), "Lamp": (.38, .37, .31),
    "SignalRed": (.75, .035, .018), "SafetyYellow": (.82, .64, .075), "NumberPlate": (.75, .73, .62),
    "LockSteel": (.44, .46, .45)}


def number_source_manifest():
    directory = ROOT / "ArtSource/CombatWarehouse/Textures/RoomNumbers"
    source = json.loads((directory / "RoomNumbers.json").read_text(encoding="utf8"))
    if source["font"] != "Assets/Resources/Fonts/Roboto-Regular.ttf" or \
            hashlib.sha256((ROOT / source["font"]).read_bytes()).hexdigest() != source["font_sha256"]:
        raise RuntimeError("Room plaques must use the unchanged project Roboto typeface")
    if [record["number"] for record in source["numbers"]] != list(range(1, 10)):
        raise RuntimeError("Room plaques require each numeral 1-9 once")
    for record in source["numbers"]:
        path = directory / f"RoomNumber{record['number']}.png"
        if record["file"] != path.name or hashlib.sha256(path.read_bytes()).hexdigest() != record["sha256"]:
            raise RuntimeError("Room numeral source changed: " + path.name)
    return source


def number_source_image(root):
    number_source_manifest()
    number = int(root.name.removeprefix("RoomSign"))
    path = ROOT / f"ArtSource/CombatWarehouse/Textures/RoomNumbers/RoomNumber{number}.png"
    image = bpy.data.images.load(str(path), check_existing=True)
    image.colorspace_settings.name = "sRGB"
    return image


def family_for(name):
    return "WarehouseMasterAtlas"


def mesh_key(root, obj):
    return root.name + "/" + obj.name.split(".")[0]


def renderer(obj):
    return obj.type == "MESH" and not obj.name.startswith("Collision_")


def atlas_renderer(obj):
    return renderer(obj) and obj.get("bp_surface") != "WearDecal"


def select_meshes(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def preserve_metre_uv(root):
    for obj in root.children_recursive:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        primary = mesh.uv_layers[0]
        primary.name = "WarehouseUV"
        metric = mesh.uv_layers.new(name="WarehouseMetres")
        coordinates = np.empty(len(primary.data) * 2, dtype=np.float32)
        primary.data.foreach_get("uv", coordinates)
        metric.data.foreach_set("uv", coordinates)
        mesh.uv_layers.active_index = 0
        primary.active_render = True


def author_decal_uv(root, item):
    cells = getattr(item, "wear_cells", [])
    if not cells:
        return
    obj = next(child for child in root.children_recursive if child.type == "MESH" and child.get("bp_surface") == "WearDecal")
    # UV polygons follow authored vertex coordinates, so winding may differ
    # without flipping a stain. Six texels inside each clamp-safe cell.
    inset = 6 / 512
    for polygon, cell in zip(obj.data.polygons, cells, strict=True):
        left, bottom = (0 if cell in (0, 2) else .5), (.5 if cell in (0, 1) else 0)
        for loop_index in polygon.loop_indices:
            local_vertex = obj.data.loops[loop_index].vertex_index % 4
            corner = ((0, 0), (1, 0), (1, 1), (0, 1))[local_vertex]
            obj.data.uv_layers[0].data[loop_index].uv = (left + inset + corner[0] * (.5 - 2 * inset),
                bottom + inset + corner[1] * (.5 - 2 * inset))


def source_images():
    provenance = json.loads((ROOT / "ArtSource/CombatWarehouse/Textures/generation.json").read_text(encoding="utf8"))
    images, sources = {}, []
    for record in provenance["images"]:
        if record["name"] not in {"Warehouse" + name for name in ROLE_SOURCE.values()}:
            continue
        path = ROOT / record["source"]
        checksum = hashlib.sha256(path.read_bytes()).hexdigest()
        if checksum != record["sha256"]:
            raise RuntimeError("Changed warehouse base source: " + record["name"])
        image = bpy.data.images.load(str(path), check_existing=True)
        image.colorspace_settings.name = "sRGB"
        images[record["name"]] = image
        sources.append({"name": record["name"], "sha256": checksum})
    return images, sources


def verify_fbx_uvs(model_dir, cache, models):
    for name in models:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(model_dir / (name + ".fbx")), use_anim=False)
        for obj in bpy.context.scene.objects:
            if obj.type != "MESH":
                continue
            key = name + "/" + obj.name.split(".")[0]
            expected = cache[key]
            if len(obj.data.uv_layers) != 2:
                raise RuntimeError("Export lost warehouse UV channels: " + key)
            for layer, target in zip(obj.data.uv_layers, expected["channels"], strict=True):
                actual = np.array([loop.uv[:] for loop in layer.data])
                if actual.shape != (len(target), 2) or not np.allclose(actual, target, atol=2e-6, rtol=0):
                    raise RuntimeError("FBX changed warehouse UV loops: " + key)
        print("WAREHOUSE FBX UV ROUND TRIP OK", name, flush=True)



# Shared helpers above are also used by the independent fish/chain generators.
from warehouse_master_atlas import (unwrap, cache_uvs, apply_uvs, uv_signature,
    bake_atlases, validate_layout, source_materials, export_variants)
