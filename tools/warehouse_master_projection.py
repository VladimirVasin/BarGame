"""Deterministic placement-local box charts for the one warehouse painting.

Parallel faces of one material may share their physical projection inside one
placement. Every placement/material/axis/sign/priority chart owns a rectangle.
No chart rotates or stretches its two projected metre axes independently.
"""
from __future__ import annotations

import math

import numpy as np


AXES = {0: (1, 2), 1: (0, 2), 2: (0, 1)}
GUTTER = 4  # Per edge: eight colour / four response pixels between contents.
MIN_INNER = 2
HIDDEN_WEIGHT = .05
ARCHITECTURAL_WEIGHTS = {
    "Shell": {"Concrete": .9, "ConcreteDark": .18, "metal": .23},
    "Platform": {"Steel_top": .65, "Steel_bottom": .20,
                 "Steel_side": .25, "WornSteel": .10},
    "Rail": {"all": .14},
    "RoomShell": {"Concrete": .85, "ConcreteDark": .35, "other": .5},
}


def chart_policy(root, obj, points, normal):
    """Share one face classification between UV packing and raster validation.

    Points and normals are root-local Blender coordinates: axis 2 is vertical.
    An exterior shell face has its own small chart; the opposite inward face
    must retain the visible-wall density even when both have the same normal.
    """
    import warehouse_master_atlas as master

    normal = np.asarray(normal, dtype=np.float64)
    normal_length = float(np.linalg.norm(normal))
    if not math.isfinite(normal_length) or normal_length <= 1e-12:
        raise RuntimeError("Warehouse chart requires a finite face normal")
    normal = normal / normal_length
    axis = int(np.argmax(np.abs(normal)))
    sign = 1 if normal[axis] >= 0 else -1
    role = str(obj["bp_surface"])
    model = master.kind(root)
    group = master.mesh_name(obj).split("__", 1)[0]
    bucket = "regular"
    weight = 5. if role == "NumberPlate" else master.PRIORITY.get(model, 1.)
    if model == "Shell":
        weights = ARCHITECTURAL_WEIGHTS[model]
        if role in ("Concrete", "ConcreteDark"):
            weight = weights[role]
        elif role in ("Steel", "WornSteel", "Rust"):
            weight = weights["metal"]
        planar = abs(normal[axis]) >= .999
        hidden_horizontal = axis == 2 and planar and (
            (group == "Floor" and sign < 0) or
            (group == "Roof" and sign > 0))
        wall = group in ("GoalWall", "LoadingWall") or group.startswith("SideWall")
        hidden_wall = wall and role == "Concrete" and axis != 2 and planar and (
            float(np.asarray(points)[:, axis].mean()) * normal[axis] > 0)
        if hidden_horizontal or hidden_wall:
            bucket, weight = "hidden", HIDDEN_WEIGHT
    elif model == "Platform":
        weights = ARCHITECTURAL_WEIGHTS[model]
        if role == "Steel":
            weight = weights["Steel_side" if axis != 2 else
                             "Steel_top" if sign > 0 else "Steel_bottom"]
        elif role == "WornSteel":
            weight = weights[role]
    elif model == "Rail":
        weight = ARCHITECTURAL_WEIGHTS[model]["all"]
    elif model == "RoomShell":
        weights = ARCHITECTURAL_WEIGHTS[model]
        weight = weights.get(role, weights["other"])
    return (root.name, role, axis, sign, bucket), weight


def _skyline_fit(nodes, index, width, height, limit_width, limit_height):
    x = nodes[index][0]
    if x + width > limit_width:
        return None
    remaining, top = width, 0
    for cursor in range(index, len(nodes)):
        _, y, span = nodes[cursor]
        top = max(top, y)
        if top + height > limit_height:
            return None
        remaining -= span
        if remaining <= 0:
            return x, top
    return None


