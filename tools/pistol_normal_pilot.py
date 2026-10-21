"""Bounded authored normals for the existing pistol slide; no geometry edits."""
import hashlib
import json
import math
import bpy
from mathutils import Vector

SETTINGS = dict(mesh="Slide__Steel", method="area-weighted bevel corner blend",
                bevel_area_limit_m2=.00025, blend=.35, preserve_large_planes=True,
                baseline="flat indexed-face normals", geometry_uv_contacts="unchanged")


def geometry(mesh):
    return dict(vertices=[list(v.co) for v in mesh.vertices],
                faces=[list(p.vertices) for p in mesh.polygons],
                uv=[[list(v.uv) for v in uv.data] for uv in mesh.uv_layers])


def author(root):
    target = next(o for o in root.children_recursive if o.type == "MESH" and o.name == SETTINGS["mesh"])
    mesh = target.data
    before = geometry(mesh)
    weighted = [Vector() for _ in mesh.vertices]
    for polygon in mesh.polygons:
        for vertex in polygon.vertices:
            weighted[vertex] += polygon.normal * polygon.area
    normals, angles = [], []
    for polygon in mesh.polygons:
        face = polygon.normal.copy()
        for loop in polygon.loop_indices:
            normal = face.copy()
            if polygon.area < SETTINGS["bevel_area_limit_m2"]:
                average = weighted[mesh.loops[loop].vertex_index].normalized()
                normal = face.lerp(average, SETTINGS["blend"]).normalized()
            angles.append(math.degrees(face.angle(normal)))
            normals.append(normal)
        polygon.use_smooth = True
    mesh.normals_split_custom_set(normals)
    mesh.update()
    if geometry(mesh) != before:
        raise ValueError("Slide normal authoring changed geometry or UVs")
    changed = sum(a > .01 for a in angles)
    if not changed or max(angles) > 35:
        raise ValueError("Slide normal pilot is absent or exceeds its bounded blend")
    print(f"SLIDE NORMAL PILOT: changed corners={changed}, max angle={max(angles):.4f}deg; geometry/UV identical", flush=True)
    return target


def read_fbx(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    records, normals, flat = {}, {}, {}
    for obj in sorted(bpy.context.scene.objects, key=lambda o: o.name):
        name = obj.name.split(".")[0]
        record = dict(parent=obj.parent.name.split(".")[0] if obj.parent else None,
                      matrix=[[round(v, 7) for v in row] for row in obj.matrix_world])
        if obj.type == "MESH":
            record.update(geometry(obj.data))
            normals[name] = [tuple(n.vector) for n in obj.data.corner_normals]
            flat[name] = [tuple(p.normal) for p in obj.data.polygons for _ in p.loop_indices]
        records[name] = record
    return records, normals, flat


def compare(before, after, require_identical_normals=False):
    if before[0] != after[0]:
        old = set(before[0]); new = set(after[0])
        moved = [name for name in old & new if before[0][name] != after[0][name]]
        raise ValueError(f"Pistol normal pilot changed FBX geometry/UV/contacts: missing={old-new}, added={new-old}, changed={moved}")
    target = SETTINGS["mesh"]
    changed = []
    for name, normals in before[1].items():
        other = after[1][name]
        if len(normals) != len(other): raise ValueError("FBX normal topology changed: " + name)
        if any((Vector(a)-Vector(b)).length > .0001 for a, b in zip(normals, other)):
            changed.append(name)
    if changed not in ([], [target]):
        raise ValueError("Normals changed outside the selected steel: " + str(changed))
    if require_identical_normals and changed:
        raise ValueError("Published slide normals differ from deterministic authoring")
    if not any((Vector(a)-Vector(b)).length > .0001 for a, b in zip(after[1][target], after[2][target])):
        raise ValueError("Published slide still has the former flat normals")
    digest = hashlib.sha256(json.dumps(after[0], sort_keys=True).encode()).hexdigest()
    print("SLIDE FBX A/B: identical vertices/indices/UV/transforms/contact anchors; only " + target + " normals changed; geometry=" + digest, flush=True)
