"""Deterministic passive left-chest lamp measured against the current Hero V2.

Run with tools/run-blender.py. Geometry uses Unity-local metres (+Y up, +Z
beam); the hero's source anatomy is +X left, -Y front, +Z up. No hero asset
is rewritten. Two geometry builds, positive solid volumes, current jacket
attachment and the exported FBX hierarchy are checked before publication.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("chest_lamp_kit", ROOT / "tools/build-city-fair-3d-model.py")
kit = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kit
spec.loader.exec_module(kit)

VERSION = "1.1.0"
MODEL = "ChestFlashlight"
OUT = ROOT / "Assets/Resources/Combat/Flashlight"
HERO_SOURCE = ROOT / "ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend"
HERO_MANIFEST = ROOT / "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json"
SURFACES = {"Paint": (.205, .225, .195), "Steel": (.29, .305, .285),
            "WornSteel": (.40, .415, .38), "Rubber": (.115, .125, .105),
            "Glass": (.74, .76, .65)}
CLIP_CONTACT = (0., .026, -.033)
LENS = (0., .025, .053)


def make_item():
    kit.SURFACES = SURFACES
    lamp = kit.Item(MODEL)
    # A compact right-angle working lamp, not a weapon accessory. The battery
    # housing hangs vertically while its circular head looks straight forward.
    lamp.box("Paint", (0., -.012, -.002), (.045, .080, .032), "Housing", chamfer=.003)
    lamp.box("Rubber", (0., -.052, -.002), (.043, .007, .030), "Housing", chamfer=.001)
    lamp.rod("Paint", (0., .025, -.009), (0., .025, .042), .023, "Housing", sides=12)
    lamp.rod("Steel", (0., .025, .040), (0., .025, .049), .025, "Bezel", sides=12)
    lamp.rod("Rubber", (0., .025, .0485), (0., .025, .0505), .0215, "Bezel", sides=12)
    lamp.rod("Glass", (0., .025, .0506), (0., .025, .0515), .0195, "Lens", sides=12)
    # One folded spring clip: front fastening, turned bridge and long rear
    # tongue. ClipContact is on its inward face, where real cloth must meet it.
    lamp.box("Steel", (0., .011, -.020), (.018, .038, .0025), "Clip", chamfer=.0004)
    lamp.box("Steel", (0., .031, -.026), (.018, .003, .014), "Clip", chamfer=.0004)
    lamp.box("Steel", (0., .011, -.0315), (.017, .039, .003), "Clip", chamfer=.0004)
    lamp.box("Steel", (0., -.010, -.029), (.017, .004, .007), "Clip", chamfer=.0004)
    lamp.rod("WornSteel", (0., .012, -.0195), (0., .012, -.017), .003, "Clip", sides=8)
    lamp.box("Rubber", (.015, .030, -.003), (.015, .011, .009), "Button", chamfer=.001)
    # A few dull rubbed edges have actual volume and remain below the bezel's
    # value. No brand, text, tactical rail, extra strap or decorative markings.
    lamp.box("WornSteel", (-.021, -.006, .0142), (.002, .017, .001), "Wear", chamfer=.0001)
    lamp.box("WornSteel", (.018, -.048, .0142), (.006, .001, .001), "Wear", chamfer=.0001)
    lamp.anchor("ClipContact", CLIP_CONTACT)
    lamp.anchor("Lens", LENS)
    return lamp


def actor_point(source):
    # The production prefab deliberately turns its source model 180 degrees
    # around Unity up after the axis swap; physical-left source +X is actor -X.
    return [-float(source.x), float(source.z), -float(source.y)]


def measure_mount():
    if not HERO_SOURCE.is_file():
        raise ValueError("Chest lamp needs the current authored Hero V2 source")
    bpy.ops.wm.open_mainfile(filepath=str(HERO_SOURCE))
    rig = bpy.data.objects.get("RIG_Player")
    jacket = bpy.data.objects.get("CLO_JacketBody")
    action = bpy.data.actions.get("Relaxed")
    if rig is None or jacket is None or action is None:
        raise ValueError("Hero source lacks rig, actual jacket shell or Relaxed")
    rig.animation_data_create().action = action
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    evaluated = jacket.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    try:
        vertices = [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
        faces = [tuple(face.vertices) for face in mesh.polygons]
        tree = BVHTree.FromPolygons(vertices, faces)
        # Left upper chest, medial to the left shoulder/front shotgun-sling
        # route and the concealed lateral pistol holster. Keep the same lamp
        # geometry, outside the jacket opening and below the standing collar.
        # Read the real posed surface rather than copying a jacket depth number.
        hit, normal, _, distance = tree.ray_cast(Vector((.085, -1., 1.365)), Vector((0., 1., 0.)), 1.2)
        if hit is None or normal.y > -.35 or hit.x <= .05:
            raise ValueError("Lamp clip cannot find the outward physical-left chest")
        contact = actor_point(hit)
        origin = [round(p - c, 7) for p, c in zip(contact, CLIP_CONTACT)]
        return {"bone": "chest", "reference_action": "Relaxed", "reference_time": 0.,
                "physical_side": "Left", "hero_source_contact": [round(float(v), 7) for v in hit],
                "hero_actor_contact": [round(v, 7) for v in contact],
                "hero_actor_position": origin, "hero_actor_forward": [0., 0., 1.],
                "hero_actor_up": [0., 1., 0.], "jacket_renderer": "CLO_JacketBody",
                "surface_normal_actor": [round(v, 7) for v in actor_point(normal)],
                "maximum_imported_clip_gap_m": .006,
                "hero_manifest_sha256": hashlib.sha256(HERO_MANIFEST.read_bytes()).hexdigest()}
    finally:
        evaluated.to_mesh_clear()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=OUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    item = make_item()
    signature = kit.signature([item])
    if signature != kit.signature([make_item()]):
        raise ValueError("Chest lamp independent geometry builds differ")
    mount = measure_mount()
    data = kit.manifest([item], signature)
    data.update(generator="tools/build-player-flashlight-3d-model.py", generator_version=VERSION,
                test_only=True, always_on_warehouse_only=True, mount=mount)
    # The body attachment is part of the asset identity too: unchanged passive
    # geometry must not permit a stale right-side binding after a side change.
    data["geometry_signature"] = signature
    data["build_signature"] = hashlib.sha256(json.dumps(
        {"geometry": signature, "mount": mount, "version": VERSION},
        sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    if data["triangle_count"] > 900:
        raise ValueError("Chest lamp exceeds its bounded passive geometry budget")
    bounds = data["models"][0]["bounds_max"]
    if LENS[2] <= bounds[2]:
        raise ValueError("Light origin must lie ahead of the lens and housing")
    manifest = args.model_dir / "ChestFlashlight3D.json"
    if not args.validate_only:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        root = kit.build_objects([item])[0]
        kit.export(root, args.model_dir / (MODEL + ".fbx"))
    kit.verify_fbx(args.model_dir, data)
    if args.validate_only:
        if json.loads(manifest.read_text(encoding="utf8")) != json.loads(json.dumps(data)):
            raise ValueError("Chest lamp manifest differs from deterministic rebuild")
    else:
        manifest.write_text(json.dumps(data, indent=2) + "\n", encoding="utf8")
    print("CHEST LAMP METRES / LEFT CHEST / CLIP / FORWARD LENS / DETERMINISM OK " +
          json.dumps({"triangles": data["triangle_count"], "mount": mount}), flush=True)


if __name__ == "__main__":
    main()
