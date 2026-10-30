"""Deterministic Hero V2 emaciation on the original rig and expression atlases.

HeroCorruption is additive to the existing joint/hand/boot corrections. Its
single spatial field is sampled in source metres for every adjoining surface;
it never edits Basis, skin weights, UVs, anchors or animation curves.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

from atlas_kit import PixelCanvas, read_generated_png
import player_corruption_hair

ROOT = Path(__file__).resolve().parents[1]
VERSION = "hero_corruption_v1"
SHAPE_NAME = "HeroCorruption"
STAGE_WEIGHTS = (0., 10., 23., 40., 60., 80., 100.)
TEXTURE_DIR = ROOT / "Assets/Resources/Player/Corruption"
SOURCES = {
    "Face": ROOT / "Assets/Player3D/V2/Textures/PlayerFaceAtlas.png",
    "BareSkin": ROOT / "Assets/Resources/Player/PlayerBareSkinAtlas.png",
    "Dialogue": ROOT / "Assets/Resources/Dialogue/Faces/HeroDialogueFace.png",
}


def smooth(a: float, b: float, value: float) -> float:
    t = min(1., max(0., (value - a) / (b - a)))
    return t * t * (3. - 2. * t)


def bell(value: float, center: float, radius: float) -> float:
    return math.exp(-((value - center) / radius) ** 2)


def field(position):
    """A continuous, side-preserving map; sleeves/joints share exact endpoints."""
    from mathutils import Vector
    x, y, z = position
    out = Vector(position)
    # The trunk loses volume inside the familiar oversized jacket. Preserve
    # collar, shoulder sockets, trouser waistband and the feet exactly.
    inside = 1. - smooth(.19, .30, abs(x))
    trunk = smooth(.82, .95, z) * (1. - smooth(1.34, 1.46, z)) * inside
    waist = bell(z, 1.09, .20)
    out.x -= x * (.18 + .10 * waist) * trunk
    out.y -= y * (.19 + .11 * waist) * trunk
    # Thin neck and the same hollowed jaw/cheeks on the cranial shell,
    # face patch, ears and lower hair. The scalp and mouth centre stay put.
    neck = bell(z, 1.475, .055) * (1. - smooth(.075, .13, abs(x)))
    cheek = bell(z, 1.575, .048) * (1. - smooth(.11, .155, abs(x)))
    jaw = bell(z, 1.525, .028) * (1. - smooth(.105, .145, abs(x)))
    out.x -= x * (.17 * neck + .26 * cheek + .10 * jaw)
    out.y -= (y + .02) * .13 * neck
    # The depression is broad enough to alter a low-poly silhouette, while
    # preserving the nose/mouth socket and the original face-surface clearance.
    front = 1. - smooth(-.035, .005, y)
    lateral = smooth(.018, .055, abs(x))
    out.y += .011 * cheek * front * lateral
    return out


def prepare_export_topology(result, scale: float = 1.) -> list[str]:
    """Freeze the original evaluated triangles before adding a first key.

    FBX cannot export modifier results together with shape keys. In particular,
    dropping the existing Triangulate modifier can choose a different diagonal
    in a non-planar collar quad although all vertices remain identical. Bake
    exactly that modifier on newly keyed surfaces, before authoring the key.
    """
    import bpy
    prepared = []
    for part in result.parts:
        obj = part.obj
        # The existing hero exporter already suppresses modifiers on joint
        # surfaces to retain shared normals; their raw topology is canonical.
        if obj.data.shape_keys or obj.get("bp_joint_surface") or part.bone.startswith(("foot.", "shin.")):
            continue
        if getattr(part, "role", "") != "hair" and not any((field((obj.matrix_world @ v.co) / scale) * scale - obj.matrix_world @ v.co).length > .000001 * scale
                   for v in obj.data.vertices):
            continue
        for modifier in list(obj.modifiers):
            if modifier.type != "TRIANGULATE" or not modifier.show_render:
                continue
            with bpy.context.temp_override(object=obj, active_object=obj,
                    selected_objects=[obj], selected_editable_objects=[obj]):
                bpy.ops.object.modifier_apply(modifier=modifier.name)
            obj["bp_corruption_preserved_export_triangulation"] = True
            prepared.append(obj.name)
    return prepared


def author(result, scale: float = 1.) -> list[str]:
    """Add or exactly replace our key, leaving every pre-existing key intact."""
    from mathutils import Vector
    prepare_export_topology(result, scale)
    changed = []
    for part in sorted(result.parts, key=lambda p: p.obj.name):
        obj = part.obj
        # Articulated contacts keep their original authored contour. Soft
        # tissue of the trunk/head takes the visible volume change instead.
        if part.bone.startswith(("foot.", "shin.")):
            continue
        world = obj.matrix_world
        inverse = world.inverted()
        basis = obj.data.shape_keys.key_blocks[0] if obj.data.shape_keys else None
        vertices = basis.data if basis else obj.data.vertices
        if obj.name.startswith("GEO_Hand."):
            # Hollow only the soft dorsal palm, smoothly pinned at every
            # boundary. Wrist and finger attachment loops remain bit-exact;
            # finger/grip shapes and sockets keep their original contact.
            bone = result.rig.data.bones[part.bone]
            origin = result.rig.matrix_world @ bone.head_local
            end = result.rig.matrix_world @ bone.tail_local
            direction = (end - origin).normalized()
            length = (end - origin).length
            target = []
            for v in vertices:
                point = world @ v.co
                t = (point - origin).dot(direction) / length
                envelope = smooth(.08, .30, t) * (1. - smooth(.62, .90, t))
                radial = point - (origin + direction * (t * length))
                target.append(inverse @ (point - radial * (.18 * envelope)))
        else:
            target = [inverse @ (field((world @ v.co) / scale) * scale) for v in vertices]
        maximum = max(((target[i] - v.co).length for i, v in enumerate(vertices)), default=0.)
        if maximum < .000001 * scale:
            continue
        if basis is None:
            obj.shape_key_add(name="Basis", from_mix=False)
        key = obj.data.shape_keys.key_blocks.get(SHAPE_NAME)
        if key is None:
            key = obj.shape_key_add(name=SHAPE_NAME, from_mix=False)
        for vertex, point in zip(key.data, target):
            vertex.co = point
        key.value = 0.
        key.slider_min = 0.
        key.slider_max = 1.
        obj["bp_corruption_shape"] = VERSION
        changed.append(obj.name)
    player_corruption_hair.author(result, scale)
    return changed


def manifest(result) -> dict:
    parts = []
    digest = hashlib.sha256()
    for part in sorted(result.parts, key=lambda p: p.obj.name):
        obj = part.obj
        keys = obj.data.shape_keys
        if keys is None or keys.key_blocks.get(SHAPE_NAME) is None:
            continue
        basis, key = keys.key_blocks[0], keys.key_blocks[SHAPE_NAME]
        maximum = 0.
        digest.update(obj.name.encode())
        for base, target in zip(basis.data, key.data):
            delta = obj.matrix_world.to_3x3() @ (target.co - base.co)
            maximum = max(maximum, delta.length)
            digest.update(json.dumps([round(float(v), 7) for v in delta], separators=(",", ":")).encode())
        parts.append(dict(renderer=obj.name, vertex_count=len(key.data), maximum_displacement_m=round(maximum, 7)))
    return dict(schema=VERSION, shape_name=SHAPE_NAME, stage_weights=list(STAGE_WEIGHTS),
                original_rig_weights_uvs_actions=True, shared_position_field=True,
                original_export_triangulation_preserved=True,
                mesh_names=[p["renderer"] for p in parts], surfaces=parts,
                geometry_sha256=digest.hexdigest(),
                hair_lengths=player_corruption_hair.manifest(result))


def preserved_signature(module, result) -> str:
    """Hash only contracts this refresh must preserve, without unrelated validators."""
    mesh_rows = []
    vector = lambda values: [round(float(v), 8) for v in values]
    for part in sorted(result.parts, key=lambda p: p.obj.name):
        obj, mesh = part.obj, part.obj.data
        keys = mesh.shape_keys
        mesh_rows.append(dict(name=obj.name,
            matrix=[vector(row) for row in obj.matrix_world],
            vertices=[vector(v.co) for v in mesh.vertices],
            polygons=[list(p.vertices) for p in mesh.polygons],
            uv={uv.name: [vector(v.uv) for v in uv.data] for uv in mesh.uv_layers},
            weights=[[(obj.vertex_groups[g.group].name, round(g.weight, 8)) for g in v.groups] for v in mesh.vertices],
            shapes={key.name: [vector(v.co) for v in key.data]
                    for key in keys.key_blocks if key.name not in (SHAPE_NAME, "Basis",
                        player_corruption_hair.SHORT_SHAPE, player_corruption_hair.LONG_SHAPE)} if keys else {}))
    rows = dict(meshes=mesh_rows,
        bones={bone.name: [vector(row) for row in bone.matrix_local] for bone in result.rig.data.bones},
        actions={name: [[c.data_path, c.array_index,
                         [[vector(k.co), vector(k.handle_left), vector(k.handle_right), k.interpolation]
                          for k in c.keyframe_points]]
                        for c in module.common.iter_action_fcurves(record.action)]
                 for name, record in sorted(result.actions.items())})
    return hashlib.sha256(json.dumps(rows, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def texture_pixels(kind: str, source, amount: float) -> PixelCanvas:
    width, height, pixels = source
    canvas = PixelCanvas(width, height)
    for y in range(height):
        for x in range(width):
            start = (y * width + x) * 4
            r, g, b, alpha = pixels[start:start + 4]
            # Features, teeth and the persistent vomit twins retain exact
            # silhouettes and pigments. Only warm skin planes are recoloured.
            skin = r > 100 and r > g + 12 and g > b + 8 and r < 210
            if skin:
                u, v = x % 64, y % 64
                shadow = 0.
                if kind != "BareSkin":
                    edge = min(1., u / 5., (63 - u) / 5.)
                    eye = bell(v, 33., 4.) * (bell(u, 21., 8.) + bell(u, 43., 8.))
                    hollow = bell(v, 44., 10.) * (bell(u, 12., 7.) + bell(u, 52., 7.))
                    shadow = (19. * eye + 23. * hollow) * edge
                r = round(r * (1. - .20 * amount) - shadow * amount)
                g = round(g * (1. - .09 * amount) - shadow * amount)
                b = round(b * (1. - .18 * amount) - shadow * .80 * amount)
            canvas.put(x, y, (max(0, r), max(0, g), max(0, b), alpha))
    return canvas


def textures(output_dir: Path = TEXTURE_DIR, validate_only: bool = False) -> dict:
    rows = []
    for kind, path in SOURCES.items():
        source = read_generated_png(path)
        source_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        for stage, weight in enumerate(STAGE_WEIGHTS[1:], 2):
            canvas = texture_pixels(kind, source, weight / 100.)
            encoded = canvas.png_bytes()
            if canvas.pixels != texture_pixels(kind, source, weight / 100.).pixels:
                raise RuntimeError("Non-deterministic corruption skin pixels")
            destination = output_dir / f"{kind}{stage}.png"
            if validate_only:
                if destination.read_bytes() != encoded:
                    raise RuntimeError("Stale corruption atlas: " + str(destination))
            else:
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(encoded)
            rows.append(dict(kind=kind, stage=stage, resource=f"Player/Corruption/{kind}{stage}",
                             source_sha256=source_hash, sha256=hashlib.sha256(encoded).hexdigest(),
                             width=source[0], height=source[1]))
    import player_corruption_clothing
    rows.extend(player_corruption_clothing.textures(output_dir, validate_only))
    return dict(source_derived=True, preserve_expression_soil_alpha=True, textures=rows)


def refresh_face_atlases(manifest_path: Path) -> None:
    """A face-only edit updates its derived stages without touching other art."""
    payload = json.loads(manifest_path.read_text(encoding="utf-8"))
    contract = payload.get("hero_corruption")
    if contract is None:
        return
    source = read_generated_png(SOURCES["Face"])
    source_hash = hashlib.sha256(SOURCES["Face"].read_bytes()).hexdigest()
    for stage, weight in enumerate(STAGE_WEIGHTS[1:], 2):
        encoded = texture_pixels("Face", source, weight / 100.).png_bytes()
        destination = TEXTURE_DIR / f"Face{stage}.png"
        destination.parent.mkdir(parents=True, exist_ok=True)
        if not destination.is_file() or destination.read_bytes() != encoded:
            destination.write_bytes(encoded)
        row = next(item for item in contract["textures"] if item["kind"] == "Face" and item["stage"] == stage)
        row["source_sha256"] = source_hash
        row["sha256"] = hashlib.sha256(encoded).hexdigest()
    manifest_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def refresh(module, config) -> None:
    """Shape-only production update; preserve the verified complete action bank."""
    import bpy
    if config.animation_fbx is not None:
        raise ValueError("Corruption refresh requires --skip-animation-export")
    original = json.loads(module.DEFAULT_MANIFEST.read_text(encoding="utf-8"))
    bpy.ops.wm.open_mainfile(filepath=str(module.DEFAULT_OUTPUT))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    rig.animation_data_create().action = None
    parts = [module.common.PartRecord(bpy.data.objects[p["name"]], p["role"], p["bone"], p["sprite_part"], p["side"])
             for p in original["parts"]]
    actions = {p["name"]: module.common.ActionRecord(bpy.data.actions[p["name"]], p["category"],
        p["duration_seconds"], p["loop"], p["source_frame_count"], p["source_fps"]) for p in original["actions"]}
    result = module.common.BuildResult(rig.parent, rig, {}, {}, parts=parts, actions=actions)
    result.authored_anchors = [obj for obj in bpy.data.objects if obj.name.startswith("ANCHOR_HandGrip")]
    builder = module.HeroV2Builder(config, module.DEFAULT_FACE_ATLAS, module.DEFAULT_CLOTHING_ATLAS)
    builder.result = result
    builder._reset_pose()
    # Freeze original evaluated triangles before creating any new key. This
    # preserves the published surface; the raw source's quad storage changes.
    prepare_export_topology(result, config.height / 1.75)
    # Preserve original shape keys too; the broader model signature invokes
    # unrelated gesture validators and does not hash their geometry itself.
    preserved = preserved_signature(module, result)
    author(result, config.height / 1.75)
    first = manifest(result)
    author(result, config.height / 1.75)
    if first != manifest(result) or preserved != preserved_signature(module, result):
        raise RuntimeError("Corruption refresh changed Basis/rig/UVs/weights/actions or was nondeterministic")
    if not {"GEO_Head", "GEO_FaceSurface", "GEO_Neck", "GEO_Torso", "CLO_JacketBody", "CLO_ShirtBody"}.issubset(first["mesh_names"]):
        raise RuntimeError("Corruption omitted an adjoining core surface")
    original["hero_corruption"] = {**first, **textures(),
        "preserved_source_sha256": preserved,
        "authoring_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    original["hair"]["lengths"] = player_corruption_hair.manifest(result)
    # New metadata has its own complete geometry hash; keep the established
    # signature backward-compatible for all existing unchanged model contracts.
    module.export_model_fbx(config.fbx, result)
    module.common.save_blend(config.output)
    config.manifest.parent.mkdir(parents=True, exist_ok=True)
    config.manifest.write_text(json.dumps(original, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Hero corruption: deterministic shared field; Basis/rig/weights/UVs/actions preserved; "
          + str(len(first["mesh_names"])) + " additive shapes, seven appearance weights and source-derived skin atlases", flush=True)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    textures(validate_only=args.validate_only)
    print("HERO CORRUPTION ATLAS CONTRACT OK: deterministic source pixels, expression/soil/alpha layout preserved")