def _skyline_insert(nodes, index, x, y, width, height):
    nodes.insert(index, (x, y + height, width))
    following = index + 1
    while following < len(nodes):
        nx, ny, nw = nodes[following]
        overlap = x + width - nx
        if overlap <= 0:
            break
        if overlap >= nw:
            nodes.pop(following)
        else:
            nodes[following] = (nx + overlap, ny, nw - overlap)
            break
    index = 0
    while index + 1 < len(nodes):
        x, y, width = nodes[index]
        nx, ny, nw = nodes[index + 1]
        if y == ny and x + width == nx:
            nodes[index] = (x, y, width + nw)
            nodes.pop(index + 1)
        else:
            index += 1


def _pack(charts, density, width, height):
    nodes, packed = [(0, 0, width)], {}
    for chart in charts:
        extent = chart["extent"]
        scale = density * chart["weight"]
        inner_width = max(MIN_INNER, math.ceil(extent[0] * scale))
        inner_height = max(MIN_INNER, math.ceil(extent[1] * scale))
        rw, rh = inner_width + 2 * GUTTER, inner_height + 2 * GUTTER
        best = None
        for index in range(len(nodes)):
            position = _skyline_fit(nodes, index, rw, rh, width, height)
            if position is not None:
                x, y = position
                candidate = (y + rh, y, x, index)
                if best is None or candidate < best:
                    best = candidate
        if best is None:
            return None
        _, y, x, index = best
        _skyline_insert(nodes, index, x, y, rw, rh)
        # Rounding/minimum reservations are empty padding, not UV stretching.
        # Both actual metre axes always receive the exact same scale.
        packed[chart["key"]] = (x + GUTTER + (inner_width - extent[0] * scale) * .5,
            y + GUTTER + (inner_height - extent[1] * scale) * .5, scale)
    return packed


def _pack_density(charts, width, height):
    charts = sorted(charts, key=lambda c: (-c["extent"][1] * c["weight"],
        -c["extent"][0] * c["weight"], c["key"]))
    minimum = _pack(charts, 0., width, height)
    if minimum is None:
        raise RuntimeError("Warehouse box-chart gutters exceed the master atlas")
    weighted_area = sum(float(c["extent"][0] * c["extent"][1]) * c["weight"] ** 2 for c in charts)
    if weighted_area <= 0 or not math.isfinite(weighted_area):
        raise RuntimeError("Warehouse projection has no finite physical area")
    low, high = 0., math.sqrt(width * height / weighted_area) * 1.01
    packed = minimum
    for _ in range(22):
        density = (low + high) * .5
        candidate = _pack(charts, density, width, height)
        if candidate is None:
            high = density
        else:
            low, packed = density, candidate
    if low <= 1e-6:
        raise RuntimeError("Warehouse projection could not retain measurable texture density")
    return packed, low


