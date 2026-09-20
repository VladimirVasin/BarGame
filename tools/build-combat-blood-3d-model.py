"""Deterministic CombatTest-only blood meshes and surface-following wound patches.

Reads the production FBXs; never rebuilds or modifies either character. The
Unity importer binds the exported surface vertices back to their original
renderer's skin weights/bind poses, retaining the original face/clothes atlases.
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


def clean():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={"EMPTY", "ARMATURE", "MESH"}, axis_forward="-Z", axis_up="Y",
        add_leaf_bones=False, bake_anim=False, use_armature_deform_only=False,
        use_mesh_modifiers=False, mesh_smooth_type="FACE", use_custom_props=False)


def selected(name, hero):
    if name in ("GEO_Head", "GEO_FaceSurface", "GEO_Neck"):
        return True
    if hero:
        return name.startswith(("CLO_JacketBody", "CLO_JacketSleeve", "CLO_JacketForearm",
                                "CLO_TrousersThigh", "CLO_TrousersShin", "GEO_HandPalm"))
    return name.startswith(("CLO_Outer_workBody", "CLO_Outer_workSleeve",
                           "CLO_Trousers_workUpper", "CLO_Trousers_workLower", "CLO_GlovePalm"))


def mesh_measurement(obj, wound=False):
    """Semantic geometry, independent of FBX headers/export timestamps."""
    mesh = obj.data
    def rounded(values):
        return [round(float(value), 6) for value in values]
    world = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    if not world or not mesh.polygons or not mesh.uv_layers.active:
        raise RuntimeError("Incomplete blood mesh: " + obj.name)
    # Established export converts Blender (x,y,z) to Unity (x,z,y).
    points = [[point.x, point.z, point.y] for point in world]
    low = [min(point[axis] for point in points) for axis in range(3)]
    high = [max(point[axis] for point in points) for axis in range(3)]
    size = [high[axis] - low[axis] for axis in range(3)]
    if not all(math.isfinite(value) for point in points for value in point):
        raise RuntimeError("Nonfinite blood mesh: " + obj.name)
    if wound and (max(size) > 1.2 or max(size) < .001 or
                  low[1] < -.1 or high[1] > 2.2 or
                  max(abs(value) for point in points for value in point) > 2.2):
        raise RuntimeError("Wound patch is outside metre-scale actor bounds: " + obj.name + str(size))
    weights = [[[obj.vertex_groups[group.group].name, round(group.weight, 6)]
                for group in sorted(vertex.groups, key=lambda item: obj.vertex_groups[item.group].name)]
               for vertex in mesh.vertices]
    if wound and any(not values or abs(sum(value[1] for value in values) - 1) > .002 for values in weights):
        raise RuntimeError("Wound has missing/non-normalized source skin: " + obj.name)
    payload = {"local_vertices": [rounded(vertex.co) for vertex in mesh.vertices],
               "world_vertices": [rounded(point) for point in points],
               "faces": [list(face.vertices) for face in mesh.polygons],
               "uv": [rounded(loop.uv) for loop in mesh.uv_layers.active.data],
               "weights": weights}
    if len(payload["uv"]) != len(mesh.loops):
        raise RuntimeError("Wound UV loop mismatch: " + obj.name)
    return {"name": obj.name, "vertices": len(mesh.vertices), "polygons": len(mesh.polygons),
            "triangles": sum(len(face.vertices) - 2 for face in mesh.polygons),
            "uv_loops": len(mesh.loops), "weighted_vertices": sum(bool(values) for values in weights),
            "bounds_unity_m": {"min": rounded(low), "max": rounded(high), "size": rounded(size)},
            "semantic_sha256": hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()}


def model_measurement(wound=False):
    meshes = [mesh_measurement(obj, wound) for obj in sorted(bpy.context.scene.objects, key=lambda item: item.name)
              if obj.type == "MESH"]
    low = [min(mesh["bounds_unity_m"]["min"][axis] for mesh in meshes) for axis in range(3)]
    high = [max(mesh["bounds_unity_m"]["max"][axis] for mesh in meshes) for axis in range(3)]
    return {"meshes": meshes, "vertices": sum(mesh["vertices"] for mesh in meshes),
            "triangles": sum(mesh["triangles"] for mesh in meshes),
            "bounds_unity_m": {"min": low, "max": high,
                               "size": [round(high[axis] - low[axis], 6) for axis in range(3)]},
            "semantic_sha256": hashlib.sha256(json.dumps(meshes, sort_keys=True, separators=(",", ":")).encode()).hexdigest()}


def wounds(kind, out, publish=True):
    clean()
    bpy.ops.import_scene.fbx(filepath=str(ROOT / SOURCES[kind]), use_anim=False)
    originals = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    patches = []
    for obj in sorted(originals, key=lambda o: o.name):
        if not selected(obj.name, kind == "Hero"):
            continue
        vertices = [obj.matrix_world @ v.co for v in obj.data.vertices]
        normal_matrix = obj.matrix_world.to_3x3().inverted().transposed()
        normals = [(normal_matrix @ p.normal).normalized() for p in obj.data.polygons]
        centres = [sum((vertices[i] for i in p.vertices), Vector()) / len(p.vertices)
                   for p in obj.data.polygons]
        centre = sum(vertices, Vector()) / len(vertices)
        span = max((p - centre).length for p in vertices)
        radius = min(.22, max(.065, span * .68))
        for side, direction in enumerate((Vector((0, -1, 0)), Vector((0, 1, 0)),
                                          Vector((-1, 0, 0)), Vector((1, 0, 0)))):
            candidates = [i for i, n in enumerate(normals) if n.dot(direction) > .3]
            if not candidates:
                continue
            # Choose a central exposed polygon, not a model-root approximation.
            index = min(candidates, key=lambda i: (centres[i] - centre).length_squared)
            point, normal = centres[index], normals[index]
            up = Vector((0, 0, 1))
            tangent = normal.cross(up).normalized()
            if tangent.length < .1:
                tangent = Vector((1, 0, 0))
            vertical = tangent.cross(normal).normalized()
            faces = [p for i, p in enumerate(obj.data.polygons)
                     if normals[i].dot(normal) > .12 and (centres[i] - point).length < radius * 1.6]
            used = sorted({v for p in faces for v in p.vertices})
            if not faces:
                continue
            mapping = {old: new for new, old in enumerate(used)}
            name = "Wound__" + obj.name + "__" + str(side)
            mesh = bpy.data.meshes.new(name)
            # Exact original vertices: the importer adds a measured 1.8 mm lift
            # in the source renderer's coordinates after restoring its skin.
            mesh.from_pydata([obj.data.vertices[i].co for i in used], [],
                             [[mapping[v] for v in p.vertices] for p in faces])
            mesh.update()
            patch = bpy.data.objects.new(name, mesh)
            bpy.context.collection.objects.link(patch)
            patch.parent = obj.parent
            patch.matrix_world = obj.matrix_world.copy()
            for group in obj.vertex_groups:
                patch.vertex_groups.new(name=group.name)
            for old in used:
                for group in obj.data.vertices[old].groups:
                    patch.vertex_groups[group.group].add([mapping[old]], group.weight, "REPLACE")
            for modifier in obj.modifiers:
                if modifier.type == "ARMATURE":
                    arm = patch.modifiers.new("Skin", "ARMATURE")
                    arm.object = modifier.object
            uv = mesh.uv_layers.new(name="BloodUV")
            for loop in mesh.loops:
                delta = vertices[used[loop.vertex_index]] - point
                uv.data[loop.index].uv = (.5 + delta.dot(tangent) / (radius * 2),
                                         .5 + delta.dot(vertical) / (radius * 2))
            patches.append({"name": name, "source_renderer": obj.name,
                            "vertices": len(used), "polygons": len(faces)})
    for obj in originals:
        bpy.data.objects.remove(obj, do_unlink=True)
    for arm in (o for o in bpy.context.scene.objects if o.type == "ARMATURE"):
        arm.animation_data_clear()
        for bone in arm.pose.bones:
            bone.matrix_basis.identity()
    if len(patches) < 16:
        raise RuntimeError("Too few authored surface patches: " + kind)
    measurements = model_measurement(wound=True)
    if publish:
        export(out / ("Wounds" + kind + ".fbx"))
    return patches, measurements


def passive(out, publish=True):
    clean()
    # Unity swaps Blender Y/Z with the established axis/export settings.
    for variant in range(4):
        count = 18
        verts = [(0, 0, 0)]
        for i in range(count):
            angle = 2 * math.pi * i / count
            radius = .5 * (1 + .17 * math.sin(i * 2.13 + variant) + .11 * math.cos(i * 4.71 + variant))
            verts.append((math.cos(angle) * radius, math.sin(angle) * radius, 0))
        mesh = bpy.data.meshes.new("Splat" + str(variant))
        mesh.from_pydata(verts, [], [(0, i + 1, (i + 1) % count + 1) for i in range(count)])
        mesh.update()
        uv = mesh.uv_layers.new(name="BloodUV")
        for loop in mesh.loops:
            p = mesh.vertices[loop.vertex_index].co
            uv.data[loop.index].uv = (.5 + p.x * .8, .5 + p.y * .8)
        bpy.context.collection.objects.link(bpy.data.objects.new(mesh.name, mesh))
    # Small authored octahedron. UVs stay inside opaque blood pixels.
    mesh = bpy.data.meshes.new("Drop")
    mesh.from_pydata([(0, 0, .5), (0, 0, -.5), (.3, 0, 0), (0, .3, 0), (-.3, 0, 0), (0, -.3, 0)], [],
                     [(0, 2, 3), (0, 3, 4), (0, 4, 5), (0, 5, 2), (1, 3, 2), (1, 4, 3), (1, 5, 4), (1, 2, 5)])
    mesh.update()
    uv = mesh.uv_layers.new(name="BloodUV")
    for loop in mesh.loops:
        uv.data[loop.index].uv = (.48, .48)
    bpy.context.collection.objects.link(bpy.data.objects.new(mesh.name, mesh))
    measurements = model_measurement()
    if publish:
        export(out / "BloodShapes.fbx")
    return measurements


def rebuild_measurements(out):
    patches, models = {}, {}
    for kind in SOURCES:
        patches[kind], models["Wounds" + kind] = wounds(kind, out, publish=False)
    models["BloodShapes"] = passive(out, publish=False)
    return {"patches": patches, "models": models}


def texture_bytes():
    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))
    pixels = bytearray()
    for y in range(64):
        pixels.append(0)
        for x in range(64):
            px, py = (x - 31.5) / 31.5, (y - 31.5) / 31.5
            angle, radius = math.atan2(py, px), math.hypot(px, py)
            edge = .77 + .11 * math.sin(angle * 7) + .07 * math.cos(angle * 13)
            speckle = ((x * 73856093) ^ (y * 19349663)) % 19
            opaque = radius < edge and (radius < .48 or speckle > 2)
            # Repeated splats must not stamp a high-contrast, glyph-like centre
            # stroke onto every surface. Keep only irregular edges and grain.
            red = (126 + speckle * 2, 12 + speckle // 3, 20 + speckle // 2)
            pixels.extend((*red, 255 if opaque else 0))
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 64, 64, 8, 6, 0, 0, 0))
    return png + chunk(b"IDAT", zlib.compress(bytes(pixels), 9)) + chunk(b"IEND", b"")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, default=ROOT / "Assets/Resources/CombatBlood")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--validate-only", action="store_true")
    mode.add_argument("--refresh-manifest-only", action="store_true")
    mode.add_argument("--texture-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    out = args.output_dir
    if args.validate_only or args.refresh_manifest_only or args.texture_only:
        manifest = json.loads((out / "CombatBlood3D.json").read_text(encoding="utf-8"))
        for kind, source in SOURCES.items():
            if hashlib.sha256((ROOT / source).read_bytes()).hexdigest() != manifest["sources"][kind]["sha256"]:
                raise RuntimeError("Wound source changed: regenerate " + kind)
        for name, digest in manifest["files"].items():
            if hashlib.sha256((out / name).read_bytes()).hexdigest() != digest:
                raise RuntimeError("Blood asset differs: " + name)
        if args.texture_only:
            pixels = texture_bytes()
            (out / "BloodSurface.png").write_bytes(pixels)
            manifest["files"]["BloodSurface.png"] = hashlib.sha256(pixels).hexdigest()
            (out / "CombatBlood3D.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            print("COMBAT BLOOD TEXTURE REFRESHED; AUTHORED MESHES RETAINED", flush=True)
            return
        if (out / "BloodSurface.png").read_bytes() != texture_bytes():
            raise RuntimeError("Blood texture differs from its generator: refresh with --texture-only")
        measured = rebuild_measurements(out)
        if measured != rebuild_measurements(out):
            raise RuntimeError("Blood geometry is nondeterministic across independent rebuilds")
        if manifest["patches"] != measured["patches"]:
            raise RuntimeError("Wound source selections differ: regenerate authored assets")
        if args.refresh_manifest_only:
            manifest["version"] = 2
            manifest["models"] = measured["models"]
            (out / "CombatBlood3D.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        elif manifest.get("models") != measured["models"]:
            raise RuntimeError("Blood semantic measurements differ from authored manifest")
        print("COMBAT BLOOD SEMANTIC GEOMETRY, SKIN, UV, BOUNDS, TEXTURE AND SOURCE SIGNATURES OK", flush=True)
        return
    out.mkdir(parents=True, exist_ok=True)
    patches, models = {}, {}
    for kind in SOURCES:
        patches[kind], models["Wounds" + kind] = wounds(kind, out)
    models["BloodShapes"] = passive(out)
    if {"patches": patches, "models": models} != rebuild_measurements(out):
        raise RuntimeError("Blood geometry is nondeterministic across independent rebuilds")
    (out / "BloodSurface.png").write_bytes(texture_bytes())
    manifest = {"generator": "tools/build-combat-blood-3d-model.py", "version": 2,
        "test_only": True, "surface_lift_m": .0018,
        "sources": {kind: {"path": source, "sha256": hashlib.sha256((ROOT / source).read_bytes()).hexdigest()}
                    for kind, source in SOURCES.items()}, "patches": patches, "models": models,
        "files": {name: hashlib.sha256((out / name).read_bytes()).hexdigest()
                  for name in ("WoundsHero.fbx", "WoundsNpc.fbx", "BloodShapes.fbx", "BloodSurface.png")}}
    (out / "CombatBlood3D.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("COMBAT BLOOD GENERATED", {kind: len(values) for kind, values in patches.items()})


if __name__ == "__main__":
    main()
