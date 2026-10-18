"""Independent, cavity-preserving damage banks for the Room combat enemy.

Run after publishing Combat/RoomBody.fbx. Exterior cuts retain that model's
UV0/skin; the corridor's tissue follows its actual thin walls instead of
filling the opening with the ordinary anatomical radial core.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
SOURCE = "Assets/Resources/Combat/RoomBody.fbx"
TORSO_TISSUE_DEPTH_M = .004


def module(label, filename):
    spec = importlib.util.spec_from_file_location(label, Path(__file__).with_name(filename))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


body = module("room_body_damage_art", "build-combat-body-3d-model.py")
gore = module("room_head_damage_art", "build-combat-gore-3d-model.py")
skull = module("room_skull_damage_art", "build-combat-skull-3d-model.py")
blood = module("room_wound_damage_art", "build-combat-blood-3d-model.py")
for builder in (body, gore, skull.body, blood):
    builder.SOURCES = {"Room": SOURCE}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def torso_structure(region, source, basis, flesh):
    """Sparse closed fragments inside actual shell surfaces, never a rib cage.

    The source has closed coat flanks, a thick tapered corridor and closed
    doorframes. Each retained prism lies 6--10mm behind one source triangle;
    the minimum authored solid thickness is 18mm. Population is bounded.
    """
    triangles = [tri for patch in range(4) for poly, _ in flesh.get((region, patch), [])
                 for tri in body.tessellate(poly)]
    step = max(1, math.ceil(len(triangles) / 48))
    vertices, faces, uv = [], [], []
    for tri in triangles[::step]:
        points = [p[0] for p in tri]
        normal = (points[1]-points[0]).cross(points[2]-points[0]).normalized()
        if normal.length < .9:
            continue
        outer = [point-normal*.006 for point in points]
        inner = [point-normal*.010 for point in points]
        topology = [(0,1,2),(5,4,3),(0,3,4),(0,4,1),
                    (1,4,5),(1,5,2),(2,5,3),(2,3,0)]
        if body.signed_volume(outer+inner, topology, stable=True) < 0:
            topology = [tuple(reversed(face)) for face in topology]
        offset = len(vertices)
        vertices.extend(outer+inner)
        uv.extend(p[1].copy() for _ in range(2) for p in tri)
        faces.extend(tuple(offset+i for i in face) for face in topology)
    if not vertices:
        raise RuntimeError("Room torso lacks source-bound structural fragments")
    return vertices, faces, uv


def validate_corridor():
    """A nine-ray bundle must pass through source, tissue and inner fragments.

    All derivatives are tested together, including normally hidden tissue.
    This detects a radial core, an anatomical rib cage or an accidental rear
    cap even when the intact outer model still looks hollow.
    """
    points, faces = [], []
    for obj in sorted(bpy.context.scene.objects, key=lambda item: item.name):
        if obj.type != "MESH" or not obj.name.endswith("__GEO_Torso"):
            continue
        offset = len(points)
        points.extend(obj.matrix_world @ vertex.co for vertex in obj.data.vertices)
        faces.extend(tuple(offset+i for i in poly.vertices) for poly in obj.data.polygons)
    if not faces:
        raise RuntimeError("Room damage lacks its authored torso shell")
    tree = BVHTree.FromPolygons(points, faces, all_triangles=False)
    contract = json.loads((ROOT / "Assets/Resources/Combat/RoomBody.json").read_text(encoding="utf-8"))["corridor"]
    mouth, far = contract["mouth"], contract["far"]
    # A real far wall is allowed; its surface is beyond the interior reach.
    start = mouth["depth"]-.05
    distance = far["depth"]-start + (.02 if contract["open_far_end"] else -.002)
    width = min(mouth["half_width"], far["half_width"])*.30
    centre = (far["bottom"]+far["top"])*.5
    offset = (far["top"]-far["bottom"])*.10
    probes = []
    for x in (-width, 0., width):
        for z in (centre-offset, centre, centre+offset):
            origin = Vector((x, start, z))
            location, _, _, _ = tree.ray_cast(origin, Vector((0, 1, 0)), distance)
            if location is not None:
                raise RuntimeError("Room damage closed the corridor: " + str(body.unity(location)))
            probes.append({"origin_unity_m": body.unity(origin), "direction_unity": [0,0,1],
                           "clear_distance_m": round(distance, 7)})
    return probes


def build(out, publish):
    gore_out = out / "CombatGore"
    blood_out = out / "CombatBlood"
    head = gore.build("Room", gore_out, publish)
    models = {"HeadRoom": head,
              "SkullRoom": skull.build("Room", gore_out, publish, head_contract={"models": {"Room": head}}),
              "BodyRoom": body.build("Room", gore_out, publish,
                  source_filter=lambda obj: obj.name.startswith("GEO_"),
                  torso_tissue_depth=TORSO_TISSUE_DEPTH_M, torso_bone_factory=torso_structure,
                  allow_empty_torso_patches=True)}
    corridor = validate_corridor()
    patches, wounds = blood.wounds("Room", blood_out, publish,
        surface_filter=lambda name: name.startswith("GEO_") and not name.startswith(("GEO_Ear", "GEO_FaceSurface")),
        required_palms={"GEO_HandPalm.L", "GEO_HandPalm.R"})
    models["WoundsRoom"] = wounds
    return models, patches, corridor


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-root", type=Path, default=ROOT / "Assets/Resources")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if not (ROOT / SOURCE).exists():
        raise RuntimeError("Publish the independent Room body before its damage banks")
    out = args.output_root.resolve()
    manifest_path = out / "CombatGore/CombatRoomDamage3D.json"
    for folder in (out / "CombatGore", out / "CombatBlood"):
        folder.mkdir(parents=True, exist_ok=True)
    previous = json.loads(manifest_path.read_text(encoding="utf-8")) if args.validate_only else None
    if previous:
        for filename, expected in previous["files"].items():
            if digest((out / filename).read_bytes()) != expected:
                raise RuntimeError("Room damage bank changed: " + filename)
    models, patches, corridor = build(out, not args.validate_only)
    if (models, patches, corridor) != build(out, False):
        raise RuntimeError("Room damage banks differ across independent source rebuilds")
    manifest = {"generator": "tools/build-combat-room-damage-3d-model.py", "version": 1, "test_only": True,
        "source": {"path": SOURCE, "sha256": digest((ROOT / SOURCE).read_bytes())},
        "body_region_count": 16, "patch_count": 4, "head_sector_count": 16, "brain_count": 8,
        "surface_lift_m": .0018, "torso_tissue_depth_m": TORSO_TISSUE_DEPTH_M,
        "flesh_patch_count": sum(mesh["name"].startswith("FleshRegion") for mesh in models["BodyRoom"]["meshes"]),
        "empty_torso_flesh_patches": models["BodyRoom"]["empty_flesh_patches"],
        "torso_structure": "bounded_source_surface_fragments", "clear_corridor_probes": corridor,
        "models": models, "wound_patches": patches,
        "source_tessellation_tolerances": [dict(kind="Room", **entry) for entry in models["BodyRoom"]["source_tessellation"]],
        "files": {filename: digest((out / filename).read_bytes()) for filename in
            ("CombatGore/BodyRoom.fbx", "CombatGore/HeadRoom.fbx", "CombatGore/SkullRoom.fbx", "CombatBlood/WoundsRoom.fbx")}}
    if args.validate_only:
        if manifest != previous:
            raise RuntimeError("Room source/UV/skin/cavity/cut/containment contract changed")
        print("ROOM DAMAGE SOURCE, UV, SKIN, CLOSED TISSUE, CLEAR CORRIDOR AND DETERMINISM OK", flush=True)
    else:
        manifest_path.write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
        print("ROOM DAMAGE BANKS GENERATED", flush=True)


if __name__ == "__main__":
    main()