def unwrap(roots, items):
    import bpy
    import warehouse_master_atlas as master
    import warehouse_surface_authoring as shared

    for root, item in zip(roots, items, strict=True):
        shared.preserve_metre_uv(root)
        shared.author_decal_uv(root, item)
    master.expand(roots)
    bpy.context.view_layer.update()
    charts, objects = {}, []
    for root in roots:
        root_low, root_high = np.full(3, np.inf), np.full(3, -np.inf)
        for obj in root.children_recursive:
            if not master.atlas_mesh(obj):
                continue
            mesh = obj.data
            coordinates = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
            mesh.vertices.foreach_get("co", coordinates)
            transform = root.matrix_world.inverted() @ obj.matrix_world
            matrix = np.asarray(transform, dtype=np.float64)
            points = coordinates.reshape(-1, 3) @ matrix[:3, :3].T + matrix[:3, 3]
            normal_matrix = transform.to_3x3().inverted().transposed()
            if not len(points) or not np.isfinite(points).all():
                raise RuntimeError("Warehouse chart requires finite vertices: " + master.key(root, obj))
            root_low = np.minimum(root_low, points.min(axis=0))
            root_high = np.maximum(root_high, points.max(axis=0))
            uv_values = np.zeros((len(mesh.loops), 2), dtype=np.float32)
            objects.append((obj, uv_values))
            for polygon in mesh.polygons:
                if polygon.area < 1e-12:
                    # Unused cap loops still remain inside the authored sheet.
                    uv_values[polygon.loop_start:polygon.loop_start + polygon.loop_total] = (.5, .5)
                    continue
                normal = normal_matrix @ polygon.normal
                polygon_points = points[list(polygon.vertices)]
                chart_key, weight = chart_policy(root, obj, polygon_points, normal)
                projected = polygon_points[:, AXES[chart_key[2]]]
                if chart_key not in charts:
                    charts[chart_key] = {"key": chart_key, "low": projected.min(axis=0),
                        "high": projected.max(axis=0), "faces": [],
                        "weight": weight, "kind": master.kind(root)}
                chart = charts[chart_key]
                chart["low"] = np.minimum(chart["low"], projected.min(axis=0))
                chart["high"] = np.maximum(chart["high"], projected.max(axis=0))
                chart["faces"].append((uv_values, polygon.loop_start, polygon.loop_total, projected))
            obj["bp_atlas"] = master.ATLAS
            obj["bp_projection_box"] = True
        if not np.isfinite(root_low).all() or not np.isfinite(root_high).all():
            raise RuntimeError("Warehouse placement has no projected surfaces: " + root.name)
        root["bp_low"] = root_low.tolist()
        root["bp_high"] = root_high.tolist()
        root["bp_projection_unique"] = True
    for chart in charts.values():
        chart["extent"] = chart["high"] - chart["low"]
        if not np.isfinite(chart["extent"]).all() or min(chart["extent"]) <= 1e-12:
            raise RuntimeError("Warehouse projection chart is degenerate: " + str(chart["key"]))
    bottom = round(master.AUX_HEIGHT * master.SIZE)
    packed, density = _pack_density(list(charts.values()), master.SIZE, master.SIZE - bottom)
    # Prevent a larger nominal bitmap from masking another allocation regression.
    # These are physical chart densities, before any perspective minification.
    required = {"Shell walls/floor": (.9, 24.), "Platform walkable top": (.65, 18.),
                "Room concrete": (.85, 24.), "Near furniture": (2., 60.)}
    for label, (weight, minimum) in required.items():
        if density * weight < minimum:
            raise RuntimeError(f"Warehouse {label} density {density * weight:.2f} < {minimum} texels/metre")
    chart_rows, visible_minimum = [], {}
    for chart_key, chart in charts.items():
        x, y, scale = packed[chart_key]
        chart_rows.append({"key": list(chart_key), "bucket": chart_key[-1],
            "weight": chart["weight"], "extent_m": [round(float(v), 6) for v in chart["extent"]],
            "local_origin_m": [float(v) for v in chart["low"]],
            "atlas_origin_px": [float(x), float(y + bottom)],
            "texels_per_metre": round(scale, 4)})
        if chart["kind"] in ARCHITECTURAL_WEIGHTS and chart_key[-1] == "regular":
            role_key = chart["kind"] + "/" + chart_key[1]
            visible_minimum[role_key] = min(visible_minimum.get(role_key, scale), scale)
        origin = np.array((x, y + bottom))
        for values, start, total, projected in chart["faces"]:
            values[start:start + total] = (origin + (projected - chart["low"]) * scale) / master.SIZE
    master.CHART_SUMMARY = {"base_texels_per_metre": round(density, 4),
        "chart_count": len(chart_rows),
        "required_minimum_texels_per_metre": {label: minimum for label, (_, minimum) in required.items()},
        "weights": {"architectural": ARCHITECTURAL_WEIGHTS,
                    "hidden": HIDDEN_WEIGHT, "default": master.PRIORITY, "NumberPlate": 5.},
        "visible_architectural_minimum_texels_per_metre": {
            key: round(value, 4) for key, value in sorted(visible_minimum.items())},
        "charts": sorted(chart_rows, key=lambda row: tuple(row["key"]))}
    for obj, values in objects:
        obj.data.uv_layers[0].data.foreach_set("uv", values.reshape(-1))
        obj.data.uv_layers.active_index = 0
        obj.data.uv_layers[0].active_render = True
    print("WAREHOUSE PLACEMENT BOX CHARTS", len(charts), "density", round(density, 4),
          "base texels/metre; reserved auxiliary rows", bottom, flush=True)
    return master.cache_uvs(roots)
