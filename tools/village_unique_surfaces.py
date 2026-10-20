"""One deterministic physical atlas for bounded close-view village props.

UV0 is the packed painting; UV1 preserves the original authoring metre UVs.
Charts never rotate or scale their two metre axes independently. Geometry,
anchors, animation pivots and special picture/fire/glass UVs remain untouched.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import sys

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SIZE = 4096
RESPONSE_SIZE = 2048
DESIGN = "village_unique_physical_surfaces_v1"
MANIFEST = ROOT / "Assets/Resources/Village/Textures/Surfaces/VillageUniqueSurfaces.json"
SOURCE = ROOT / "ArtSource/Village/Surfaces/Atlases"
ALBEDO = "VillageUniqueAlbedo"
RESPONSE = "VillageUniqueResponse"
EXCLUDED = {"Glass", "DullGlass", "Fire", "Lamp", "LodgePictures", "LodgeGroupPhotograph", "Paper", "WindSnow"}
WORKROOM_ARCHITECTURE = {"Floor", "PrivateFloor", "Ceiling", "FrontLining", "LeftLining", "RearLining", "PrivatePartition", "PrivateScreen"}


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf8")).hexdigest()


def family(row):
    if row.get("appearance"):
        return row["appearance"]
    surface, name = row["surface"], row.get("name", row["mesh"])
    if surface == "Canvas":
        if "Cork" in name:
            return "Bark"
        if "SkiBoots" in name or "EyesAndNose" in name:
            return "Rubber"
        if "Antlers" in name:
            return "Paper"  # Quiet bone-like microtexture, without a woven relief.
    if surface == "Timber":
        return "Timber"
    if surface in ("Canvas", "Cloth"):
        return "Cloth"
    if surface in ("LayeredStone", "BareStone", "Masonry"):
        return "Stone"
    if surface == "BarkAndDeadwood":
        return "Bark"
    if surface == "LighterMetal":
        return "Enamel" if "Cup" in name else "PaintedMetal"
    return surface


def selected(owner, row):
    if row["surface"] in EXCLUDED or row.get("name") == "LanternMantle":
        return False
    if owner == "Expansion":
        if row["kind"] in ("Lighter", "WoodLog"):
            return True
        return row["kind"] == "SkiLodge" and row["name"].startswith(("Lodge", "Stove", "Lantern", "Cellar", "EmptyRental"))
    if owner == "Workroom":
        return not row["name"].startswith("Shell") and row["name"] not in WORKROOM_ARCHITECTURE
    return owner in ("Life", "Narrative")


def rounded(values, digits=8):
    return np.round(np.asarray(values, dtype=np.float64), digits).tolist()


def describe(obj, row, owner):
    mesh = obj.data
    metric = mesh.uv_layers.get("VillageMetres") or mesh.uv_layers[0]
    faces = []
    projection = np.zeros((len(mesh.loops), 2), dtype=np.float64)
    for polygon in mesh.polygons:
        axis = int(np.argmax(np.abs(polygon.normal)))
        faces.append({"axis": axis, "sign": 1 if polygon.normal[axis] >= 0 else -1,
                      "loops": list(polygon.loop_indices), "vertices": list(polygon.vertices)})
        axes = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[axis]
        for index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[index].vertex_index].co
            projection[index] = tuple(metric.data[index].uv) if row.get("appearance") else (point[axes[0]], point[axes[1]])
    geometry = {"vertices": rounded([tuple(v.co) for v in mesh.vertices], 6),
                "faces": [face["vertices"] for face in faces]}
    uv = [tuple(loop.uv) for loop in metric.data]
    return {"mesh": row["mesh"], "owner": owner, "family": family(row),
            "tint": list(row["tint"]), "unique_surface": selected(owner, row),
            "metric_uv": uv, "projection_uv": projection.tolist(), "faces": faces, "geometry_signature": digest(geometry),
            "uv1_signature": digest(rounded(uv)), "part_name": row.get("name", row["mesh"])}


def layout(parts):
    from warehouse_master_projection import _pack_density
    charts = {}
    for part in sorted(parts, key=lambda p: p["mesh"]):
        if not part["unique_surface"]:
            continue
        metric = np.asarray(part["projection_uv"])
        for face in part["faces"]:
            key = (part["mesh"], face["axis"], face["sign"])
            points = metric[face["loops"]]
            row = charts.setdefault(key, {"key": key, "part": part,
                "low": points.min(axis=0), "high": points.max(axis=0), "faces": [], "weight": 1.0})
            row["low"] = np.minimum(row["low"], points.min(axis=0))
            row["high"] = np.maximum(row["high"], points.max(axis=0))
            row["faces"].append(face)
    for chart in charts.values():
        chart["extent"] = chart["high"] - chart["low"]
        if not np.isfinite(chart["extent"]).all() or min(chart["extent"]) <= 1e-10:
            raise RuntimeError("Degenerate village unique chart: " + str(chart["key"]))
    packed, density = _pack_density(list(charts.values()), SIZE, SIZE)
    occupied = np.zeros((SIZE, SIZE), dtype=np.uint16)
    rows = []
    for number, chart in enumerate(charts.values(), 1):
        x, y, scale = packed[chart["key"]]
        chart["origin"] = np.asarray((x, y))
        chart["scale"] = scale
        lo = np.floor(chart["origin"] - 3).astype(int)
        hi = np.ceil(chart["origin"] + chart["extent"] * scale + 3).astype(int)
        if np.any(lo < 0) or np.any(hi > SIZE) or np.any(hi <= lo):
            raise RuntimeError("Village unique chart escaped guarded atlas bounds")
        region = occupied[lo[1]:hi[1], lo[0]:hi[0]]
        if region.any():
            raise RuntimeError("Independent village unique chart gutters overlap")
        region[:] = number
        rows.append({"key": list(chart["key"]), "origin_px": chart["origin"].tolist(),
                     "origin_m": chart["low"].tolist(), "extent_m": chart["extent"].tolist(),
                     "texels_per_metre": scale})
    if density < 80:
        for chart in sorted(charts.values(), key=lambda c: float(np.prod(c["extent"])), reverse=True)[:25]:
            print("LARGE UNIQUE CHART", chart["key"], chart["extent"].tolist(), flush=True)
        raise RuntimeError(f"Village close-view surfaces fell below 80 texels/metre: {density:.3f}")
    return charts, {"chart_count": len(rows), "base_texels_per_metre": density,
                    "disjoint_chart_gutters": True, "metric_axis_scale_ratio": 1.0,
                    "pack_gutter_per_edge_px": 4, "charts": rows}


def packed_uv(part, charts):
    coordinates = np.asarray(part["projection_uv"])
    result = coordinates.copy()
    for face in part["faces"]:
        chart = charts[(part["mesh"], face["axis"], face["sign"])]
        loops = face["loops"]
        result[loops] = (np.asarray(chart["origin_px"]) +
            (coordinates[loops] - chart["origin_m"]) * chart["texels_per_metre"]) / SIZE
    if not np.isfinite(result).all() or result.min() < 0 or result.max() > 1:
        raise RuntimeError("Village UV0 escaped its atlas: " + part["mesh"])
    return np.asarray(result, dtype=np.float32)


def apply(objects, rows, owner):
    """Apply the registered atlas to a freshly generated package only."""
    contract = json.loads(MANIFEST.read_text(encoding="utf8"))
    if contract["design_id"] != DESIGN:
        raise RuntimeError("Unexpected village unique surface design")
    parts = {part["mesh"]: part for part in contract["parts"]}
    charts = {tuple(chart["key"]): chart for chart in contract["projection"]["charts"]}
    for obj, row in zip(objects, rows, strict=True):
        current = describe(obj, row, owner)
        registered = parts[current["mesh"]]
        for key in ("geometry_signature", "uv1_signature", "family", "unique_surface", "tint", "owner"):
            if current[key] != registered[key]:
                raise RuntimeError("Stale village unique source: " + current["mesh"] + "/" + key)
        row["unique_surface"] = current["unique_surface"]
        if not current["unique_surface"]:
            continue
        mesh = obj.data
        old = mesh.uv_layers[0]
        if len(mesh.uv_layers) != 1:
            raise RuntimeError("Unique village prop unexpectedly owns a special UV channel: " + obj.name)
        metric = mesh.uv_layers.new(name="VillageMetres")
        for index, coordinate in enumerate(current["metric_uv"]):
            metric.data[index].uv = coordinate
        old = mesh.uv_layers[0]
        old.name = "VillageSurfaceUV"
        coords = packed_uv(current, charts)
        for index, coordinate in enumerate(coords):
            old.data[index].uv = coordinate
        mesh.uv_layers.active_index = 0
        old.active_render = True
        obj["bp_surface_unique_uv"] = True
        row.update(surface_family=current["family"], uv0_signature=digest(rounded(coords)),
                   uv1_signature=current["uv1_signature"], uv_channel_count=2)
        if digest(rounded([tuple(loop.uv) for loop in metric.data])) != current["uv1_signature"]:
            raise RuntimeError("Village original metre UVs changed while applying the atlas")
        if row["uv0_signature"] != registered["uv0_signature"]:
            raise RuntimeError("Village UV packing changed while applying it")
        review_material(obj, row)


def review_material(obj, row):
    """The saved authoring source previews the same atlas the game consumes."""
    import bpy
    metallic = .72 if row["surface"] == "LighterMetal" else .2 if row["surface"] == "RustedIron" else 0.
    name = "VillageUniqueSurface_Review_" + str(round(metallic * 100))
    material = bpy.data.materials.get(name)
    if material is None:
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        material.diffuse_color = (1, 1, 1, 1)
        shader = material.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Metallic"].default_value = metallic
        albedo = material.node_tree.nodes.new("ShaderNodeTexImage")
        albedo.image = bpy.data.images.load(str(SOURCE / (ALBEDO + ".source.png")), check_existing=True)
        albedo.image.pack()
        albedo.extension = "EXTEND"
        material.node_tree.links.new(albedo.outputs["Color"], shader.inputs["Base Color"])
        response = material.node_tree.nodes.new("ShaderNodeTexImage")
        response.image = bpy.data.images.load(str(SOURCE / (RESPONSE + ".source.png")), check_existing=True)
        response.image.colorspace_settings.name = "Non-Color"
        response.image.pack()
        response.extension = "EXTEND"
        separate = material.node_tree.nodes.new("ShaderNodeSeparateColor")
        material.node_tree.links.new(response.outputs["Color"], separate.inputs["Color"])
        material.node_tree.links.new(separate.outputs["Blue"], shader.inputs["Roughness"])
        material.node_tree.nodes.active = albedo
    obj.data.materials.clear()
    obj.data.materials.append(material)


def module(name):
    path = ROOT / ("tools/build-village-" + name + "-3d-model.py")
    spec = importlib.util.spec_from_file_location("unique_village_" + name.replace("-", "_"), path)
    value = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = value
    spec.loader.exec_module(value)
    return value


def collect():
    """Rebuild source meshes without exports, runtime objects or scene edits."""
    import bpy
    parts = []
    source_signatures = {}
    expansion = module("expansion")
    authored = expansion.create_parts()
    objects, rows = expansion.build(authored)
    parts.extend(describe(obj, row, "Expansion") for obj, row in zip(objects, rows, strict=True))
    source_signatures["Expansion"] = digest(authored)
    workroom = module("workroom")
    data, objects = workroom.build_pack()
    parts.extend(describe(obj, row, "Workroom") for obj, row in zip(objects, data["parts"], strict=True))
    source_signatures["Workroom"] = data["build_signature"]
    for name, owner in (("life-props", "Life"), ("narrative", "Narrative")):
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        generator = module(name)
        props = generator.make_props()
        source_signatures[owner] = generator.signature(props)
        for prop in props:
            root = bpy.data.objects.new(prop["kind"], None)
            bpy.context.scene.collection.objects.link(root)
            for row in prop["parts"]:
                maker = generator.make_mesh if owner == "Life" else generator.p.make_mesh
                parts.append(describe(maker(row, root), row, owner))
    if len({part["mesh"] for part in parts}) != len(parts):
        raise RuntimeError("Village unique source mesh names are not globally unique")
    return parts, source_signatures


def save(data, path, linear):
    import bpy
    image = bpy.data.images.new(path.stem, width=data.shape[1], height=data.shape[0],
                                alpha=data.shape[2] == 4, float_buffer=False)
    image.colorspace_settings.name = "Non-Color" if linear else "sRGB"
    rgba = data if data.shape[2] == 4 else np.concatenate((data, np.ones((*data.shape[:2], 1), np.float32)), axis=-1)
    image.pixels.foreach_set(np.asarray(rgba, np.float32).reshape(-1))
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def bake(charts):
    from village_surface_authoring import sample
    SOURCE.mkdir(parents=True, exist_ok=True)
    records = {}
    for size, name, response in ((SIZE, ALBEDO, False), (RESPONSE_SIZE, RESPONSE, True)):
        output = np.zeros((size, size, 4 if response else 3), dtype=np.float32)
        if response:
            output[..., :2] = .5
            output[..., 2:] = 1
        ratio = size / SIZE
        for chart in charts.values():
            origin, scale = chart["origin"] * ratio, chart["scale"] * ratio
            low = np.maximum(0, np.floor(origin - 3 * ratio).astype(int))
            high = np.minimum(size, np.ceil(origin + chart["extent"] * scale + 3 * ratio).astype(int))
            yy, xx = np.mgrid[low[1]:high[1], low[0]:high[0]]
            u = chart["low"][0] + (xx + .5 - origin[0]) / scale
            v = chart["low"][1] + (yy + .5 - origin[1]) / scale
            part = chart["part"]
            color, physical = sample(part["family"], u, v, part_name=part["part_name"],
                                     tint=part["tint"], pixel_metres=1 / scale)
            value = physical if response else color
            if not np.isfinite(value).all() or np.min(value) < 0 or np.max(value) > 1:
                raise RuntimeError("Invalid village unique map samples: " + part["mesh"])
            output[low[1]:high[1], low[0]:high[0]] = value
        path = SOURCE / (name + ".source.png")
        records[name] = {"source": path.relative_to(ROOT).as_posix(), "size": size,
                         "sha256": save(output, path, response)}
        del output
    return records


def prepare(validate_only=False, layout_only=False):
    parts, geometry = collect()
    charts, projection = layout(parts)
    lookup = {tuple(row["key"]): row for row in projection["charts"]}
    rows = []
    for part in sorted(parts, key=lambda p: p["mesh"]):
        row = {key: value for key, value in part.items() if key not in ("metric_uv", "projection_uv", "faces")}
        if part["unique_surface"]:
            row["uv0_signature"] = digest(rounded(packed_uv(part, lookup)))
        rows.append(row)
    current = {"design_id": DESIGN, "version": 1, "albedo": "Village/Textures/Surfaces/" + ALBEDO,
               "response": "Village/Textures/Surfaces/" + RESPONSE, "albedo_size": SIZE,
               "response_size": RESPONSE_SIZE, "unique_part_count": sum(p["unique_surface"] for p in parts),
               "geometry_signatures": geometry, "source_geometry_signature": digest(geometry),
               "uv_signature": digest(rows), "projection": projection, "parts": rows,
               "channels": ["height_slope_u", "height_slope_v", "roughness", "cavity"],
               "uv1_contract": "exact original UV0 metre coordinates; picture/glass/fire/light channels untouched",
               "pack_gutters": "four colour pixels each edge; two response pixels; equal metric U/V scale",
               "mipmaps": True, "srgb_albedo": True, "srgb_response": False,
               "tint": "final source tint baked once; shared runtime material is white",
               "recipe_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    print(f"VILLAGE UNIQUE LAYOUT: {current['unique_part_count']} meshes; {projection['chart_count']} charts; {projection['base_texels_per_metre']:.2f} texels/metre", flush=True)
    if layout_only:
        return current
    from village_surface_authoring import provenance
    current["pigment_provenance"] = provenance()
    current["physical_recipe_sha256"] = hashlib.sha256((ROOT / "tools/village_surface_authoring.py").read_bytes()).hexdigest()
    if validate_only:
        recorded = json.loads(MANIFEST.read_text(encoding="utf8"))
        for key, value in current.items():
            if recorded.get(key) != value:
                raise RuntimeError("Published village unique surfaces differ: " + key)
        for row in recorded["maps"].values():
            if hashlib.sha256((ROOT / row["source"]).read_bytes()).hexdigest() != row["sha256"]:
                raise RuntimeError("Village unique source map checksum changed")
        validate_model_contracts(recorded)
        return recorded
    current["maps"] = bake(charts)
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps(current, indent=2) + "\n", encoding="utf8")
    return current


def validate_model_contracts(contract):
    registered = {row["mesh"]: row for row in contract["parts"]}
    for owner, path in (("Expansion", "Village/Expansion/VillageExpansion3D.json"),
                        ("Workroom", "VillageLife/VillageWorkroom3D.json"),
                        ("Life", "VillageLife/VillageLifeProps3D.json"),
                        ("Narrative", "VillageNarrative/VillageNarrative3D.json")):
        data = json.loads((ROOT / "Assets/Resources" / path).read_text(encoding="utf8"))
        if data.get("unique_surface_design") != DESIGN:
            raise RuntimeError("Model has not adopted its registered village atlas: " + owner)
        rows = data["parts"] if "parts" in data else [part for prop in data["props"] for part in prop["parts"]]
        for row in rows:
            expected = registered[row["mesh"]]
            if row.get("unique_surface") != expected["unique_surface"]:
                raise RuntimeError("Model surface ownership differs: " + row["mesh"])
            if expected["unique_surface"] and any(row.get(field) != expected[key] for field, key in
                    (("surface_family", "family"), ("uv0_signature", "uv0_signature"), ("uv1_signature", "uv1_signature"))):
                raise RuntimeError("Model packed/metre UV signature differs: " + row["mesh"])
