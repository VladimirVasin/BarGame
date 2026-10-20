"""Deterministic, metre-scaled unique house painting and aligned material response.

Only the pure rectangle packer is reused from the warehouse. House pigments,
grain, contact wear, room geometry and provenance are independent.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/MothersHouse/Textures"
OUTPUT = ROOT / "Assets/Resources/MothersHouse/Textures"
ALBEDO, RESPONSE = "MothersHouseSurfaceAtlas", "MothersHouseMaterialResponse"
SIZE, RESPONSE_SIZE = 4096, 2048
DESIGN = "mothers_house_unique_surface_v1"
CELLS = (("Wallpaper", "CeilingPlaster", "PlankFloor", "DarkWood"),
         ("Upholstery", "BedLinen", "Rug", "Concrete"),
         ("Ceramic", "PaintedMetal", "Glass", "Fire"),
         ("BookCloth", "Wicker", "TeaCloth", "PaleWood"))
AXES = ((1, 2), (0, 2), (0, 1))  # Blender source coordinates: Z is vertical.
ROUGHNESS = dict(Wallpaper=.94, CeilingPlaster=.96, PlankFloor=.82, DarkWood=.78,
                 Upholstery=.96, BedLinen=.96, Rug=.98, Concrete=.93,
                 Ceramic=.55, PaintedMetal=.70, BookCloth=.94, Wicker=.93,
                 TeaCloth=.96, PaleWood=.86)


def unique(part):
    return not part.emissive and part.sheet not in ("Glass", "Fire")


def pigment_for_part(part):
    if part.role in ("bathroom_floor", "bathroom_splashback"):
        return "BathroomTile"
    if part.role in ("bathroom_fittings", "tea_tray", "wall_clock_hands"):
        return "Brass"
    if part.sheet == "Ceramic" and part.role in ("bathroom_tub", "bathroom_toilet", "bathroom_basin", "bathroom_soap"):
        return "PaintedMetal"  # Neutral ivory enamel pigment; retain Ceramic glaze/flat relief.
    return part.sheet


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"),
                                     ensure_ascii=False).encode("utf8")).hexdigest()


def uv_signature(asset):
    return digest({part.name: [[[round(float(x), 8) for x in loop.uv] for loop in layer.data]
                              for layer in part.obj.data.uv_layers]
                   for part in sorted(asset.parts, key=lambda p: p.name)})


def geometry_signature(asset):
    return digest({"parts": {part.name: {"vertices": [[round(float(x), 6) for x in v] for v in part.geometry[0]],
                                        "faces": [list(f) for f in part.geometry[1]]} for part in asset.parts},
                   "anchors": {name: [round(float(x), 6) for x in anchor.location]
                               for name, anchor in sorted(asset.anchors.items())}})


def weight(part, axis, sign):
    if part.role in ("floor", "upper_floor", "wing_niche_floor") or part.sheet == "PlankFloor":
        return 1.0 if axis == 2 and sign > 0 else .12
    if part.sheet in ("Wallpaper", "CeilingPlaster"):
        # Outward wall backs and roof tops do not take space from the lived-in face.
        pts = np.asarray(part.geometry[0])
        hidden = axis == 2 and sign > 0 and part.sheet == "CeilingPlaster"
        hidden |= axis < 2 and float(pts[:, axis].mean()) * sign > 3.7
        return .18 if hidden else .8
    if part.sheet in ("Ceramic", "PaintedMetal", "TeaCloth") or "table" in part.role:
        return 2.0
    if part.sheet in ("BedLinen", "Upholstery", "Rug"):
        return 1.35
    return 1.2


def layout(asset):
    """Unique per-part/material/normal charts; equal scale on both metric axes."""
    from warehouse_master_projection import _pack_density
    charts = {}
    for part in sorted(asset.parts, key=lambda p: p.name):
        mesh = part.obj.data
        if not part.flame_uv:
            while len(mesh.uv_layers) < 2:
                mesh.uv_layers.new(name="HouseMetres")
            metric = mesh.uv_layers[1]
            metric.name = "HouseMetres"
            for polygon in mesh.polygons:
                axis = int(np.argmax(np.abs(polygon.normal)))
                for loop in polygon.loop_indices:
                    point = mesh.vertices[mesh.loops[loop].vertex_index].co
                    metric.data[loop].uv = (point[AXES[axis][0]], point[AXES[axis][1]])
        part.obj["bp_surface_unique_uv"] = unique(part)
        if not unique(part):
            continue
        for polygon in mesh.polygons:
            axis = int(np.argmax(np.abs(polygon.normal)))
            sign = 1 if polygon.normal[axis] >= 0 else -1
            key = (part.name, axis, sign)
            loops = list(polygon.loop_indices)
            points = np.asarray([tuple(mesh.vertices[mesh.loops[i].vertex_index].co) for i in loops])
            projected = points[:, AXES[axis]]
            row = charts.setdefault(key, {"key": key, "part": part, "axis": axis, "sign": sign,
                "low": projected.min(axis=0), "high": projected.max(axis=0), "faces": [], "planes": [],
                "weight": weight(part, axis, sign)})
            row["low"] = np.minimum(row["low"], projected.min(axis=0))
            row["high"] = np.maximum(row["high"], projected.max(axis=0))
            row["faces"].append((loops, projected))
            row["planes"].append((float(points[:, axis].mean()), float(polygon.area)))
    for chart in charts.values():
        chart["extent"] = chart["high"] - chart["low"]
        if not np.isfinite(chart["extent"]).all() or min(chart["extent"]) <= 1e-10:
            raise RuntimeError("Degenerate house surface chart: " + str(chart["key"]))
        planes = np.asarray(chart["planes"])
        chart["plane"] = float(np.average(planes[:, 0], weights=planes[:, 1]))
    packed, density = _pack_density(list(charts.values()), SIZE, SIZE)
    for key, chart in charts.items():
        x, y, scale = packed[key]
        chart["origin"] = np.asarray((x, y))
        chart["scale"] = scale
        mesh = chart["part"].obj.data
        mesh.uv_layers[0].name = "HouseSurfaceUV"
        for loops, projected in chart["faces"]:
            coords = (chart["origin"] + (projected - chart["low"]) * scale) / SIZE
            for index, loop in enumerate(loops):
                mesh.uv_layers[0].data[loop].uv = coords[index]
        mesh.uv_layers.active_index = 0
        mesh.uv_layers[0].active_render = True
    report = validate_charts(asset, charts, density)
    return charts, report


def validate_charts(asset, charts, density):
    occupied = np.zeros((SIZE, SIZE), dtype=np.uint16)
    rows = []
    for number, chart in enumerate(charts.values(), 1):
        low = np.floor(chart["origin"] - 3).astype(int)
        high = np.ceil(chart["origin"] + chart["extent"] * chart["scale"] + 3).astype(int)
        if np.any(low < 0) or np.any(high > SIZE) or np.any(high <= low):
            raise RuntimeError("House chart escaped guarded atlas bounds")
        region = occupied[low[1]:high[1], low[0]:high[0]]
        if region.any():
            raise RuntimeError("House independent chart gutters overlap: " + str(chart["key"]))
        region[:] = number
        rows.append({"key": list(chart["key"]), "origin_px": chart["origin"].tolist(),
                     "origin_m": chart["low"].tolist(), "extent_m": chart["extent"].tolist(),
                     "texels_per_metre": round(chart["scale"], 5)})
        for loops, _ in chart["faces"]:
            coords = np.asarray([tuple(chart["part"].obj.data.uv_layers[0].data[i].uv) for i in loops])
            if not np.isfinite(coords).all() or coords.min() < 0 or coords.max() > 1:
                raise RuntimeError("Invalid house UV0")
    minima = {}
    for chart in charts.values():
        if chart["weight"] < .5:
            continue
        sheet = chart["part"].sheet
        minima[sheet] = min(minima.get(sheet, math.inf), chart["scale"])
    if min(minima.values()) < 32 or min(c["scale"] for c in charts.values() if c["weight"] >= 2) < 80:
        raise RuntimeError("House physical texture density fell below architecture32 / close surface80 texels per metre")
    return {"chart_count": len(charts), "base_texels_per_metre": round(density, 5),
            "minimum_visible_texels_per_metre": {k: round(v, 5) for k, v in sorted(minima.items())},
            "disjoint_chart_gutters": True, "metric_axis_scale_ratio": 1.0,
            "pack_gutter_per_edge_px": 4, "charts": rows,
            "unique_part_count": sum(unique(p) for p in asset.parts)}


def pixels(path):
    image = bpy.data.images.load(str(path), check_existing=False)
    data = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(data)
    result = data.reshape(image.size[1], image.size[0], 4)[..., :3].copy()
    bpy.data.images.remove(image)
    return result


def sources():
    manifest = SOURCE / "Source/generation.json"
    records = json.loads(manifest.read_text(encoding="utf8"))
    original = ROOT / records["original"]
    if not original.is_file():
        raise RuntimeError("The original positive house atlas must be backed up before surface authoring")
    atlas = pixels(original)
    h, w = atlas.shape[:2]
    result = {}
    for row, names in enumerate(CELLS):
        for column, name in enumerate(names):
            # Blender pixels have their origin at the bottom left.
            cell = atlas[round((3-row)*h/4):round((4-row)*h/4), round(column*w/4):round((column+1)*w/4)]
            inset = max(2, round(min(cell.shape[:2]) * .025))
            result[name] = cell[inset:-inset, inset:-inset]
    if hashlib.sha256(original.read_bytes()).hexdigest() != records["original_sha256"]:
        raise RuntimeError("Original auxiliary house atlas provenance changed")
    evidence = []
    for row in records["images"]:
        path = ROOT / row["source"]
        checksum = hashlib.sha256(path.read_bytes()).hexdigest()
        if checksum != row["sha256"]:
            raise RuntimeError("House pigment source hash changed: " + str(path))
        image = pixels(path)
        columns, rows = row["grid"]
        for cell_record in row["cells"]:
            name = cell_record["sheet"]
            if name not in result and name not in ("BathroomTile", "Brass"):
                raise RuntimeError("Unknown house pigment sheet " + name)
            x, y = cell_record["column"], cell_record["row"]
            cell = image[round((rows-1-y)*image.shape[0]/rows):round((rows-y)*image.shape[0]/rows),
                         round(x*image.shape[1]/columns):round((x+1)*image.shape[1]/columns)]
            # Preserve the complete authored eight-tile grid for physical grout.
            inset = 0 if name == "BathroomTile" else max(2, round(min(cell.shape[:2]) * .025))
            result[name] = cell if inset == 0 else cell[inset:-inset, inset:-inset]
        evidence.append({"name": row["name"], "source": row["source"], "sha256": checksum, "cells": row["cells"]})
    # Each ingredient has one bounded, shared box-filtered pyramid. Baking a
    # large ingredient into a small physical chart samples its footprint rather
    # than skipping fine threads or painted source texels.
    return {name: box_mips(image) for name, image in result.items()}, \
        {"original_sha256": hashlib.sha256(original.read_bytes()).hexdigest(), "images": evidence}


def box_mips(image):
    levels = [image]
    while max(image.shape[:2]) > 1:
        height, width = image.shape[:2]
        padded = np.pad(image, ((0, height % 2), (0, width % 2), (0, 0)), mode="edge")
        image = (padded[0::2, 0::2] + padded[1::2, 0::2] +
                 padded[0::2, 1::2] + padded[1::2, 1::2])*.25
        levels.append(image)
    return tuple(levels)


def bilinear(image, u, v):
    # Offline mirrored ingredients have continuous colour at every source edge.
    u, v = 1 - np.abs(np.mod(u, 2) - 1), 1 - np.abs(np.mod(v, 2) - 1)
    x, y = u * (image.shape[1]-1), v * (image.shape[0]-1)
    ix, iy = x.astype(np.int32), y.astype(np.int32)
    jx, jy = np.minimum(ix+1, image.shape[1]-1), np.minimum(iy+1, image.shape[0]-1)
    fx, fy = (x-ix)[..., None], (y-iy)[..., None]
    return (image[iy, ix]*(1-fx)+image[iy, jx]*fx)*(1-fy) + (image[jy, ix]*(1-fx)+image[jy, jx]*fx)*fy


def sample(levels, u, v, pixels_per_period):
    # Constant footprint per metre-scaled chart. Interpolating adjacent box
    # levels avoids sudden changes between charts with different priorities.
    source_size = max(levels[0].shape[:2])
    lod = min(len(levels)-1, max(0., math.log2(source_size/max(1., pixels_per_period))))
    first = int(lod)
    fraction = lod-first
    result = bilinear(levels[first], u, v)
    if fraction > 0:
        result = result*(1-fraction) + bilinear(levels[first+1], u, v)*fraction
    return result


def repair_spec(chart):
    # Two authored, tidy repairs on the visible front edge of existing cushions.
    # Coordinates follow each baked cloth surface; the chair uses its local frame.
    if chart["axis"] != 2 or chart["sign"] != 1:
        return None
    if chart["part"].name == "FIX_Sofa.Cushions":
        return (-2.17, -.74), (.060, .045)
    if chart["part"].name == "FIX_RockingChair.Cushion":
        return (.17, -.25), (.055, .050)
    return None


def authored_repair(chart, u, v, bake_ratio):
    """Tone-matched reweaving/stitches, footprint-filtered before baking."""
    zero = np.zeros_like(u)
    spec = repair_spec(chart)
    if spec is None:
        return zero, zero, zero, zero, zero
    center, half = spec
    x, y = u-center[0], v-center[1]
    footprint = .35/(chart["scale"]*bake_ratio)
    feather = math.sqrt(.003**2+footprint**2)
    def window(coordinate, extent):
        a = 1/(1+np.exp(-np.clip((coordinate+extent)/feather, -60, 60)))
        b = 1/(1+np.exp(-np.clip((coordinate-extent)/feather, -60, 60)))
        return a-b, (a*(1-a)-b*(1-b))/feather
    wx, dx = window(x, half[0]); wy, dy = window(y, half[1])
    patch, patch_u, patch_v = wx*wy, dx*wy, wx*dy
    # Fine darning has three orderly passes each way. Gaussian footprint
    # integration retains its mean instead of aliasing subpixel thread width.
    width = math.sqrt(.0018**2+footprint**2)
    amplitude = .0018/width
    ridge, ridge_u, ridge_v = zero.copy(), zero.copy(), zero.copy()
    for offset in (-.025, 0., .025):
        gx = np.exp(-.5*((x-offset)/width)**2)*amplitude*.65
        gy = np.exp(-.5*((y-offset)/width)**2)*amplitude
        ridge += gx+gy
        ridge_u += -gx*(x-offset)/(width**2)
        ridge_v += -gy*(y-offset)/(width**2)
    raw = 1-np.exp(-ridge)
    raw_u, raw_v = np.exp(-ridge)*ridge_u, np.exp(-ridge)*ridge_v
    threads = raw*patch
    threads_u, threads_v = raw_u*patch+raw*patch_u, raw_v*patch+raw*patch_v
    # Fractions of a millimetre: sewing never changes the cushion silhouette.
    return (.00018*threads+.000012*patch, .00018*threads_u+.000012*patch_u,
            .00018*threads_v+.000012*patch_v, patch, threads)


def physical_height(chart, pigment, u, v, phase):
    """Height in metres and analytic metric derivatives, band-limited to the bake.

    Pigment ornaments never become relief. Different physical constructions own
    different fields; a glazed basin or brass fitting has no broad normal waves.
    """
    sheet = chart["part"].sheet
    zero = np.zeros_like(u)
    ppm = chart["scale"] * RESPONSE_SIZE / SIZE
    if pigment == "BathroomTile":
        # Eight authored 100 mm tiles span the source ingredient. The narrow
        # grout is prefiltered for this chart's response footprint, not widened
        # into a decorative ridge; painted blue flowers remain flat glaze.
        pitch, physical_width = .1, .002
        width = math.sqrt(physical_width**2 + (.35/ppm)**2)
        sigma = math.pi*width/pitch
        a, b = np.pi*u/pitch, np.pi*v/pitch
        gu = np.exp(-np.sin(a)**2/(2*sigma*sigma))
        gv = np.exp(-np.sin(b)**2/(2*sigma*sigma))
        dgu = -gu*np.sin(a)*np.cos(a)*np.pi/(pitch*sigma*sigma)
        dgv = -gv*np.sin(b)*np.cos(b)*np.pi/(pitch*sigma*sigma)
        depth = .0008*physical_width/width
        return -depth*(gu+gv-gu*gv), -depth*dgu*(1-gv), -depth*dgv*(1-gu), gu+gv-gu*gv
    if sheet in ("PlankFloor", "DarkWood", "PaleWood"):
        # This exact longitudinal field also drives the restrained colour grain.
        swapped = sheet != "PlankFloor" and chart["extent"][0] > chart["extent"][1]
        cross, along = (v, u) if swapped else (u, v)
        angle = cross*39 + np.sin(along*2.1)
        amplitude = .00055 if sheet == "PlankFloor" else .00038
        h = np.sin(angle)*amplitude
        dc = np.cos(angle)*39*amplitude
        da = np.cos(angle)*np.cos(along*2.1)*2.1*amplitude
        return (h, da, dc, zero) if swapped else (h, dc, da, zero)
    if sheet in ("Upholstery", "BedLinen", "Rug", "TeaCloth", "BookCloth", "Wicker"):
        # Resolved cross-weave is limited below the response Nyquist frequency;
        # subpixel threads contribute matte roughness rather than aliasing.
        frequency = min(14., ppm*.20)
        ku, kv = 2*np.pi*frequency, 2*np.pi*frequency*.89
        a, b = u*ku+phase*6.28, v*kv+phase*3.14
        amplitude = .00022 if sheet in ("Rug", "Wicker") else .00013
        repair_h, repair_u, repair_v, _, _ = authored_repair(chart, u, v, RESPONSE_SIZE/SIZE)
        return (amplitude*(.6*np.sin(a)+.4*np.sin(b))+repair_h,
                amplitude*.6*ku*np.cos(a)+repair_u, amplitude*.4*kv*np.cos(b)+repair_v, zero)
    if pigment == "Brass" or sheet in ("Ceramic", "PaintedMetal"):
        # Glaze and cared-for metal stay optically near-flat. Their diffuse
        # decorations and directional reflection do not invent a displaced bowl.
        return zero, zero, zero, zero
    if sheet in ("Wallpaper", "CeilingPlaster", "Concrete"):
        ku, kv = 2*np.pi*1.7, 2*np.pi*2.3
        amplitude = .000018 if sheet != "Concrete" else .000035
        a, b = u*ku+phase*6.28, v*kv
        return (amplitude*np.sin(a)*np.sin(b), amplitude*ku*np.cos(a)*np.sin(b),
                amplitude*kv*np.sin(a)*np.cos(b), zero)
    return zero, zero, zero, zero


def surface(chart, u, v, ingredients, bake_ratio):
    part = chart["part"]
    sheet, axis = part.sheet, chart["axis"]
    axes = AXES[axis]
    point = np.empty((*u.shape, 3), dtype=np.float32)
    point[..., axis] = chart["plane"]
    point[..., axes[0]], point[..., axes[1]] = u, v
    structural = sheet in ("Wallpaper", "CeilingPlaster", "PlankFloor")
    phase = 0.0 if structural else int(hashlib.sha256(part.name.encode()).hexdigest()[:8], 16) / 2**32
    local_origin = np.zeros(3, dtype=np.float32)
    if part.name.startswith("FIX_RockingChair"):
        # Pattern follows the one actual chair mesh when it rocks, rather than
        # sampling the room in the runtime shader.
        local_origin = np.asarray((.02, 1.62, .55), dtype=np.float32)
        point -= local_origin
        u, v = point[..., axes[0]], point[..., axes[1]]
    pitch = {"Wallpaper": 1.9, "CeilingPlaster": 2.8, "PlankFloor": 1.6,
             "Upholstery": .85, "BedLinen": .9, "Rug": 1.5, "PaintedMetal": 1.,
             "Ceramic": .7, "Concrete": 2.4, "DarkWood": 1.1, "PaleWood": 1.15,
             "Wicker": .55, "TeaCloth": .95, "BookCloth": .85}[sheet]
    pigment = pigment_for_part(part)
    if pigment == "BathroomTile":
        # Source has an eight by eight grid. Metric grid origin is shared by the
        # floor and splashback; their paintings are colour, their joints relief.
        rgb = sample(ingredients[pigment], u/.8, v/.8, chart["scale"]*bake_ratio*.8)
    else:
        rgb = sample(ingredients[pigment], u/pitch + phase*.53, v/pitch + phase*.71,
                     chart["scale"]*bake_ratio*pitch)
    broad = np.sin(u*.47 + v*.23 + phase*6.28) * np.cos(v*.31-u*.17)
    rgb *= (1 + broad*.018)[..., None]
    # Very restrained causal handling on furniture edges; no industrial decay.
    bounds = np.asarray(part.geometry[0]) - local_origin
    lo, hi = bounds.min(axis=0), bounds.max(axis=0)
    distances = np.stack((point[..., axes[0]]-lo[axes[0]], point[..., axes[0]]-hi[axes[0]],
                          point[..., axes[1]]-lo[axes[1]], point[..., axes[1]]-hi[axes[1]]))
    nearest = np.argmin(np.abs(distances), axis=0)
    signed_edge = np.take_along_axis(distances, nearest[None], axis=0)[0]
    edge = np.abs(signed_edge)
    handled = np.exp(-((edge/.055)**2)) if not structural else np.zeros_like(u)
    handled_derivative = -2*edge/(.055**2)*handled*np.sign(signed_edge)
    handled_u = np.where(nearest < 2, handled_derivative, 0.)
    handled_v = np.where(nearest >= 2, handled_derivative, 0.)
    rgb *= (1 + handled*.009)[..., None]
    _, _, _, repair_patch, repair_threads = authored_repair(chart, u, v, bake_ratio)
    # Same cloth, a slightly different dye lot and neat darker matching thread.
    rgb *= (1-repair_patch*.025-repair_threads*.19)[..., None]
    wood = sheet in ("PlankFloor", "DarkWood", "PaleWood")
    if wood:
        grain_cross, grain_along = (u, v) if structural or chart["extent"][1] >= chart["extent"][0] else (v, u)
        rgb *= (1 + np.sin(grain_cross*39 + np.sin(grain_along*2.1))*.012)[..., None]
    height, slope_u, slope_v, grout = physical_height(chart, pigment, u, v, phase)
    # The same contact mask gently polishes colour/roughness and flattens relief.
    # Product-rule derivatives retain a physically consistent handled boundary.
    slope_u = slope_u*(1-.18*handled) - height*.18*handled_u
    slope_v = slope_v*(1-.18*handled) - height*.18*handled_v
    rough = np.clip(ROUGHNESS[sheet] - handled*.025 + broad*.012 + grout*.10 + repair_threads*.015, .42, .99)
    cavity = np.clip(.995 - handled*.012 - grout*.025, .95, 1.)
    if part.role in ("firebox", "fire_logs", "fire_ash"):
        rgb *= np.asarray(part.tint[:3])
    response = np.stack((.5 + slope_u*.5, .5 + slope_v*.5, rough, cavity), axis=-1)
    return np.clip(rgb, 0, 1), response


def validate_height_fields(charts):
    """Independently finite-difference the analytic physical derivatives."""
    worst, maximum, checked = 0., 0., 0
    for chart in charts.values():
        part = chart["part"]
        pigment = pigment_for_part(part)
        phase = 0. if part.sheet in ("Wallpaper", "CeilingPlaster", "PlankFloor") else \
            int(hashlib.sha256(part.name.encode()).hexdigest()[:8], 16)/2**32
        u = chart["low"][0] + chart["extent"][0]*np.asarray((.173, .379, .631, .823))
        v = chart["low"][1] + chart["extent"][1]*np.asarray((.219, .431, .673, .877))
        repair = repair_spec(chart)
        if repair:
            center, _ = repair
            u = np.append(u, center[0]+np.asarray((-.025, -.012, .005, .025)))
            v = np.append(v, center[1]+np.asarray((-.009, .015, .025, -.025)))
        height, du, dv, _ = physical_height(chart, pigment, u, v, phase)
        epsilon = 1e-6
        finite_u = (physical_height(chart, pigment, u+epsilon, v, phase)[0] -
                    physical_height(chart, pigment, u-epsilon, v, phase)[0])/(2*epsilon)
        finite_v = (physical_height(chart, pigment, u, v+epsilon, phase)[0] -
                    physical_height(chart, pigment, u, v-epsilon, phase)[0])/(2*epsilon)
        worst = max(worst, float(np.max(np.abs(finite_u-du))), float(np.max(np.abs(finite_v-dv))))
        maximum = max(maximum, float(np.max(np.abs(du))), float(np.max(np.abs(dv))))
        if not np.isfinite(np.asarray((height, du, dv))).all() or worst > .00001 or maximum > .12:
            raise RuntimeError("House physical height field has invalid metric derivatives: "+str(chart["key"]))
        checked += 1
    return {"model": "material-specific analytic height derivatives in metres per metre",
            "checked_charts": checked, "maximum_absolute_slope": round(maximum, 7),
            "maximum_finite_difference_error": round(worst, 10),
            "glazed_ceramic_and_brass": "flat; painted ornaments do not displace glaze",
            "authored_repairs": [{"part": c["part"].name, "center_m": list(repair_spec(c)[0]),
                                  "size_m": [2*x for x in repair_spec(c)[1]],
                                  "surface": "visible upper/front cushion edge; chair baked in local frame"}
                                 for c in charts.values() if repair_spec(c)],
            "response_band_limit": "weave <=0.20 cycles per response texel; grout footprint-prefiltered"}


def save(data, path, linear):
    image = bpy.data.images.new(path.stem, width=data.shape[1], height=data.shape[0], alpha=data.shape[2] == 4,
                                float_buffer=False)
    image.colorspace_settings.name = "Non-Color" if linear else "sRGB"
    rgba = data if data.shape[2] == 4 else np.concatenate((data, np.ones((*data.shape[:2], 1), np.float32)), axis=-1)
    image.pixels.foreach_set(np.asarray(rgba, np.float32).reshape(-1))
    image.filepath_raw = str(path); image.file_format = "PNG"; image.save()
    bpy.data.images.remove(image)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def seam_proof(data, charts, size, response):
    """Compare actual packed pixels where structural parts share a plane/edge."""
    rows = [c for c in charts.values() if c["part"].sheet in ("Wallpaper", "CeilingPlaster", "PlankFloor") and c["weight"] >= .5]
    checks, worst = 0, 0.0
    def read(chart, points):
        coords = (chart["origin"] + (points-chart["low"])*chart["scale"])*size/SIZE-.5
        floor = np.floor(coords).astype(int); fraction = coords-floor
        x, y = floor[:, 0], floor[:, 1]; fx, fy = fraction[:, :1], fraction[:, 1:]
        return ((data[y, x]*(1-fx)+data[y, x+1]*fx)*(1-fy) +
                (data[y+1, x]*(1-fx)+data[y+1, x+1]*fx)*fy)
    for i, first in enumerate(rows):
        for second in rows[i+1:]:
            if first["part"].name == second["part"].name or first["part"].sheet != second["part"].sheet or \
                first["axis"] != second["axis"] or first["sign"] != second["sign"] or abs(first["plane"]-second["plane"]) > .001:
                continue
            low = np.maximum(first["low"], second["low"]); high = np.minimum(first["high"], second["high"])
            if np.any(high < low-.001) or max(high-low) < .1:
                continue
            points = low + np.linspace(.01, .99, 33)[:, None]*np.maximum(0, high-low)
            difference = np.abs(read(first, points)-read(second, points))
            error = float(difference.mean())
            worst = max(worst, error); checks += 1
            if error > (.035 if not response else .025):
                raise RuntimeError(f"House structural packed-map seam {first['key']} / {second['key']}: {error:.5f}")
    return {"paired_structural_charts": checks, "worst_mean_difference": round(worst, 7),
            "sample_space": "actual packed pixels, bilinear in the working colour space"}


def bake(charts, ingredients):
    directory = SOURCE / "Atlases"; directory.mkdir(parents=True, exist_ok=True)
    records = {}
    for size, name, response in ((SIZE, ALBEDO, False), (RESPONSE_SIZE, RESPONSE, True)):
        output = np.zeros((size, size, 4 if response else 3), dtype=np.float32)
        if response: output[..., :2] = .5; output[..., 2:] = 1
        else: output[:] = (.65, .61, .52)
        ratio = size/SIZE
        for chart in charts.values():
            origin, scale = chart["origin"]*ratio, chart["scale"]*ratio
            low = np.maximum(0, np.floor(origin-3*ratio).astype(int))
            high = np.minimum(size, np.ceil(origin+chart["extent"]*scale+3*ratio).astype(int))
            yy, xx = np.mgrid[low[1]:high[1], low[0]:high[0]]
            u = chart["low"][0] + (xx+.5-origin[0])/scale
            v = chart["low"][1] + (yy+.5-origin[1])/scale
            color, physical = surface(chart, u, v, ingredients, ratio)
            output[low[1]:high[1], low[0]:high[0]] = physical if response else color
        records[name] = {"source": "ArtSource/MothersHouse/Textures/Atlases/"+name+".source.png",
                         "seam_proof": seam_proof(output, charts, size, response),
                         "sha256": save(output, directory/(name+".source.png"), response)}
        del output
    return records


def auxiliary():
    provenance = json.loads((SOURCE/"Source/generation.json").read_text(encoding="utf8"))
    image = bpy.data.images.load(str(ROOT/provenance["original"]), check_existing=False)
    image.scale(1024, 1024)
    destination = SOURCE/"Atlases/MothersHousePositiveAtlas.source.png"
    image.filepath_raw = str(destination); image.file_format = "PNG"; image.save()
    bpy.data.images.remove(image)
    return hashlib.sha256(destination.read_bytes()).hexdigest()


def prepare(asset, validate_only=False, recorded=None):
    before = geometry_signature(asset)
    charts, projection = layout(asset)
    signature = uv_signature(asset)
    _, second = layout(asset)
    if signature != uv_signature(asset) or projection != second or before != geometry_signature(asset):
        raise RuntimeError("House surface packing changed geometry or is nondeterministic")
    ingredients, provenance = sources()
    physical = validate_height_fields(charts)
    if validate_only:
        if recorded is None or recorded.get("uv_signature") != signature:
            raise RuntimeError("Published house surface UV signature is stale")
        if recorded.get("source_provenance") != provenance:
            raise RuntimeError("Published house pigment provenance is stale")
        if recorded.get("recipe_sha256") != hashlib.sha256(Path(__file__).read_bytes()).hexdigest():
            raise RuntimeError("House surface authoring recipe changed; rebuild its registered maps")
        if recorded.get("physical_response") != physical or recorded.get("geometry_signature") != before:
            raise RuntimeError("House physical response/unchanged geometry proof is stale")
        for key, name in (("albedo_sha256", ALBEDO), ("response_sha256", RESPONSE),
                          ("auxiliary_sha256", "MothersHousePositiveAtlas")):
            path = SOURCE/"Atlases"/(name+".source.png")
            if hashlib.sha256(path.read_bytes()).hexdigest() != recorded.get(key):
                raise RuntimeError("Published house source map changed: "+name)
        return recorded
    maps = bake(charts, ingredients)
    return {"version": 1, "design_id": DESIGN, "albedo": "Textures/"+ALBEDO,
            "response": "Textures/"+RESPONSE, "albedo_size": SIZE, "response_size": RESPONSE_SIZE,
            "albedo_sha256": maps[ALBEDO]["sha256"], "response_sha256": maps[RESPONSE]["sha256"],
            "auxiliary_sha256": auxiliary(),
            "uv_signature": signature, "geometry_signature": before,
            "recipe_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            "unique_part_count": projection["unique_part_count"], "mipmaps": True,
            "srgb_albedo": True, "srgb_response": False, "texture_bytes_budget": 30*1024*1024,
            "channels": ["height_slope_u", "height_slope_v", "roughness", "cavity"],
            "projection": projection, "source_provenance": provenance,
            "physical_response": physical,
            "continuity": {name: record["seam_proof"] for name, record in maps.items()},
            "structural_coordinates": "shared fixed house metres; moving chair local baked coordinates",
            "pigment_prefilter": "cached per-ingredient box-mip pyramid; trilinear level selected from baked physical chart footprint",
            "runtime_detail": "one unique albedo and one aligned physical response; no runtime tiling"}
