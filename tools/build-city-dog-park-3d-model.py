#!/usr/bin/env python3
"""Deterministic residential dog park and three articulated dogs, Unity metres.

Use tools/run-blender.py. +Y is up, +Z is muzzle/entrance, paws contact Y=0.
The source is passive: no actions, armatures, cameras, lights or collision.
Every mesh belongs to an authored pivot; Unity only poses the imported parts.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("dog_park_port", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)
base = port.base
VERSION = "1.0.0"
MODEL_DIR = ROOT / "Assets/Resources/City/DogPark"
SOURCE_DIR = ROOT / "ArtSource/City/DogPark"
PALETTE = (
    (.30, .235, .17, 1), (.47, .375, .25, 1), (.65, .565, .41, 1),
    (.76, .715, .59, 1), (.37, .385, .36, 1), (.19, .205, .195, 1),
    (.105, .115, .105, 1), (.055, .062, .055, 1), (.59, .445, .285, 1),
    (.36, .29, .225, 1), (.72, .665, .55, 1), (.285, .355, .33, 1),
    (.40, .375, .315, 1), (.49, .465, .39, 1), (.255, .265, .225, 1),
    (.58, .555, .485, 1),
)
DOGS = (
    dict(name="DogSmall", shoulder=.38, length=.49, width=.22, head=.14,
         coat=1, dark=0, light=3, kind="small alert terrier", ear="pointed", tail="up"),
    dict(name="DogShaggy", shoulder=.57, length=.71, width=.31, head=.19,
         coat=4, dark=5, light=10, kind="medium shaggy mongrel", ear="drop", tail="curved"),
    dict(name="DogLarge", shoulder=.74, length=.96, width=.38, head=.23,
         coat=8, dark=9, light=2, kind="large calm short coat mongrel", ear="drop", tail="down"),
)
PARK_ANCHORS = {
    "ANCHOR_Entry": (0, 0, 5), "ANCHOR_Interior": (0, 0, 0),
    "ANCHOR_OwnerStanding": (2.7, 0, 2.3),
    "ANCHOR_OwnerSeated": (-2.6, 0, -4.05),
    "ANCHOR_BenchSeat": (-2.6, .46, -4.05),
    "ANCHOR_Barrier": (2.45, .22, -2.3),
}


def ellipsoid(g, center, radii, color, rings=6, sides=12, tilt=0):
    """Closed low-poly anatomical volume; no stacked runtime primitives."""
    vertices = []
    angle = math.radians(tilt)
    for i in range(rings + 1):
        phi = math.pi * (i + .035) / (rings + .07)
        for j in range(sides):
            theta = j * math.tau / sides
            x = radii[0] * math.sin(phi) * math.cos(theta)
            y = radii[1] * math.cos(phi)
            z = radii[2] * math.sin(phi) * math.sin(theta)
            vertices.append((center[0] + x, center[1] + y * math.cos(angle) - z * math.sin(angle),
                             center[2] + y * math.sin(angle) + z * math.cos(angle)))
    faces = [tuple(reversed(range(sides))), tuple(range(rings * sides, (rings + 1) * sides))]
    for i in range(rings):
        for j in range(sides):
            a, b = i * sides + j, i * sides + (j + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    g.add(vertices, faces, PALETTE[color])


def geometry():
    return base.Geometry()


def part(g, name, parent, material, pivot_position=None, role="Plain"):
    o = g.object(name + "__" + role, None, material)
    # Geometry is authored in world metres; move the origin without moving it.
    if pivot_position is not None:
        delta = Vector(base.source(pivot_position))
        for v in o.data.vertices:
            v.co -= delta
        o.location = delta
    world = Matrix.Translation(Vector(base.source(pivot_position or (0, 0, 0))))
    o.parent = parent
    o.matrix_world = world
    bpy.context.view_layer.update()
    for polygon, color in zip(o.data.polygons, g.colors):
        index = PALETTE.index(color) if color in PALETTE else 12
        for loop in polygon.loop_indices:
            o.data.uv_layers.active.data[loop].uv = ((index + .5) / len(PALETTE), .5)
            if role != "Plain":
                vertex = o.data.vertices[o.data.loops[loop].vertex_index].co
                n = polygon.normal
                axis = max(range(3), key=lambda k: abs(n[k]))
                axes = ((1, 2), (0, 2), (0, 1))[axis]
                o.data.uv_layers.active.data[loop].uv = (vertex[axes[0]], vertex[axes[1]])
    return o


def pivot(name, point, parent):
    o = base.empty(name, None, point)
    world = Matrix.Translation(Vector(base.source(point)))
    o.parent = parent
    o.matrix_world = world
    bpy.context.view_layer.update()
    return o


def coat_tufts(g, centers, radius, color, seed):
    # Short overlapping closed wedges sit on the coat, never floating particles.
    for i, (x, y, z) in enumerate(centers):
        angle = (i * 2.399963 + seed) % math.tau
        dx, dz = math.cos(angle) * radius, math.sin(angle) * radius
        a = (x - dz * .45, y, z + dx * .45)
        b = (x + dz * .45, y, z - dx * .45)
        c = (x + dx, y - radius * .85, z + dz)
        d = (x - dx * .15, y + radius * .33, z - dz * .15)
        g.add([a, b, c, d], [(0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3)], PALETTE[color])


def dog(config, material):
    name, h, length, width = config["name"], config["shoulder"], config["length"], config["width"]
    main, dark, light = config["coat"], config["dark"], config["light"]
    root = base.empty(name)
    hip = (0, h * .92, -length * .32)
    torso = pivot("MOVE_Torso", hip, root)
    body = geometry()
    ellipsoid(body, (0, h * .87, -.025), (width * .51, h * .235, length * .57), main)
    ellipsoid(body, (0, h * .88, length * .29), (width * .55, h * .285, length * .22), main)
    ellipsoid(body, (0, h * .89, -length * .34), (width * .50, h * .23, length * .23), main)
    # A distinct withers/back and chest bib keep a canine silhouette from above.
    ellipsoid(body, (0, h * .97, -length * .13), (width * .43, h * .13, length * .37), dark)
    ellipsoid(body, (0, h * .78, length * .425), (width * .34, h * .22, length * .09), light)
    if name == "DogShaggy":
        centers = [(math.sin(i * 1.7) * width * .47, h * (.80 + (i % 3) * .07),
                    -length * .41 + i * length * .073) for i in range(12)]
        coat_tufts(body, centers, h * .075, light, 12)
    part(body, "DogBody", torso, material, hip)

    # Neck is carried with the head; a broad chest overlap hides the swivel.
    head_point = (0, h * 1.07, length * .43)
    head = pivot("MOVE_Head", head_point, torso)
    shape = geometry()
    ellipsoid(shape, (0, h * 1.09, length * .40), (width * .37, h * .24, length * .19), main, tilt=-24)
    ellipsoid(shape, (0, h * 1.235, length * .51), (config["head"] * .61, config["head"] * .67, config["head"] * .67), main)
    ellipsoid(shape, (0, h * 1.17, length * .51 + config["head"] * .72),
              (config["head"] * .43, config["head"] * .34, config["head"] * .63), light)
    ellipsoid(shape, (0, h * 1.19, length * .51 + config["head"] * 1.18),
              (config["head"] * .29, config["head"] * .21, config["head"] * .18), 7, 4, 8)
    # Muzzle mouth seam, brows and small near-black eyes, no emotive painted grin.
    for side in (-1, 1):
        eye = (side * config["head"] * .48, h * 1.28, length * .51 + config["head"] * .39)
        ellipsoid(shape, eye, (config["head"] * .105, config["head"] * .10, config["head"] * .09), 7, 4, 8)
        ellipsoid(shape, (eye[0], eye[1] + config["head"] * .09, eye[2] - .008),
                  (config["head"] * .15, config["head"] * .055, config["head"] * .13), dark, 4, 8)
        shape.rod((side * config["head"] * .25, h * 1.12, length * .51 + config["head"] * .50),
                  (side * config["head"] * .27, h * 1.12, length * .51 + config["head"] * 1.03),
                  config["head"] * .021, PALETTE[7], 4)
    if name == "DogShaggy":
        coat_tufts(shape, [(-.085, h * 1.25, length * .56), (.085, h * 1.25, length * .56),
                          (0, h * 1.09, length * .56 + .12)], .06, light, 3)
    part(shape, "DogHead", head, material, head_point)
    pivot("ANCHOR_Muzzle", (0, h * 1.18, length * .51 + config["head"] * 1.34), head)

    for side, suffix in ((-1, "L"), (1, "R")):
        p = (side * config["head"] * .49, h * 1.32, length * .48)
        ear = pivot("MOVE_Ear" + suffix, p, head)
        mesh = geometry()
        if config["ear"] == "pointed":
            x, y, z = p
            s = config["head"]
            mesh.add([(x - s * .27, y, z - s * .18), (x + s * .27, y, z - s * .18),
                      (x + side * s * .12, y + s * .78, z - s * .20),
                      (x - s * .24, y, z + s * .13), (x + s * .24, y, z + s * .13),
                      (x + side * s * .12, y + s * .78, z - s * .05)],
                     [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)], PALETTE[dark])
        else:
            ellipsoid(mesh, (p[0] * 1.15, p[1] - config["head"] * .34, p[2] - .015),
                      (config["head"] * .24, config["head"] * .64, config["head"] * .34), dark, 5, 8, 8)
        part(mesh, "DogEar" + suffix, ear, material, p)

    joints = []
    paw_radius = h * .11
    for front, z in ((True, length * .30), (False, -length * .36)):
        for side, suffix in ((-1, "L"), (1, "R")):
            code = ("F" if front else "B") + suffix
            x = side * width * (.36 if front else .38)
            upper_point = (x, h * (.89 if front else .85), z)
            lower_point = (x, h * .42, z + (h * -.035 if front else -h * .10))
            paw_point = (x, paw_radius * .52, z + h * .055)
            upper = pivot("MOVE_Leg" + code + "_Upper", upper_point, torso)
            lower = pivot("MOVE_Leg" + code + "_Lower", lower_point, upper)
            paw = pivot("MOVE_Leg" + code + "_Paw", paw_point, lower)
            up_mesh = geometry()
            up_mesh.rod(upper_point, lower_point, h * (.076 if front else .105), PALETTE[main], 8, h * .052)
            ellipsoid(up_mesh, upper_point, (h * .10, h * .15, h * .125), main, 4, 8)
            part(up_mesh, "DogLeg" + code + "Upper", upper, material, upper_point)
            low_mesh = geometry()
            low_mesh.rod(lower_point, paw_point, h * .050, PALETTE[main], 8, h * .032)
            ellipsoid(low_mesh, lower_point, (h * .057, h * .066, h * .057), main, 4, 8)
            part(low_mesh, "DogLeg" + code + "Lower", lower, material, lower_point)
            foot = geometry()
            ellipsoid(foot, (x, paw_radius * .52, paw_point[2] + paw_radius * .26),
                      (paw_radius * .64, paw_radius * .52, paw_radius), light, 4, 8)
            part(foot, "DogPaw" + code, paw, material, paw_point)
            pivot("ANCHOR_Foot" + code, (x, 0, paw_point[2] + paw_radius * .26), paw)
            joints.append(dict(leg=code, upper=list(upper_point), lower=list(lower_point),
                               paw=list(paw_point), contact=[x, 0, paw_point[2] + paw_radius * .26]))

    start = (0, h * .98, -length * .56)
    if config["tail"] == "up":
        mid, end = (0, h * 1.30, -length * .72), (0, h * 1.53, -length * .65)
    elif config["tail"] == "curved":
        mid, end = (0, h * 1.04, -length * .87), (0, h * 1.28, -length * .99)
    else:
        mid, end = (0, h * .65, -length * .83), (0, h * .40, -length * .83)
    tail_a = pivot("MOVE_Tail01", start, torso)
    tail_b = pivot("MOVE_Tail02", mid, tail_a)
    for a, b, parent, n, taper in ((start, mid, tail_a, "TailA", .7), (mid, end, tail_b, "TailB", .2)):
        mesh = geometry()
        mesh.rod(a, b, h * (.065 if name == "DogShaggy" else .042), PALETTE[main], 8,
                 h * .042 * taper)
        part(mesh, "Dog" + n, parent, material, a)
    return root, joints


def park(material):
    root = base.empty("StaticDogPark")
    # Welded green-grey wire, substantial posts and a real unobstructed entrance.
    steel = geometry()
    edges = [((-3.94, -4.94), (3.94, -4.94)), ((-3.94, -4.94), (-3.94, 4.94)),
             ((3.94, -4.94), (3.94, 4.94)), ((-3.94, 4.94), (-.86, 4.94)),
             ((.86, 4.94), (3.94, 4.94))]
    for a, b in edges:
        distance = math.dist(a, b)
        count = math.ceil(distance / 1.98)
        for i in range(count + 1):
            t = i / count
            x, z = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            steel.rod((x, .0, z), (x, 1.23, z), .045, PALETTE[11], 8)
            steel.rod((x, 1.23, z), (x, 1.25, z), .044, PALETTE[11], 8, .025)
        for y in (.12, 1.17):
            steel.rod((a[0], y, a[1]), (b[0], y, b[1]), .022, PALETTE[11], 6)
    part(steel, "ParkFencePosts", root, material)
    wire = geometry()
    for a, b in edges:
        count = math.ceil(math.dist(a, b) / .24)
        for i in range(count + 1):
            t = i / count
            x, z = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            wire.rod((x, .16, z), (x, 1.13, z), .006, PALETTE[11], 4)
        for y in (.24, .44, .64, .84, 1.04):
            wire.rod((a[0], y, a[1]), (b[0], y, b[1]), .006, PALETTE[11], 4)
    part(wire, "ParkFenceWire", root, material)
    ground = geometry()
    # Low, irregular pigment cells read as compacted soil, never an asphalt slab.
    for ix in range(10):
        for iz in range(12):
            x0, x1 = -4 + ix * .8, -4 + (ix + 1) * .8
            z0, z1 = -5 + iz * (10 / 12), -5 + (iz + 1) * (10 / 12)
            color = 12 + ((ix * 7 + iz * 3) % 3)
            ground.add([(x0, 0, z0), (x0, 0, z1), (x1, 0, z1), (x1, 0, z0)],
                       [(0, 1, 2, 3)], PALETTE[color], solid=False)
    part(ground, "ParkGround", root, material, role="Ground")
    bench = geometry()
    for x in (-3.28, -1.92):
        for z in (-4.33, -3.88):
            bench.rod((x, 0, z), (x, .43, z), .035, PALETTE[11], 6)
        bench.rod((x, .39, -4.40), (x, .91, -4.47), .035, PALETTE[11], 6)
    for z in (-4.31, -4.13, -3.95):
        bench.box((-2.6, .43, z), (1.80, .06, .15), PALETTE[0])
    for y in (.65, .83):
        bench.box((-2.6, y, -4.44), (1.8, .13, .06), PALETTE[0])
    part(bench, "ParkBench", root, material)
    hurdle = geometry()
    for x in (1.88, 3.02):
        hurdle.box((x, .20, -2.3), (.10, .40, .36), PALETTE[11])
        hurdle.box((x, .025, -2.3), (.26, .05, .36), PALETTE[11])
    hurdle.box((2.45, .39, -2.3), (1.40, .10, .10), PALETTE[15])
    part(hurdle, "ParkLowBarrier", root, material)
    for name, point in PARK_ANCHORS.items():
        base.empty(name, root, point)
    return root


def measure(root, joints=None):
    result = port.describe(root)
    meshes = [o for o in root.children_recursive if o.type == "MESH"]
    result["renderer_count"] = len(meshes)
    result["maximum_triangles"] = 12000 if joints is None else 6500
    result["maximum_renderers"] = 6 if joints is None else 23
    if result["triangles"] > result["maximum_triangles"] or len(meshes) > result["maximum_renderers"]:
        raise RuntimeError("Dog park exceeds its finite mesh budget: " + root.name)
    result["pivots"] = [dict(name=o.name, parent=o.parent.name,
                              position=[round(v, 5) for v in base.source(o.matrix_world.translation)])
                         for o in root.children_recursive if o.name.startswith("MOVE_")]
    if joints is not None:
        result["joints"] = joints
        result["ground_y"] = 0
        if abs(result["bounds_min"][1]) > .002:
            raise RuntimeError("Dog paws lost ground contact: " + root.name)
        for joint in joints:
            if math.dist(joint["upper"], joint["lower"]) < .08 or math.dist(joint["lower"], joint["paw"]) < .08:
                raise RuntimeError("Dog leg articulation lost a segment")
    return result


def build():
    material = base.material("DogParkVertexPalette")
    roots = [park(material)]
    models = [measure(roots[0])]
    for config in DOGS:
        root, joints = dog(config, material)
        roots.append(root)
        result = measure(root, joints)
        result["variant"] = config["kind"]
        result["shoulder_height"] = config["shoulder"]
        models.append(result)
    return roots, dict(design_id="city_residential_dog_park_v1", version=VERSION,
                       generator=Path(__file__).name,
                       coordinates="Unity metres +Y up, +Z muzzle/entrance, paw sole Y=0",
                       footprint=[-4, -5, 4, 5], entrance_width=1.6, fence_height=1.25,
                       palette=[list(c) for c in PALETTE], models=models)


def write_palette(path):
    image = bpy.data.images.new("DogParkPalette", width=len(PALETTE), height=1, alpha=False)
    image.pixels = [channel for color in PALETTE for channel in color]
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    base.reset()
    roots, result = build()
    if args.validate_only:
        saved = json.loads((args.model_dir / "CityDogPark3D.json").read_text())
        if saved != result:
            raise RuntimeError("Dog park source differs from committed geometry manifest")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            base.export(root, args.model_dir / (root.name + ".fbx"))
        write_palette(args.model_dir / "DogParkPalette.png")
        (args.model_dir / "CityDogPark3D.json").write_text(json.dumps(result, indent=2) + "\n")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityDogPark3D.blend"), check_existing=False)
    base.reset()
    _, repeated = build()
    if repeated != result:
        raise RuntimeError("Dog park deterministic rebuild differs")
    print("CITY DOG PARK ART CONTRACT OK: authored canine anatomy, four articulated legs, metre contacts, open entry, finite shared palette and deterministic geometry", flush=True)
    print(json.dumps([{k: model[k] for k in ("name", "triangles", "renderer_count", "bounds_min", "bounds_max")} for model in result["models"]]), flush=True)


if __name__ == "__main__":
    main()
