"""One bounded, non-tiled warehouse painting and its registered physical response.

Placement counts come from the projection of the actual C# planner. UV variants
are authored here and exported as passive meshes; runtime never edits vertices,
unwraps a mesh, or allocates a material for a placement.
"""
from __future__ import annotations

from collections import Counter
import hashlib
import json
import math
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SIZE = 4096
RESPONSE_SIZE = 2048
VERSION = "2.2.0"
ATLAS = "WarehouseMasterAtlas"
MARGIN = 8 / SIZE
AUX_HEIGHT = 1 / 16
AUX_INSET = 8
PRIORITY = {"Shell": .28, "Platform": .34, "Rail": .30, "RoomShell": .65,
            "Stair": .85, "ChainDrum": 4., "PusherHead": 3., "PusherFrame": 2.,
            "GateControl": 4., "Gate": 2., "TechnicalProps": 2., "LockerProps": 2.,
            "BreakProps": 2., "FanHousing": .35, "FanRotor": .6, "LiftFrame": .55,
            "LiftPlatform": 1.5, "TwistedFrame": .5, "Pipes": .55}
AUX_ROLES = ("Concrete", "ConcreteDark", "Steel", "WornSteel", "Rust", "Timber",
             "Cardboard", "Rubber", "LockSteel", "SafetyYellow", "SpoiledFish",
             "SpoiledFishFlesh", "SpoiledFishDark", "Glass", "Lamp", "SignalRed")
COUNTS = {}
CHART_SUMMARY = {}
CONNECTED_KINDS = frozenset(("Shell", "Platform", "RoomShell", "Stair", "Rail"))
CONNECTED_SEED = .271828


def connected_placements(layout):
    """Only invariant structural slots may own a shared world-frame painting."""
    result = None
    for template in layout["templates"]:
        frames, ordinals = {}, Counter()
        records = [(row["kind"], row["position"], row["yaw"], 1.) for row in template["modules"]]
        records += [("Stair", row["foot"], 0., 1.) for row in template["stairs"]]
        records += [("Rail", row["position"], row["yaw"], row["length"]) for row in template["rails"]]
        for name, position, yaw, length in records:
            slot = ordinals[name]
            ordinals[name] += 1
            if name in CONNECTED_KINDS:
                frames[(name, slot)] = (tuple(position), float(yaw), float(length))
        if result is not None and frames != result:
            raise RuntimeError("Connected warehouse painting requires identical structural slot transforms in every layout")
        result = frames
    return result


def placement_frame(record):
    position, yaw, length = record
    radians = math.radians(yaw)
    c, s = math.cos(radians), math.sin(radians)
    # Unity yaw in Blender's (Unity X, Unity Z, Unity Y) coordinate order.
    matrix = np.array(((c * length, s, 0.), (-s * length, c, 0.), (0., 0., 1.)))
    return matrix, np.array((position[0], position[2], position[1]))


def connected_surface_context(roots):
    layout = json.loads((ROOT / "ArtSource/CombatWarehouse/WarehouseLayout.json").read_text(encoding="utf8"))
    placements = connected_placements(layout)
    frames = {key: placement_frame(record) for key, record in placements.items()}
    shell = next(root for root in roots if kind(root) == "Shell")
    low, high = np.array(shell["bp_low"]), np.array(shell["bp_high"])
    levels = sorted({0., *(record[0][1] for key, record in placements.items() if key[0] == "Platform")})
    causes = []
    for root in roots:
        frame = frames.get((kind(root), root.get("bp_slot", 0)))
        if frame is None:
            continue
        matrix, offset = frame
        for category, center, dimensions in physical_causes(root):
            causes.append((category, matrix @ center + offset, np.abs(matrix) @ dimensions))
    return layout, frames, low, high, levels, causes


