"""Guarded City-only pub facade charts; geometry and original UVs stay exact.

This module owns layout and model authoring only. The City texture publisher
bakes the registered physical rectangles from its pigment/response sampler.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "ArtSource/City/Surfaces/city-bar-facade-surfaces.json"
BLEND = ROOT / "ArtSource/City/Surfaces/Blender/CityBarFacade3D.blend"
SIZE, RESPONSE_SIZE = 4096, 2048
DESIGN = "city_bar_facade_unique_surfaces_v1"
ALBEDO = "City/Surfaces/CityBarFacadeAlbedo"
RESPONSE = "City/Surfaces/CityBarFacadeResponse"
SHEETS = {
    "ExteriorBrick": ("Brick", "Bar/Textures/BarExteriorBrickAlbedo", 1.498),
    "ExteriorPlaster": ("Plaster", "Bar/Textures/BarExteriorPlasterAlbedo", 1.4065),
    "DarkWood": ("PaintedWood", "Bar/Textures/BarDarkWoodAlbedo", 1.4495),
    "CityRoof": ("Roof", "Textures/CityRoofAlbedo", 1 / .62),
}
_MEANS = {}


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf8")).hexdigest()


def rounded(values, digits=8):
    return np.round(np.asarray(values, dtype=np.float64), digits).tolist()


def selected(part):
    return not part.emissive and not part.role.startswith("exterior_window") and part.role != "sign_part"


def profile(part):
    """Preserve the facade's delivered fixed colour, with compensation applied once."""
    from village_surface_authoring import load_rgb, linear, gamma
    if part.tint.get("field") or part.tint.get("lerp_field") or part.tint.get("lerp_t", 0):
        raise RuntimeError("A variable facade tint cannot be baked into the fixed City painting: " + part.name)
    tint = np.asarray(part.tint["rgb"], dtype=np.float32) * part.tint.get("scale", 1.)
    family, resource, compensation = SHEETS.get(part.sheet, ("PaintedMetal", "", 1.))
    if not resource:
        return family, rounded(np.clip(tint, 0, 1))
    path = ROOT / ("Assets/Resources/" + resource + ".png")
    if resource not in _MEANS:
        _MEANS[resource] = linear(load_rgb(path)).mean(axis=(0, 1))
    displayed = linear(np.clip(tint * compensation, 0, 1)) * _MEANS[resource]
    return family, rounded(gamma(np.clip(displayed, 0, 1)))


def describe(part):
    mesh = part.obj.data
    if len(mesh.uv_layers) != 1:
        raise RuntimeError("Fresh facade source needs exactly one original UV channel: " + part.name)
    metric = mesh.uv_layers[0]
    faces, projection = [], np.zeros((len(mesh.loops), 2), dtype=np.float64)
    parents = list(range(len(mesh.vertices)))

    def component(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index

    for polygon in mesh.polygons:
        first = component(polygon.vertices[0])
        for vertex in polygon.vertices[1:]:
            other = component(vertex)
            parents[max(first, other)] = min(first, other)
            first = min(first, other)
    for polygon in mesh.polygons:
        axis = int(np.argmax(np.abs(polygon.normal)))
        axes = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[axis]
        faces.append({"axis": axis, "sign": 1 if polygon.normal[axis] >= 0 else -1,
                      "loops": list(polygon.loop_indices), "vertices": list(polygon.vertices),
                      "component": component(polygon.vertices[0]), "physical_axes": list(axes)})
        for index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[index].vertex_index].co
            projection[index] = point[axes[0]], point[axes[1]]
    geometry = {"vertices": rounded([tuple(vertex.co) for vertex in mesh.vertices], 6),
                "faces": [face["vertices"] for face in faces],
                "matrix": rounded([list(row) for row in part.obj.matrix_local], 6)}
    family, tint = profile(part) if selected(part) else ("", [])
    uv = [tuple(loop.uv) for loop in metric.data]
    return {"mesh": part.name, "source_name": part.name, "role": part.role, "sheet": part.sheet,
            "family": family, "tint": tint, "unique_surface": selected(part),
            "metric_uv": uv, "projection_uv": projection.tolist(), "faces": faces,
            "geometry_signature": digest(geometry), "uv1_signature": digest(rounded(uv))}


def chart_key(part, face):
    return part["mesh"], face["axis"], face["sign"], face["component"]


