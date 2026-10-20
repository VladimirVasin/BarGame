#!/usr/bin/env python3
"""Publish City-owned source-aligned pigments and causal physical response maps.

Existing owner atlases retain their authored broad palette, panels and UV
boundaries. New ImageGen pigments supply filtered material grain, while an
independent metric height field supplies physical slopes/roughness/cavity.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import math
from pathlib import Path
import uuid

import numpy as np
from PIL import Image, ImageFilter

import city_surface_authoring as art

ROOT, OUTPUT = art.ROOT, art.OUTPUT
PREFIX = "City/Surfaces/"
MANIFEST = OUTPUT / "CitySurfaceTextures.json"
BAR = ROOT / "ArtSource/City/Surfaces/city-bar-facade-surfaces.json"
LUMA = np.asarray((.2126, .7152, .0722), np.float32)
_spec = importlib.util.spec_from_file_location("city_private_texture_publisher", ROOT / "tools/build-village-surface-textures.py")
_publisher = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_publisher)
bc7_bytes, meta = _publisher.bc7_bytes, _publisher.meta


def sha(data):
    return hashlib.sha256(data).hexdigest()


def source_path(row):
    return ROOT / ("Assets/Resources/" + row["source"] + ".png" if row["source"] else row["source_asset"])


def png(values):
    stream = io.BytesIO()
    Image.fromarray(np.uint8(np.clip(values[::-1] * 255 + .5, 0, 255))).save(stream, format="PNG", compress_level=6)
    return stream.getvalue()


def mean_luma(rgb):
    return float(np.mean(np.sum(art.linear(rgb) * LUMA, axis=-1)))


def write_map(resource, values, linear, clamp=False):
    path = ROOT / ("Assets/Resources/" + resource + ".png")
    path.parent.mkdir(parents=True, exist_ok=True)
    data = png(values)
    path.write_bytes(data)
    Path(str(path) + ".meta").write_text(meta(path, values.shape[0], linear, clamp=clamp), encoding="utf8", newline="\n")
    return sha(data)


def coordinates(row, u, v):
    """Keep owner UV layouts, while declaring a finite metric sampling frame."""
    pitch = row["meters_per_tile"]
    base = art.spec(row["material_family"])[2]
    layout = row["layout"]
    if layout == "building_side_atlas":
        # Four side columns are distinct full facade images, never four repeats.
        face = np.minimum(3, np.floor(u * 4))
        reference = row["reference_dimensions_m"]
        width = np.where(face < 2, reference[0], reference[1])
        local_u = np.clip((np.mod(u * 4, 1) - 4 / 256) / (1 - 8 / 256), 0, 1)
        local_v = np.clip((v - 4 / 1024) / (1 - 8 / 1024), 0, 1)
        return local_u * width + face * 19.2, local_v * reference[2], float(max(reference[:2])) * 4 / (1 - 8 / 256), reference[2] / (1 - 8 / 1024)
    if layout == "full_face":
        reference = row["reference_dimensions_m"]
        return u * reference[0], v * reference[2], reference[0], reference[2]
    if layout == "bay_grid":
        return u * 10, v * 9.4, 10., 9.4
    if layout == "quadrant_atlas":
        panel = np.minimum(1, np.floor(u * 2)) + np.minimum(1, np.floor(v * 2)) * 2
        reference = row["reference_dimensions_m"]
        local_u = np.clip((np.mod(u * 2, 1) - .006) / .988, 0, 1)
        local_v = np.clip((np.mod(v * 2, 1) - .006) / .988, 0, 1)
        return local_u * reference[0] + panel * 16.8, local_v * reference[2], reference[0] * 2 / .988, reference[2] * 2 / .988
    # An integer number of physical periods meets cleanly at the repeat seam.
    actual = max(1, round(pitch / base)) * base
    return u * actual, v * actual, actual, actual


def sample_sheet(row, size, ground=False):
    result = np.empty((size, size, 4), np.float32)
    for start in range(0, size, 96):
        stop = min(size, start + 96)
        u, v = np.meshgrid((np.arange(size, dtype=np.float32) + .5) / size,
                           (np.arange(start, stop, dtype=np.float32) + .5) / size)
        x, y, width, height = coordinates(row, u, v)
        footprint = max(width, height) / size
        elevation, response = art.structure(row["material_family"], x, y, footprint)
        if row["layout"] in ("repeat", "authored_atlas"):
            # dH/d(world metre) = dH/d(sample metre) * sample/world period.
            factor = width / row["meters_per_tile"]
            response[..., :2] = .5 + (response[..., :2] - .5) * factor
        if ground:
            values = np.stack((np.clip((1 - response[..., 2]) / .1, .2, 1.4),
                               np.clip(.5 + elevation / .0015, 0, 1),
                               1 - response[..., 3], np.ones_like(elevation)), axis=-1).clip(0, 1)
        else:
            values = response
        result[start:stop] = values
    return result


def render_albedo(row):
    path = source_path(row)
    size = row["size"]
    with Image.open(path) as image:
        old = image.convert("RGB")
        source_mean = mean_luma(np.asarray(old, np.float32) / 255)
        old = old.resize((size, size), Image.Resampling.LANCZOS)
        # Remove old fine raster noise, retain owner-specific broad paint,
        # masonry courses, atlas borders, repair patches and panel rhythm.
        blur = max(1.5, size / 160) if row["layout"] in ("repeat", "authored_atlas") else 1.
        old = old.filter(ImageFilter.GaussianBlur(blur))
        macro = np.asarray(old, np.float32)[::-1] / 255
    if row["layout"] == "repeat":
        macro = art._pigment.periodic(macro)
    linear_macro = art.linear(macro)
    result = np.empty((size, size, 3), np.float32)
    for start in range(0, size, 96):
        stop = min(size, start + 96)
        u, v = np.meshgrid((np.arange(size, dtype=np.float32) + .5) / size,
                           (np.arange(start, stop, dtype=np.float32) + .5) / size)
        x, y, width, height = coordinates(row, u, v)
        colour, _ = art.sample(row["material_family"], x, y, pixel_metres=max(width, height) / size)
        detail = np.sum(art.linear(colour) * LUMA, axis=-1) / .58
        result[start:stop] = linear_macro[start:stop] * detail[..., None]
    # Keep existing authored tint/compensation valid for all shared consumers.
    for _ in range(4):
        result = np.clip(result * (source_mean / max(1e-6, float(np.mean(np.sum(result * LUMA, axis=-1))))), 0, 1)
    return art.gamma(result), source_mean


def publish_bar():
    contract = json.loads(BAR.read_text(encoding="utf8"))
    row = dict(family="CityBarFacade", source="", source_name="", source_asset="", material_family="AuthoredFacade",
               layout="unique_metric_charts", unique_uv=True, full_color=True, size=contract["albedo_size"],
               response_size=contract["response_size"], ground_response="", ground_response_size=0,
               meters_per_tile=1., wrap_mode="Clamp", renderer_names=[p["source_name"] for p in contract["parts"] if p["unique_surface"]],
               chart_count=contract["projection"]["chart_count"], chart_manifest_sha256=sha(BAR.read_bytes()))
    for kind, size, linear in (("albedo", row["size"], False), ("response", row["response_size"], True)):
        scale = size / row["size"]
        values = np.zeros((size, size, 4 if linear else 3), np.float32)
        if linear:
            values[:] = (.5, .5, .96, 1)
        else:
            values[:] = (.35, .35, .35)
        coverage = np.zeros((size, size), bool)
        for chart in contract["projection"]["charts"]:
            low = np.maximum(0, np.floor(np.asarray(chart["guarded_min_px"]) * scale).astype(int))
            high = np.minimum(size, np.ceil(np.asarray(chart["guarded_max_px"]) * scale).astype(int))
            if np.any(high <= low):
                raise ValueError("Empty City bar chart")
            # At the half-size response, neighbouring half-pixel guards may
            # share a pixel. Actual chart interiors are still disjoint.
            coverage[low[1]:high[1], low[0]:high[0]] = True
            x, y = np.meshgrid(np.arange(low[0], high[0]) + .5, np.arange(low[1], high[1]) + .5)
            density = chart["texels_per_metre"] * scale
            origin = np.asarray(chart["origin_px"]) * scale
            u = chart["origin_m"][0] + (x - origin[0]) / density
            v = chart["origin_m"][1] + (y - origin[1]) / density
            # Replicate the last physical surface sample through each guard.
            epsilon = .5 / density
            u = np.clip(u, chart["origin_m"][0] + epsilon, chart["origin_m"][0] + max(epsilon, chart["extent_m"][0] - epsilon))
            v = np.clip(v, chart["origin_m"][1] + epsilon, chart["origin_m"][1] + max(epsilon, chart["extent_m"][1] - epsilon))
            colour, response = art.sample(chart["family"], u, v, tint=chart["tint"], part_name=chart["mesh"], pixel_metres=1 / density)
            values[low[1]:high[1], low[0]:high[0]] = response if linear else colour
        if coverage.mean() < .1:
            raise ValueError("City bar atlas chart coverage unexpectedly empty")
        resource = contract[kind]
        row[kind] = resource
        row[kind + "_sha256"] = write_map(resource, values, linear, True)
        if not linear:
            row["mean_linear_luminance"] = round(mean_luma(values), 8)
    return row


def check_physics():
    u, v = np.meshgrid(np.linspace(.13, .83, 13), np.linspace(.17, .91, 11))
    epsilon = .00005
    for family, _, _, _, _ in art.SPECS:
        _, response = art.structure(family, u, v, .002)
        dx = (art.structure(family, u + epsilon, v, .002)[0] - art.structure(family, u - epsilon, v, .002)[0]) / (2 * epsilon)
        dy = (art.structure(family, u, v + epsilon, .002)[0] - art.structure(family, u, v - epsilon, .002)[0]) / (2 * epsilon)
        if max(float(np.max(abs(dx - (response[..., 0] * 2 - 1)))), float(np.max(abs(dy - (response[..., 1] * 2 - 1))))) > .001:
            raise ValueError("City metric height derivative/sign drift: " + family)
    _, response, _ = art.bar_brick_structure(u, v, .01)
    dx = (art.bar_brick_structure(u + epsilon, v, .01)[0] - art.bar_brick_structure(u - epsilon, v, .01)[0]) / (2 * epsilon)
    dy = (art.bar_brick_structure(u, v + epsilon, .01)[0] - art.bar_brick_structure(u, v - epsilon, .01)[0]) / (2 * epsilon)
    if np.max(abs(np.stack((dx, dy), axis=-1) - (response[..., :2] * 2 - 1))) > .001:
        raise ValueError("Unique Bar mortar metric derivatives drifted")


def validate():
    art.sources(); check_physics()
    manifest = json.loads(MANIFEST.read_text(encoding="utf8"))
    if manifest["design_id"] != art.DESIGN or manifest["gpu_budget_bytes"] != art.BUDGET:
        raise ValueError("City manifest contract drift")
    recipes = {r["family"]:r for r in art.catalogue()}
    if {r["family"] for r in manifest["sheets"]} != set(recipes) | {"CityBarFacade"}:
        raise ValueError("City explicit owner coverage drift")
    estimated = 0; resources = set()
    for row in manifest["sheets"]:
        if row["family"] in recipes:
            recipe = recipes[row["family"]]
            if any(row.get(key) != value for key, value in recipe.items() if key != "ground"):
                raise ValueError("City owner recipe drifted: " + row["family"])
            if row["source_sha256"] != sha(source_path(recipe).read_bytes()):
                raise ValueError("Original City source changed: " + row["family"])
        for kind, field, linear in (("albedo", "size", False), ("response", "response_size", True), ("ground_response", "ground_response_size", True)):
            if not row.get(kind):
                continue
            resource = row[kind]; size = row[field]
            if resource in resources:
                raise ValueError("Repeated City resource allocation")
            resources.add(resource)
            path = ROOT / ("Assets/Resources/" + resource + ".png")
            raw = path.read_bytes()
            if sha(raw) != row[kind + "_sha256"]:
                raise ValueError("City map bytes drifted: " + resource)
            with Image.open(io.BytesIO(raw)) as image:
                if image.size != (size, size):
                    raise ValueError("City map size drifted: " + resource)
                data = np.asarray(image, np.float32) / 255
            if kind == "albedo":
                measured = mean_luma(data[..., :3])
                if abs(measured - row["mean_linear_luminance"]) > .00001:
                    raise ValueError("City measured mean drifted: " + resource)
                if row.get("source_mean_linear_luminance") is not None and abs(measured - row["source_mean_linear_luminance"]) > .001:
                    raise ValueError("City authored palette compensation drifted: " + resource)
            elif kind == "response" and row["family"] in recipes:
                expected = sample_sheet(recipes[row["family"]], size)
                if np.max(abs(data[::-1] - expected)) > 1 / 255 + .0001:
                    raise ValueError("City packed physical field/orientation drifted: " + resource)
            if Path(str(path) + ".meta").read_text(encoding="utf8") != meta(path, size, linear, clamp=row["wrap_mode"] == "Clamp"):
                raise ValueError("City import profile drifted: " + resource)
            estimated += bc7_bytes(size)
    if manifest["estimated_gpu_bytes"] != estimated or estimated > art.BUDGET:
        raise ValueError("City BC7 mip budget mismatch")
    if manifest["source_provenance"] != art.provenance() or manifest["sheets"][-1]["chart_manifest_sha256"] != sha(BAR.read_bytes()):
        raise ValueError("City generation provenance drifted")
    if manifest.get("authoring_recipe_sha256") != sha(Path(art.__file__).read_bytes() + Path(__file__).read_bytes()):
        raise ValueError("City authoring recipe changed; rebuild affected sheets")
    print(f"CITY SURFACES OK: {len(recipes)} source-aligned sheets + guarded Bar atlas; {len(resources)} BC7 maps {estimated}/{art.BUDGET} bytes; independent metric response and source means.", flush=True)


def build(only=None):
    art.sources(); check_physics()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    retained = {}
    if only:
        previous = json.loads(MANIFEST.read_text(encoding="utf8"))
        if previous["source_provenance"] != art.provenance():
            raise ValueError("Changed source pigments require a complete City rebuild")
        retained = {row["family"]:row for row in previous["sheets"]}
        if not set(only) <= {row["family"] for row in art.catalogue()} | {"CityBarFacade"}:
            raise ValueError("Unknown selected City sheet")
    bar = retained["CityBarFacade"] if only and "CityBarFacade" not in only else publish_bar()
    print("City Bar atlas pair published.", flush=True)
    rows = []
    for recipe in art.catalogue():
        if only and recipe["family"] not in only:
            rows.append(retained[recipe["family"]])
            continue
        row = dict(recipe)
        row.pop("ground")
        row.update(unique_uv=False, full_color=False, response_size=row["size"] // 2,
                   ground_response="", ground_response_size=512 if recipe["ground"] else 0, wrap_mode="Repeat",
                   source_sha256=sha(source_path(recipe).read_bytes()))
        albedo, source_mean = render_albedo(recipe)
        row["source_mean_linear_luminance"] = round(source_mean, 8)
        for kind, size, linear in (("albedo", row["size"], False), ("response", row["response_size"], True), ("ground_response", row["ground_response_size"], True)):
            if size == 0:
                continue
            values = albedo if kind == "albedo" else sample_sheet(recipe, size, kind == "ground_response")
            resource = PREFIX + row["family"] + {"albedo":"Albedo", "response":"Response", "ground_response":"GroundResponse"}[kind]
            row[kind] = resource
            row[kind + "_sha256"] = write_map(resource, values, linear)
            if kind == "albedo":
                row["mean_linear_luminance"] = round(mean_luma(np.uint8(np.clip(values * 255 + .5, 0, 255)).astype(np.float32) / 255), 8)
        rows.append(row)
        print("City surface:", row["family"], flush=True)
    rows.append(bar)
    # Record actual encoded unique mean, exactly as imported rather than float intermediates.
    with Image.open(OUTPUT / "CityBarFacadeAlbedo.png") as image:
        rows[-1]["mean_linear_luminance"] = round(mean_luma(np.asarray(image, np.float32) / 255), 8)
    estimated = sum(bc7_bytes(row[field]) for row in rows for kind, field in (("albedo", "size"), ("response", "response_size"), ("ground_response", "ground_response_size")) if row.get(kind))
    if estimated > art.BUDGET:
        raise ValueError(f"City textures exceed fixed cap: {estimated}/{art.BUDGET}")
    result = dict(design_id=art.DESIGN, generator_version=art.VERSION, gpu_budget_bytes=art.BUDGET,
                  estimated_gpu_bytes=estimated, channels=["signed_physical_slope_u", "signed_physical_slope_v", "roughness", "cavity"],
                  ground_channels=["smoothness_multiplier", "centred_height", "cavity_tendency", "opaque"],
                  source_provenance=art.provenance(), sheets=rows, additional_maps=[],
                  authoring_recipe_sha256=sha(Path(art.__file__).read_bytes() + Path(__file__).read_bytes()))
    MANIFEST.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf8", newline="\n")
    sidecar = Path(str(MANIFEST) + ".meta")
    if not sidecar.exists():
        sidecar.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid5(uuid.NAMESPACE_URL, MANIFEST.relative_to(ROOT).as_posix()).hex + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf8")
    folder_meta = Path(str(OUTPUT) + ".meta")
    if not folder_meta.exists():
        folder_meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid5(uuid.NAMESPACE_URL, OUTPUT.relative_to(ROOT).as_posix()).hex + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf8")
    validate()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--only", nargs="+", help="Rebuild named sheets from a complete existing manifest; validate every retained output.")
    args = parser.parse_args()
    if args.validate_only:
        validate()
    else:
        build(args.only)