def nearby_causes(root, matrix, offset, causes):
    low, high = np.array(root["bp_low"]), np.array(root["bp_high"])
    corners = np.array([[x, y, z] for x in (low[0], high[0]) for y in (low[1], high[1]) for z in (low[2], high[2])])
    corners = corners @ matrix.T + offset
    low, high = corners.min(axis=0), corners.max(axis=0)
    # Five Gaussian radii discard only negligible tails. Nearby placements
    # receive the same causal field at their join, including rail foot runoff.
    return [cause for cause in causes if cause[0] == "floor" or np.all(
        np.maximum(np.maximum(low[:2] - cause[1][:2], cause[1][:2] - high[:2]), 0.)
        <= 5 * (cause[2][:2] * .7 + .08))]


def kind(root):
    return root.get("bp_kind", root.name.split("~")[0])


def mesh_name(obj):
    return obj.get("bp_mesh", obj.name.split(".")[0])


def key(root, obj):
    return root.name + "/" + mesh_name(obj)


def atlas_mesh(obj):
    return obj.type == "MESH" and not mesh_name(obj).startswith("Collision_") and obj.get("bp_surface") != "WearDecal"


def placement_counts(names):
    layout = json.loads((ROOT / "ArtSource/CombatWarehouse/WarehouseLayout.json").read_text(encoding="utf8"))
    source = ROOT / "Assets/Scripts/Runtime/Combat/WarehouseArenaPlan.cs"
    authority = layout["authority"]
    # The exporter owns the exact spelling of its provenance fields. Retain
    # its whole authority record in the generated contract as well.
    hashes = [v for k, v in authority.items() if "sha256" in k and "source" in k]
    if hashes and hashlib.sha256(source.read_bytes()).hexdigest() not in hashes:
        raise RuntimeError("Warehouse layout source changed; rerun its placement exporter")
    counts = {name: 1 for name in names}
    for template in layout["templates"]:
        present = Counter(record["kind"] for record in template["modules"])
        present["Stair"] += len(template["stairs"])
        present["Rail"] += len(template["rails"])
        present.update(record["kind"] for record in template.get("lamps", []))
        for name in counts:
            counts[name] = max(counts[name], present[name])
    return counts, authority


def copy_variant(root, slot):
    clone = root.copy()
    clone.name = kind(root) + "~" + str(slot)
    clone["bp_slot"] = slot
    bpy.context.collection.objects.link(clone)
    copied = {root: clone}
    for obj in root.children_recursive:
        if obj.type == "MESH" and not atlas_mesh(obj):
            continue
        duplicate = obj.copy()
        if obj.type == "MESH":
            duplicate.data = obj.data.copy()
        duplicate.parent = copied[obj.parent]
        bpy.context.collection.objects.link(duplicate)
        copied[obj] = duplicate
    return clone


def expand(roots):
    global COUNTS
    COUNTS, _ = placement_counts([kind(root) for root in roots])
    for root in list(roots):
        root["bp_kind"] = kind(root)
        root["bp_slot"] = 0
        for obj in root.children_recursive:
            if obj.type == "MESH":
                obj["bp_mesh"] = mesh_name(obj)
        roots.extend(copy_variant(root, slot) for slot in range(1, COUNTS[kind(root)]))


