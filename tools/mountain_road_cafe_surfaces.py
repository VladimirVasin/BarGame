"""Guarded metric cafe painting; geometry and original UV coordinates stay exact."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/MountainRoad/Surfaces/Atlases"
MANIFEST = ROOT / "ArtSource/MountainRoad/Surfaces/cafe-surfaces.json"
ALBEDO = "MountainRoadCafeSurfaceAtlas"
RESPONSE = "MountainRoadCafeSurfaceResponse"
SIZE, RESPONSE_SIZE = 4096, 2048
FLOOR_TILE, FLOOR_BORDER, FLOOR_TRIM = .32, .19, 8
DESIGN = "mountain_road_cafe_physical_surfaces_v1"
EXCLUDED_SHEETS = {"CafeGlassDetail", "CafeWarmEmission", "CafeCoffee"}
_PALETTE_MEANS = {}
_SHEET_TINTS = {"CafeExteriorDetail": (.72, .88, .82), "CafeInteriorDetail": (.98, .91, .70),
                "CafeCounterDetail": (.92, .72, .58), "CafeMetalDetail": (.86, .90, .82),
                "CafePropsDetail": (.95, .93, .82)}


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf8")).hexdigest()


def rounded(values, digits=8):
    return np.round(np.asarray(values, dtype=np.float64), digits).tolist()


def selected(part):
    return not part.emissive and part.sheet not in EXCLUDED_SHEETS and part.role not in {"menu_cover", "menu_pages"}


def profile(part):
    """The already accepted cafe colours, multiplied into pigment exactly once."""
    role = part.role
    if role == "fridge_gasket":
        return "Rubber", (.26, .28, .24), 0.
    if role == "folded_napkins":
        return "Paper", (.93, .91, .79), 0.
    if role == "condiment_jar":
        return "Enamel", (.74, .79, .73), .04
    if role in {"refrigerator_body", "fridge_door"}:
        return "Enamel", (.68, .69, .54), .05
    if role == "refrigerator_cavity":
        return "Enamel", (.78, .81, .70), .02
    if role == "refrigerator_shelf":
        return "Enamel", (.62, .72, .70), .12
    if role == "stove":
        return "Enamel", (.88, .91, .86), .16
    if role == "frying_pan":
        return "PaintedMetal", (.56, .59, .56), .35
    if role == "service_towel":
        return "Cloth", (.95, .93, .82), 0.
    if role == "stool_seat":
        return "Cloth", _SHEET_TINTS[part.sheet], 0.
    if role == "floor":
        return "Linoleum", (.98, .91, .70), 0.
    if role == "menu_cover":
        return "Paper", _SHEET_TINTS[part.sheet], 0.
    if part.sheet == "CafeExteriorDetail":
        return "PaintedMetal", (.72, .88, .82), 0.
    if part.sheet == "CafeInteriorDetail":
        return "WallPaint", (.98, .91, .70), 0.
    if part.sheet == "CafeCounterDetail":
        return "Timber", (.92, .72, .58), 0.
    if part.sheet == "CafeMetalDetail":
        return "PaintedMetal", (.86, .90, .82), .28
    return "Enamel", (.95, .93, .82), 0.


def appearance(part, sample_pixel=None, panel_region=None):
    family, tint, metallic = profile(part)
    if sample_pixel is None or not selected(part):
        return family, tint, metallic
    from mountain_road_surface_authoring import linear, gamma
    appliance = part.role in {"refrigerator_body", "fridge_door", "refrigerator_cavity",
                              "refrigerator_shelf", "stove", "frying_pan"}
    region = panel_region(part) if appliance else (0., 0., 1., 1.)
    key = part.sheet, region
    if key not in _PALETTE_MEANS:
        count = 96 if appliance else 512
        pixels = np.asarray([sample_pixel(part.sheet,
            min(511, int((region[0] + (x + .5) / count * region[2]) * 512)),
            min(511, int((region[1] + (y + .5) / count * region[3]) * 512)), 512)[:3]
            for y in range(count) for x in range(count)], np.float32) / 255.
        _PALETTE_MEANS[key] = linear(pixels).mean(axis=0)
    # Retain the former material's delivered pigment colour, not merely its
    # MPB multiplier. Grids/rivets/stains are not carried into the new atlas.
    final = gamma(np.clip(linear(np.asarray(tint)) * _PALETTE_MEANS[key], 0, 1))
    return family, tuple(float(value) for value in final), metallic


def describe(part, sample_pixel=None, panel_region=None):
    mesh = part.obj.data
    metric = mesh.uv_layers[0]
    faces, projection = [], np.zeros((len(mesh.loops), 2), np.float64)
    parents = list(range(len(mesh.vertices)))
    def component(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index
    for polygon in mesh.polygons:
        root = component(polygon.vertices[0])
        for vertex in polygon.vertices[1:]:
            other = component(vertex)
            parents[max(root, other)] = min(root, other)
            root = min(root, other)
    for polygon in mesh.polygons:
        axis = int(np.argmax(np.abs(polygon.normal)))
        faces.append({"axis": axis, "sign": 1 if polygon.normal[axis] >= 0 else -1,
                      "loops": list(polygon.loop_indices), "vertices": list(polygon.vertices),
                      "component": component(polygon.vertices[0])})
        axes = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[axis]
        for index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[index].vertex_index].co
            projection[index] = point[axes[0]], point[axes[1]]
        if polygon.index in part.floor_charts:
            tile, metric_uv = part.floor_charts[polygon.index]
            faces[-1]["component"] = tile
            for index in polygon.loop_indices:
                projection[index] = metric_uv[mesh.loops[index].vertex_index]
    geometry = {"vertices": rounded([tuple(vertex.co) for vertex in mesh.vertices], 6),
                "faces": [face["vertices"] for face in faces]}
    family, tint, metallic = appearance(part, sample_pixel, panel_region)
    uv = [tuple(loop.uv) for loop in metric.data]
    return {"mesh": part.name, "part_name": part.name, "family": family,
            "role": part.role, "sheet": part.sheet, "tint": list(tint), "metallic": metallic,
            "unique_surface": selected(part), "metric_uv": uv, "projection_uv": projection.tolist(),
            "faces": faces, "geometry_signature": digest(geometry), "uv1_signature": digest(rounded(uv))}


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
            key, points = chart_key(part, face), metric[face["loops"]]
            chart = charts.setdefault(key, {"key": key, "part": part, "low": points.min(axis=0),
                                            "high": points.max(axis=0), "weight": 1.})
            chart["low"] = np.minimum(chart["low"], points.min(axis=0))
            chart["high"] = np.maximum(chart["high"], points.max(axis=0))
    for chart in charts.values():
        if chart["part"]["role"] == "floor" and chart["key"][1] == 2:
            chart["weight"] = 7.5 if chart["key"][2] > 0 else .25
        chart["extent"] = chart["high"] - chart["low"]
        if not np.isfinite(chart["extent"]).all() or chart["extent"].min() <= 1e-10:
            raise RuntimeError("Degenerate cafe metric chart: " + str(chart["key"]))
    packed, density = _pack_density(list(charts.values()), SIZE, SIZE)
    occupied, rows = np.zeros((SIZE, SIZE), np.uint16), []
    for number, chart in enumerate(charts.values(), 1):
        x, y, scale = packed[chart["key"]]
        chart["origin"], chart["scale"] = np.asarray((x, y)), scale
        low = np.floor(chart["origin"] - 3).astype(int)
        high = np.ceil(chart["origin"] + chart["extent"] * scale + 3).astype(int)
        if np.any(low < 0) or np.any(high > SIZE):
            raise RuntimeError("Cafe atlas chart escaped its guard")
        region = occupied[low[1]:high[1], low[0]:high[0]]
        if region.any():
            raise RuntimeError("Cafe atlas chart gutters overlap")
        region[:] = number
        rows.append({"key": list(chart["key"]), "origin_px": chart["origin"].tolist(),
                     "origin_m": chart["low"].tolist(), "extent_m": chart["extent"].tolist(),
                     "texels_per_metre": scale})
    if density < 80:
        raise RuntimeError(f"Cafe chart density {density:.3f} fell below 80 texels/metre")
    return charts, {"chart_count": len(rows), "base_texels_per_metre": density,
                    "disjoint_chart_gutters": True, "metric_axis_scale_ratio": 1.,
                    "pack_gutter_per_edge_px": 4, "charts": rows}


def packed_uv(part, charts):
    coords = np.asarray(part["projection_uv"])
    result = coords.copy()
    for face in part["faces"]:
        chart = charts[chart_key(part, face)]
        loops = face["loops"]
        result[loops] = (chart["origin_px"] +
            (coords[loops] - chart["origin_m"]) * chart["texels_per_metre"]) / SIZE
    if not np.isfinite(result).all() or result.min() < 0 or result.max() > 1:
        raise RuntimeError("Cafe UV0 escaped its atlas")
    return np.asarray(result, np.float32)


def review(part, row):
    import bpy
    name = "CafeUniqueSurfaceReview_" + str(round(row["metallic"] * 100))
    material = bpy.data.materials.get(name)
    if material is None:
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        shader = material.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Metallic"].default_value = row["metallic"]
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
    part.obj.data.materials.clear()
    part.obj.data.materials.append(material)


def lino_noise(x, y, seed):
    """Continuous metre-space noise, shared by both map resolutions."""
    ix, iy = np.floor(x), np.floor(y)
    fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)

    def corner(dx, dy):
        return np.mod(np.sin((ix + dx) * 127.1 + (iy + dy) * 311.7 + seed) * 43758.5453, 1.)

    lower = (1 - fx) * corner(0, 0) + fx * corner(1, 0)
    upper = (1 - fx) * corner(0, 1) + fx * corner(1, 1)
    return lower * (1 - fy) + upper * fy


def diner_tile(slot, u, v, physical):
    # One cell is 32 cm in BOTH axes, including partial cells at the windows.
    if slot == FLOOR_TRIM:
        pin = np.exp(-((v - FLOOR_BORDER + .030) / .006)**4)
        color = (1-pin[...,None])*np.asarray((.35,.38,.385)) + pin[...,None]*np.asarray((.70,.68,.60))
        seam = np.exp(-((v-FLOOR_BORDER)/.0025)**2)
        roughness = .62
    else:
        dark, variant = divmod(slot, 4)
        base = (.38,.41,.42) if dark else (.76,.73,.65)
        offset = .008 if variant % 2 else -.005
        color = np.broadcast_to(np.asarray(base)+offset, (*u.shape,3))
        edge = np.minimum.reduce((u, v, FLOOR_TILE-u, FLOOR_TILE-v))
        seam = np.exp(-(edge/.0022)**2)
        roughness = .47 if variant >= 2 else .63
    # Dispersed vinyl pigment and tiny wear marks, with no earthy mottle.
    grain = lino_noise(u*168, v*168, 17+slot) - .5
    pigment = lino_noise(u*15, v*26, 5+slot) - .5
    color = color * (1 + .031*grain + .017*pigment)[...,None]
    color = color*(1-.20*seam[...,None])
    physical[...,0:2] = .5
    physical[...,2] = roughness + .024*grain + .10*seam
    physical[...,3] = 1 - .09*seam
    return np.clip(color, 0, 1), np.clip(physical, 0, 1)


def fitting_wear(chart, u, v, color, physical):
    # Metric coordinates use Blender XY for horizontal faces and XZ/YZ for uprights.
    role = chart["part"]["role"]
    axis = chart["key"][1]
    if role == "floor" and axis == 2 and chart["key"][2] > 0:
        return diner_tile(chart["key"][3], u, v, physical)
    grain = .5 + .5 * np.sin(31*u + .75*np.sin(17*v)) * np.cos(43*v + 2*u)
    wear = np.zeros_like(u)
    if role == "counter_top" and axis == 2:
        wear = .75*np.exp(-((v+1.58)/.095)**2)
        for x in (-1.50, -.38, .75, 1.8):
            wear += .22*np.exp(-((u-x)/.34)**2 - ((v+1.42)/.17)**2)
    elif role == "counter_rail" or role == "kitchen_cabinet_handle":
        wear = .65 * grain
    elif role == "stool_seat" and axis == 2:
        wear = .45 * grain
    elif role == "stove" and axis == 2:
        edge = np.minimum(np.abs(v-chart["low"][1]), np.abs(v-chart["high"][1]))
        chips = np.exp(-(edge/.012)**2) * np.clip((grain-.62)*4,0,1)
        color *= (1-.32*chips)[...,None]
        physical[...,2] += .11 * chips
    elif role == "folded_napkins":
        physical[...,2] = .86 + .08*grain
    elif role == "condiment_jar":
        physical[...,2] = .24 + .10*grain
    wear = np.clip(wear,0,1)
    color *= (1+.12*wear)[...,None]
    physical[...,2] -= .20*wear
    return np.clip(color,0,1), np.clip(physical,0,1)


def bake(charts):
    from mountain_road_surface_authoring import sample
    from village_unique_surfaces import save
    SOURCE.mkdir(parents=True, exist_ok=True)
    records = {}
    for size, name, response in ((SIZE, ALBEDO, False), (RESPONSE_SIZE, RESPONSE, True)):
        print("CAFE SURFACE BAKE: " + name, flush=True)
        output = np.zeros((size, size, 4 if response else 3), np.float32)
        if response:
            output[..., :2] = .5
            output[..., 2:] = 1
        ratio = size / SIZE
        for number, chart in enumerate(charts.values(), 1):
            origin, scale = chart["origin"] * ratio, chart["scale"] * ratio
            low = np.maximum(0, np.floor(origin - 3 * ratio).astype(int))
            high = np.minimum(size, np.ceil(origin + chart["extent"] * scale + 3 * ratio).astype(int))
            yy, xx = np.mgrid[low[1]:high[1], low[0]:high[0]]
            u = chart["low"][0] + (xx + .5 - origin[0]) / scale
            v = chart["low"][1] + (yy + .5 - origin[1]) / scale
            row = chart["part"]
            color, physical = sample(row["family"], u, v, part_name=row["part_name"],
                                     tint=row["tint"], pixel_metres=1 / scale)
            color, physical = fitting_wear(chart, u, v, color, physical)
            value = physical if response else color
            if not np.isfinite(value).all() or value.min() < 0 or value.max() > 1:
                raise RuntimeError("Invalid cafe physical samples: " + row["mesh"])
            output[low[1]:high[1], low[0]:high[0]] = value
            if number % 200 == 0:
                print(f"CAFE SURFACE BAKE: {name} chart {number}/{len(charts)}", flush=True)
        path = SOURCE / (name + ".source.png")
        records[name] = {"source": path.relative_to(ROOT).as_posix(), "size": size,
                         "sha256": save(output, path, response)}
    return records


def prepare(asset, validate_only=False, layout_only=False, sample_pixel=None, panel_region=None):
    parts = [describe(part, sample_pixel, panel_region) for part in asset.parts]
    charts, projection = layout(parts)
    lookup = {tuple(row["key"]): row for row in projection["charts"]}
    rows = []
    for part in parts:
        row = {key: value for key, value in part.items() if key not in ("metric_uv", "projection_uv", "faces")}
        if part["unique_surface"]:
            row["uv0_signature"] = digest(rounded(packed_uv(part, lookup)))
            row["uv_channel_count"] = 2
        else:
            row["uv_channel_count"] = 1
        rows.append(row)
    current = {"design_id": DESIGN, "albedo": "MountainRoad/Cafe/Textures/" + ALBEDO,
               "response": "MountainRoad/Cafe/Textures/" + RESPONSE,
               "albedo_size": SIZE, "response_size": RESPONSE_SIZE,
               "unique_part_count": sum(part["unique_surface"] for part in parts),
               "geometry_signature": digest([(row["mesh"], row["geometry_signature"]) for row in rows]),
               "projection": projection, "parts": rows,
               "uv_signature": digest(rows), "tint": "final tint baked once; runtime white",
               "channels": ["height_slope_u", "height_slope_v", "roughness", "cavity"],
               "recipe_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    print(f"CAFE UNIQUE LAYOUT: {current['unique_part_count']} meshes, "
          f"{projection['chart_count']} charts, {projection['base_texels_per_metre']:.2f} texels/metre", flush=True)
    if layout_only:
        return current
    from mountain_road_surface_authoring import provenance
    current["pigment_provenance"] = provenance()
    current["physical_recipe_sha256"] = hashlib.sha256((ROOT / "tools/mountain_road_surface_authoring.py").read_bytes()).hexdigest()
    if validate_only:
        recorded = json.loads(MANIFEST.read_text(encoding="utf8"))
        for key, value in current.items():
            if recorded.get(key) != value:
                raise RuntimeError("Cafe registered surface contract differs: " + key)
        for row in recorded["maps"].values():
            if hashlib.sha256((ROOT / row["source"]).read_bytes()).hexdigest() != row["sha256"]:
                raise RuntimeError("Cafe source map checksum differs")
        current = recorded
    else:
        current["maps"] = bake(charts)
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_text(json.dumps(current, indent=2) + "\n", encoding="utf8")
    for part, source, row in zip(asset.parts, parts, rows, strict=True):
        if not row["unique_surface"]:
            continue
        mesh = part.obj.data
        metric = mesh.uv_layers.new(name="CafeOriginalUV")
        for index, coordinate in enumerate(source["metric_uv"]):
            metric.data[index].uv = coordinate
        coords = packed_uv(source, lookup)
        for index, coordinate in enumerate(coords):
            mesh.uv_layers[0].data[index].uv = coordinate
        mesh.uv_layers.active_index = 0
        mesh.uv_layers[0].active_render = True
        if digest(rounded([tuple(loop.uv) for loop in metric.data])) != row["uv1_signature"]:
            raise RuntimeError("Cafe original coordinates changed while packing")
        review(part, row)
    return current


def textures(contract):
    return [{"sheet": name, "file": "Assets/Resources/MountainRoad/Cafe/Textures/" + name + ".png",
             "resource_path": "MountainRoad/Cafe/Textures/" + name,
             "width": record["size"], "height": record["size"], "wrap": "Clamp",
             "sha256": record["sha256"], "srgb": name == ALBEDO}
            for name, record in contract["maps"].items()]
