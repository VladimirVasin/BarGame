#!/usr/bin/env python3
"""Authored profiles for the existing cableway's bounded presentation slots.

Every box slot is exactly [-.5,.5]^3; the three cylinder slots retain Unity's
[-.5,-1,-.5]..[.5,1,.5] cylinder envelope. Runtime preserves the old placement,
UV projection, tint, pivots and collision. No world placements, labels or lights
are authored here. Each closed solid is checked before it joins its role mesh.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp

VERSION = "1.0.0"
DESIGN = "mountain_cableway_crafted_parts_v1"
NAME = "CablewayCraftedParts3D"
MODEL_DIR = ROOT / "Assets/Resources/MountainRoad"
SOURCE_DIR = ROOT / "ArtSource/MountainRoad/CablewayCraftedParts"
CYLINDERS = {"Bullwheel", "Hub", "Roller"}
MAX_TRIANGLES = 12000


def closed(geometry):
    vertices, faces = geometry
    volume = bp.signed_volume(geometry)
    if abs(volume) < 1e-9:
        raise ValueError("Degenerate cableway solid")
    if volume < 0:
        faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def box(center=(0, 0, 0), size=(1, 1, 1), cut=.018):
    return closed(bp.u_box(center, size, cut))


def merged(solids):
    return kit.merge_all(closed(solid) for solid in solids)


def rotate(geometry, axis):
    """Proper rotations only: the final axis reflection belongs to to_source."""
    vertices, faces = geometry
    if axis == "X":
        return [(-z, y, x) for x, y, z in vertices], faces
    if axis == "Y":
        return [(x, z, -y) for x, y, z in vertices], faces
    return geometry


def profile_beam(channel=False):
    if channel:
        pieces = [box((-.43, 0, 0), (.14, 1, 1), .008)]
    else:
        pieces = [box((0, 0, 0), (.16, .76, 1), .008)]
    for y in (-.43, .43):
        pieces.append(box((0, y, 0), (1, .14, 1), .01))
    # Closed end plates read as the existing welded connection, with the
    # original endpoints and no extra reach into the passenger envelope.
    for z in (-.499, .499):
        pieces.append(box((0, 0, z), (1, 1, .002), .0002))
    return merged(pieces)


def turned(rings, segments=24, tube=False):
    """Closed Y-axis turned metal: (y,radius), or annulus inner/outer contour."""
    vertices = [(math.cos(a * math.tau / segments) * r, y,
                 math.sin(a * math.tau / segments) * r)
                for y, r in rings for a in range(segments)]
    faces = []
    for ring in range(len(rings) - 1):
        for a in range(segments):
            b = (a + 1) % segments
            first, second = ring * segments, (ring + 1) * segments
            faces.append((first + a, second + a, second + b, first + b))
    if tube:
        first, second = (len(rings) - 1) * segments, 0
        for a in range(segments):
            b = (a + 1) % segments
            faces.append((first + a, second + a, second + b, first + b))
    else:
        faces += [tuple(reversed(range(segments))),
                  tuple((len(rings) - 1) * segments + a for a in range(segments))]
    return closed((vertices, faces))


def canopy():
    # A folded sheet with a genuine underside and end faces. The mounting
    # rails end at the old underside, exactly where the four columns end.
    xs = [-.5 + i / 160 for i in range(161)]
    zs = [-.5, .5]
    heights = [.31 if i % 4 in (1, 2) else .04 for i in range(161)]
    vertices = [(x, heights[i] - lower, z)
                for lower in (0, .12) for z in zs for i, x in enumerate(xs)]
    n, sheet = len(xs), len(xs) * 2
    faces = []
    for i in range(n - 1):
        faces += [(i, n + i, n + i + 1, i + 1),
                  (sheet + i, sheet + i + 1, sheet + n + i + 1, sheet + n + i),
                  (i, i + 1, sheet + i + 1, sheet + i),
                  (n + i + 1, n + i, sheet + n + i, sheet + n + i + 1)]
    faces += [(0, sheet, sheet + n, n),
              (n - 1, n * 2 - 1, sheet + n * 2 - 1, sheet + n - 1)]
    parts = [closed((vertices, faces))]
    for z in (-.44, .44):
        parts.append(box((0, -.42, z), (1, .16, .055), .006))
    for x in (-.485, .485):
        parts.append(box((x, .12, 0), (.03, .76, 1), .006))
    # Crown seam has the exact former top, without adding a new roof mass.
    parts.append(box((0, .43, 0), (.035, .14, 1), .005))
    return merged(parts)


def panelled_block():
    pieces = [box((0, .01, 0), (.96, .98, .96), .018)]
    for x in (-.486, .486):
        pieces.append(box((x, 0, 0), (.028, .93, 1), .006))
    for z in (-.486, .486):
        pieces.append(box((0, 0, z), (1, .93, .028), .006))
    pieces.append(box((0, -.475, 0), (1, .05, 1), .006))
    for x in (-.25, .25):
        for z in (-.491, .491):
            pieces.append(box((x, 0, z), (.025, .88, .018), .004))
    return merged(pieces)


def bench():
    pieces = []
    for z in (-.338, 0, .338):
        pieces.append(box((0, .41, z), (1, .18, .324), .012))
    for x in (-.365, .365):
        pieces.append(box((x, -.08, 0), (.12, .84, .84), .012))
    pieces.append(box((0, -.28, -.28), (.83, .12, .10), .009))
    return merged(pieces)


def post():
    pieces = [box((0, 0, 0), (.78, .90, .78), .025)]
    for y in (-.475, .475):
        pieces.append(box((0, y, 0), (1, .05, 1), .008))
    return merged(pieces)


def casing():
    pieces = [box((0, .04, 0), (.86, .92, .86), .055),
              box((0, -.455, 0), (1, .09, 1), .012)]
    for x in (-.46, .46):
        pieces.append(box((x, .05, 0), (.08, .73, .94), .014))
    for z in (-.46, .46):
        pieces.append(box((0, .05, z), (.73, .73, .08), .014))
    return merged(pieces)


def make_geometry():
    beam = profile_beam()
    rail = rotate(turned([(-.5, .46), (-.47, .5), (.47, .5), (.5, .46)], 12), "Y")
    return {
        "Canopy": canopy(),
        "Door": merged([box((0, 0, 0), (.80, .94, .94), .018),
                        box((0, .48, 0), (1, .04, 1), .005),
                        box((0, -.48, 0), (1, .04, 1), .005),
                        box((0, 0, -.48), (1, .94, .04), .005),
                        box((0, 0, .48), (1, .94, .04), .005)]),
        "Reducer": casing(),
        "Deck": merged([box((0, .38, 0), (1, .24, 1), .012),
                        box((-.42, -.12, 0), (.16, .76, 1), .008),
                        box((.42, -.12, 0), (.16, .76, 1), .008)]),
        "Roof": merged([box((0, .04, 0), (1, .92, 1), .055),
                        box((0, -.47, 0), (.95, .06, .95), .01)]),
        "Skirt": panelled_block(),
        "Bench": bench(), "PostY": post(),
        "Grip": merged([box((0, -.12, 0), (1, .76, .70), .025),
                        box((-.32, .36, 0), (.22, .28, 1), .025),
                        box((.32, .36, 0), (.22, .28, 1), .025)]),
        "RailZ": rail, "BeamZ": beam, "BeamX": rotate(beam, "X"),
        "ChannelZ": profile_beam(True),
        "Weight": merged([box((0, 0, 0), (1, .84, 1), .055),
                          box((0, .46, 0), (.83, .08, .83), .018),
                          box((0, -.46, 0), (.83, .08, .83), .018)]),
        "Bullwheel": turned([(-1, .46), (-.75, .5), (.75, .5), (1, .46),
                             (1, .38), (-1, .38)], 32, True),
        "Hub": turned([(-1, .46), (-.85, .5), (-.64, .5), (-.48, .37),
                        (.48, .37), (.64, .5), (.85, .5), (1, .46)]),
        "Roller": turned([(-1, .46), (-.86, .5), (-.55, .5), (-.30, .35),
                           (.30, .35), (.55, .5), (.86, .5), (1, .46)]),
        "Hut": panelled_block(), "Carriage": casing(),
        "Nosing": merged([box((0, .35, 0), (1, .30, 1), .006),
                          box((-.35, -.15, 0), (.30, .70, 1), .006)]),
    }


def stable_signature(geometry):
    data = json.dumps(geometry, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(data.encode()).hexdigest()


def validate(geometry):
    if len(geometry) != 20:
        raise ValueError("Expected twenty cableway roles")
    total = 0
    for role, solid in geometry.items():
        minimum, maximum = kit.bounds(solid)
        height = 1 if role in CYLINDERS else .5
        if any(abs(a - b) > 1e-6 for a, b in zip(minimum, (-.5, -height, -.5))) or \
           any(abs(a - b) > 1e-6 for a, b in zip(maximum, (.5, height, .5))):
            raise ValueError(f"{role} changed its passive envelope: {minimum}..{maximum}")
        if bp.signed_volume(solid) <= 0 or bp.signed_volume(bp.to_source(solid)) <= 0:
            raise ValueError(f"{role} has inward faces")
        total += kit.triangle_count(solid)
    if total > MAX_TRIANGLES:
        raise ValueError(f"Cableway geometry exceeded {MAX_TRIANGLES} triangles: {total}")
    return total


def build(args):
    geometry = make_geometry()
    total = validate(geometry)
    signature = stable_signature(geometry)
    if stable_signature(make_geometry()) != signature:
        raise ValueError("Cableway profiles are not deterministic")
    if args.validate_only:
        manifest = json.loads((args.model_dir / (NAME + ".json")).read_text())
        if manifest["build_signature"] != signature or manifest["triangle_count"] != total:
            raise ValueError("Published cableway manifest differs from the generator")
        print(f"CABLEWAY CRAFTED PARTS VALIDATED: {total} triangles; {signature}")
        return
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    root = bpy.data.objects.new(NAME, None)
    bpy.context.collection.objects.link(root)
    records, objects = [], []
    for role, unity in geometry.items():
        source = bp.to_source(unity)
        mesh = bpy.data.meshes.new("GEO_Cableway_" + role)
        mesh.from_pydata(source[0], [], source[1])
        mesh.update()
        uv = mesh.uv_layers.new(name="UVMap")
        for face in mesh.polygons:
            drop = max(range(3), key=lambda axis: abs(face.normal[axis]))
            axes = [axis for axis in range(3) if axis != drop]
            angles = [(math.atan2(mesh.vertices[mesh.loops[loop].vertex_index].co.y,
                mesh.vertices[mesh.loops[loop].vertex_index].co.x) / math.tau) % 1
                for loop in face.loop_indices]
            seam = max(angles) - min(angles) > .5
            for loop in face.loop_indices:
                vertex = mesh.vertices[mesh.loops[loop].vertex_index].co
                # Existing slot-local UV projection remains owned by the
                # surface MPB. These charts retain its normalized coordinates.
                if role in CYLINDERS and abs(face.normal.z) < .85:
                    angle = (math.atan2(vertex.y, vertex.x) / math.tau) % 1
                    if seam and angle < .5:
                        angle += 1
                    uv.data[loop].uv = (angle, (vertex.z + 1) * .5)
                else:
                    uv.data[loop].uv = (vertex[axes[0]] + .5, vertex[axes[1]] + .5)
        obj = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
        objects.append(obj)
        minimum, maximum = kit.bounds(unity)
        records.append(dict(name=mesh.name, role=role, bounds_min=minimum,
            bounds_max=maximum, triangle_count=kit.triangle_count(unity),
            vertex_count=len(unity[0]), signed_volume=bp.signed_volume(unity)))
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    for obj in [root] + objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir / (NAME + ".fbx")),
        use_selection=True, object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
        add_leaf_bones=False, bake_anim=False, mesh_smooth_type="FACE", path_mode="STRIP")
    payload = dict(design_id=DESIGN, generator_version=VERSION, build_signature=signature,
        mesh_count=len(records), triangle_count=total, parts=records,
        source_axes="X right, Y forward, Z up", unity_axes="X right, Y up, Z forward",
        origin="existing_primitive_envelope", colliders=False, lights=False, cameras=False,
        animation_count=0, maximum_template_vertices=24000, maximum_replacements=512)
    (args.model_dir / (NAME + ".json")).write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / (NAME + ".blend")))
    if args.preview:
        # Diagnostic contact sheet only; representative anisotropic slots.
        sizes = {"Canopy": (4, .30, 2.8), "Door": (.10, 1.8, 1.2),
                 "Bench": (2, .44, .42), "PostY": (.28, 2, .28),
                 "RailZ": (.08, .08, 1.8), "BeamZ": (.25, .25, 2),
                 "BeamX": (2, .25, .25), "ChannelZ": (.25, .25, 2),
                 "Bullwheel": (2.2, .12, 2.2), "Hub": (.45, .20, .45),
                 "Roller": (.5, .15, .5), "Weight": (.92, .2, .72),
                 "Roof": (2.2, .18, 2), "Skirt": (2, .7, 1.8)}
        for index, (role, obj) in enumerate(zip(geometry, objects)):
            size = sizes.get(role, (1.4, 1, 1.3))
            obj.scale = (size[0], size[2], size[1])
            obj.location = ((index % 5) * 4, (index // 5) * 3.8, size[1] * .5)
        center = Vector((8, 5.7, 0))
        camera_data = bpy.data.cameras.new("CablewayPartsReview")
        camera = bpy.data.objects.new("CablewayPartsReview", camera_data)
        bpy.context.collection.objects.link(camera)
        camera.location = center + Vector((13, -20, 22))
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera_data.type = "ORTHO"
        camera_data.ortho_scale = 25
        scene = bpy.context.scene
        scene.camera = camera
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.display.shading.light = "STUDIO"
        scene.display.shading.color_type = "SINGLE"
        scene.display.shading.single_color = (.29, .34, .31)
        scene.display.shading.show_cavity = True
        scene.render.resolution_x, scene.render.resolution_y = 1500, 1100
        scene.render.resolution_percentage = 100
        scene.render.filepath = str(args.preview)
        bpy.ops.render.render(write_still=True)
    print(f"CABLEWAY CRAFTED PARTS BUILD OK: {total} triangles; {signature}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--preview", type=Path)
    parser.add_argument("--validate-only", action="store_true")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build(parser.parse_args(argv))