def select(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def unwrap(roots, items):
    from warehouse_master_projection import unwrap as project
    return project(roots, items)


def cache_uvs(roots):
    return {key(root, obj): {"atlas": obj.get("bp_atlas") if atlas_mesh(obj) else None,
        "channels": [[[round(float(v), 8) for v in loop.uv] for loop in layer.data]
                     for layer in obj.data.uv_layers]}
        for root in roots for obj in root.children_recursive if obj.type == "MESH"}


def apply_uvs(root, cache):
    for obj in root.children_recursive:
        if obj.type != "MESH":
            continue
        record = cache[key(root, obj)]
        while len(obj.data.uv_layers) < 2:
            obj.data.uv_layers.new()
        for layer, values, name in zip(obj.data.uv_layers, record["channels"], ("WarehouseUV", "WarehouseMetres"), strict=True):
            layer.name = name
            layer.data.foreach_set("uv", np.asarray(values, dtype=np.float32).reshape(-1))
        obj.data.uv_layers.active_index = 0
        obj.data.uv_layers[0].active_render = True
        if record["atlas"]:
            obj["bp_atlas"] = record["atlas"]



def uv_signature(cache):
    return hashlib.sha256(json.dumps(cache, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def auxiliary():
    width, height = SIZE // 8, SIZE // 32
    return [{"role": role, "rect": [(width - 2 * AUX_INSET) / SIZE, (height - 2 * AUX_INSET) / SIZE,
            ((index % 8) * width + AUX_INSET) / SIZE, ((index // 8) * height + AUX_INSET) / SIZE]}
            for index, role in enumerate(AUX_ROLES)]


def pixels_for_triangle(uvs, size):
    pixels = uvs * size
    low = np.maximum(0, np.floor(pixels.min(axis=0)).astype(int))
    high = np.minimum(size - 1, np.ceil(pixels.max(axis=0)).astype(int))
    if np.any(high < low):
        return None
    matrix = (pixels[1:] - pixels[0]).T
    if abs(np.linalg.det(matrix)) < 1e-12:
        return None
    xx, yy = np.meshgrid(np.arange(low[0], high[0] + 1) + .5, np.arange(low[1], high[1] + 1) + .5)
    bary = np.stack((xx - pixels[0, 0], yy - pixels[0, 1]), axis=-1) @ np.linalg.inv(matrix).T
    inside = (bary[..., 0] >= 1e-5) & (bary[..., 1] >= 1e-5) & (bary.sum(axis=-1) <= 1 - 1e-5)
    return low, high, bary, inside


SOURCE_FIELDS = None


def source_fields():
    global SOURCE_FIELDS
    if SOURCE_FIELDS is not None:
        return SOURCE_FIELDS
    import warehouse_surface_authoring as shared
    source_data = {}
    for name, image in shared.source_images()[0].items():
        data = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
        image.pixels.foreach_get(data)
        data = data.reshape(image.size[1], image.size[0], 4)[..., :3]
        # Retain native source detail; the target's mip chain handles minification.
        source_data[name] = (data, data.mean(axis=(0, 1)))
    SOURCE_FIELDS = source_data
    return SOURCE_FIELDS


def raster(roots, size, paint=None):
    import warehouse_surface_authoring as shared
    from warehouse_master_projection import chart_policy
    if paint not in (None, "colour", "response"):
        raise ValueError("Choose one bounded raster output")
    coverage = np.zeros((size, size), dtype=np.int32)
    charts = {}
    conflicts = 0
    colour = physical = None
    if paint == "colour":
        colour = np.empty((size, size, 4), dtype=np.float32)
        colour[..., :3] = (.045, .05, .04)
        colour[..., 3] = 1
    elif paint == "response":
        physical = np.empty((size, size, 4), dtype=np.float32)
        physical[..., :2] = .5
        physical[..., 2:] = (.8, 1.)
    source_data = source_fields() if paint else {}
    context = connected_surface_context(roots) if paint else None
    density, stretch, model_rows = [], [], []
    for root_index, root in enumerate(roots):
        slot = root.get("bp_slot", 0)
        seed = int(hashlib.sha256((kind(root) + str(slot)).encode()).hexdigest()[:8], 16) / 2**32
        area_total = uv_total = 0.
        causes = physical_causes(root) if paint else ()
        frame = context[1].get((kind(root), slot)) if context else None
        if frame is not None:
            causes = nearby_causes(root, *frame, context[5])
        for obj in root.children_recursive:
            if not atlas_mesh(obj):
                continue
            mesh = obj.data
            mesh.calc_loop_triangles()
            uv = mesh.uv_layers[0].data
            transform = root.matrix_world.inverted() @ obj.matrix_world
            normal_matrix = transform.to_3x3().inverted().transposed()
            points = np.array([tuple(transform @ vertex.co) for vertex in mesh.vertices], dtype=np.float64)
            low = np.array(root["bp_low"], dtype=float)
            high = np.array(root["bp_high"], dtype=float)
            for triangle in mesh.loop_triangles:
                p = points[list(triangle.vertices)]
                u = np.array([uv[index].uv[:] for index in triangle.loops], dtype=np.float64)
                area = np.linalg.norm(np.cross(p[1] - p[0], p[2] - p[0])) * .5
                if area < 1e-12:
                    continue
                uv_area = abs(np.linalg.det((u[1:] - u[0]).T)) * .5
                if not np.isfinite(u).all() or np.any(u < -1e-6) or np.any(u > 1 + 1e-6) or uv_area < 1e-15:
                    raise RuntimeError("Invalid unique master UV " + key(root, obj) + " uv=" + str(u.tolist()) + " area=" + str(uv_area))
                if not paint and area > 1e-9:
                    first, second = p[1:] - p[0]
                    ex = first / np.linalg.norm(first)
                    ey = np.cross(np.cross(first, second), first)
                    ey /= max(np.linalg.norm(ey), 1e-20)
                    plane = np.array([[np.dot(first, ex), np.dot(second, ex)],
                                      [np.dot(first, ey), np.dot(second, ey)]])
                    singular = np.linalg.svd((u[1:] - u[0]).T @ np.linalg.inv(plane), compute_uv=False)
                    distortion = float(singular[0] / max(singular[1], 1e-20))
                    if distortion > 3.5:
                        raise RuntimeError(f"Warehouse master UV stretch {distortion:.3f}: {key(root, obj)}")
                    stretch.append(distortion)
                area_total += area
                uv_total += uv_area
                density.append(math.sqrt(uv_area / area) * size)
                record = pixels_for_triangle(u, size)
                if record is None:
                    continue
                lower, upper, bary, inside = record
                target = np.s_[lower[1]:upper[1] + 1, lower[0]:upper[0] + 1]
                polygon = mesh.polygons[triangle.polygon_index]
                polygon_normal = normal_matrix @ polygon.normal
                axis = max(range(3), key=lambda i: abs(polygon_normal[i]))
                sign = 1 if polygon_normal[axis] >= 0 else -1
                chart, _ = chart_policy(root, obj, points[list(polygon.vertices)], polygon_normal)
                chart_id = charts.setdefault(chart, len(charts) + 1)
                previous = coverage[target]
                conflicts += int(np.count_nonzero(inside & (previous != 0) & (previous != chart_id)))
                fresh = inside & (previous == 0)
                previous[inside] = chart_id
                if paint:
                    inside = fresh
                if not paint or not inside.any():
                    continue
                position = p[0] + bary @ (p[1:] - p[0])
                normal = np.cross(p[1] - p[0], p[2] - p[0])
                normal /= max(np.linalg.norm(normal), 1e-12)
                # A chart is a coherent projected painting. Equivalent parallel
                # fittings share its physical field inside this one placement;
                # there is no repetition between placements or shader tiling.
                projected = position.copy()
                projected[..., axis] = low[axis] if sign > 0 else high[axis]
                plane_normal = np.zeros(3)
                plane_normal[axis] = sign
                if frame is None:
                    rgb, response = surface(projected, plane_normal, low, high, obj["bp_surface"], seed, source_data, causes)
                    field_axis = axis
                else:
                    matrix, offset = frame
                    field_normal = np.linalg.inv(matrix).T @ plane_normal
                    field_normal /= np.linalg.norm(field_normal)
                    field_axis = int(np.argmax(np.abs(field_normal)))
                    rgb, response = surface(position @ matrix.T + offset, field_normal,
                        context[2], context[3], obj["bp_surface"], CONNECTED_SEED,
                        source_data, causes, context[4])
                # Rotate object-space physical slopes into this packed island's
                # UV frame, including reflected/rotated islands. Runtime uses
                # this SAME normalized UV0 derivative basis on the posed mesh.
                basis = np.linalg.solve(u[1:] - u[0], p[1:] - p[0])
                if frame is not None:
                    basis = basis @ frame[0].T
                basis /= np.maximum(np.linalg.norm(basis, axis=1, keepdims=True), 1e-12)
                axes = ((1, 2), (0, 2), (0, 1))[field_axis]
                gradient = np.zeros_like(position)
                gradient[..., axes[0]] = response[..., 0] * 2 - 1
                gradient[..., axes[1]] = response[..., 1] * 2 - 1
                response[..., 0] = .5 + (gradient @ basis[0]) * .5
                response[..., 1] = .5 + (gradient @ basis[1]) * .5
                if obj["bp_surface"] == "NumberPlate":
                    image = shared.number_source_image(root)
                    values = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
                    image.pixels.foreach_get(values)
                    values = values.reshape(image.size[1], image.size[0], 4)
                    metres = np.array([mesh.uv_layers[1].data[i].uv[:] for i in triangle.loops])
                    metric = metres[0] + bary @ (metres[1:] - metres[0])
                    xx = np.clip(((metric[..., 0] / .416 + .5) * image.size[0]).astype(int), 0, image.size[0] - 1)
                    yy = np.clip(((metric[..., 1] / .476 + .5) * image.size[1]).astype(int), 0, image.size[1] - 1)
                    rgb = values[yy, xx, :3]
                if colour is not None:
                    colour[target][inside, :3] = rgb[inside]
                if physical is not None:
                    physical[target][inside] = response[inside]
        model_rows.append({"model": root.name, "surface_area_m2": round(area_total, 4),
            "uv_area": round(uv_total, 7), "texels_per_metre": round(math.sqrt(uv_total / max(area_total, 1e-8)) * size, 3)})
        if root_index % 35 == 0:
            print("WAREHOUSE MASTER RASTER", size, root_index, "/", len(roots), flush=True)
    overlap = conflicts
    if overlap:
        raise RuntimeError(f"Placement master UV overlap: {overlap} pixels")
    return colour, physical, coverage, {"occupied_texels": int((coverage > 0).sum()), "overlap_texels": overlap,
        "texels_per_metre_median": round(float(np.median(density)), 3),
        "triangle_stretch_max": round(max(stretch, default=1.), 4), "model_density": model_rows}


def physical_causes(root):
    causes = []
    for obj in root.children_recursive:
        if not atlas_mesh(obj):
            continue
        name = mesh_name(obj).lower()
        category = "oxide" if any(word in name for word in ("bearing", "hinge", "flange", "cylinder", "drain", "feet", "shoe")) else \
                   "handling" if any(word in name for word in ("handle", "wheel", "lockerdoor", "controldoor", "tabletop", "pushplate")) else None
        if category is None:
            continue
        matrix = root.matrix_world.inverted() @ obj.matrix_world
        points = np.array([tuple(matrix @ vertex.co) for vertex in obj.data.vertices])
        low, high = points.min(axis=0), points.max(axis=0)
        causes.append((category, (low + high) * .5, np.maximum(high - low, .12)))
    if kind(root) == "Shell":
        layout = json.loads((ROOT / "ArtSource/CombatWarehouse/WarehouseLayout.json").read_text(encoding="utf8"))
        for machine in layout["templates"][0]["mechanisms"]:
            x, y, z = machine["position"]
            if y < 2:
                causes.append(("floor", np.array((x, z, 0.)), np.array((2., 2., 1.))))
    return causes


def surface(p, n, low, high, role, seed, sources, causes=(), floor_levels=None):
    """Registered colour/roughness/cavity causes in a fixed local metre frame."""
    import warehouse_surface_authoring as shared
    x, y, z = p[..., 0], p[..., 1], p[..., 2]  # Blender Z is authored Unity height.
    phase = seed * math.tau
    grain = np.sin(x * 1.7 + phase + np.sin(y * .9)) * np.cos(z * 1.1 - phase)
    broad = .5 + .22 * grain + .13 * np.sin(x * .59 - y * .81 + z * .31 + phase)
    vertical = 1 - abs(n[2])
    drip = np.maximum(0, np.sin(x * 7.3 + y * 5.1 + phase) - .72) * vertical
    damp = np.clip((low[2] + .24 - z) * 2.8, 0, 1) * (.6 + .4 * broad)
    if floor_levels is not None:
        floor_distance = np.minimum.reduce([abs(z - level) for level in floor_levels])
        damp = np.exp(-floor_distance * 6) * (.35 + .25 * broad)
    # Face-frame boundaries, original joints and horizontal bearing surfaces
    # supply contact/handling locations. Tiny paint grain is subordinate.
    edge = np.minimum.reduce([np.minimum(abs(p[..., axis] - low[axis]), abs(high[axis] - p[..., axis]))
        for axis in range(3) if abs(n[axis]) < .9])
    polish = np.exp(-edge * 36) * (1 - damp) * (.55 + .45 * broad)
    contact_oxide = np.zeros_like(x)
    handled = np.zeros_like(x)
    for category, center, dimensions in causes:
        horizontal = np.exp(-(((x - center[0]) / (dimensions[0] * .7 + .08)) ** 2 if abs(n[0]) < .9 else 0)
                            - (((y - center[1]) / (dimensions[1] * .7 + .08)) ** 2 if abs(n[1]) < .9 else 0))
        if category == "floor":
            damp = np.maximum(damp, horizontal * np.exp(-abs(z - low[2]) * 9) * .8)
        elif category == "oxide":
            below = np.clip((center[2] + dimensions[2] * .5 - z) / max(.2, dimensions[2]), 0, 1)
            runoff = horizontal * below * np.exp(-np.maximum(0, center[2] - z) * .9)
            contact_oxide = np.maximum(contact_oxide, runoff)
        else:
            reach = horizontal * np.exp(-((z - center[2]) / (dimensions[2] * .65 + .08)) ** 2)
            handled = np.maximum(handled, reach)
    polish = np.maximum(polish, handled * (1 - damp) * .7)
    drip = np.maximum(drip * .25, contact_oxide * .8)
    oxide = np.clip(damp * .55 + contact_oxide * .60 + (1 - broad) * .10, 0, .85)
    tint = np.array(shared.ROLE_TINT.get(role, {"SpoiledFish": (.40, .45, .37),
        "SpoiledFishFlesh": (.57, .57, .43), "SpoiledFishDark": (.14, .19, .145)}.get(role, (.3, .32, .25))))
    source_name = shared.ROLE_SOURCE.get(role)
    if source_name and "Warehouse" + source_name in sources:
        data, average = sources["Warehouse" + source_name]
        axes = (1, 2) if abs(n[0]) > .8 else (0, 2) if abs(n[1]) > .8 else (0, 1)
        # Sources are authoring ingredients only; this projected composition is
        # frozen into the one unique atlas and is never tiled by the renderer.
        # Mirror the authoring ingredient continuously. A hard fractional wrap
        # would cut the source painting every 7.69 world metres, even inside a
        # single floor panel. Bilinear filtering retains equal boundary colours.
        uu = 1 - abs((p[..., axes[0]] * .13 + seed * .37) % 2 - 1)
        vv = 1 - abs((p[..., axes[1]] * .13 + seed * .61) % 2 - 1)
        xx, yy = uu * (data.shape[1] - 1), vv * (data.shape[0] - 1)
        ix, iy = xx.astype(int), yy.astype(int)
        jx, jy = np.minimum(ix + 1, data.shape[1] - 1), np.minimum(iy + 1, data.shape[0] - 1)
        fx, fy = (xx - ix)[..., None], (yy - iy)[..., None]
        sampled = (data[iy, ix] * (1 - fx) + data[iy, jx] * fx) * (1 - fy) + \
                  (data[jy, ix] * (1 - fx) + data[jy, jx] * fx) * fy
        rgb = (average * .10 + sampled * .90) * tint
    else:
        rgb = np.broadcast_to(tint, (*x.shape, 3)).copy()
    rgb *= (.87 + broad * .22 - damp * .18 - drip * .14)[..., None]
    metal = role in ("Steel", "WornSteel", "Rust", "LockSteel", "SafetyYellow")
    rough_base = {"Concrete": .89, "ConcreteDark": .91, "Steel": .69, "WornSteel": .40,
        "Rust": .93, "LockSteel": .38, "Timber": .83, "Cardboard": .95, "Rubber": .84,
        "SafetyYellow": .63}.get(role, .75)
    if metal:
        rust_colour = np.array((.19, .075, .023))
        amount = oxide * (.40 if role != "Rust" else .20)
        rgb = rgb * (1 - amount[..., None]) + rust_colour * amount[..., None]
        rgb = rgb * (1 - polish[..., None] * .22) + np.array((.20, .22, .19)) * polish[..., None] * .22
    if role == "Timber":
        rgb *= (1 - (.035 + damp * .045) * np.sin(x * 32 + np.sin(y * 3)))[..., None]
    # Physical pits and broken coating have medium-scale edges that survive
    # the game's composite. Their mask is shared by colour AND response.
    pits = np.clip((np.sin(x * 19 + phase + np.cos(y * 6)) * np.cos(z * 17 - phase) - .48) * 1.6, 0, 1)
    if role == "Rust":
        rgb *= (1 - pits * .36)[..., None]
    elif role in ("Steel", "SafetyYellow"):
        chips = np.clip((grain + contact_oxide * .8 - .26) * 1.8, 0, .75) * (1 - handled * .5)
        rgb = rgb * (1 - chips[..., None]) + np.array((.16, .065, .022)) * chips[..., None]
        oxide = np.maximum(oxide, chips)
    rough = np.clip(rough_base + oxide * .12 - polish * (.24 if metal else .06) - damp * .17, .25, .98)
    cavity = np.clip(.99 - drip * .12 - oxide * .07 - pits * (.23 if role == "Rust" else .05), .65, 1)
    # Explicit physical fields; height is not derived from albedo luminance.
    slope_u = np.sin(x * 29 + y * 11 + phase) * (.025 if metal else .055)
    slope_v = np.cos(z * 31 - y * 17 + phase) * (.025 if metal else .055)
    response = np.stack((.5 + slope_u, .5 + slope_v, rough, cavity), axis=-1)
    return np.clip(rgb, 0, 1), response


def dilate(data, occupied, rounds):
    valid = occupied > 0
    for _ in range(rounds):
        expanded = valid.copy()
        for axis, shift in ((0, 1), (0, -1), (1, 1), (1, -1)):
            source, target = [slice(None)] * 2, [slice(None)] * 2
            source[axis] = slice(None, -1) if shift > 0 else slice(1, None)
            target[axis] = slice(1, None) if shift > 0 else slice(None, -1)
            source, target = tuple(source), tuple(target)
            nearby = valid[source]
            fill = nearby & ~expanded[target]
            data[target][fill] = data[source][fill]
            expanded[target] |= nearby
        valid = expanded
    return data


def save(data, path, linear=False):
    image = bpy.data.images.new(path.stem, width=data.shape[1], height=data.shape[0], alpha=True, float_buffer=False)
    image.colorspace_settings.name = "Non-Color" if linear else "sRGB"
    image.pixels.foreach_set(np.asarray(data, dtype=np.float32).reshape(-1))
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    return {"name": ATLAS if not linear else "WarehouseMaterialResponse", "source": str(path),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def bake_atlases(roots, directory):
    import warehouse_surface_authoring as shared
    from warehouse_master_continuity import validate_platform_edges
    directory.mkdir(parents=True, exist_ok=True)
    records = []
    # Paint/save/release each map in turn: a 4K validation never allocates an
    # unused pair of float RGBA images, and response baking never retains colour.
    for size, channel, filename in ((SIZE, "colour", ATLAS + ".source.png"),
                                   (RESPONSE_SIZE, "response", "WarehouseMaterialResponse.source.png")):
        colour, physical, coverage, _ = raster(roots, size, channel)
        target = colour if colour is not None else physical
        for row in auxiliary():
            sx, sy, ox, oy = row["rect"]
            x0, y0 = round((ox - AUX_INSET / SIZE) * size), round((oy - AUX_INSET / SIZE) * size)
            x1, y1 = round((ox + sx + AUX_INSET / SIZE) * size), round((oy + sy + AUX_INSET / SIZE) * size)
            yy, xx = np.mgrid[0:y1-y0, 0:x1-x0]
            position = np.stack((xx / max(1, x1-x0-1) * 4 - 2, np.zeros_like(xx), yy / max(1, y1-y0-1) * 4), axis=-1)
            rgb, response = surface(position, np.array((0., -1., 0.)), np.array((-2, 0, 0)), np.array((2, 0, 4)), row["role"], .31, source_fields())
            if channel == "colour":
                target[y0:y1, x0:x1, :3] = rgb
            else:
                target[y0:y1, x0:x1] = response
        dilate(target, coverage, 1)
        layout = json.loads((ROOT / "ArtSource/CombatWarehouse/WarehouseLayout.json").read_text(encoding="utf8"))
        seam_proof = validate_platform_edges(target, size, channel, CHART_SUMMARY, layout)
        print("WAREHOUSE CONTINUOUS PLATFORM EDGES OK " + json.dumps(seam_proof), flush=True)
        records.append(save(target, directory / filename, channel == "response"))
        del colour, physical, target, coverage
    for record in records:
        record["source"] = "ArtSource/CombatWarehouse/Textures/Atlases/" + Path(record["source"]).name
    return shared.source_images()[1], records


def validate_layout(roots, cache, directory=None):
    _, _, coverage, summary = raster(roots, SIZE)
    _, authority = placement_counts([kind(root) for root in roots])
    if directory:
        layout = np.ones((SIZE, SIZE, 4), dtype=np.float32)
        layout[..., :3] = np.where((coverage > 0)[..., None], (.24, .43, .34), (.025, .025, .025))
        save(layout, directory / (ATLAS + ".layout.png"))
    print("WAREHOUSE UNIQUE PLACEMENT UV OK", summary["occupied_texels"], "texels", flush=True)
    return {"version": VERSION, "size": SIZE, "UV0": "unique placement/material face charts; equivalent detail shares its local projection",
        "UV1": "object-anchored metre face projection", "uv_signature": uv_signature(cache),
        "bake": "registered causal colour and physical response; source paintings are authoring ingredients only",
        "runtime_detail": "UV0 unique albedo and unique packed physical response; no tiled colour or response",
        "bake_margin_px": 1, "response_margin_px": 1, "pack_margin_px": 8,
        "detail_projection_reuse": "only inside one placement/material face chart; no shader tiling", "uv_importance": PRIORITY,
        "projection_density": CHART_SUMMARY, "tiled_models": [],
        "structural_field": {"models": sorted(CONNECTED_KINDS), "coordinates": "shared planner-world metres",
            "seed": CONNECTED_SEED, "source_sampling": "continuous mirrored bilinear authoring ingredients",
            "join_validation": "actual atlas RGB and roughness/cavity on every adjoining gallery edge"},
        "families": [{"name": ATLAS, **summary}], "placement_authority": authority,
        "printed_room_numbers": __import__("warehouse_surface_authoring").number_source_manifest()}


def export_variants(items, passive, directory, cache):
    roots = passive.build_objects(items)
    expand(roots)
    for root in roots:
        apply_uvs(root, cache)
    library = bpy.data.objects.new("WarehouseSurfaceVariants", None)
    bpy.context.collection.objects.link(library)
    records = []
    for root in roots:
        slot = root.get("bp_slot", 0)
        if not slot:
            continue
        meshes = []
        for index, obj in enumerate(child for child in root.children_recursive if atlas_mesh(child)):
            node = f"V_{kind(root)}_{slot}_{index}"
            clone = obj.copy()
            clone.data = obj.data.copy()
            clone.name = node
            clone.data.name = node
            clone.parent = library
            clone.matrix_basis.identity()
            bpy.context.collection.objects.link(clone)
            meshes.append({"source": mesh_name(obj), "node": node})
        records.append({"kind": kind(root), "slot": slot, "meshes": meshes})
    passive.export(library, directory / "WarehouseSurfaceVariants.fbx")
    payload = {"version": VERSION, "atlas": ATLAS, "uv_signature": uv_signature(cache),
        "counts": [{"kind": name, "count": count} for name, count in sorted(COUNTS.items())],
        "auxiliary": auxiliary(), "variants": records}
    (directory / "WarehouseSurfaceVariants.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8", newline="\n")
    print("WAREHOUSE PASSIVE UV VARIANTS EXPORTED", len(records), flush=True)


def source_materials(roots, directory):
    image = bpy.data.images.load(str(directory / (ATLAS + ".source.png")), check_existing=True)
    material = bpy.data.materials.new(ATLAS)
    material.use_nodes = True
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = image
    material.node_tree.links.new(texture.outputs["Color"], material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"])
    for root in roots:
        for obj in root.children_recursive:
            if atlas_mesh(obj):
                obj.data.materials.clear()
                obj.data.materials.append(material)
