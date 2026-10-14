"""Warehouse-local UV authoring and unlit colour bake; no shared exporter changes.

UV0 is a unique family atlas for props, or metre tiling for structural modules.
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
SIZE = 1024
BAKE_MARGIN = 2
PACK_MARGIN = 4 / SIZE
VERSION = "1.1.0"
FAMILIES = {
    "WarehouseMechanismsAtlas": ("Gate", "FanHousing", "FanRotor", "LiftFrame", "LiftPlatform",
        "ChainDrum", "PusherFrame", "PusherHead", "TwistedFrame", "PipeStraight", "PipeElbow", "PipeEnd", "Pipes"),
    "WarehouseFurnishingsAtlas": ("MainDoor", "LockerProps", "BreakProps", "TechnicalProps", "Lamp"),
    "WarehouseCargoAtlas": ("Rack", "Cargo"),
}
TILED = ("Shell", "RoomShell", "Platform", "Stair", "Rail")
# Allocate the fixed atlas by actual viewing distance. Large ceiling/shaft
# members must not consume the texels needed by the reachable working parts.
UV_IMPORTANCE = {
    "ChainDrum": 4., "PusherHead": 3., "PusherFrame": 2., "LiftPlatform": 2., "Gate": 1.,
    "FanHousing": .65, "FanRotor": .65, "LiftFrame": .70, "TwistedFrame": .65,
    "Pipes": .70, "PipeStraight": .80, "PipeElbow": .80, "PipeEnd": .80,
}
ROLE_SOURCE = {"Concrete": "Concrete", "ConcreteDark": "Concrete", "Steel": "Paint",
    "WornSteel": "Steel", "Rust": "Rust", "Timber": "Timber", "Cardboard": "Cardboard", "Rubber": "Rubber"}
ROLE_TINT = {"Concrete": (.84, .88, .83), "ConcreteDark": (.57, .63, .58), "Steel": (.72, .77, .70),
    "WornSteel": (.88, .88, .82), "Rust": (.90, .83, .72), "Timber": (.80, .77, .68),
    "Cardboard": (.88, .82, .72), "Rubber": (.70, .73, .69), "Glass": (.22, .30, .28), "Lamp": (.38, .37, .31)}


def family_for(name):
    return next((family for family, names in FAMILIES.items() if name in names), None)


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


def unwrap(roots, items):
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    by_name = {root.name: root for root in roots}
    for root, item in zip(roots, items, strict=True):
        preserve_metre_uv(root)
        author_decal_uv(root, item)
    for family, names in FAMILIES.items():
        objects = [obj for name in names for obj in by_name[name].children_recursive if atlas_renderer(obj)]
        select_meshes(objects)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0, area_weight=0,
            correct_aspect=True, scale_to_bounds=False)
        bpy.ops.uv.average_islands_scale()
        # Apply one isotropic scale per model AFTER density normalization,
        # keeping aspect and metre UV1. The following shared pack normalizes
        # everything back into the same 1024 sheet with no memory increase.
        if family == "WarehouseMechanismsAtlas":
            import bmesh
            for name in names:
                importance = UV_IMPORTANCE.get(name, 1.)
                for obj in by_name[name].children_recursive:
                    if not atlas_renderer(obj):
                        continue
                    mesh = bmesh.from_edit_mesh(obj.data)
                    layer = mesh.loops.layers.uv.active
                    for face in mesh.faces:
                        for loop in face.loops:
                            loop[layer].uv *= importance
                    bmesh.update_edit_mesh(obj.data, loop_triangles=False, destructive=False)
        bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.pack_islands(udim_source="ACTIVE_UDIM", rotate=True, scale=True,
            margin_method="FRACTION", margin=PACK_MARGIN, shape_method="CONVEX")
        bpy.ops.object.mode_set(mode="OBJECT")
        # Keep border room for the bake's dilation too.
        for obj in objects:
            # Metric kit caps occasionally retain a zero-area polygon where a
            # swept profile meets itself. Blender correctly excludes it from
            # unwrapping; keep its unused loops in bounds without touching mesh.
            for polygon in obj.data.polygons:
                if polygon.area < 1e-12:
                    for index in polygon.loop_indices:
                        obj.data.uv_layers[0].data[index].uv = (.5, .5)
            for loop in obj.data.uv_layers[0].data:
                loop.uv = tuple(value * (1 - 2 * PACK_MARGIN) + PACK_MARGIN for value in loop.uv)
            obj["bp_atlas"] = family
        print("WAREHOUSE UV PACKED", family, len(objects), flush=True)
    return cache_uvs(roots)


def cache_uvs(roots):
    result = {}
    for root in roots:
        for obj in root.children_recursive:
            if obj.type != "MESH":
                continue
            result[mesh_key(root, obj)] = {
                "atlas": family_for(root.name) if atlas_renderer(obj) else None,
                "channels": [[[round(float(value), 8) for value in loop.uv] for loop in layer.data]
                    for layer in obj.data.uv_layers],
            }
    return result


def apply_uvs(root, cache):
    for obj in root.children_recursive:
        if obj.type != "MESH":
            continue
        record = cache[mesh_key(root, obj)]
        while len(obj.data.uv_layers) < 2:
            obj.data.uv_layers.new()
        for layer, values, name in zip(obj.data.uv_layers, record["channels"], ("WarehouseUV", "WarehouseMetres"), strict=True):
            layer.name = name
            layer.data.foreach_set("uv", np.asarray(values, dtype=np.float32).reshape(-1))
        obj.data.uv_layers.active_index = 0
        obj.data.uv_layers[0].active_render = True
        if record["atlas"]:
            obj["bp_atlas"] = record["atlas"]


def node(tree, kind, label):
    result = tree.nodes.new(kind)
    result.label = label
    return result


def math_node(tree, operation, a, b=None):
    result = node(tree, "ShaderNodeMath", operation)
    result.operation = operation
    for index, value in enumerate((a, b)):
        if value is None:
            continue
        if isinstance(value, (float, int)):
            result.inputs[index].default_value = value
        else:
            tree.links.new(value, result.inputs[index])
    return result.outputs[0]


def colour_multiply(tree, colour, factor):
    result = node(tree, "ShaderNodeMixRGB", "Material stain")
    result.blend_type = "MULTIPLY"
    result.inputs[0].default_value = 1
    tree.links.new(colour, result.inputs[1])
    if isinstance(factor, tuple):
        result.inputs[2].default_value = (*factor, 1)
    else:
        tree.links.new(factor, result.inputs[2])
    return result.outputs[0]


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


def bake_material(root, obj, target, sources):
    role = obj["bp_surface"]
    material = bpy.data.materials.new("WarehouseBake_" + mesh_key(root, obj))
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    output = node(tree, "ShaderNodeOutputMaterial", "Unlit albedo only")
    emission = node(tree, "ShaderNodeEmission", "No lighting, normals or AO baked")
    tree.links.new(emission.outputs[0], output.inputs["Surface"])
    coordinates = node(tree, "ShaderNodeTexCoord", "Coordinates fixed to module, including moving pivots")
    coordinates.object = root
    metric = node(tree, "ShaderNodeUVMap", "Preserved metre projection")
    metric.uv_map = "WarehouseMetres"
    texture = node(tree, "ShaderNodeTexImage", "Unchanged ImageGen base surface")
    name = ROLE_SOURCE.get(role)
    if name:
        texture.image = sources["Warehouse" + name]
        texture.extension = "REPEAT"
        tree.links.new(metric.outputs[0], texture.inputs["Vector"])
        colour = colour_multiply(tree, texture.outputs["Color"], ROLE_TINT[role])
    else:
        rgb = node(tree, "ShaderNodeRGB", "Solid lens or glass")
        rgb.outputs[0].default_value = (*ROLE_TINT[role], 1)
        colour = rgb.outputs[0]
    noise = node(tree, "ShaderNodeTexNoise", "Large nonrepeating corrosion and water patches")
    noise.inputs["Scale"].default_value = .73
    noise.inputs["Detail"].default_value = 2.5
    noise.inputs["Roughness"].default_value = .68
    tree.links.new(coordinates.outputs["Object"], noise.inputs["Vector"])
    macro = math_node(tree, "ADD", math_node(tree, "MULTIPLY", noise.outputs["Fac"], .40), .70)
    colour = colour_multiply(tree, colour, macro)
    height = node(tree, "ShaderNodeSeparateXYZ", "Moisture at module foot")
    tree.links.new(coordinates.outputs["Object"], height.inputs[0])
    low = min((root.matrix_world.inverted() @ obj.matrix_world @ vertex.co).z for obj in root.children_recursive
        if atlas_renderer(obj) for vertex in obj.data.vertices)
    damp = math_node(tree, "SUBTRACT", low + .55, height.outputs["Z"])
    damp = math_node(tree, "MULTIPLY", damp, 1.2)
    damp_node = node(tree, "ShaderNodeClamp", "Damp rises from lowest contact")
    tree.links.new(damp, damp_node.inputs[0])
    colour = colour_multiply(tree, colour, math_node(tree, "SUBTRACT", 1, math_node(tree, "MULTIPLY", damp_node.outputs[0], .28)))
    if role in ("Steel", "WornSteel", "Rust"):
        geometry = node(tree, "ShaderNodeNewGeometry", "Mesh curvature wear on bevels")
        edge = math_node(tree, "MULTIPLY", math_node(tree, "SUBTRACT", geometry.outputs["Pointiness"], .49), 7)
        clamp = node(tree, "ShaderNodeClamp", "Restrained edge wear")
        tree.links.new(edge, clamp.inputs[0])
        corrosion = node(tree, "ShaderNodeTexImage", "Oxide follows edges and damp patches")
        corrosion.image = sources["WarehouseRust"]
        tree.links.new(metric.outputs[0], corrosion.inputs["Vector"])
        blend = node(tree, "ShaderNodeMixRGB", "Edge-corrosion albedo")
        tree.links.new(math_node(tree, "MULTIPLY", clamp.outputs[0], .26), blend.inputs[0])
        tree.links.new(colour, blend.inputs[1])
        tree.links.new(corrosion.outputs["Color"], blend.inputs[2])
        colour = blend.outputs[0]
    tree.links.new(colour, emission.inputs["Color"])
    target_node = node(tree, "ShaderNodeTexImage", "Family atlas bake target")
    target_node.image = target
    tree.nodes.active = target_node
    obj.data.materials.clear()
    obj.data.materials.append(material)


def save_image(image, path):
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()


def bake_atlases(roots, directory):
    directory.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False
    scene.render.bake.use_clear = False
    scene.render.bake.margin = BAKE_MARGIN
    scene.render.bake.margin_type = "EXTEND"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1
    sources, source_records = source_images()
    atlas_records = []
    for family, names in FAMILIES.items():
        target = bpy.data.images.new(family, width=SIZE, height=SIZE, alpha=False, float_buffer=False)
        target.colorspace_settings.name = "sRGB"
        target.generated_color = (.045, .045, .035, 1)
        objects = []
        for root in roots:
            if root.name not in names:
                continue
            for obj in root.children_recursive:
                if atlas_renderer(obj):
                    bake_material(root, obj, target, sources)
                    objects.append(obj)
        select_meshes(objects)
        # Bake one temporary mesh per family rather than rebuilding Cycles for
        # each small part. Materials retain their original module coordinates.
        # Published meshes are independently rebuilt from unchanged geometry.
        bpy.ops.object.join()
        print("WAREHOUSE EMIT BAKE", family, flush=True)
        bpy.ops.object.bake(type="EMIT")
        path = directory / (family + ".source.png")
        save_image(target, path)
        atlas_records.append({"name": family, "source": "ArtSource/CombatWarehouse/Textures/Atlases/" + path.name,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    return source_records, atlas_records


def uv_signature(cache):
    return hashlib.sha256(json.dumps(cache, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def raster_family(roots, names):
    coverage = np.zeros((SIZE, SIZE), dtype=np.uint16)
    checker = np.zeros((SIZE, SIZE, 4), dtype=np.float32)
    checker[:, :, :3] = (.024, .028, .026)
    checker[:, :, 3] = 1
    densities, stretch, records, model_records = [], [], [], []
    for root in roots:
        if root.name not in names:
            continue
        model_density = []
        model_uv_area = model_surface_area = 0
        for obj in root.children_recursive:
            if not atlas_renderer(obj):
                continue
            mesh = obj.data
            mesh.calc_loop_triangles()
            uv0, uv1 = mesh.uv_layers[0].data, mesh.uv_layers[1].data
            area_uv = area_surface = 0
            for triangle in mesh.loop_triangles:
                points = np.array([mesh.vertices[index].co[:] for index in triangle.vertices], dtype=np.float64)
                uvs = np.array([uv0[index].uv[:] for index in triangle.loops], dtype=np.float64)
                metres = np.array([uv1[index].uv[:] for index in triangle.loops], dtype=np.float64)
                world_area = np.linalg.norm(np.cross(points[1] - points[0], points[2] - points[0])) * .5
                if world_area < 1e-12:
                    continue
                if not np.isfinite(uvs).all() or np.any(uvs < PACK_MARGIN - 1e-6) or np.any(uvs > 1 - PACK_MARGIN + 1e-6):
                    raise RuntimeError("Invalid atlas UV: " + mesh_key(root, obj) + " UV=" + str(uvs.tolist())
                        + " points=" + str(points.tolist()) + " area=" + str(world_area)
                        + " polygon=" + str(triangle.polygon_index))
                uv_area = abs(np.linalg.det((uvs[1:] - uvs[0]).T)) * .5
                if world_area > 1e-12 and uv_area < 1e-13:
                    raise RuntimeError("Degenerate atlas triangle: " + mesh_key(root, obj))
                area_uv += uv_area
                area_surface += world_area
                if world_area > 1e-10:
                    density = math.sqrt(uv_area / world_area) * SIZE
                    densities.append(density)
                    model_density.append(density)
                    first, second = points[1:] - points[0]
                    basis_x = first / np.linalg.norm(first)
                    basis_y = np.cross(np.cross(first, second), first)
                    basis_y /= np.linalg.norm(basis_y)
                    planar = np.array([[np.dot(first, basis_x), np.dot(second, basis_x)],
                                       [np.dot(first, basis_y), np.dot(second, basis_y)]])
                    singular = np.linalg.svd((uvs[1:] - uvs[0]).T @ np.linalg.inv(planar), compute_uv=False)
                    stretch.append(float(singular[0] / singular[1]))
                pixels = uvs * SIZE
                low = np.maximum(0, np.floor(pixels.min(axis=0)).astype(int))
                high = np.minimum(SIZE - 1, np.ceil(pixels.max(axis=0)).astype(int))
                xx, yy = np.meshgrid(np.arange(low[0], high[0] + 1) + .5, np.arange(low[1], high[1] + 1) + .5)
                transform = np.linalg.inv((pixels[1:] - pixels[0]).T)
                bary = np.stack((xx - pixels[0, 0], yy - pixels[0, 1]), axis=-1) @ transform.T
                inside = (bary[:, :, 0] > 1e-6) & (bary[:, :, 1] > 1e-6) & (bary.sum(axis=-1) < 1 - 1e-6)
                coverage[low[1]:high[1] + 1, low[0]:high[0] + 1] += inside
                metric = metres[0] + bary @ (metres[1:] - metres[0])
                pattern = (np.floor(metric[:, :, 0] * 5) + np.floor(metric[:, :, 1] * 5)).astype(int) % 2
                colour = np.where(pattern[:, :, None] == 0, (.12, .52, .38), (.78, .80, .72))
                checker[low[1]:high[1] + 1, low[0]:high[0] + 1, :3][inside] = colour[inside]
            records.append({"mesh": mesh_key(root, obj), "surface_area_m2": round(area_surface, 6),
                "uv_area": round(area_uv, 6), "triangles": len(mesh.loop_triangles)})
            model_uv_area += area_uv
            model_surface_area += area_surface
        model_records.append({"model": root.name, "uv_importance": UV_IMPORTANCE.get(root.name, 1.),
            "surface_area_m2": round(model_surface_area, 6), "uv_area": round(model_uv_area, 6),
            "texels_per_metre_area_equivalent": round(math.sqrt(model_uv_area / model_surface_area) * SIZE, 3),
            "texels_per_metre_min": round(min(model_density), 3),
            "texels_per_metre_median": round(float(np.median(model_density)), 3),
            "texels_per_metre_max": round(max(model_density), 3)})
    overlaps = int(np.count_nonzero(coverage > 1))
    if overlaps:
        raise RuntimeError(f"Unique warehouse UV overlap: {overlaps} pixels")
    if max(stretch) > 3.5:
        raise RuntimeError(f"Warehouse atlas projection stretched: {max(stretch):.3f}")
    summary = {"uv_area": round(sum(record["uv_area"] for record in records), 6),
        "occupied_texels": int(np.count_nonzero(coverage)), "overlap_texels": overlaps,
        "texels_per_metre_median": round(float(np.median(densities)), 3),
        "triangle_stretch_max": round(max(stretch), 4), "triangle_stretch_p95": round(float(np.quantile(stretch, .95)), 4),
        "model_density": model_records, "meshes": records}
    return summary, coverage, checker


def validate_layout(roots, cache, directory=None):
    families = []
    for family, names in FAMILIES.items():
        summary, coverage, checker = raster_family(roots, names)
        summary.update(name=family, models=list(names))
        families.append(summary)
        if directory:
            image = bpy.data.images.new(family + " UV checker", width=SIZE, height=SIZE)
            image.pixels.foreach_set(checker.reshape(-1))
            save_image(image, directory / (family + ".checker.png"))
            layout = np.ones_like(checker)
            layout[:, :, :3] = np.where(coverage[:, :, None] > 0, (.24, .43, .34), (.025, .025, .025))
            image.pixels.foreach_set(layout.reshape(-1))
            save_image(image, directory / (family + ".layout.png"))
            bpy.data.images.remove(image)
        print("WAREHOUSE UNIQUE UV OK", family, summary["occupied_texels"], summary["triangle_stretch_max"], flush=True)
        if family == "WarehouseMechanismsAtlas":
            print("WAREHOUSE PRIORITY DENSITY", json.dumps(summary["model_density"]), flush=True)
    return {"version": VERSION, "size": SIZE, "UV0": "unique nonoverlapping prop family atlas / tiled structural metres / authored cutout cells",
        "UV1": "object-anchored metre face projection", "uv_signature": uv_signature(cache),
        "bake": "Cycles EMIT only; unchanged ImageGen albedo sources with module-anchored macro, damp and mesh-curvature wear",
        "bake_margin_px": BAKE_MARGIN, "pack_margin_px": round(PACK_MARGIN * SIZE),
        "uv_importance": UV_IMPORTANCE,
        "tiled_models": list(TILED), "families": families}


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


def source_materials(roots, directory):
    materials = {}
    for family in FAMILIES:
        image = bpy.data.images.load(str(directory / (family + ".source.png")), check_existing=True)
        material = bpy.data.materials.new(family)
        material.use_nodes = True
        texture = material.node_tree.nodes.new("ShaderNodeTexImage")
        texture.image = image
        material.node_tree.links.new(texture.outputs["Color"], material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"])
        materials[family] = material
    for root in roots:
        for obj in root.children_recursive:
            if obj.type != "MESH":
                continue
            if obj.name.startswith("Collision_"):
                obj.hide_render = True
                obj.hide_set(True)
            family = family_for(root.name)
            if family and atlas_renderer(obj):
                obj.data.materials.clear()
                obj.data.materials.append(materials[family])
