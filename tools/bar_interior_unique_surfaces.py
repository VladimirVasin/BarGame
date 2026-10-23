"""Compact pub's guarded unique UV0, metre UV1 and registered physical painting.

The existing pigment sheets remain source material. Runtime samples one unique
colour and one aligned slopes/roughness/cavity sheet; no live painting or mesh
editing is needed. Glass, emission, paper and the cafe stool donor stay intact.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import re
import subprocess
import sys
import uuid

sys.dont_write_bytecode = True
import bar_washroom_surfaces as washroom_surfaces

import numpy as np
try:
    from PIL import Image
except ImportError:  # Blender keeps geometry in-process; the pinned Python publishes bitmap assets.
    Image = None

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "ArtSource/Bar/bar-interior-surfaces.json"
OUTPUT = ROOT / "Assets/Resources/Bar/Textures"
SIZE, RESPONSE_SIZE = 4096, 2048
ALBEDO = "Bar/Textures/BarInteriorAlbedo"
RESPONSE = "Bar/Textures/BarInteriorResponse"
DESIGN = "compact_bar_unique_surfaces_v1"
MESH_PREFIX = "BarInteriorUnique_"
GPU_BUDGET_BYTES = 28 * 1024 * 1024
SHEETS = {
    "WornPlank": (1.5, .92, .24), "Wallpaper": (1.8, .96, .10),
    "DarkWood": (1.1, .88, .20), "WornLeather": (.9, .94, .14),
    "CeilingPlaster": (2.4, .975, .17), "PolishedWood": (.75, .66, .16),
    "AgedBrass": (.42, .58, .11), "PubCarpet": (1.15, .985, .11),
    "WornFabric": (.68, .98, .10), "PaintedMetal": (.82, .80, .12),
    "Ceramic": (.42, .52, .07),
}
STOOL_ROLES = {"stool_leg", "hero_stool_legs", "hero_stool_footring", "stool_seat", "hero_stool_seat"}


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf8")).hexdigest()


def rounded(value):
    return np.round(np.asarray(value, dtype=np.float64), 8).tolist()


def selected(part):
    return not part.emissive and part.sheet in SHEETS and part.role not in STOOL_ROLES


def describe(part):
    mesh = part.obj.data
    if len(mesh.uv_layers) != 1:
        raise RuntimeError("Fresh bar mesh must retain one source UV: " + part.name)
    uv = [tuple(loop.uv) for loop in mesh.uv_layers[0].data]
    parents = list(range(len(mesh.vertices)))

    def component(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index

    for polygon in mesh.polygons:
        for vertex in polygon.vertices[1:]:
            first, other = component(polygon.vertices[0]), component(vertex)
            parents[max(first, other)] = min(first, other)
    faces, projection = [], np.zeros((len(mesh.loops), 2), dtype=np.float64)
    for polygon in mesh.polygons:
        axis = int(np.argmax(np.abs(polygon.normal)))
        axes = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[axis]
        faces.append({"axis": axis, "sign": 1 if polygon.normal[axis] >= 0 else -1,
            "loops": list(polygon.loop_indices), "component": component(polygon.vertices[0]), "axes": axes})
        for index in polygon.loop_indices:
            vertex = mesh.vertices[mesh.loops[index].vertex_index].co
            projection[index] = vertex[axes[0]], vertex[axes[1]]
    return {"mesh": part.name, "sheet": part.sheet, "unique_surface": selected(part),
        "metric_uv": uv, "projection_uv": projection, "faces": faces,
        "geometry_signature": digest({"vertices": rounded([tuple(v.co) for v in mesh.vertices]),
            "faces": [list(p.vertices) for p in mesh.polygons], "matrix": rounded(part.obj.matrix_local)}),
        "uv1_signature": digest(rounded(uv))}


def chart_key(part, face):
    return part["mesh"], face["axis"], face["sign"], face["component"]


def layout(parts):
    from warehouse_master_projection import _pack_density
    charts = {}
    for part in sorted(parts, key=lambda row: row["mesh"]):
        if not part["unique_surface"]:
            continue
        for face in part["faces"]:
            key = chart_key(part, face)
            points = part["projection_uv"][face["loops"]]
            chart = charts.setdefault(key, {"key": key, "part": part, "low": points.min(axis=0),
                "high": points.max(axis=0), "weight": 1., "axes": face["axes"]})
            chart["low"] = np.minimum(chart["low"], points.min(axis=0))
            chart["high"] = np.maximum(chart["high"], points.max(axis=0))
    for chart in charts.values():
        chart["extent"] = chart["high"] - chart["low"]
        if not np.isfinite(chart["extent"]).all() or min(chart["extent"]) <= 1e-10:
            raise RuntimeError("Degenerate bar chart: " + str(chart["key"]))
    packed, density = _pack_density(list(charts.values()), SIZE, SIZE)
    if density < 64:
        raise RuntimeError(f"Bar unique chart density below 64 pixels/metre: {density:.3f}")
    rows, occupied = [], np.zeros((SIZE, SIZE), dtype=np.uint8)
    for key, chart in charts.items():
        x, y, scale = packed[key]
        lo = np.floor(np.asarray((x, y)) - 3).astype(int)
        hi = np.ceil(np.asarray((x, y)) + chart["extent"] * scale + 3).astype(int)
        if np.any(lo < 0) or np.any(hi > SIZE) or np.any(hi <= lo):
            raise RuntimeError("Bar chart escaped guarded atlas")
        region = occupied[lo[1]:hi[1], lo[0]:hi[0]]
        if region.any():
            raise RuntimeError("Bar chart gutters overlap")
        region[:] = 1
        rows.append({"key": list(key), "mesh": chart["part"]["mesh"], "sheet": chart["part"]["sheet"],
            "origin_px": [x, y], "origin_m": chart["low"].tolist(), "extent_m": chart["extent"].tolist(),
            "physical_axes": list(chart["axes"]), "texels_per_metre": scale,
            "guarded_min_px": lo.tolist(), "guarded_max_px": hi.tolist()})
    return {"chart_count": len(rows), "base_texels_per_metre": density,
        "pack_gutter_per_edge_px": 4, "metric_axis_scale_ratio": 1., "charts": rows}


def packed_uv(part, charts):
    result = part["projection_uv"].copy()
    for face in part["faces"]:
        chart = charts[chart_key(part, face)]
        loops = face["loops"]
        result[loops] = (chart["origin_px"] + (result[loops] - chart["origin_m"]) * chart["texels_per_metre"]) / SIZE
    if not np.isfinite(result).all() or result.min() < 0 or result.max() > 1:
        raise RuntimeError("Bar UV escaped atlas: " + part["mesh"])
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
        "albedo_size": SIZE, "response_size": RESPONSE_SIZE,
        "unique_part_count": sum(part["unique_surface"] for part in parts),
        "projection": projection, "parts": rows, "uv_signature": digest(rows),
        "tint": "original district tint and compensation applied once at runtime",
        "uv1_contract": "exact original metre UV0; geometry/anchors/collision/special sheets preserved",
        "channels": ["signed_physical_slope_u", "signed_physical_slope_v", "roughness", "cavity"],
        "recipe_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "washroom_recipe_sha256": hashlib.sha256(Path(washroom_surfaces.__file__).read_bytes()).hexdigest(),
        "source_sha256": {sheet: hashlib.sha256((OUTPUT / f"Bar{sheet}Albedo.png").read_bytes()).hexdigest()
            for sheet in sorted({part["sheet"] for part in parts if part["unique_surface"]})}}
    encoded = json.dumps(current, indent=2) + "\n"
    if write:
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_text(encoded, encoding="utf8")
    elif json.loads(MANIFEST.read_text(encoding="utf8")) != current:
        raise RuntimeError("Stale bar interior surface contract")
    if Image is None:
        subprocess.run(["python", str(Path(__file__)), *([] if write else ["--validate-only"])], cwd=ROOT, check=True)
    else:
        publish(current, validate_only=not write)
    if apply:
        for part, source in zip(asset.parts, parts, strict=True):
            if not source["unique_surface"]:
                continue
            mesh = part.obj.data
            original = mesh.uv_layers.new(name="BarOriginalMetres")
            for index, uv in enumerate(source["metric_uv"]):
                original.data[index].uv = uv
            for index, uv in enumerate(packed_uv(source, charts)):
                mesh.uv_layers[0].data[index].uv = uv
            mesh.uv_layers[0].name = "BarInteriorPainting"
            mesh.uv_layers.active_index = 0
            mesh.uv_layers[0].active_render = True
            mesh.name = MESH_PREFIX + part.name
            if digest(rounded([tuple(loop.uv) for loop in original.data])) != source["uv1_signature"]:
                raise RuntimeError("Bar metre UV1 changed: " + part.name)
        asset.interior_surface_contract = {key: current[key] for key in (
            "design_id", "albedo", "response", "albedo_size", "response_size", "unique_part_count", "uv_signature")}
    print(f"BAR UNIQUE SURFACES OK: {current['unique_part_count']} parts; {projection['chart_count']} guarded charts; "
          f"{projection['base_texels_per_metre']:.2f} pixels/metre", flush=True)
    return current


def fields(sheet, u, v, chart):
    """Independent metre-scale physical fields: pigment brightness is never relief."""
    _, base_roughness, amplitude = SHEETS[sheet]
    phase = int(hashlib.sha256(chart["mesh"].encode("utf8")).hexdigest()[:6], 16) / 0xffffff * math.tau
    broad = np.sin(u * 1.7 + phase) * np.sin(v * 2.3 - phase)
    handling = np.zeros_like(u)
    height = .00008 * np.sin(u * 43 + np.sin(v * 5 + phase)) * np.sin(v * 29)
    if sheet in ("WornPlank", "DarkWood", "PolishedWood"):
        height += .00018 * np.sin(v * 112 + np.sin(u * 4 + phase) * 2)
    if sheet == "WornPlank":
        # A quiet continuous foot-worn lane, never a painted direction marker.
        handling = np.exp(-((u / 1.2) ** 2)) * .7 if chart["physical_axes"] == [0, 1] else handling
    elif sheet == "PolishedWood" and chart["physical_axes"] == [0, 1]:
        low, extent = chart["origin_m"], chart["extent_m"]
        for fraction in (.22, .63):
            radius = np.sqrt((u - low[0] - extent[0] * fraction) ** 2 + (v - low[1] - extent[1] * .52) ** 2)
            handling = np.maximum(handling, np.exp(-((radius - .041) / .004) ** 2) * .6)
    elif sheet == "AgedBrass":
        handling = (np.sin(u * 3.3 + phase) * .5 + .5) * .5
        height = .00006 * np.sin(v * 180)
    elif sheet in ("PubCarpet", "WornFabric", "WornLeather"):
        height += .00012 * np.sin(u * 190) * np.sin(v * 210)
    roughness = np.clip(base_roughness + amplitude * broad * .17 + handling * .09, .08, .995)
    cavity = np.clip(.98 - amplitude * (1 - broad) * .10 - handling * .025, .86, 1.)
    pigment = 1. + broad * .035 - handling * .06
    result = height, roughness, cavity, pigment
    return washroom_surfaces.fields(u, v, chart, result) if washroom_surfaces.applies(chart) else result


def paint_chart(chart, source, size, *, response):
    ratio = size / SIZE
    lo = np.floor(np.asarray(chart["guarded_min_px"]) * ratio).astype(int)
    hi = np.ceil(np.asarray(chart["guarded_max_px"]) * ratio).astype(int)
    # Half a response texel is sampled inside the colour chart's support.
    x = (np.arange(lo[0], hi[0], dtype=np.float32) + .5) / ratio
    y = (np.arange(lo[1], hi[1], dtype=np.float32) + .5) / ratio
    u, v = np.meshgrid(x, y)
    u = (u - chart["origin_px"][0]) / chart["texels_per_metre"] + chart["origin_m"][0]
    v = (v - chart["origin_px"][1]) / chart["texels_per_metre"] + chart["origin_m"][1]
    # Extend edge content into gutters so mip filtering never samples black.
    u = np.clip(u, chart["origin_m"][0], chart["origin_m"][0] + chart["extent_m"][0])
    v = np.clip(v, chart["origin_m"][1], chart["origin_m"][1] + chart["extent_m"][1])
    sheet = chart["sheet"]
    height, roughness, cavity, pigment = fields(sheet, u, v, chart)
    if response:
        epsilon = 1 / max(chart["texels_per_metre"] * ratio, 1.)
        du = (fields(sheet, u + epsilon, v, chart)[0] - fields(sheet, u - epsilon, v, chart)[0]) / (2 * epsilon)
        dv = (fields(sheet, u, v + epsilon, chart)[0] - fields(sheet, u, v - epsilon, chart)[0]) / (2 * epsilon)
        pixels = np.stack((du * .5 + .5, dv * .5 + .5, roughness, cavity), axis=-1)
    else:
        pitch = SHEETS[sheet][0]
        # Filter the source to the chart's physical footprint before bilinear
        # sampling; a unique chart must not freeze high-frequency tile aliasing.
        source_density = source.shape[1] / pitch
        target_density = chart["texels_per_metre"] * ratio
        mip = max(0, int(math.floor(math.log2(max(source_density / target_density, 1.)))))
        if mip:
            step = 2 ** mip
            source = np.asarray(Image.fromarray(source).resize(
                (max(1, source.shape[1] // step), max(1, source.shape[0] // step)), Image.Resampling.BOX))
        sx = (u / pitch % 1.) * source.shape[1] - .5
        sy = (1. - v / pitch % 1.) * source.shape[0] - .5
        ix, iy = np.floor(sx).astype(int), np.floor(sy).astype(int)
        fx, fy = (sx - ix)[..., None], (sy - iy)[..., None]
        a = source[iy % source.shape[0], ix % source.shape[1]]
        b = source[iy % source.shape[0], (ix + 1) % source.shape[1]]
        c = source[(iy + 1) % source.shape[0], ix % source.shape[1]]
        d = source[(iy + 1) % source.shape[0], (ix + 1) % source.shape[1]]
        pixels = ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy) / 255. * pigment[..., None]
        if washroom_surfaces.applies(chart):
            pixels = washroom_surfaces.colour(pixels, u, v, chart)
    return lo, hi, np.uint8(np.clip(pixels, 0, 1) * 255 + .5)


def png_bytes(image):
    stream = io.BytesIO()
    image.save(stream, format="PNG", optimize=False)
    return stream.getvalue()


def bc7_bytes(size):
    total = 0
    while True:
        total += max(1, (size + 3) // 4) ** 2 * 16
        if size == 1:
            return total
        size //= 2


def ensure_meta(path, *, size, linear):
    meta = Path(str(path) + ".meta")
    # Repainting is not an import-settings edit. Preserve an existing sidecar
    # exactly, including the user's chosen compression/readability settings.
    if meta.exists():
        return
    template = meta.read_text(encoding="utf8") if meta.exists() else (
        ROOT / "Assets/Resources/City/Surfaces/CityBarFacadeResponse.png.meta").read_text(encoding="utf8")
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, "bar-promenade/" + path.relative_to(ROOT).as_posix()).hex
        template = re.sub(r"(?m)^guid: .*$", "guid: " + guid, template)
    for key, value in (("maxTextureSize", size), ("sRGBTexture", int(not linear)), ("linearTexture", int(linear)),
                       ("wrapU", 1), ("wrapV", 1), ("wrapW", 1), ("isReadable", 0)):
        template = re.sub(r"(?m)^(\s*" + key + r":) \d+$", lambda m: m[1] + " " + str(value), template)
    meta.write_text(template, encoding="utf8", newline="\n")


def publish(contract, *, validate_only):
    if contract["recipe_sha256"] != hashlib.sha256(Path(__file__).read_bytes()).hexdigest():
        raise RuntimeError("Bar surface recipe changed; regenerate its chart contract")
    if contract["washroom_recipe_sha256"] != hashlib.sha256(Path(washroom_surfaces.__file__).read_bytes()).hexdigest():
        raise RuntimeError("Bar washroom physical recipe changed; regenerate its chart contract")
    unique_meshes = sorted(part["mesh"] for part in contract["parts"] if part["unique_surface"])
    if len(unique_meshes) != contract["unique_part_count"] or len(set(unique_meshes)) != len(unique_meshes):
        raise RuntimeError("Bar unique mesh registry must contain every authored opaque node exactly once")
    for name in ("BarFacade3D", "BarServiceProps3D"):
        other = json.loads((ROOT / f"Assets/Bar/Models/{name}.json").read_text(encoding="utf8"))
        overlap = set(unique_meshes).intersection(part["name"] for part in other["parts"])
        if overlap:
            raise RuntimeError("Bar unique mesh registry collides with another model: " + ", ".join(sorted(overlap)))
    sources = {}
    for sheet, checksum in contract["source_sha256"].items():
        path = OUTPUT / f"Bar{sheet}Albedo.png"
        if hashlib.sha256(path.read_bytes()).hexdigest() != checksum:
            raise RuntimeError("Bar pigment source changed: " + sheet)
        sources[sheet] = np.asarray(Image.open(path).convert("RGB"))
    outputs = []
    for resource, size, response in ((ALBEDO, SIZE, False), (RESPONSE, RESPONSE_SIZE, True)):
        pixels = np.empty((size, size, 4 if response else 3), dtype=np.uint8)
        pixels[:] = (128, 128, 245, 255) if response else (178, 178, 178)
        for chart in contract["projection"]["charts"]:
            lo, hi, patch = paint_chart(chart, sources[chart["sheet"]], size, response=response)
            # The array's bottom row is UV y=0; PNG encoding flips to image origin.
            pixels[lo[1]:hi[1], lo[0]:hi[0]] = patch
        data = png_bytes(Image.fromarray(pixels[::-1]))
        path = ROOT / ("Assets/Resources/" + resource + ".png")
        if validate_only:
            if path.read_bytes() != data:
                raise RuntimeError("Stale bar surface texture: " + resource)
        else:
            path.write_bytes(data)
            ensure_meta(path, size=size, linear=response)
        outputs.append({"resource": resource, "size": size, "sha256": hashlib.sha256(data).hexdigest(),
            "sRGB": not response, "wrap": "Clamp", "readable": False})
    memory = bc7_bytes(SIZE) + bc7_bytes(RESPONSE_SIZE)
    if memory > GPU_BUDGET_BYTES:
        raise RuntimeError("Bar unique texture budget exceeded")
    manifest = {"design_id": DESIGN, "unique_meshes": unique_meshes,
        "sheets": outputs, "mipmaps": True, "windows_compression": "BC7",
        "estimated_gpu_bytes": memory, "gpu_budget_bytes": GPU_BUDGET_BYTES,
        "surface_contract_sha256": digest(contract)}
    path = OUTPUT / "BarInteriorTextures.json"
    encoded = json.dumps(manifest, indent=2) + "\n"
    if validate_only:
        if path.read_text(encoding="utf8") != encoded:
            raise RuntimeError("Stale bar texture manifest")
    else:
        path.write_text(encoded, encoding="utf8")
        sidecar = Path(str(path) + ".meta")
        if not sidecar.exists():
            guid = uuid.uuid5(uuid.NAMESPACE_URL, "bar-promenade/" + path.relative_to(ROOT).as_posix()).hex
            sidecar.write_text(f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf8")
    print(f"BAR PHYSICAL TEXTURES OK: unique 4K colour/2K physical, BC7+mips {memory} <= {GPU_BUDGET_BYTES} B", flush=True)


def review_materials(asset):
    """The editable Blender source displays the actual published unique painting."""
    import bpy
    albedo = bpy.data.images.load(str(OUTPUT / "BarInteriorAlbedo.png"), check_existing=True)
    albedo.colorspace_settings.name = "sRGB"
    albedo.pack()
    response = bpy.data.images.load(str(OUTPUT / "BarInteriorResponse.png"), check_existing=True)
    response.colorspace_settings.name = "Non-Color"
    response.pack()
    materials = {}
    for part in asset.parts:
        if not selected(part):
            continue
        key = part.sheet
        if key not in materials:
            material = part.obj.data.materials[0].copy()
            material.name = "PREVIEW_BarUnique_" + key
            material.use_nodes = True
            nodes, links = material.node_tree.nodes, material.node_tree.links
            for node in nodes:
                if node.type == "TEX_IMAGE":
                    node.image = albedo
                    node.extension = "EXTEND"
            physical = nodes.new("ShaderNodeTexImage")
            physical.image = response
            physical.extension = "EXTEND"
            separate = nodes.new("ShaderNodeSeparateColor")
            links.new(physical.outputs["Color"], separate.inputs["Color"])
            links.new(separate.outputs["Blue"], nodes.get("Principled BSDF").inputs["Roughness"])
            materials[key] = material
        part.obj.data.materials.clear()
        part.obj.data.materials.append(materials[key])


def verify_export(path, asset):
    from city_bar_unique_surfaces import verify_export as verify
    verify(path, asset)


if __name__ == "__main__":
    import toolchain
    toolchain.check_python(toolchain.load_config(), ("Pillow", "numpy"))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--repaint", action="store_true",
                        help="Repaint current registered charts without changing geometry, packing or UVs")
    args = parser.parse_args()
    if args.repaint and args.validate_only:
        parser.error("--repaint and --validate-only are mutually exclusive")
    contract=json.loads(MANIFEST.read_text(encoding="utf8"))
    if args.repaint:
        contract["recipe_sha256"]=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
        contract["washroom_recipe_sha256"]=hashlib.sha256(Path(washroom_surfaces.__file__).read_bytes()).hexdigest()
        publish(contract,validate_only=False)
        MANIFEST.write_text(json.dumps(contract,indent=2)+"\n",encoding="utf8")
    else:
        publish(contract, validate_only=args.validate_only)
