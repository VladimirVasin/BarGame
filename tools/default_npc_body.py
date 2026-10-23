"""Hero-derived default male anatomy and bounded, shared garment morphs.

The canonical hero Blender source is a read-only dependency. Its bare anatomy
is copied through the two rigs' rest frames, never reconstructed approximately.
Heads, hair, hands, contacts and the NPC action bank remain NPC-owned.
"""
from __future__ import annotations

import hashlib
import math

import bpy
from mathutils import Vector


CONTRACT = "hero_derived_male_v1"
SHAPE_NAMES = ("BodyShape.Slim", "BodyShape.Chubby")
SOURCE_PARTS = (
    "GEO_Torso", "GEO_Pelvis",
    *(f"GEO_{part}.{side}" for side in ("L", "R")
      for part in ("Shoulder", "UpperArm", "Forearm", "Thigh", "Shin", "Foot")),
)


def _smooth(value):
    t = max(0., min(1., value))
    return t * t * (3. - 2. * t)


def copy_hero_anatomy(builder, source_path, root):
    """Append only source objects needed by the anatomy, then dispose of them."""
    if not source_path.is_file():
        raise RuntimeError("Build Hero V2 before the default NPC: " + str(source_path))
    before = {obj.as_pointer() for obj in bpy.data.objects}
    disposable = (bpy.data.meshes, bpy.data.armatures, bpy.data.materials,
                  bpy.data.images, bpy.data.actions, bpy.data.collections)
    previous_data = [{item.as_pointer() for item in collection} for collection in disposable]
    with bpy.data.libraries.load(str(source_path), link=False) as (available, loaded):
        missing = set(SOURCE_PARTS) - set(available.objects)
        if missing:
            raise RuntimeError("Hero source lacks bare anatomy: " + str(sorted(missing)))
        loaded.objects = list(SOURCE_PARTS)
    source_records = []
    combined = {}
    try:
        for name, source in zip(SOURCE_PARTS, loaded.objects):
            source_rig = source.find_armature()
            if source_rig is None:
                raise RuntimeError("Hero anatomy has no rest rig: " + name)
            transforms = {}
            for group in source.vertex_groups:
                if group.name not in builder.result.rig.data.bones:
                    raise RuntimeError("Hero body uses a non-NPC bone: " + group.name)
                transforms[group.name] = (builder.result.rig.data.bones[group.name].matrix_local
                                          @ source_rig.data.bones[group.name].matrix_local.inverted())
            vertices = []
            assignments = []
            for vertex in source.data.vertices:
                weights = {source.vertex_groups[g.group].name: g.weight
                           for g in vertex.groups if g.weight > 1e-7}
                total = sum(weights.values())
                if total <= 0:
                    raise RuntimeError("Unweighted hero anatomy: " + name)
                weights = {bone: weight / total for bone, weight in weights.items()}
                # Both authoring systems store rest points as mesh vertex +
                # named-part origin (see character_joint_surfaces._point).
                # A saved preview/pose must not enter the anatomy bind frame;
                # appended, unlinked objects also have stale world matrices.
                point = vertex.co + source.location
                vertices.append(sum(((transforms[bone] @ point) * weight
                                     for bone, weight in weights.items()), Vector()))
                assignments.append(weights)
            # Loaded objects use the same global names as the source. Rename
            # them before creating the destination, so wardrobe paths stay exact.
            source.name = "SOURCE_" + name
            if name.startswith(("GEO_Shoulder.", "GEO_UpperArm.", "GEO_Forearm.")):
                target, bone = "GEO_Arm." + name[-1], "upper_arm." + name[-1]
            elif name.startswith(("GEO_Pelvis", "GEO_Thigh.")):
                target, bone = "GEO_LegsUpper", "pelvis"
            elif name.startswith("GEO_Shin."):
                target, bone = "GEO_LegsLower." + name[-1], "shin." + name[-1]
            elif name.startswith("GEO_Foot."):
                target, bone = "GEO_FootShoe." + name[-1], "foot." + name[-1]
            else:
                target, bone = name, "chest"
            bundle = combined.setdefault(target, {"vertices": [], "faces": [], "weights": [], "bone": bone})
            offset = len(bundle["vertices"])
            bundle["vertices"].extend(vertices)
            bundle["faces"].extend(tuple(index + offset for index in p.vertices) for p in source.data.polygons)
            bundle["weights"].extend(assignments)
            source_records.append({"renderer": name, "vertices": len(vertices),
                                   "target_renderer": target,
                                   "triangles": sum(len(p.vertices) - 2 for p in source.data.polygons)})
        for name, bundle in combined.items():
            obj = builder.add(name, (bundle["vertices"], bundle["faces"]), bundle["bone"], "skin", "body")
            builder.weights(obj, bundle["weights"])
    finally:
        # Remove appended dependency objects too (armature, source parents).
        # Destination objects are recognized by result ownership, never names.
        keep = {p.obj.as_pointer() for p in builder.result.parts}
        keep.update((builder.result.root.as_pointer(), builder.result.rig.as_pointer()))
        for obj in list(bpy.data.objects):
            if obj.as_pointer() not in before and obj.as_pointer() not in keep:
                bpy.data.objects.remove(obj, do_unlink=True)
        for collection, previous in zip(disposable, previous_data):
            for item in list(collection):
                if item.as_pointer() not in previous and (item.users == 0 or
                        (item.use_fake_user and item.users == 1)):
                    collection.remove(item)
    return {"source_path": source_path.relative_to(root).as_posix(),
            "source_sha256": hashlib.sha256(source_path.read_bytes()).hexdigest(),
            "source_renderers": source_records,
            "rest_frame_remap": True, "npc_identity_preserved": True}


