#!/usr/bin/env python3
"""Build the reusable city woman from the cannery operator's rest anatomy."""
from __future__ import annotations

import argparse
from dataclasses import replace
import importlib.util
import itertools
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
spec = importlib.util.spec_from_file_location("default_woman_anatomy", TOOLS / "build-cannery-woman-3d-model.py")
cannery = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = cannery
spec.loader.exec_module(cannery)
base = cannery.base
resident = cannery.resident
import hand_bowl_grip

HEIGHT = 1.63
HEAD_CROWN = 1.738
HEIGHT_SCALE = HEIGHT / HEAD_CROWN
MODEL = "OrdinaryWoman"
COLORS = {
    "skin": "B79A87", "shirt": "B5A58C", "cardigan": "73636A", "jacket": "60716A",
    "slacks": "5D5C4E", "twill": "4F5458", "shoe": "564A3D", "boot": "484645",
    "sole": "353431", "hat": "686659", "scarf": "7F8B7D", "mittens": "666058",
    "hair": "716D66",
}
HAIR_COLORS = {"gray": "716D66", "brunette": "47392F", "blond": "897858"}
STYLE_IDS = ("loose", "ponytail", "braid")
SCALP_ROWS = (*cannery.HEAD_ROWS, (1.735, .019, .022, -.014), (HEAD_CROWN, .003, .004, -.014))
CROWN_RINGS, CROWN_SEGMENTS = 13, 32
LOOSE_HAIR = "HAIR_Loose"
ATLAS_SIZE = 256
# Each row has a separate chart for the front/back pair. Small parts sample
# a neutral chart with their own color, keeping collars and soles independent.
CHARTS = {"shirt": 0, "cardigan": 1, "jacket": 2, "slacks": 3, "twill": 4, "hair": 5}
CHART_HEIGHT = 36
BODY_SHAPES = ("BodyShape.Slim", "BodyShape.Chubby")
GRIP_SHAPE = "CylindricalGrip"
GRIP_RADIUS = .022
TROUSER_NAMES = ("CLO_Trousers_SlacksUpper", "CLO_Trousers_StraightUpper")
CLIPS = (("Idle", 4.0, True), ("Walk", 1.25, True), ("Sit", 4.0, True),
         ("Guard", 1.0, False), ("Shove", 1.0, False))


def gray(level):
    return (level, level, level, 255)


def smoothstep(value):
    t = max(0., min(1., value))
    return t * t * (3. - 2. * t)


def clothing_atlas():
    canvas = cannery.PixelCanvas(ATLAS_SIZE, ATLAS_SIZE)
    canvas.rect(0, 0, ATLAS_SIZE, ATLAS_SIZE, gray(255))
    for cloth, chart in CHARTS.items():
        top = chart * CHART_HEIGHT
        for y in range(top, top + CHART_HEIGHT):
            for x in range(ATLAS_SIZE):
                if cloth == "hair":
                    lock = math.cos((x + 2 * math.sin(y * .19)) * .37)
                    level = 224 + round(11 * lock + 3 * math.sin(x * 2.1))
                else:
                    level = 242 - (x % 3 == 0) * 4 - (y % 3 == 1) * 3
                    level -= (x * 17 + y * 31) % 7
                canvas.put(x, y, gray(level))
        if cloth == "cardigan":
            canvas.rect(60, top + 1, 69, top + 35, gray(218))
            canvas.line(61, top + 1, 61, top + 35, gray(228))
            for y in range(top + 9, top + 34, 6):
                canvas.ellipse(66, y, 2, 1, gray(124))
        elif cloth == "jacket":
            canvas.rect(63, top + 1, 66, top + 35, gray(204))
            for x in (28, 91):
                canvas.line(x - 8, top + 26, x + 7, top + 24, gray(184))
                canvas.line(x - 8, top + 27, x + 7, top + 25, gray(246))
        elif cloth in ("slacks", "twill"):
            canvas.line(60, top + 3, 61, top + 10, gray(181))
            canvas.line(17, top + 3, 29, top + 8, gray(188))
            canvas.line(109, top + 3, 98, top + 8, gray(188))
            for x in (30, 97):
                canvas.line(x, top + 13, x, top + 34, gray(225))
    return canvas


