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

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {"Hero": "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx",
           "Npc": "Assets/Resources/VillageLife/StationWorker.fbx"}
ANGLE_COUNT = 8
BAND_COUNT = 2
SECTOR_COUNT = ANGLE_COUNT * BAND_COUNT
BRAIN_COUNT = 8


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


def brain(source, centre, half_extent):
    pieces = []
    inverse = source.matrix_world.inverted()
    # Eight closed lobulated chunks fill the cranium, above the lower face.
    origin = centre + Vector((0, .006, half_extent.z * .22))
    for index in range(BRAIN_COUNT):
        signs = Vector((1 if index & 1 else -1, 1 if index & 2 else -1, 1 if index & 4 else -1))
        offset = Vector((signs.x * half_extent.x * .32,
                         signs.y * half_extent.y * .32,
                         signs.z * half_extent.z * .19))
        radii = Vector((half_extent.x * .36, half_extent.y * .36, half_extent.z * .24))
        vertices = [inverse @ (origin + offset + Vector((0, 0, radii.z)))]
        rings, sides = 5, 10
        for ring in range(1, rings):
            latitude = math.pi * ring / rings
            for side in range(sides):
                angle = 2 * math.pi * side / sides
                # Alternating shallow ridges read as tissue at the project's
                # low-poly scale without an unrelated stock sphere silhouette.
                groove = 1 + .10 * math.cos(side * math.pi + ring * .8 + index)
                point = origin + offset + Vector((
                    radii.x * math.sin(latitude) * math.cos(angle) * groove,
                    radii.y * math.sin(latitude) * math.sin(angle) * groove,
                    radii.z * math.cos(latitude)))
                vertices.append(inverse @ point)
        bottom = len(vertices)
        vertices.append(inverse @ (origin + offset - Vector((0, 0, radii.z))))
        faces = [(0, 1 + side, 1 + (side + 1) % sides) for side in range(sides)]
        for ring in range(rings - 2):
            for side in range(sides):
                a = 1 + ring * sides + side
                b = 1 + ring * sides + (side + 1) % sides
                c = 1 + (ring + 1) * sides + (side + 1) % sides
                d = 1 + (ring + 1) * sides + side
                faces.append((a, d, c, b))
        final_ring = 1 + (rings - 2) * sides
        faces.extend((final_ring + side, bottom, final_ring + (side + 1) % sides)
                     for side in range(sides))
        # Ring order starts at the top; reverse only if the measured volume
        # says it is inward. Assert, rather than guessing exporter handedness.
        if signed_volume(vertices, faces) < 0:
            faces = [tuple(reversed(face)) for face in faces]
        if signed_volume(vertices, faces) <= 1e-10:
            raise RuntimeError("Brain piece is not a positive solid")
        pieces.append(make_mesh(f"Brain{index:02d}__GEO_Head", source, vertices, faces))
    return pieces


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
    outputs.extend(brain(skull, centre, half_extent))
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
            "shell_volumes_m3": sector_shell_volumes, "meshes": measurements,
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
    manifest = {"generator": "tools/build-combat-gore-3d-model.py", "version": 1,
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
        print("COMBAT GORE SOURCE COVERAGE, UV, SKIN, CLOSED SHELLS, SCALE AND DETERMINISM OK", flush=True)
    else:
        (out / "CombatGore3D.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print("COMBAT GORE GENERATED", {kind: len(model["meshes"]) for kind, model in models.items()}, flush=True)


if __name__ == "__main__":
    main()
