"""Build deterministic CombatTest head sectors from the two production FBXs.

Exterior polygons, loop UVs and skin weights remain the source polygons: every
head-family polygon appears in exactly one sector. Closed inward skull surfaces
and eight brain pieces are authored here, never assembled from runtime shapes.
The source FBXs are read only. Unity's source-aware importer restores their exact
skin bind-pose ordering after the FBX round trip.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import zlib

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {"Hero": "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx",
           "Npc": "Assets/Resources/VillageLife/StationWorker.fbx"}
ANGLE_COUNT = 8
BAND_COUNT = 2
SECTOR_COUNT = ANGLE_COUNT * BAND_COUNT
BRAIN_COUNT = 8
BRAIN_RINGS = 16
BRAIN_SIDES = 32
BRAIN_CLEARANCE_M = .0035


def digest(data):
    return hashlib.sha256(data).hexdigest()


def rounded(values):
    return [round(float(value), 6) for value in values]


def unity(point):
    # Project FBX convention: Blender (x,y,z) reaches Unity as (x,z,y).
    return rounded((point.x, point.z, point.y))


def bounds(points):
    positions = [unity(point) for point in points]
    low = [min(point[axis] for point in positions) for axis in range(3)]
    high = [max(point[axis] for point in positions) for axis in range(3)]
    return {"min": low, "max": high,
            "size": rounded(high[axis] - low[axis] for axis in range(3))}


def head_bone(name):
    return name == "head" or name.startswith(("face.", "HairBack.", "HairLeft.", "HairRight."))


def head_family(obj):
    weighted = {obj.vertex_groups[group.group].name for vertex in obj.data.vertices
                for group in vertex.groups if group.weight > .0001}
    return bool(weighted) and all(head_bone(name) for name in weighted)


def make_mesh(name, source, vertices, faces, source_vertices=None, source_faces=None):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = source.parent
    obj.matrix_world = source.matrix_world.copy()
    for group in source.vertex_groups:
        obj.vertex_groups.new(name=group.name)
    if source_vertices is not None:
        for index, original in enumerate(source_vertices):
            for group in source.data.vertices[original].groups:
                obj.vertex_groups[group.group].add([index], group.weight, "REPLACE")
    else:
        group = obj.vertex_groups.get("head") or obj.vertex_groups.new(name="head")
        group.add(list(range(len(vertices))), 1.0, "REPLACE")
    for modifier in source.modifiers:
        if modifier.type == "ARMATURE":
            skin = obj.modifiers.new("Skin", "ARMATURE")
            skin.object = modifier.object
    if source_faces is not None:
        for material in source.data.materials:
            mesh.materials.append(material)
        for source_layer in source.data.uv_layers:
            layer = mesh.uv_layers.new(name=source_layer.name)
            for polygon, original in zip(mesh.polygons, source_faces):
                for loop, original_loop in zip(polygon.loop_indices, original.loop_indices):
                    layer.data[loop].uv = source_layer.data[original_loop].uv
        for polygon, original in zip(mesh.polygons, source_faces):
            polygon.material_index = original.material_index
            polygon.use_smooth = original.use_smooth
    else:
        uv = mesh.uv_layers.new(name="UVMap")
        for loop in mesh.loops:
            point = mesh.vertices[loop.vertex_index].co
            uv.data[loop.index].uv = (.5 + point.x * 1.7, .5 + point.z * 1.7)
    if not mesh.uv_layers:
        raise RuntimeError("Source exterior has no UVs: " + source.name)
    return obj


def measure(obj):
    mesh = obj.data
    world = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    weights = [[[obj.vertex_groups[group.group].name, round(group.weight, 6)]
                for group in sorted(vertex.groups, key=lambda item: obj.vertex_groups[item.group].name)]
               for vertex in mesh.vertices]
    if not world or not mesh.polygons or not mesh.uv_layers:
        raise RuntimeError("Incomplete head fragment: " + obj.name)
    if any(not values or abs(sum(weight for _, weight in values) - 1) > .002 for values in weights):
        raise RuntimeError("Head fragment has invalid skin weights: " + obj.name)
    if any(not math.isfinite(value) for point in world for value in point):
        raise RuntimeError("Nonfinite head fragment: " + obj.name)
    measured_bounds = bounds(world)
    if (measured_bounds["min"][1] < 1.2 or measured_bounds["max"][1] > 2.1 or
            max(measured_bounds["size"]) > .7):
        raise RuntimeError("Head fragment is outside metre-scale head bounds: " + obj.name)
    payload = {"vertices": [rounded(vertex.co) for vertex in mesh.vertices],
               "faces": [list(face.vertices) for face in mesh.polygons],
               "uv_layers": {layer.name: [rounded(loop.uv) for loop in layer.data]
                             for layer in mesh.uv_layers},
               "weights": weights,
               "materials": [polygon.material_index for polygon in mesh.polygons],
               "world": [unity(point) for point in world]}
    return {"name": obj.name, "vertices": len(mesh.vertices), "polygons": len(mesh.polygons),
            "triangles": sum(len(face.vertices) - 2 for face in mesh.polygons),
            "bounds_unity_m": measured_bounds,
            "semantic_sha256": digest(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode())}


def signed_volume(vertices, faces):
    volume = 0.0
    for face in faces:
        for index in range(1, len(face) - 1):
            a, b, c = (vertices[face[i]] for i in (0, index, index + 1))
            volume += a.dot(b.cross(c)) / 6
    return volume


def interior(source, selected_faces, sector, centre):
    # StationWorker's face IS the front skull, whereas the hero's facial atlas
    # overlays a complete skull. Weld the NPC's two adjoining source renderers
    # only for the inward shell; keep their exterior renderers independent.
    outer, exterior_faces, mapping = [], [], {}
    inverse = source.matrix_world.inverted()
    for original, polygon in selected_faces:
        face = []
        for vertex in polygon.vertices:
            point = inverse @ (original.matrix_world @ original.data.vertices[vertex].co)
            key = tuple(round(value, 8) for value in point)
            if key not in mapping:
                mapping[key] = len(outer)
                outer.append(point)
            face.append(mapping[key])
        exterior_faces.append(face)
    # Scale the surface towards the head centre, retaining a substantial skull
    # wall rather than a zero-thickness decorative back face.
    local_centre = source.matrix_world.inverted() @ centre
    inner = [local_centre + (point - local_centre) * .78 for point in outer]
    count = len(outer)
    faces = [[index + count for index in reversed(face)] for face in exterior_faces]
    edges = {}
    for face in exterior_faces:
        for a, b in zip(face, face[1:] + face[:1]):
            key = tuple(sorted((a, b)))
            edges.setdefault(key, []).append((a, b))
    for occurrences in edges.values():
        if len(occurrences) == 1:
            a, b = occurrences[0]
            faces.append((b, a, a + count, b + count))
        elif len(occurrences) != 2:
            raise RuntimeError("Nonmanifold skull exterior sector: " + str(sector))
    all_vertices = outer + inner
    volume = signed_volume(all_vertices, exterior_faces + faces)
    if volume <= 1e-10:
        raise RuntimeError("Head shell winding/volume is invalid: " + str(sector) + ": " + str(volume))
    result = make_mesh(f"Sector{sector:02d}__Interior", source, all_vertices, faces)
    return result, round(volume * abs(source.matrix_world.determinant()), 10)


def closed_edges(faces, label):
    edges = {}
    for face in faces:
        for a, b in zip(face, face[1:] + face[:1]):
            edges.setdefault(tuple(sorted((a, b))), []).append((a, b))
    if any(len(pair) != 2 or pair[0] != tuple(reversed(pair[1])) for pair in edges.values()):
        raise RuntimeError("Brain solid is not closed and consistently wound: " + label)


def brain(source, skull_sources, centre, half_extent):
    # Fit ONE filled star-shaped cranial volume against the actual inward skull,
    # then partition it. Separate overlapping ellipsoids leave both voids and
    # duplicated tissue; shared vertices/caps make the union exact instead.
    inner_vertices, inner_faces, welded = [], [], {}
    for skull in skull_sources:
        mapping = {}
        for vertex in skull.data.vertices:
            position = centre + (skull.matrix_world @ vertex.co - centre) * .78
            key = tuple(round(value, 8) for value in position)
            if key not in welded:
                welded[key] = len(inner_vertices)
                inner_vertices.append(position)
            mapping[vertex.index] = welded[key]
        inner_faces.extend(tuple(mapping[index] for index in polygon.vertices)
                           for polygon in skull.data.polygons)
    # Production heads may end in an open neck or a tiny crown ring. Close
    # those horizontal end rings only, leaving their measured silhouette intact.
    edges = {}
    for face in inner_faces:
        for a, b in zip(face, face[1:] + face[:1]):
            edges.setdefault(tuple(sorted((a, b))), []).append((a, b))
    boundary = [pair[0] for pair in edges.values() if len(pair) == 1]
    end_caps, source_seams = 0, 0
    while boundary:
        component = [boundary.pop()]
        connected = set(component[0])
        changed = True
        while changed:
            changed = False
            for edge in list(boundary):
                if connected.intersection(edge):
                    component.append(edge)
                    connected.update(edge)
                    boundary.remove(edge)
                    changed = True
        points = [inner_vertices[index] for index in sorted(connected)]
        low, high = min(point.z for point in points), max(point.z for point in points)
        is_end = high - low <= 1e-5 and (high < centre.z - half_extent.z * .15 or
                                       low > centre.z + half_extent.z * .72)
        rows = {}
        for point in points:
            rows.setdefault(round(point.z, 6), []).append(point)
        # The worker's cheek paint surface has a sub-millimetre mismatch to the
        # rear head along two source seam strips. Close only that measured narrow
        # slit in the containment surface; production exterior polygons stay exact.
        is_seam = all(len(row) <= 2 and (len(row) == 1 or (row[0] - row[1]).length <= .001)
                      for row in rows.values())
        if not is_end and not is_seam:
            raise RuntimeError("Skull has an unsupported source opening: " + str((low, high)))
        cap = len(inner_vertices)
        inner_vertices.append(sum(points, Vector()) / len(points))
        inner_faces.extend((b, a, cap) for a, b in component)
        end_caps += int(is_end)
        source_seams += int(not is_end)
    closed_edges(inner_faces, "source cranial containment surface")
    cavity = BVHTree.FromPolygons(inner_vertices, inner_faces, all_triangles=False)
    origin = centre + Vector((0, 0, half_extent.z * .32))
    nearest, normal, _, _ = cavity.find_nearest(origin)
    if nearest is None or normal.dot(origin - nearest) >= 0:
        raise RuntimeError("Filled brain origin is not inside its source cranial surface")
    radii = Vector((half_extent.x * .76, half_extent.y * .76, half_extent.z * .44))
    outer = []

    def point(latitude, angle):
        direction = Vector((math.sin(latitude) * math.cos(angle),
                            math.sin(latitude) * math.sin(angle), math.cos(latitude)))
        # Exact partition planes, including pole/equator, avoid hairline cracks.
        for axis in range(3):
            if abs(direction[axis]) < 1e-10:
                direction[axis] = 0
        direction.normalize()
        hit, _, _, exit_distance = cavity.ray_cast(origin, direction, .5)
        if hit is None or exit_distance <= BRAIN_CLEARANCE_M:
            raise RuntimeError("Brain ray is outside the cranial cavity: " +
                               str((unity(origin), rounded(direction), exit_distance, source.name)))
        ellipse_distance = 1 / math.sqrt(sum((direction[axis] / radii[axis]) ** 2
                                             for axis in range(3)))
        # Shallow winding sulci affect the shared outer hull, never individual
        # pieces. Muted tissue colour/flat facets preserve the PS1 vocabulary.
        ridge = .5 + .5 * math.cos(angle * 10 + math.sin(latitude * 6) * 1.8)
        fold = 1 - .055 * ridge * math.sin(latitude) ** 2
        radius = min(ellipse_distance, exit_distance - BRAIN_CLEARANCE_M) * fold
        return origin + direction * radius

    outer.append(point(0, 0))
    for ring in range(1, BRAIN_RINGS):
        for side in range(BRAIN_SIDES):
            outer.append(point(math.pi * ring / BRAIN_RINGS, 2 * math.pi * side / BRAIN_SIDES))
    bottom = len(outer)
    outer.append(point(math.pi, 0))

    def vertex(ring, side):
        if ring == 0:
            return 0
        if ring == BRAIN_RINGS:
            return bottom
        return 1 + (ring - 1) * BRAIN_SIDES + side % BRAIN_SIDES

    hull_faces = []
    for ring in range(BRAIN_RINGS):
        for side in range(BRAIN_SIDES):
            if ring == 0:
                hull_faces.append((0, vertex(1, side), vertex(1, side + 1)))
            elif ring == BRAIN_RINGS - 1:
                hull_faces.append((vertex(ring, side), bottom, vertex(ring, side + 1)))
            else:
                a, b = vertex(ring, side), vertex(ring, side + 1)
                c, d = vertex(ring + 1, side + 1), vertex(ring + 1, side)
                hull_faces.extend(((a, d, c), (a, c, b)))
    if signed_volume(outer, hull_faces) < 0:
        hull_faces = [tuple(reversed(face)) for face in hull_faces]
    closed_edges(hull_faces, "whole volume")
    hull_volume = signed_volume(outer, hull_faces)
    if hull_volume <= 1e-8:
        raise RuntimeError("Filled brain volume is not a positive solid")
    if min(point.z for point in outer) < centre.z - half_extent.z * .13:
        raise RuntimeError("Brain extends into the lower face instead of the upper cranium")

    # Test the source surface itself, not a head bounding box. Edge midpoints
    # and face centres catch triangles crossing a concave skull between rays.
    samples = list(outer)
    for face in hull_faces:
        a, b, c = (outer[index] for index in face)
        samples.extend(((a + b + c) / 3, (a + b) / 2, (b + c) / 2, (c + a) / 2))
    clearance = math.inf
    for sample in samples:
        radial = sample - origin
        _, _, _, distance = cavity.ray_cast(origin, radial.normalized(), .5)
        if distance is None or distance < radial.length - 1e-7:
            raise RuntimeError("Brain surface crosses the actual inward skull")
        nearest, _, _, nearest_distance = cavity.find_nearest(sample)
        if nearest is None:
            raise RuntimeError("Cannot measure brain/skull clearance")
        clearance = min(clearance, nearest_distance)
    if clearance < .002:
        raise RuntimeError("Brain has insufficient clearance for bounded soft deformation: " + str(clearance))

    selections = [[] for _ in range(BRAIN_COUNT)]
    for face in hull_faces:
        middle = sum((outer[index] for index in face), Vector()) / len(face) - origin
        octant = (1 if middle.x > 0 else 0) | (2 if middle.y > 0 else 0) | (4 if middle.z > 0 else 0)
        selections[octant].append(face)
    inverse = source.matrix_world.inverted()
    pieces, volumes, partition_caps = [], {}, {}
    core = len(outer)
    whole = outer + [origin]
    for index, exterior_faces in enumerate(selections):
        edge_counts = {}
        for face in exterior_faces:
            for a, b in zip(face, face[1:] + face[:1]):
                edge_counts.setdefault(tuple(sorted((a, b))), []).append((a, b))
        cap_faces = [(pair[0][1], pair[0][0], core) for pair in edge_counts.values() if len(pair) == 1]
        for face in cap_faces:
            partition_caps.setdefault(tuple(sorted(face)), []).append(face)
        faces = exterior_faces + cap_faces
        closed_edges(faces, str(index))
        volume = signed_volume(whole, faces)
        if volume <= 1e-8:
            raise RuntimeError("Brain partition is not a positive solid: " + str(index))
        volumes[str(index)] = round(volume, 10)
        used = sorted({vertex for face in faces for vertex in face})
        mapping = {old: new for new, old in enumerate(used)}
        vertices = [inverse @ whole[old] for old in used]
        local_faces = [tuple(mapping[old] for old in face) for face in faces]
        obj = make_mesh(f"Brain{index:02d}__GEO_Head", source, vertices, local_faces)
        # Tissue unfolds across the common cranial volume, including the exposed
        # cut faces, rather than projecting a postage-stamp UV from world origin.
        uv = obj.data.uv_layers.active
        for loop in obj.data.loops:
            position = whole[used[loop.vertex_index]] - origin
            uv.data[loop.index].uv = (.5 + position.x / (2 * radii.x),
                                     .5 + (position.z + position.y * .45) / (2 * radii.z))
        pieces.append(obj)
    # Each internal triangle occurs twice with opposite winding. Combined with
    # the closed hull/positive octant solids this proves there are no holes,
    # interpenetrating chunks or hidden duplicated tissue in the resting brain.
    for pair in partition_caps.values():
        if len(pair) != 2 or set((pair[0][i], pair[0][(i + 1) % 3]) for i in range(3)) != set(
                (pair[1][(i + 1) % 3], pair[1][i]) for i in range(3)):
            raise RuntimeError("Brain partition boundaries do not match with opposite winding")
    volume_error = abs(sum(volumes.values()) - hull_volume)
    if volume_error > 1e-8:
        raise RuntimeError("Brain chunks do not fill their one common hull")
    return pieces, {"partition": "eight matching closed octants of one filled cranial hull",
        "octant_bits_unity": "bit0=+X, bit1=+Z, bit2=+Y relative to brain center",
        "center_unity_m": unity(origin), "bounds_unity_m": bounds(outer),
        "volume_m3": round(hull_volume, 10), "piece_volumes_m3": volumes,
        "shared_cap_triangle_pairs": len(partition_caps),
        "partition_volume_error_m3": round(volume_error, 12),
        "minimum_skull_clearance_m": round(clearance, 6),
        "source_end_rings_closed_for_containment": end_caps,
        "submillimetre_source_seams_closed_for_containment": source_seams,
        "containment": "vertices, edge midpoints and triangle centers inside actual inward source skull"}


def export(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={"EMPTY", "ARMATURE", "MESH"}, axis_forward="-Z", axis_up="Y",
        add_leaf_bones=False, bake_anim=False, use_armature_deform_only=False,
        use_mesh_modifiers=False, mesh_smooth_type="FACE", use_custom_props=False)


def build(kind, out, publish=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT / SOURCES[kind]), use_anim=False)
    originals = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    family = sorted((obj for obj in originals if head_family(obj)), key=lambda obj: obj.name)
    skull = next((obj for obj in family if obj.name == "GEO_Head"), None)
    if skull is None:
        raise RuntimeError("Head source lacks its primary skull: " + kind)
    skull_sources = [obj for obj in family if obj.name == "GEO_Head" or
                     kind == "Npc" and obj.name == "GEO_FaceSurface"]
    skull_world = [obj.matrix_world @ vertex.co for obj in skull_sources for vertex in obj.data.vertices]
    low = Vector(tuple(min(point[axis] for point in skull_world) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in skull_world) for axis in range(3)))
    centre = (low + high) * .5
    half_extent = (high - low) * .5
    # The split follows the upper/lower cranium, without cutting the neck.
    band_height = centre.z + half_extent.z * .08
    outputs, renderers, sector_shell_volumes = [], [], {}
    shell_selections = [[] for _ in range(SECTOR_COUNT)]
    for source in family:
        selections = [[] for _ in range(SECTOR_COUNT)]
        for polygon in source.data.polygons:
            point = sum((source.matrix_world @ source.data.vertices[index].co
                         for index in polygon.vertices), Vector()) / len(polygon.vertices)
            angle = math.atan2(point.y - centre.y, point.x - centre.x) % (2 * math.pi)
            sector = min(ANGLE_COUNT - 1, int(angle * ANGLE_COUNT / (2 * math.pi)))
            sector += ANGLE_COUNT if point.z >= band_height else 0
            selections[sector].append(polygon)
            if source in skull_sources:
                shell_selections[sector].append((source, polygon))
        if sum(len(faces) for faces in selections) != len(source.data.polygons):
            raise RuntimeError("Exterior polygon coverage changed")
        renderers.append({"name": source.name, "polygons": len(source.data.polygons),
                          "sector_polygons": [len(faces) for faces in selections]})
        for sector, faces in enumerate(selections):
            if not faces:
                continue
            used = sorted({vertex for face in faces for vertex in face.vertices})
            mapping = {old: new for new, old in enumerate(used)}
            obj = make_mesh(f"Sector{sector:02d}__{source.name}", source,
                [source.data.vertices[index].co for index in used],
                [[mapping[index] for index in face.vertices] for face in faces], used, faces)
            outputs.append(obj)
    for sector, selected_faces in enumerate(shell_selections):
        if selected_faces:
            cap, volume = interior(skull, selected_faces, sector, centre)
            outputs.append(cap)
            sector_shell_volumes[str(sector)] = volume
    if len(sector_shell_volumes) != SECTOR_COUNT:
        raise RuntimeError("Every skull sector must contain an authored closed shell: " + kind + "; " +
                           str(next(entry for entry in renderers if entry["name"] == "GEO_Head")))
    brain_pieces, brain_contract = brain(skull, skull_sources, centre, half_extent)
    outputs.extend(brain_pieces)
    measurements = [measure(obj) for obj in sorted(outputs, key=lambda obj: obj.name)]
    for obj in originals:
        bpy.data.objects.remove(obj, do_unlink=True)
    for arm in (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"):
        arm.animation_data_clear()
        for bone in arm.pose.bones:
            bone.matrix_basis.identity()
    if publish:
        export(out / ("Head" + kind + ".fbx"))
    return {"source_renderers": renderers, "head_center_unity_m": unity(centre),
            "head_bounds_unity_m": bounds(skull_world),
            "band_height_unity_m": round(band_height, 6),
            "shell_volumes_m3": sector_shell_volumes, "brain": brain_contract, "meshes": measurements,
            "semantic_sha256": digest(json.dumps(measurements, sort_keys=True, separators=(",", ":")).encode())}


def texture_bytes(brain_texture=False):
    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))
    pixels = bytearray()
    for y in range(32):
        pixels.append(0)
        for x in range(32):
            grain = ((x * 73856093) ^ (y * 19349663)) % 13
            if brain_texture:
                fold = int(9 * math.sin(x * .62 + math.sin(y * .47) * 2))
                colour = (137 + grain + fold, 75 + grain + fold, 79 + grain + fold)
            else:
                vein = 12 if (x + 2 * y) % 17 < 2 else 0
                colour = (92 + grain - vein, 18 + grain // 2, 25 + grain // 2)
            pixels.extend((*colour, 255))
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 32, 32, 8, 6, 0, 0, 0))
    return png + chunk(b"IDAT", zlib.compress(bytes(pixels), 9)) + chunk(b"IEND", b"")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, default=ROOT / "Assets/Resources/CombatGore")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    out = args.output_dir
    previous = None
    if args.validate_only:
        previous = json.loads((out / "CombatGore3D.json").read_text(encoding="utf-8"))
        for name, expected in previous["files"].items():
            if digest((out / name).read_bytes()) != expected:
                raise RuntimeError("Published gore asset changed: " + name)
    else:
        out.mkdir(parents=True, exist_ok=True)
    models = {kind: build(kind, out, publish=not args.validate_only) for kind in SOURCES}
    rebuilt = {kind: build(kind, out) for kind in SOURCES}
    if models != rebuilt:
        raise RuntimeError("Head assets differ across independent source rebuilds")
    textures = {"FleshSurface.png": texture_bytes(), "BrainSurface.png": texture_bytes(True)}
    for name, data in textures.items():
        if args.validate_only:
            if (out / name).read_bytes() != data:
                raise RuntimeError("Gore texture differs from deterministic generator: " + name)
        else:
            (out / name).write_bytes(data)
    manifest = {"generator": "tools/build-combat-gore-3d-model.py", "version": 2,
        "test_only": True, "angle_count": ANGLE_COUNT, "band_count": BAND_COUNT,
        "sector_count": SECTOR_COUNT, "brain_count": BRAIN_COUNT,
        "sector_angle_axes": "atan2(Unity Z, Unity X), positive around model up",
        "sources": {kind: {"path": source, "sha256": digest((ROOT / source).read_bytes()),
                           "renderers": [entry["name"] for entry in models[kind]["source_renderers"]]}
                    for kind, source in SOURCES.items()},
        "models": models,
        "files": {name: digest((out / name).read_bytes()) for name in
                  ("HeadHero.fbx", "HeadNpc.fbx", "FleshSurface.png", "BrainSurface.png")}}
    if args.validate_only:
        if manifest != previous:
            raise RuntimeError("Head source signatures, exterior coverage, UV, skin or semantic geometry changed")
        print("COMBAT GORE SOURCE COVERAGE, UV, SKIN, CLOSED SHELLS, FILLED MATCHING BRAIN PARTITIONS, "
              "ACTUAL SKULL CONTAINMENT, SCALE AND DETERMINISM OK", flush=True)
    else:
        (out / "CombatGore3D.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print("COMBAT GORE GENERATED", {kind: len(model["meshes"]) for kind, model in models.items()}, flush=True)


if __name__ == "__main__":
    main()
