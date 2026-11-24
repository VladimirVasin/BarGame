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
import dog_park_finishes as finishes

base = port.base
VERSION = "2.0.0"
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
    config = None
    if name.startswith("Dog"):
        ancestor = parent
        while ancestor.parent:
            ancestor = ancestor.parent
        config = next(row for row in DOGS if row["name"] == ancestor.name)
    for polygon, color in zip(o.data.polygons, g.colors):
        slot = PALETTE.index(color)
        normal = Vector(base.source(polygon.normal))
        dominant = max(range(3), key=lambda k: abs(normal[k]))
        wood = name in ("ParkBench","ParkLowBarrier") and slot in (0,1)
        if wood:
            slot = 13 if dominant == 0 else 12
        uv = o.data.uv_layers.active.data
        for loop in polygon.loop_indices:
            vtx = o.data.vertices[o.data.loops[loop].vertex_index].co
            x,y,z = base.source(vtx + Vector(base.source(pivot_position or (0,0,0))))
            if role == "Ground":
                uv[loop].uv = ((x+4)/8,(z+5)/10)
                continue
            if config:
                length,h,width = config["length"],config["shoulder"],config["width"]
                # Same chart across torso and articulated parts in bind metres.
                u = (z/length+.65)/1.65
                v = 1-(y/h-.20)/1.13
                if dominant == 1:
                    v = .07 + .66*abs(x)/width
                elif dominant == 2:
                    u = .56 + x/(width*1.8)
            elif wood:
                u = (x+3.5)/1.80 if name=="ParkBench" else (x-1.75)/1.40
                if slot == 13:
                    u,v = (z*3)%1, (y*2)%1
                elif dominant == 1:
                    v = (z+4.41)/.63 if name=="ParkBench" else (z+2.36)/.12
                else:
                    v = y/.97
            else:
                u,v = (x+4)/8 if dominant != 0 else (z+5)/10, y/1.26
                if dominant == 1:
                    v = (z+5)/10
            uv[loop].uv = finishes.uv(slot,u,v)
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