def layout(parts):
    from warehouse_master_projection import _pack_density
    charts = {}
    for part in sorted(parts, key=lambda row: row["mesh"]):
        if not part["unique_surface"]:
            continue
        metric = np.asarray(part["projection_uv"])
        for face in part["faces"]:
            key = chart_key(part, face); points = metric[face["loops"]]
            chart = charts.setdefault(key, {"key": key, "part": part, "low": points.min(axis=0),
                "high": points.max(axis=0), "physical_axes": face["physical_axes"], "weight": 1.})
            chart["low"] = np.minimum(chart["low"], points.min(axis=0))
            chart["high"] = np.maximum(chart["high"], points.max(axis=0))
    for chart in charts.values():
        chart["extent"] = chart["high"] - chart["low"]
        if not np.isfinite(chart["extent"]).all() or min(chart["extent"]) <= 1e-10:
            raise RuntimeError("Degenerate unique facade chart: " + str(chart["key"]))
    packed, density = _pack_density(list(charts.values()), SIZE, SIZE)
    occupied = np.zeros((SIZE, SIZE), dtype=np.uint8)
    rows = []
    for key, chart in charts.items():
        x, y, scale = packed[key]
        lo = np.floor(np.asarray((x, y)) - 3).astype(int)
        hi = np.ceil(np.asarray((x, y)) + chart["extent"] * scale + 3).astype(int)
        if np.any(lo < 0) or np.any(hi > SIZE) or np.any(hi <= lo):
            raise RuntimeError("Unique facade chart escaped its guarded atlas")
        region = occupied[lo[1]:hi[1], lo[0]:hi[0]]
        if region.any():
            raise RuntimeError("Unique facade chart gutters overlap")
        region[:] = 1
        rows.append({"key": list(key), "mesh": chart["part"]["mesh"], "family": chart["part"]["family"],
            "tint": chart["part"]["tint"], "origin_px": [x, y], "origin_m": chart["low"].tolist(),
            "extent_m": chart["extent"].tolist(), "physical_axes": chart["physical_axes"],
            "texels_per_metre": scale, "guarded_min_px": lo.tolist(), "guarded_max_px": hi.tolist()})
    if density < 80:
        raise RuntimeError(f"City bar facade fell below 80 texels/metre: {density:.3f}")
    return {"chart_count": len(rows), "base_texels_per_metre": density,
            "disjoint_chart_gutters": True, "metric_axis_scale_ratio": 1.,
            "pack_gutter_per_edge_px": 4, "charts": rows}


def packed_uv(part, charts):
    coordinates = np.asarray(part["projection_uv"])
    result = coordinates.copy()
    for face in part["faces"]:
        chart = charts[chart_key(part, face)]
        loops = face["loops"]
        result[loops] = (np.asarray(chart["origin_px"]) + (coordinates[loops] - chart["origin_m"]) *
                         chart["texels_per_metre"]) / SIZE
    if not np.isfinite(result).all() or result.min() < 0 or result.max() > 1:
        raise RuntimeError("City bar facade UV escaped its atlas: " + part["mesh"])
    return np.asarray(result, dtype=np.float32)