def _limb_axis(point, bone):
    direction = bone.tail_local - bone.head_local
    fraction = max(0., min(1., (point - bone.head_local).dot(direction) / direction.length_squared))
    return bone.head_local + direction * fraction, fraction


def shaped_point(point, kind, rig, weights):
    """One continuous field for skin, sewn layers and attached surface details."""
    p = Vector(point)
    chubby = kind == SHAPE_NAMES[1]
    # Preserve head, hand, sole and the exact wrist/ankle contact frames.
    if p.z > 1.47 or p.z < .12:
        return p
    limb = {name: weight for name, weight in weights.items()
            if name.startswith(("upper_arm.", "forearm.", "thigh.", "shin."))}
    limb_weight = sum(limb.values())
    if limb_weight > .01:
        delta = Vector()
        for name, weight in limb.items():
            axis, fraction = _limb_axis(p, rig.data.bones[name])
            amount = .23 if chubby else -.12
            if name.startswith("forearm."):
                amount *= 1. - _smooth((fraction - .65) / .35)
            if name.startswith("shin."):
                amount *= _smooth((p.z - .12) / .18)
            delta += (p - axis) * amount * weight
        p += delta
    # The central field varies through the trunk and hips. All concentric
    # garment layers move together, retaining their authored clearance.
    trunk = _smooth((p.z - .76) / .15) * (1. - _smooth((p.z - 1.30) / .14))
    lateral = 1. - _smooth((abs(p.x) - .19) / .10)
    torso_weight = max(0., 1. - limb_weight) * trunk * lateral
    if torso_weight > 0:
        belly = math.exp(-((p.z - 1.055) / .145) ** 2)
        front = _smooth((.012 - p.y) / .045)
        amount = .30 if chubby else -.12
        p.x += p.x * amount * torso_weight
        p.y += ((-.103 if chubby else .014) * front * belly
                + (.022 if chubby else -.008) * (1. - front)) * torso_weight
    return p


def author_shapes(builder, source, height_scale):
    renderers = []
    rig = builder.result.rig
    for part in builder.result.parts:
        obj = part.obj
        # Head/hair and grip fingers stay identity/contact owned. Accessories
        # still carry zero keys where the shape field intentionally pins them.
        if part.role != "body" and not obj.name.startswith("CLO_"):
            continue
        if obj.name.startswith(("GEO_Head", "GEO_Neck")):
            continue
        if obj.data.shape_keys is None:
            obj.shape_key_add(name="Basis")
        for shape_name in SHAPE_NAMES:
            key = obj.shape_key_add(name=shape_name)
            for vertex, output in zip(obj.data.vertices, key.data):
                weights = {obj.vertex_groups[g.group].name: g.weight for g in vertex.groups}
                output.co = shaped_point(vertex.co + obj.location, shape_name, rig, weights) - obj.location
            key.value = 0.
        renderers.append(obj.name)
    # Stature excludes hair/headwear. The old importer scale stays unchanged;
    # runtime uses this measured reference rather than the old nominal height.
    crown = max(v.co.z + p.obj.location.z for p in builder.result.parts
                if p.obj.name in ("GEO_Head", "GEO_FaceSurface") for v in p.obj.data.vertices)
    sole = min(v.co.z + p.obj.location.z for p in builder.result.parts
               if p.obj.name.startswith("GEO_FootShoe.") for v in p.obj.data.vertices)
    return {"contract": CONTRACT, "shape_names": list(SHAPE_NAMES), "renderers": renderers,
            "base_height_m": round((crown - sole) * height_scale, 7),
            "height_range_cm": [160, 185], "neutral_build": .5, **source}


def validate(builder, data):
    parts = {part.obj.name: part.obj for part in builder.result.parts}
    extrema = {}
    signatures = []
    for shape in SHAPE_NAMES:
        points = []
        moved = 0
        for name in data["renderers"]:
            obj = parts[name]
            key = obj.data.shape_keys.key_blocks.get(shape)
            if key is None or len(key.data) != len(obj.data.vertices):
                raise RuntimeError("Missing body morph: " + name + ":" + shape)
            for vertex, output in zip(obj.data.vertices, key.data):
                point = output.co + obj.location
                if not all(math.isfinite(c) for c in point):
                    raise RuntimeError("Nonfinite body morph: " + name)
                moved += (output.co - vertex.co).length > 1e-6
                points.append(point)
            signatures.append((name, shape, [[round(c, 7) for c in v.co] for v in key.data]))
        if moved < 100:
            raise RuntimeError("Body build does not change the anatomy and garments: " + shape)
        torso = parts["GEO_Torso"]
        belly = [v.co + torso.location for v in torso.data.shape_keys.key_blocks[shape].data
                 if .98 < v.co.z + torso.location.z < 1.17]
        extrema[shape] = {"minimum_front_y_m": round(min(p.y for p in belly), 7),
                          "waist_width_m": round(max(p.x for p in belly) - min(p.x for p in belly), 7),
                          "changed_vertices": moved}
    if extrema[SHAPE_NAMES[1]]["minimum_front_y_m"] >= extrema[SHAPE_NAMES[0]]["minimum_front_y_m"] - .05:
        raise RuntimeError("Chubby build requires a localized visible belly")
    import json
    return {"extremes": extrema,
            "signature": hashlib.sha256(json.dumps(signatures, separators=(",", ":")).encode()).hexdigest(),
            "source_geometry_reused": True, "shared_skin_garment_field": True,
            "head_hair_hand_contact_preserved": True}