def loft(g, stations, color, upright=False, sides=10):
    """Elliptical sections, centred and tapered independently along the limb or spine."""
    points = []
    for x, y, z, rx, ry in stations:
        for j in range(sides):
            angle = math.tau * j / sides
            a, b = rx * math.cos(angle), ry * math.sin(angle)
            points.append((x + a, y if upright else y + b, z + b if upright else z))
    last = len(points) - sides
    faces = [tuple(reversed(range(sides))), tuple(range(last, last + sides))]
    for i in range(0, last, sides):
        for j in range(sides):
            a, b = i + j, i + (j + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    g.add(points, faces, PALETTE[color])


def paw_mesh(g, x, z, r, coat, light):
    # A heel with a flat weight-bearing sole; two longer middle toes lead the step.
    loft(g, [(x, y*r, z+dz*r, w*r, d*r) for y,dz,w,d in
             ((0,.13,.52,.46),(.20,.14,.68,.66),(.62,-.02,.54,.53),
              (1.02,-.16,.30,.32))], light, upright=True)
    for toe in (-1.5,-.5,.5,1.5):
        mid = z + r * (1.00 - .16 * abs(toe))
        loft(g, [(x + toe*r*.34, y*r, mid+dz*r, w*r, d*r) for y,dz,w,d in
                 ((0,0,.16,.28),(.17,.02,.205,.37),(.39,-.05,.17,.28),(.51,-.13,.08,.12))],
             coat if abs(toe)>1 else light, upright=True, sides=8)


def muzzle(g, h, length, size, main, light, terrier):
    # Broad cheek tapers over a separate, slightly shorter lower jaw.
    origin = length * .51
    for stations, color in ((
            ((.19,.01,.47,.28),(.52,-.03,.45,.28),(.93,-.06,.33,.22),
             (1.19,-.05,.25,.15)), light), (
            ((.25,-.18,.38,.16),(.72,-.23,.32,.14),(1.11,-.20,.22,.09)), main)):
        loft(g, [(0,h*1.19+y*size,origin+z*size,w*size,d*size)
                 for z,y,w,d in stations],color)
    loft(g, [(0,h*1.19+y*size,origin+z*size,w*size,d*size)
             for z,y,w,d in ((1.10,.01,.25,.15),(1.24,.015,.28,.17),(1.34,-.02,.19,.10))],7)
    if terrier:
        for side in (-1,1):
            coat_tufts(g, [(side*size*.38,h*1.18,origin+size*.62),
                           (side*size*.32,h*1.155,origin+size*.89)],size*.22,light,11)


def dog(config, material):
    name, h, length, width = config["name"], config["shoulder"], config["length"], config["width"]
    main, dark, light = config["coat"], config["dark"], config["light"]
    root = base.empty(name)
    hip = (0, h * .92, -length * .32)
    torso = pivot("MOVE_Torso", hip, root)
    body = geometry()
    # One continuous rib cage, tucked flank, rump and brisket. The big dog's
    # ribs and withers are fuller; the terrier has a shorter, squarer trunk.
    chest = 1.12 if name == "DogLarge" else 1.0
    loft(body, [(0,y*h,z*length,w*width,d*h) for z,y,w,d in
                ((-.58,.90,.20,.14),(-.45,.89,.46,.24),(-.31,.91,.51,.23),
                 (-.13,.93,.43,.18),(.06,.88,.50,.23),(.24,.86,.55,.29*chest),
                 (.39,.89,.46,.25),(.47,.95,.29,.17))], main, sides=14)
    ellipsoid(body, (0,h*.81,length*.37),(width*.37,h*.21,length*.12),light,5,10)
    if name == "DogShaggy":
        centers = [(math.sin(i * 1.7) * width * .47, h * (.80 + (i % 3) * .07),
                    -length * .41 + i * length * .073) for i in range(12)]
        coat_tufts(body, centers, h * .075, light, 12)
        for side in (-1,1):
            centers = [(side*width*.42,h*(.84+.075*i),length*(.35-.015*i)) for i in range(4)]
            coat_tufts(body, centers, h*.12, main, 5)
    part(body, "DogBody", torso, material, hip)

    # Neck is carried with the head; a broad chest overlap hides the swivel.
    head_point = (0, h * .91, length * .16)
    head = pivot("MOVE_Head", head_point, torso)
    shape = geometry()
    ellipsoid(shape, (0, h * 1.09, length * .40), (width * .37, h * .24, length * .19), main, tilt=-24)
    ellipsoid(shape, (0, h * 1.235, length * .51), (config["head"] * .61, config["head"] * .67, config["head"] * .67), main)
    muzzle(shape, h, length, config["head"], main, light, name == "DogSmall")
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
            thigh = not front and name == "DogLarge"
            def shaft(g, top, bottom, profile):
                sections = []
                for t, width_factor, depth_factor, shift in profile:
                    y = top[1] + (bottom[1] - top[1]) * t
                    axis_z = top[2] + (bottom[2] - top[2]) * t + h * shift
                    sections.append((x,y,axis_z,h*width_factor,h*depth_factor))
                loft(g, sections, main, upright=True)
            shaft(up_mesh,upper_point,lower_point,
                  ((-.12,.06,.09,0),(.08,.10,.145 if front else .17,0),
                   (.34,.094,.16 if thigh else .12,.012),(.72,.065,.09,.01),
                   (1.02,.057,.068,0)))
            if name == "DogShaggy":
                coat_tufts(up_mesh, [(x+side*h*.057,h*.59,z),(x,h*.63,z-h*.075)],h*.082,light,9)
            part(up_mesh, "DogLeg" + code + "Upper", upper, material, upper_point)
            low_mesh = geometry()
            shaft(low_mesh,lower_point,paw_point,
                  ((-.07,.045,.048,0),(.05,.059,.064,0),(.28,.052,.059,-.012),
                   (.62,.035,.037,-.022 if not front else 0),(.84,.043,.047,-.012),
                   (1,.038,.040,0)))
            part(low_mesh, "DogLeg" + code + "Lower", lower, material, lower_point)
            foot = geometry()
            paw_mesh(foot,x,paw_point[2],paw_radius,main,light)
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
    wire = geometry()
    installed = set()
    for a, b in edges:
        distance = math.dist(a, b)
        count = math.ceil(distance / 1.98)
        dx, dz = (b[0]-a[0])/distance, (b[1]-a[1])/distance
        for i in range(count + 1):
            x, z = a[0]+dx*distance*i/count, a[1]+dz*distance*i/count
            key = (round(x,3),round(z,3))
            if key not in installed:
                installed.add(key)
                foot_radius = .058 if abs(x) < 1 and z > 4 else .068
                steel.rod((x,0,z),(x,.085,z),foot_radius,PALETTE[14],8,.055)
                steel.rod((x,.065,z),(x,1.22,z),.045,PALETTE[11],10)
                steel.rod((x,1.215,z),(x,1.25,z),.052,PALETTE[11],10,.039)
                for y in (.19,1.13):
                    steel.rod((x,y-.021,z),(x,y+.021,z),.049,PALETTE[15],8)
            if i == count:
                continue
            # Each welded panel ends short of the post; its tabs bridge the gap.
            span = distance/count
            def at(t,y):
                return (x+dx*t,y,z+dz*t)
            for t in (.066,span-.066):
                wire.rod(at(t,.14),at(t,1.16),.013,PALETTE[11],6)
                for y in (.19,1.13):
                    post = 0 if t < span/2 else span
                    steel.rod(at(post,y),at(t,y),.018,PALETTE[11],6)
            for y in (.15,.35,.55,.75,.95,1.15):
                wire.rod(at(.066,y),at(span-.066,y),.009,PALETTE[11],5)
            div = math.ceil((span-.132)/.21)
            for j in range(1,div):
                t = .066+(span-.132)*j/div
                wire.rod(at(t,.15),at(t,1.15),.007,PALETTE[11],5)
    part(steel, "ParkFencePosts", root, material)
    part(wire, "ParkFenceWire", root, material)
    ground = geometry()
    ground.add([(-4,0,-5),(-4,0,5),(4,0,5),(4,0,-5)],[(0,1,2,3)],PALETTE[12],solid=False)
    # A thin, irregular lip of trampled earth meets the lower approach between the posts.
    lip = [(-.81,0,5),(-.82,-.024,5.17),(-.48,-.024,5.29),(-.13,-.024,5.25),
           (.24,-.024,5.33),(.57,-.024,5.22),(.82,-.024,5.16),(.81,0,5)]
    ground.add(lip,[(0,i,i+1) for i in range(1,7)],PALETTE[12],solid=False)
    part(ground, "ParkGround", root, material, role="Ground")
    bench = geometry()
    def fitting(g, centre, size, index, bevel):
        port.chamfer(g,centre,size,PALETTE[index],bevel)
    for x in (-3.28,-1.92):
        for z in (-4.33,-3.88):
            footing = z + (-.035 if z < -4 else .035)
            fitting(bench,(x,.018,footing),(.115,.032,.14),11,.007)
            bench.rod((x,.030,footing),(x,.39,z),.030,PALETTE[11],8)
        fitting(bench,(x,.38,-4.13),(.059,.052,.55),11,.007)
        bench.rod((x,.37,-4.34),(x,.91,-4.47),.029,PALETTE[11],8)
    bench.rod((-3.28,.25,-4.30),(-1.92,.25,-4.30),.022,PALETTE[11],8)
    for z in (-4.31,-4.13,-3.95):
        fitting(bench,(-2.6,.431,z),(1.80,.058,.161),0,.010)
        for x in (-3.28,-1.92):
            bench.rod((x,.458,z),(x,.462,z),.013,PALETTE[15],8,.010)
    for y in (.65,.83):
        fitting(bench,(-2.6,y,-4.40),(1.8,.133,.056),0,.009)
        for x in (-3.28,-1.92):
            bench.rod((x,y,-4.376),(x,y,-4.369),.012,PALETTE[15],8)
    part(bench, "ParkBench", root, material)
    hurdle = geometry()
    for x in (1.88,3.02):
        fitting(hurdle,(x,.025,-2.3),(.235,.05,.35),11,.010)
        fitting(hurdle,(x,.198,-2.3),(.069,.30,.096),11,.009)
        fitting(hurdle,(x,.334,-2.3),(.09,.022,.15),15,.004)
        for sign in (-1,1):
            fitting(hurdle,(x,.382,-2.3+sign*.061),(.09,.105,.014),11,.004)
            hurdle.rod((x,.39,-2.3+sign*.061),(x,.39,-2.3+sign*.071),.017,PALETTE[15],6)
            hurdle.rod((x,.045,-2.3+sign*.125),(x,.059,-2.3+sign*.125),.019,PALETTE[15],6)
    fitting(hurdle,(2.45,.39,-2.3),(1.40,.09,.108),1,.011)
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    base.reset()
    roots, result = build()
    textures = finishes.maps()
    result["texture_sha256"] = {name: hashlib.sha256(data).hexdigest() for name,data in textures.items()}
    if args.validate_only:
        saved = json.loads((args.model_dir / "CityDogPark3D.json").read_text())
        if any((args.model_dir / (name+".png")).read_bytes() != data for name,data in textures.items()):
            raise RuntimeError("Dog park finish maps differ")
        if saved != result:
            raise RuntimeError("Dog park source differs from committed geometry manifest")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            base.export(root, args.model_dir / (root.name + ".fbx"))
        for name,data in textures.items():
            (args.model_dir / (name+".png")).write_bytes(data)
        (args.model_dir / "CityDogPark3D.json").write_text(json.dumps(result, indent=2) + "\n")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityDogPark3D.blend"), check_existing=False)
    base.reset()
    _, repeated = build()
    repeated["texture_sha256"] = result["texture_sha256"]
    if repeated != result:
        raise RuntimeError("Dog park deterministic rebuild differs")
    print("CITY DOG PARK ART CONTRACT OK: authored canine anatomy, four articulated legs, metre contacts, open entry, finite shared palette and deterministic geometry", flush=True)
    print(json.dumps([{k: model[k] for k in ("name", "triangles", "renderer_count", "bounds_min", "bounds_max")} for model in result["models"]]), flush=True)


if __name__ == "__main__":
    main()