def face_atlas(index):
    """Four unadorned adult faces. The perimeter matches the shared skin mesh."""
    canvas = cannery.PixelCanvas(64, 64)
    skin = (183, 154, 135)
    eye_y, spacing, width, brow_lift, nose, mouth, iris, age = (
        (26, 12, 6, 2, 2, 7, (86, 101, 94), 0),
        (28, 11, 5, 0, 4, 8, (100, 79, 65), 1),
        (27, 13, 6, 1, 3, 6, (102, 112, 104), 2),
        (29, 12, 5, 2, 5, 8, (111, 90, 63), 3),
    )[index]
    for y in range(64):
        for x in range(64):
            feather = math.sin(x / 63 * math.pi) * math.sin(y / 63 * math.pi)
            planes = 4 * math.exp(-((x - 25) / 16)**2 - ((y - 31) / 24)**2)
            planes -= 5 * math.exp(-((x - 34) / 13)**2 - ((y - 56) / 8)**2)
            tone = feather * (planes + ((x * 7 + y * 13) % 9 == 0))
            canvas.put(x, y, (*[round(channel + tone) for channel in skin], 255))
    brow = (96 + 9 * age, 72 + 8 * age, 61 + 7 * age, 255)
    lid = (93, 71, 62, 255)
    fold = (160, 129, 112, 255)
    for sign in (-1, 1):
        x = 32 + sign * spacing
        y = eye_y + (sign > 0 and index in (1, 3))
        canvas.line(x - sign * width, y - 5, x, y - 7 - brow_lift, brow)
        canvas.line(x, y - 7 - brow_lift, x + sign * (width + 1), y - 6, brow)
        canvas.ellipse(x, y, width, 2, (201, 188, 168, 255))
        canvas.ellipse(x - sign, y, 2, 2, (*iris, 255))
        canvas.put(x - sign, y, (58, 47, 41, 255))
        canvas.put(x - 1, y - 1, (203, 188, 166, 255))
        canvas.line(x - width, y - 1, x - 2, y - 2, lid)
        canvas.line(x - 2, y - 2, x + width, y - 1, lid)
        if age > 0:
            canvas.line(x - width + 1, y + 4, x + width - 1, y + 4, fold)
        if age > 1:
            canvas.line(x + sign * (width + 2), y + 1, x + sign * (width + 4), y + 3, fold)
    canvas.line(31, eye_y + 5, 32 - nose // 2, 40, (163, 128, 109, 255))
    canvas.line(34, eye_y + 5, 34 + nose // 2, 39, (192, 162, 140, 255))
    canvas.line(31 - nose // 2, 41, 32, 42, (137, 105, 91, 255))
    canvas.line(34, 42, 34 + nose // 2, 41, (146, 110, 94, 255))
    canvas.line(32 - mouth, 49, 32, 48 + index % 2, (133, 90, 81, 255))
    canvas.line(32, 48 + index % 2, 32 + mouth, 49, (133, 90, 81, 255))
    canvas.line(35 - mouth, 51, 29 + mouth, 51, (175, 124, 112, 255))
    if age >= 2:
        for sign in (-1, 1):
            canvas.line(32 + sign * (nose + 2), 43, 32 + sign * (mouth + 3), 49, fold)
    if index == 0:
        for x, y in ((17, 35), (21, 36), (25, 35), (39, 35), (44, 36), (47, 34)):
            canvas.put(x, y, (155, 119, 100, 255))
    return canvas


class OrdinaryWomanBuilder(cannery.WomanBuilder):
    def __init__(self):
        super().__init__()
        self.parts = {}
        self.items = []
        self.shaped = []

    def add(self, name, geometry, bone, color, role="clothing"):
        color = {"white": "white", "longsleeve": "shirt", "denim": "slacks"}.get(color, color)
        obj = super().add(name, geometry, bone, color, role)
        self.parts[name] = self.result.parts[-1]
        return obj

    def head(self):
        super().head(with_hair=False)
        self.shell("GEO_Scalp", ((1.712, .054, .059, -.014), (1.719, .049, .055, -.014),
                                (1.735, .019, .022, -.014), (HEAD_CROWN, .003, .004, -.014)),
                   "head", "skin", role="body")

    def jeans(self):
        super().jeans(with_belt=False)
        record = self.parts.pop("CLO_HighWaistJeans")
        record.obj.name = TROUSER_NAMES[0]
        self.parts[record.obj.name] = record

    def shell(self, name, rows, bone, color, *, sides=16, x=0., role="clothing"):
        vertices, faces = base.make_vertical_shell(rows, sides)
        obj = self.add(name, ([(a + x, b, c) for a, b, c in vertices], faces), bone, color, role)
        for polygon in obj.data.polygons:
            polygon.use_smooth = len(polygon.vertices) == 4
        return obj

    def copy_part(self, source_name, name, color, reshape, role="clothing"):
        record = self.parts[source_name]
        source = record.obj
        vertices = [reshape(vertex.co + source.location) for vertex in source.data.vertices]
        faces = [tuple(polygon.vertices) for polygon in source.data.polygons]
        result = self.add(name, (vertices, faces), record.bone, color, role)
        result.vertex_groups.clear()
        groups = [result.vertex_groups.new(name=group.name) for group in source.vertex_groups]
        for vertex in source.data.vertices:
            for weight in vertex.groups:
                if weight.weight > 0:
                    groups[weight.group].add([vertex.index], weight.weight, "REPLACE")
        for before, after in zip(source.data.polygons, result.data.polygons):
            after.use_smooth = before.use_smooth
        return name

    def remove_part(self, name):
        record = self.parts.pop(name)
        self.result.parts.remove(record)
        mesh = record.obj.data
        bpy.data.objects.remove(record.obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)

    def item(self, item_id, names, covers=()):
        self.items.append({"id": item_id, "slot": item_id.split(".")[0],
                           "renderers": list(names), "covered_renderers": list(covers)})

    def shoes(self):
        for side in ("L", "R"):
            for old in ("GEO_WorkShoe.", "GEO_ShoeWelt.", "GEO_ShoeHeel."):
                self.remove_part(old + side)
            bone = "foot." + side
            x = base.BONE_BY_NAME[bone].head[0]
            self.shell("GEO_BareFoot." + side,
                       ((0., .043, .098, -.049), (.039, .049, .102, -.045),
                        (.099, .035, .042, -.005), (.137, .032, .035, 0.)),
                       bone, "skin", x=x, role="body")
        for item, label, color, ankle in (("everyday", "WalkingShoe", "shoe", .101),
                                          ("work", "AnkleBoot", "boot", .137)):
            names, covered = [], []
            for side in ("L", "R"):
                bone = "foot." + side
                x = base.BONE_BY_NAME[bone].head[0]
                upper = "CLO_" + label + "." + side
                sole = "CLO_" + label + "Sole." + side
                self.shell(sole, ((0., .053, .118, -.059), (.006, .057, .122, -.060),
                                  (.023, .056, .123, -.061), (.028, .053, .117, -.060)),
                           bone, "sole", x=x, sides=14)
                self.shell(upper, ((.024, .053, .118, -.060), (.043, .055, .118, -.060),
                                   (.062, .049, .104, -.052), (.084, .042, .063, -.024),
                                   (ankle, .042, .046, -.008)),
                           bone, color, x=x, sides=14)
                sock = "CLO_" + label + "Sock." + side
                obj = self.shell(sock, ((.079, .038, .051, -.010), (.114, .035, .042, -.002),
                                        (.150, .037, .037, .004), (.165, .035, .035, .004)),
                                 bone, "sole", x=x, sides=10)
                self.weight_height(obj, ((.084, bone), (.145, "shin." + side)))
                names.extend((upper, sole, sock))
                covered.append("GEO_BareFoot." + side)
            self.item("boots." + item, names, covered)

    def clothes(self):
        shirt = ("CLO_LongSleeveTop", "CLO_CrewNeckRib", "CLO_Sleeve.L", "CLO_Sleeve.R",
                 "CLO_Cuff.L", "CLO_Cuff.R")
        self.shaped.extend(shirt)
        underlayer = []
        for i, name in enumerate(shirt[:4]):
            if i == 1:
                continue
            shrink = lambda p: (p.x * .86, p.y * .82, p.z)
            if i >= 2:
                shrink = lambda p: (p.x * .99, p.y * .75, p.z)
            underlayer.append(self.copy_part(name, "GEO_Under" + name[4:], "skin", shrink, "body"))
        self.shaped.extend(underlayer)
        self.item("shirt.everyday", shirt, underlayer)
        for item_id, label, color, width, depth, hem in (
            ("cardigan", "Cardigan", "cardigan", 1.10, 1.13, .025),
            ("everyday", "ShortJacket", "jacket", 1.16, 1.18, .045),
        ):
            def torso(point):
                # Drape the outer hem over the widest trouser waist, including
                # the tucked tee. Keep the hip ease in the actual cloth shell.
                hip = max(0., min(1., (1.20 - point.z) / .14))**2
                y = point.y * (depth + .43 * hip)
                z = point.z - hem * max(0., min(1., (1.29 - point.z) / .22))
                return point.x * (width + .44 * hip), y, z

            def sleeve(point):
                taper = max(0., min(1., (abs(point.x) - .22) / .25))
                return point.x * (1 + (width - 1) * .26 * (1 - taper)), point.y * depth, point.z

            names = [self.copy_part(shirt[0], "CLO_" + label, color, torso)]
            names += [self.copy_part("CLO_Sleeve." + side, "CLO_" + label + "Sleeve." + side, color, sleeve)
                      for side in ("L", "R")]
            names += [self.copy_part("CLO_Cuff." + side, "CLO_" + label + "Cuff." + side, color, sleeve)
                      for side in ("L", "R")]
            # The raised rib at the neck follows the chest on either top.
            neck = "CLO_" + label + "Collar"
            self.shell(neck, ((1.421, .069, .058, -.010), (1.437, .066, .056, -.010),
                              (1.454 if color == "jacket" else 1.444, .059, .053, -.010)),
                       "chest", color)
            names.append(neck)
            self.shaped.extend(names)
            self.item("outerwear." + item_id, names, shirt + tuple(underlayer))

        def straight_leg(point):
            taper = max(0., min(1., (.70 - point.z) / .54))
            center = math.copysign(.085, point.x)
            return center + (point.x - center) * (1 + taper * .22), point.y * (1 + taper * .18), point.z

        self.copy_part(TROUSER_NAMES[0], TROUSER_NAMES[1], "twill", straight_leg)
        self.copy_part(TROUSER_NAMES[0], "GEO_UnderTrousers", "skin", lambda p: (p.x * .87, p.y * .85, p.z), "body")
        self.shaped.extend((*TROUSER_NAMES, "GEO_UnderTrousers"))
        self.item("trousers.everyday", TROUSER_NAMES[:1], ("GEO_UnderTrousers",))
        self.item("trousers.work", TROUSER_NAMES[1:], ("GEO_UnderTrousers",))
        self.shell("CLO_Scarf", ((1.409, .080, .067, -.009), (1.430, .079, .071, -.013),
                                  (1.449, .070, .064, -.012), (1.469, .063, .056, -.009)),
                   "neck", "scarf")
        self.item("scarf.warm", ("CLO_Scarf",))

    def hands(self):
        gloves, skin = [], []
        radius = GRIP_RADIUS / HEIGHT_SCALE
        for side in ("L", "R"):
            bone = "hand." + side
            wrist = Vector(base.BONE_BY_NAME[bone].head)
            direction = (Vector(base.BONE_BY_NAME[bone].tail) - wrist).normalized()
            q = direction.to_track_quat("Z", "Y")
            normal = q @ Vector((0., -1., 0.))
            across = q @ Vector((1., 0., 0.))
            centre = wrist + direction * .070 + normal * .036
            for label, point in (("Centre", centre), ("Axis", centre - across * .05), ("Palm", centre + normal * .05)):
                name = "ANCHOR_HandGrip" + label + "." + side
                anchor = bpy.data.objects.new(name, None)
                self.result.export_collection.objects.link(anchor)
                anchor.parent = self.result.rig
                anchor.parent_type, anchor.parent_bone = "BONE", bone
                bpy.context.view_layer.update()
                anchor.matrix_world = Matrix.Translation(point)
                self.result.anchors[name] = anchor

            for part in ("Palm", "Finger0", "Finger1", "Finger2", "Finger3", "Thumb"):
                suffix = part + "." + side
                record = self.parts.pop("GEO_" + suffix)
                obj = record.obj
                obj.name = "GEO_Hand" + suffix
                self.parts[obj.name] = record
                skin.append(obj.name)
                points = [v.co + obj.location for v in obj.data.vertices]
                if part == "Palm":
                    closed = [point - normal * max(0., (point - centre).dot(normal) + radius + .001)
                              if (point - wrist).dot(direction) > .036 else point for point in points]
                else:
                    sides = 8 if part == "Thumb" else 6
                    rings = [points[i:i+sides] for i in range(0, len(points), sides)]
                    neutral = [sum(ring, Vector()) / sides for ring in rings]
                    radii = [max((v - origin).length for v in ring) for origin, ring in zip(neutral, rings)]
                    if part == "Thumb":
                        target = hand_bowl_grip.thumb_centres(
                            (neutral[0], neutral[1], neutral[3]), centre, direction, normal, radius, radii[-1])
                        fraction = (neutral[2] - neutral[1]).length / (neutral[3] - neutral[1]).length
                        target.insert(2, target[1].lerp(target[2], min(1., fraction)))
                    else:
                        target = hand_bowl_grip.finger_centres(neutral, centre, direction, normal, radius, radii[0])
                    # Carry each original cross section onto the new phalanx
                    # direction. The surface keeps its shape and vertex order.
                    closed = []
                    for i, (ring, origin, bent) in enumerate(zip(rings, neutral, target)):
                        before, after = max(0, i - 1), min(len(rings) - 1, i + 1)
                        turn = (neutral[after] - neutral[before]).rotation_difference(target[after] - target[before])
                        closed.extend(bent + turn @ (vertex - origin) for vertex in ring)
                glove = self.copy_part(obj.name, "CLO_Glove" + suffix, "mittens", lambda p: wrist + (p - wrist) * 1.075)
                gloves.append(glove)
                for name, amount in ((obj.name, 1.), (glove, 1.075)):
                    mesh = self.parts[name].obj
                    mesh.shape_key_add(name="Basis")
                    key = mesh.shape_key_add(name=GRIP_SHAPE)
                    for vertex, origin, bent in zip(key.data, points, closed):
                        vertex.co = wrist + (origin - wrist) * amount + (bent - origin) - mesh.location
                    for polygon in mesh.data.polygons:
                        polygon.use_smooth = True
        self.item("gloves.work", gloves, skin)

    @staticmethod
    def scalp_at(z):
        for lower, upper in zip(SCALP_ROWS, SCALP_ROWS[1:]):
            if lower[0] <= z <= upper[0]:
                t = (z - lower[0]) / (upper[0] - lower[0])
                return tuple(a + (b - a) * t for a, b in zip(lower[1:], upper[1:]))
        raise ValueError("Hair sampled outside the source skull")

    def crown(self, style):
        # Follow the actual skull cross-sections, including the crown. The
        # uneven lower edge is the hairline, with volume built above it.
        rings, segments = CROWN_RINGS, CROWN_SEGMENTS
        vertices = []
        for row in range(rings):
            t = math.sin(row / (rings - 1) * math.pi * .5)
            for j in range(segments):
                angle = math.tau * j / segments
                front = (1 + math.cos(angle)) * .5
                forehead = {"loose": 1.643, "ponytail": 1.652, "braid": 1.647}[style]
                edge = 1.523 + (forehead - 1.523) * front**2.4
                edge += .009 * math.sin(angle * 2 + .7) * front
                if style == "ponytail":
                    edge += .020 * math.sin(angle)**6
                z = edge + (HEAD_CROWN - edge) * t
                rx, ry, cy = self.scalp_at(min(z, HEAD_CROWN))
                lift = .005 + .007 * math.sin(math.pi * t * .5)
                lock = .003 * math.sin(angle * 5 + t * 3) * math.sin(t * math.pi)
                lift += lock if style == "loose" else lock * .43
                y = cy - (ry + lift) * math.cos(angle)
                if style == "loose":
                    # The back of the crown gains depth toward the nape.
                    # Its lowest rear row is also the first row of the length.
                    rear = max(0., -math.cos(angle))**1.4
                    fall = .097 + .035 * smoothstep((1.675 - z) / .152)
                    y += rear * smoothstep((1.71 - z) / .055) * max(0., fall - ry - lift)
                vertices.append(((rx + lift) * math.sin(angle), y, z + .016 * t**3))
        faces = [(row * segments + j, row * segments + (j + 1) % segments,
                  (row + 1) * segments + (j + 1) % segments, (row + 1) * segments + j)
                 for row in range(rings - 1) for j in range(segments)]
        faces.append(tuple(range((rings - 1) * segments, rings * segments)))
        return vertices, faces

    def loose_hair(self):
        vertices, faces = self.crown("loose")
        columns = tuple(range(6, CROWN_SEGMENTS - 5))
        hairline = [Vector(vertices[column]) for column in columns]
        uv_columns = [index % CROWN_SEGMENTS for index in range(len(vertices))]

        def row(points):
            start = len(vertices)
            vertices.extend(points)
            uv_columns.extend(columns)
            return tuple(range(start, len(vertices)))

        # Both exposed sides and the ends have thickness. The inner surface
        # begins inside the skull and returns along the free edges.
        outer = columns
        inner = row((point.x * .94, point.y - .014, point.z + .004) for point in hairline)
        for t, width, side_y, rear_y in (
            (.12, .100, .012, .129), (.24, .110, .059, .151), (.38, .118, .101, .172),
            (.52, .119, .126, .180), (.67, .114, .144, .185), (.81, .107, .151, .182),
            (.92, .101, .150, .174), (1.0, .091, .147, .170),
        ):
            blend = smoothstep(t / .38)
            points = []
            for i, origin in enumerate(hairline):
                across = 2 * i / (len(hairline) - 1) - 1
                arch = math.sqrt(1 - across**2)
                hem = 1.202 + .078 * abs(across)**2.6 + .014 * math.sin(7 * across + .8)
                wave = .005 * math.sin(across * 19 + t * .85) * arch
                target = Vector((-across * width + .010 * t**2,
                                 side_y + (rear_y - side_y) * arch + wave,
                                 origin.z))
                point = origin.lerp(target, blend)
                point.z = origin.z + t * (hem - origin.z)
                points.append(point)
            thickness = .015 + .007 * math.sin(math.pi * t)
            next_outer = row(points)
            next_inner = row((point.x * .94, point.y - thickness, point.z + .004) for point in points)
            for i in range(len(columns) - 1):
                faces.append((next_outer[i], next_outer[i + 1], outer[i + 1], outer[i]))
                faces.append((inner[i], inner[i + 1], next_inner[i + 1], next_inner[i]))
            faces.extend(((next_outer[0], outer[0], inner[0], next_inner[0]),
                          (outer[-1], next_outer[-1], next_inner[-1], inner[-1])))
            outer, inner = next_outer, next_inner
        faces.extend((outer[i + 1], outer[i], inner[i], inner[i + 1]) for i in range(len(columns) - 1))

        hair = self.add(LOOSE_HAIR, (vertices, faces), "head", "hair", "hair")
        for polygon in hair.data.polygons:
            polygon.use_smooth = True
        self.weight_height(hair, ((1.24, "chest"), (1.43, "neck"), (1.522, "head")))

        # Along the back, each strip keeps the same U from crown to ends.
        # Wrap at the front parting, away from the crown-to-length transition.
        layer = hair.data.uv_layers.new(name="UVMap")
        bottom, top = min(point[2] for point in vertices), max(point[2] for point in vertices)
        chart = CHARTS["hair"] * CHART_HEIGHT
        for polygon in hair.data.polygons:
            indices = [hair.data.loops[index].vertex_index for index in polygon.loop_indices]
            wrap = len(indices) == 4 and any(uv_columns[index] == CROWN_SEGMENTS - 1 for index in indices)
            for loop, index in zip(polygon.loop_indices, indices):
                column = uv_columns[index]
                if wrap and column == 0:
                    column = CROWN_SEGMENTS
                length = (top - vertices[index][2]) / (top - bottom)
                layer.data[loop].uv = ((2 + 251 * column / CROWN_SEGMENTS) / ATLAS_SIZE,
                                       1 - (chart + 1 + 33 * length) / ATLAS_SIZE)
        self.item("hair.loose", (LOOSE_HAIR,))

    def ponytail(self):
        points = ((.007, .100, 1.554), (.012, .128, 1.525), (.021, .163, 1.468),
                  (.027, .202, 1.388), (.030, .213, 1.306), (.044, .196, 1.226),
                  (.053, .184, 1.187))
        return self.tube_path(points, (.020, .026, .047, .048, .040, .023, .006), 14,
                              ellipticity=(.82, .82, .69, .64, .63, .67, .77))

    def braid(self):
        strands = []
        for strand in range(3):
            points, radii = [], []
            for row in range(19):
                t = row / 18
                phase = math.tau * (2.55 * t + strand / 3)
                braid_width = .023 * (1 - .63 * t)
                points.append((-.005 - .092 * t + braid_width * math.sin(phase),
                               .102 + .112 * math.sin(math.pi * t * .76) + braid_width * .58 * math.cos(phase),
                               1.546 - .397 * t))
                radii.append(.020 * (1 - .70 * t))
            strands.append(self.tube_path(points, radii, 8))
        return base.combine_geometry(*strands)

    def hairstyles(self):
        self.loose_hair()
        for style, lengths in (("ponytail", self.ponytail()), ("braid", self.braid())):
            crown = "HAIR_" + style.title() + "Crown"
            length = "HAIR_" + style.title() + "Length"
            scalp = self.add(crown, self.crown(style), "head", "hair", "hair")
            tails = self.add(length, lengths, "head", "hair", "hair")
            for obj in (scalp, tails):
                for polygon in obj.data.polygons:
                    polygon.use_smooth = True
            # The length hangs from the scalp and follows the back as the
            # head turns. This stays on the existing animated body bones.
            self.weight_height(tails, ((1.23, "chest"), (1.42, "neck"), (1.54, "head")))
            self.item("hair." + style, (crown, length))
        # A turned knit brim sits over the crown. All three lengths and the
        # hairline below the brim stay visible with this optional garment.
        self.shell("CLO_KnitCap", ((1.629, .103, .111, -.014), (1.634, .111, .118, -.014),
                                   (1.651, .111, .117, -.014), (1.656, .107, .113, -.014),
                                   (1.687, .104, .110, -.014), (1.719, .089, .098, -.014),
                                   (1.744, .065, .072, -.014), (1.761, .035, .040, -.014),
                                   (1.769, .009, .011, -.014)), "head", "hat", sides=20)
        self.item("headwear.everyday", ("CLO_KnitCap",))

    def uv(self, part):
        obj = part.obj
        layer = obj.data.uv_layers.active or obj.data.uv_layers.new(name="UVMap")
        chart = CHARTS.get(part.palette_name)
        if chart is None:
            for loop in layer.data:
                loop.uv = (.5, .06)
            return
        points = [vertex.co + obj.location for vertex in obj.data.vertices]
        low = [min(point[axis] for point in points) for axis in (0, 2)]
        high = [max(point[axis] for point in points) for axis in (0, 2)]
        detail = obj.name.endswith((".L", ".R", "Collar", "Rib"))
        for polygon in obj.data.polygons:
            start = 2 if polygon.normal.y < 0 and not detail else 130
            for index in polygon.loop_indices:
                point = points[obj.data.loops[index].vertex_index]
                u = (point.x - low[0]) / (high[0] - low[0])
                v = (high[1] - point.z) / (high[1] - low[1])
                layer.data[index].uv = ((start + u * 123) / ATLAS_SIZE,
                                        1 - (chart * CHART_HEIGHT + 1 + v * 33) / ATLAS_SIZE)

    def body_shapes(self):
        for name in self.shaped:
            obj = self.parts[name].obj
            obj.shape_key_add(name="Basis")
            for key, sign in zip(BODY_SHAPES, (-.68, 1.)):
                shape = obj.shape_key_add(name=key)
                for vertex in obj.data.vertices:
                    x, y, z = vertex.co + obj.location
                    torso = math.exp(-((z - 1.16) / .24)**2)
                    hip = math.exp(-((z - .94) / .25)**2)
                    cuff = max(0., min(1., (.45 - abs(x)) / .24))
                    shape.data[vertex.index].co = vertex.co + Vector((
                        sign * x * (.091 * torso + .076 * hip) * cuff,
                        sign * y * (.22 * torso + .15 * hip) * cuff,
                        0.,
                    ))

    def build(self):
        base.PALETTE.update({name: cannery.hex_to_linear_rgba(value) for name, value in COLORS.items()})
        result = super().build()
        base.PALETTE.update({name: cannery.hex_to_linear_rgba(value) for name, value in COLORS.items()})
        self.clothes()
        self.shoes()
        self.hands()
        self.hairstyles()
        for part in result.parts:
            if part.obj.name not in (cannery.FACE_NAME, LOOSE_HAIR):
                self.uv(part)
        self.body_shapes()
        bpy.context.view_layer.update()
        return result


def validate_hair_coverage(builder):
    def surface(names):
        vertices, faces = base.combine_geometry(*[
            ([vertex.co + builder.parts[name].obj.location for vertex in builder.parts[name].obj.data.vertices],
             [tuple(polygon.vertices) for polygon in builder.parts[name].obj.data.polygons])
            for name in names
        ])
        return BVHTree.FromPolygons(vertices, faces)

    scalp = surface((cannery.FACE_NAME, "GEO_Head", "GEO_Scalp"))
    rays = [(Vector((0., -.014, z)), Vector((math.sin(angle), -math.cos(angle), 0.)))
            for z in (1.670, 1.688, 1.705, 1.722, 1.732)
            for angle in (step * math.tau / 32 for step in range(32))]
    rays.append((Vector((0., -.014, 1.69)), Vector((0., 0., 1.))))
    minimum = {}
    for style in STYLE_IDS:
        hair = surface((LOOSE_HAIR if style == "loose" else "HAIR_" + style.title() + "Crown",))
        gaps = []
        for origin, direction in rays:
            skin_distance = scalp.ray_cast(origin, direction)[3]
            hair_distance = hair.ray_cast(origin, direction)[3]
            if skin_distance is None or hair_distance is None:
                raise RuntimeError(f"{style} leaves the skull exposed at {origin}, {direction}")
            gaps.append(hair_distance - skin_distance)
        if min(gaps) < .002:
            raise RuntimeError(f"{style} intersects the scalp: {min(gaps):.5f} m clearance")
        minimum[style] = round(min(gaps), 6)
    return minimum


def validate_loose_hair(builder):
    mesh = builder.parts[LOOSE_HAIR].obj.data
    neighbours = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        neighbours[a].append(b)
        neighbours[b].append(a)
    seen = {0}
    pending = [0]
    while pending:
        for vertex in neighbours[pending.pop()]:
            if vertex not in seen:
                seen.add(vertex)
                pending.append(vertex)
    if len(seen) != len(mesh.vertices):
        raise RuntimeError("Loose hair requires one connected crown and length.")

    # Each join edge must have one crown face and one continuation face,
    # carrying exactly the same strand coordinates on both sides.
    layer = mesh.uv_layers.active.data
    for column in range(7, CROWN_SEGMENTS - 7):
        edge = (column, column + 1)
        sides = []
        for face in mesh.polygons:
            if all(vertex in face.vertices for vertex in edge):
                coordinates = {loop.vertex_index: tuple(layer[loop.index].uv)
                               for loop in (mesh.loops[i] for i in face.loop_indices)}
                sides.append(tuple(coordinates[vertex] for vertex in edge))
        if len(sides) != 2 or sides[0] != sides[1]:
            raise RuntimeError("The crown-to-length transition split an edge or its UVs.")


def street_arm_pose(pose, side_offset, elbow_flex=1., swing=1.):
    adjusted = dict(pose)
    for side, sign in (("L", 1), ("R", -1)):
        upper = "upper_arm." + side
        forearm = "forearm." + side
        x, y, z = pose[upper].target_direction
        bend, twist, roll = pose[forearm].rotation_degrees
        adjusted[upper] = replace(pose[upper], target_direction=(x + sign * side_offset, y * swing, z))
        adjusted[forearm] = replace(pose[forearm], rotation_degrees=(bend * elbow_flex, twist, roll))
    return adjusted


def woman_walk_keys(neutral):
    keys = []
    for phase, pose in base.citizen_walk_keys(neutral):
        # Weight moves over the supporting leg between heel contacts. The
        # pelvis tips with the transfer and the shoulders follow more quietly.
        transfer = math.sin(math.tau * (phase % 1.))
        pelvis, chest = pose["pelvis"], pose["chest"]
        pitch, yaw, roll = pelvis.rotation_degrees
        pose = base.merge_pose(pose, {
            "pelvis": replace(pelvis, location_m=(.012 * transfer, 0., 0.),
                              rotation_degrees=(pitch, yaw, roll + 1.6 * transfer)),
            "chest": replace(chest, rotation_degrees=(*chest.rotation_degrees[:2],
                                                     chest.rotation_degrees[2] - .65 * transfer)),
        })
        # A softer elbow fold keeps the wrist beside the hip during the swing.
        keys.append((phase, street_arm_pose(pose, side_offset=.044, elbow_flex=.55, swing=.85)))
    return keys


def make_actions(builder):
    scene = bpy.context.scene
    scene.render.fps = 24
    rig = builder.result.rig
    neutral = resident.action_pose("Idle", 0.)
    seated = base.seated_pose(base.weigh_attendant_base_pose())
    seated_breath = base.merge_pose(seated, {"chest": base.BonePose(rotation_degrees=(3., .8, -1.))})
    keys = {
        "Idle": [(i / 4, street_arm_pose(resident.action_pose("Idle", i / 4), side_offset=.025)) for i in range(5)],
        "Walk": woman_walk_keys(neutral),
        "Sit": ((0., seated), (.5, seated_breath), (1., seated)),
        "Guard": base.personal_space_keys(rig, neutral, False),
        "Shove": base.personal_space_keys(rig, neutral, True),
    }
    actions = {}
    for name, seconds, loop in CLIPS:
        action = base.ActionSpec(MODEL + name, "ordinary-woman-v1", seconds, round(seconds * 24),
                                 "ordinary street posture", "hip clearance and weight transfer" if name == "Walk"
                                 else "shared city motion", one_shot=not loop)
        actions[name] = base.create_action(rig, action, keys[name])
    return keys["Sit"], actions


def validate_arm_clearance(builder, action):
    trousers = [builder.parts[name] for name in TROUSER_NAMES]
    arms = [part for name, part in builder.parts.items() if name.startswith(("GEO_Hand", "CLO_Glove")) or
            "Sleeve." in name or "Cuff." in name]
    faces = {part.obj.name: [tuple(poly.vertices) for poly in part.obj.data.polygons] for part in trousers + arms}
    shaped = [part.obj.data.shape_keys.key_blocks for part in trousers + arms if part.obj.data.shape_keys]
    scene = bpy.context.scene
    rig = builder.result.rig
    rig.animation_data.action = action
    minimum = math.inf

    def set_build(build):
        for blocks in shaped:
            for name in BODY_SHAPES:
                if name in blocks:
                    blocks[name].value = float(name == build)

    try:
        for build in (None, *BODY_SHAPES):
            set_build(build)
            for step in range(round(action.frame_end * 2) + 1):
                frame = step * .5
                scene.frame_set(step // 2, subframe=frame % 1.)
                deps = bpy.context.evaluated_depsgraph_get()
                hips = [BVHTree.FromPolygons(base.evaluated_part_world_vertices(part, deps), faces[part.obj.name])
                        for part in trousers]
                for part in arms:
                    vertices = base.evaluated_part_world_vertices(part, deps)
                    surface = BVHTree.FromPolygons(vertices, faces[part.obj.name])
                    for hip in hips:
                        if hip.overlap(surface):
                            raise RuntimeError(f"{action.name} {build} at {frame:g}: {part.obj.name} crosses the trousers")
                        for vertex in vertices:
                            point, normal, _, distance = hip.find_nearest(vertex)
                            gap = distance * HEIGHT_SCALE if (vertex - point).dot(normal) >= 0. else -distance * HEIGHT_SCALE
                            if gap < .010:
                                raise RuntimeError(f"{action.name} {build} at {frame:g}: {part.obj.name} hip gap {gap:.4f} m")
                            minimum = min(minimum, gap)
    finally:
        rig.animation_data.action = None
        set_build(None)
        scene.frame_set(0)
        base.reset_pose(rig)
    return round(minimum, 6)


def seat_contract(builder, seated_keys):
    rig = builder.result.rig
    support = []
    headroom = []
    for _, pose in seated_keys:
        base.reset_pose(rig)
        base.apply_pose(rig, pose)
        pelvis = rig.pose.bones["pelvis"].head.copy()
        deps = bpy.context.evaluated_depsgraph_get()
        for name in TROUSER_NAMES:
            vertices = base.evaluated_part_world_vertices(builder.parts[name], deps)
            seat = min(v.z for v in vertices if abs(v.x) < .18 and v.y > pelvis.y - .06)
            support.append((pelvis.z - seat) * HEIGHT_SCALE)
        cap = base.evaluated_part_world_vertices(builder.parts["CLO_KnitCap"], deps)
        headroom.append((max(vertex.z for vertex in cap) - pelvis.z) * HEIGHT_SCALE)
    base.reset_pose(rig)
    if not .05 <= min(support) <= max(support) <= .13:
        raise RuntimeError("Seated trousers must meet the cushion at the hip: " + str(support))
    return {"lift": round(max(support), 3), "headroom": round(max(headroom), 3)}


def manifest(builder, assets, verify):
    result = builder.result
    if len(result.rig.data.bones) != 31:
        raise RuntimeError("The reusable body needs exactly the 31 anatomy bones.")
    validate_loose_hair(builder)
    metrics = resident.measured(result)
    items = builder.items
    item_parts = {name for item in items for name in item["renderers"]}
    permanent = set(builder.parts) - item_parts
    by_slot = {}
    for item in items:
        by_slot.setdefault(item["slot"], []).append(item)
    for slot in ("headwear", "scarf", "gloves", "outerwear"):
        by_slot[slot].append(None)
    triangles = {}
    for part in result.parts:
        mesh = part.obj.data
        triangles[part.obj.name] = base.triangulated_count(mesh)
        if len(mesh.uv_layers) != 1 or any(not 0 <= value <= 1 for entry in mesh.uv_layers[0].data for value in entry.uv):
            raise RuntimeError("Invalid UV chart: " + part.obj.name)
        if any(abs(sum(weight.weight for weight in vertex.groups) - 1) > .00001 for vertex in mesh.vertices):
            raise RuntimeError("Invalid skin weights: " + part.obj.name)
        edges = {}
        for polygon in mesh.polygons:
            face = tuple(polygon.vertices)
            for a, b in zip(face, face[1:] + face[:1]):
                key = tuple(sorted((a, b)))
                edges[key] = edges.get(key, 0) + 1
        if all(count == 2 for count in edges.values()):
            vertices = [vertex.co for vertex in mesh.vertices]
            volume = sum(vertices[f.vertices[0]].dot(vertices[f.vertices[i]].cross(vertices[f.vertices[i + 1]]))
                         for f in mesh.polygons for i in range(1, len(f.vertices) - 1)) / 6
            if volume <= 0:
                raise RuntimeError("Inward closed part: " + part.obj.name)
    maximum = 0
    for combination in itertools.product(*by_slot.values()):
        worn = [item for item in combination if item]
        covered = {name for item in worn for name in item["covered_renderers"]}
        visible = permanent | {name for item in worn for name in item["renderers"]}
        maximum = max(maximum, sum(triangles[name] for name in visible - covered))
    if maximum > 8000:
        raise RuntimeError(f"The maximum equipped body is {maximum} triangles")
    faces = []
    for i in range(4):
        name = f"{MODEL}Face{i + 1}.png"
        faces.append({"id": f"face-{i + 1:02}", "texture": name,
                      "sha256": cannery.publish_bytes(assets / name, face_atlas(i).png_bytes(), verify)})
    atlas = MODEL + "Clothing.png"
    atlas_hash = cannery.publish_bytes(assets / atlas, clothing_atlas().png_bytes(), verify)
    return {
        "generator": Path(__file__).name, "catalog_model_id": "ordinary-woman-v1",
        "anatomy_source": "tools/build-cannery-woman-3d-model.py", "bone_count": 31,
        "height_m": HEIGHT, "height_scale": HEIGHT_SCALE, "min_height_cm": 155, "max_height_cm": 178,
        **metrics, "maximum_visible_triangles": maximum,
        "parts": [{"name": part.obj.name, "color": list(part.color)} for part in result.parts],
        "shaped_renderers": builder.shaped,
        "grip_shape": GRIP_SHAPE, "grip_radius_m": GRIP_RADIUS,
        "items": items, "faces": faces, "clothing_atlas": atlas, "atlas_sha256": atlas_hash,
        "hair_colors": [{"id": id, "color": list(cannery.hex_to_linear_rgba(color))}
                        for id, color in HAIR_COLORS.items()],
        "outfits": [
            {"id": "everyday", "items": ["shirt.everyday", "trousers.everyday", "boots.everyday", "hair.loose"]},
            {"id": "warm", "items": ["shirt.everyday", "outerwear.cardigan", "trousers.work",
                                       "boots.work", "hair.braid", "headwear.everyday", "scarf.warm", "gloves.work"]},
            {"id": "work", "items": ["shirt.everyday", "outerwear.everyday", "trousers.work", "boots.work", "hair.ponytail"]},
        ],
        "clips": [{"name": MODEL + name, "seconds": seconds, "loop": loop} for name, seconds, loop in CLIPS],
        "crown_clearance_m": validate_hair_coverage(builder),
        "geometry_signature": resident.geometry_signature(result),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Resources/City/Residents")
    parser.add_argument("--source-dir", type=Path, default=ROOT / "ArtSource/City/Residents")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if not args.validate_only:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
    base.SKELETON = tuple(bone for bone in base.SKELETON if not bone.name.startswith("Hair"))
    base.BONE_BY_NAME = {bone.name: bone for bone in base.SKELETON}
    builder = OrdinaryWomanBuilder()
    result = builder.build()
    data = manifest(builder, args.model_dir, args.validate_only)
    seated_keys, actions = make_actions(builder)
    data["seat"] = seat_contract(builder, seated_keys)
    data["arm_clearance_m"] = {name.lower(): validate_arm_clearance(builder, actions[name]) for name in ("Idle", "Walk")}
    payload = (json.dumps(data, indent=2) + "\n").encode()
    cannery.publish_bytes(args.model_dir / (MODEL + ".json"), payload, args.validate_only)
    if not args.validate_only:
        base.export_fbx(args.model_dir / (MODEL + ".fbx"), result)
        base.export_animation_fbx(args.model_dir / (MODEL + "Actions.fbx"), result)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / (MODEL + ".blend")), check_existing=False)
    print(f"{MODEL}: {data['maximum_visible_triangles']} worn triangles, seat {data['seat']}, arm clearance {data['arm_clearance_m']}")


if __name__ == "__main__":
    main()