def prepare(asset, *, write=False, apply=True):
    parts = [describe(part) for part in asset.parts]
    projection = layout(parts)
    charts = {tuple(row["key"]): row for row in projection["charts"]}
    rows = []
    for part in parts:
        row = {key: value for key, value in part.items() if key not in ("metric_uv", "projection_uv", "faces")}
        row["uv_channel_count"] = 2 if part["unique_surface"] else 1
        row["uv0_signature"] = digest(rounded(packed_uv(part, charts))) if part["unique_surface"] else part["uv1_signature"]
        rows.append(row)
    current = {"design_id": DESIGN, "albedo": ALBEDO, "response": RESPONSE,
        "albedo_size": SIZE, "response_size": RESPONSE_SIZE, "full_color": True,
        "unique_part_count": sum(part["unique_surface"] for part in parts),
        "geometry_signature": digest([(row["mesh"], row["geometry_signature"]) for row in rows]),
        "anchors_signature": digest({name: rounded(anchor.matrix_local) for name, anchor in sorted(asset.anchors.items())}),
        "projection": projection, "parts": rows, "uv_signature": digest(rows),
        "tint": "fixed delivered facade colour baked once; runtime white",
        "uv1_contract": "exact original UV0; geometry/anchors/collision/pivots/glass/emission/sign untouched",
        "channels": ["signed_physical_slope_u", "signed_physical_slope_v", "roughness", "cavity"],
        "recipe_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    encoded = json.dumps(current, indent=2) + "\n"
    if write:
        MANIFEST.parent.mkdir(parents=True, exist_ok=True); MANIFEST.write_text(encoded, encoding="utf8")
    else:
        recorded = json.loads(MANIFEST.read_text(encoding="utf8"))
        for key, value in current.items():
            if recorded.get(key) != value:
                raise RuntimeError("Stale City bar facade surface contract: " + key)
    if apply:
        for part, source, row in zip(asset.parts, parts, rows, strict=True):
            if not source["unique_surface"]:
                continue
            mesh = part.obj.data
            original = mesh.uv_layers.new(name="CityBarOriginalUV")
            for index, coordinate in enumerate(source["metric_uv"]): original.data[index].uv = coordinate
            coords = packed_uv(source, charts)
            for index, coordinate in enumerate(coords): mesh.uv_layers[0].data[index].uv = coordinate
            mesh.uv_layers[0].name = "CityBarFacadeUV"
            mesh.uv_layers.active_index = 0; mesh.uv_layers[0].active_render = True
            if digest(rounded([tuple(loop.uv) for loop in original.data])) != source["uv1_signature"]:
                raise RuntimeError("City facade original UVs changed: " + part.name)
            geometry = {"vertices": rounded([tuple(vertex.co) for vertex in mesh.vertices], 6),
                "faces": [list(face.vertices) for face in mesh.polygons],
                "matrix": rounded([list(matrix_row) for matrix_row in part.obj.matrix_local], 6)}
            if digest(geometry) != source["geometry_signature"]:
                raise RuntimeError("City facade geometry changed while packing: " + part.name)
            part.obj["bp_city_surface_unique_uv"] = True
        asset.city_surface_contract = {key: current[key] for key in ("design_id", "albedo", "response", "albedo_size",
            "response_size", "unique_part_count", "geometry_signature", "anchors_signature", "uv_signature")}
    print(f"CITY BAR UNIQUE SURFACES OK: {current['unique_part_count']} parts; "
          f"{projection['chart_count']} guarded charts; {projection['base_texels_per_metre']:.2f} texels/metre", flush=True)
    return current


def review_materials(asset):
    """Pack the runtime painting into the City-only editable Blender source."""
    import bpy
    albedo_path = ROOT / ("Assets/Resources/" + ALBEDO + ".png")
    response_path = ROOT / ("Assets/Resources/" + RESPONSE + ".png")
    if not albedo_path.is_file() or not response_path.is_file():
        raise RuntimeError("Publish City facade albedo/response before the final model export")
    albedo = bpy.data.images.load(str(albedo_path), check_existing=True)
    albedo.colorspace_settings.name = "sRGB"; albedo.pack()
    response = bpy.data.images.load(str(response_path), check_existing=True)
    response.colorspace_settings.name = "Non-Color"; response.pack()
    materials = {}
    for part in asset.parts:
        if not selected(part): continue
        metallic = .38 if part.role == "exterior_metal" else 0.
        if metallic not in materials:
            material = bpy.data.materials.new("PREVIEW_CityBarFacade_" + ("Metal" if metallic else "Matte"))
            material.use_nodes = True
            nodes = material.node_tree.nodes; links = material.node_tree.links
            shader = nodes.get("Principled BSDF"); shader.inputs["Metallic"].default_value = metallic
            pigment = nodes.new("ShaderNodeTexImage"); pigment.image = albedo; pigment.extension = "EXTEND"
            physical = nodes.new("ShaderNodeTexImage"); physical.image = response; physical.extension = "EXTEND"
            separate = nodes.new("ShaderNodeSeparateColor")
            links.new(pigment.outputs["Color"], shader.inputs["Base Color"])
            links.new(physical.outputs["Color"], separate.inputs["Color"])
            links.new(separate.outputs["Blue"], shader.inputs["Roughness"])
            materials[metallic] = material
        part.obj.data.materials.clear(); part.obj.data.materials.append(materials[metallic])


def verify_export(path, asset):
    """Read actual FBX UV loops after export; imported source geometry stays metric."""
    import bpy
    expected = {part.obj.name: {
        "vertices": np.asarray([tuple(vertex.co) for vertex in part.obj.data.vertices]),
        "faces": [list(face.vertices) for face in part.obj.data.polygons],
        "uvs": [np.asarray([tuple(loop.uv) for loop in layer.data]) for layer in part.obj.data.uv_layers]
    } for part in asset.parts}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    found = set()
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH": continue
        if obj.name not in expected: raise RuntimeError("Unexpected exported facade mesh: " + obj.name)
        target = expected[obj.name]; found.add(obj.name)
        vertices = np.asarray([tuple(vertex.co) for vertex in obj.data.vertices])
        if vertices.shape != target["vertices"].shape or not np.allclose(vertices, target["vertices"], atol=2e-6, rtol=0):
            raise RuntimeError("FBX changed facade geometry: " + obj.name)
        if [list(face.vertices) for face in obj.data.polygons] != target["faces"]:
            raise RuntimeError("FBX changed facade faces: " + obj.name)
        if len(obj.data.uv_layers) != len(target["uvs"]): raise RuntimeError("FBX lost facade UV channels: " + obj.name)
        for layer, coordinates in zip(obj.data.uv_layers, target["uvs"], strict=True):
            actual = np.asarray([tuple(loop.uv) for loop in layer.data])
            if actual.shape != coordinates.shape or not np.allclose(actual, coordinates, atol=2e-6, rtol=0):
                raise RuntimeError("FBX changed facade UV loops: " + obj.name)
    if found != set(expected): raise RuntimeError("FBX lost a registered facade part")
    print("CITY BAR FACADE FBX ROUND TRIP OK: actual geometry/faces/UV0/UV1 retained.", flush=True)
