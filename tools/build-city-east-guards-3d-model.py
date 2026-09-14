#!/usr/bin/env python3
"""Two authored east-post duty characters and one separate shoulder-carried prop.

Uses the production NpcHumanV2/hero geometry, rig, export and contact helpers.
Each character has measured anatomy, its own drawn face/outfit and four
bone-only in-place loops. The carried rifle has no gameplay components.
"""
from __future__ import annotations

import argparse
from collections import Counter
from dataclasses import dataclass, replace
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
import interior_kit as kit
from atlas_kit import PixelCanvas
from player_3d_model_common import hex_to_linear_rgba

spec = importlib.util.spec_from_file_location("east_guard_anatomy", ROOT / "tools/build-cannery-receiver-3d-model.py")
receiver = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = receiver
spec.loader.exec_module(receiver)
resident, base = receiver.resident, receiver.base
RAW_SKELETON = tuple(base.SKELETON)
VERSION = "1.0.0"
FPS = 24
DEFAULT_MODELS = ROOT / "Assets/Resources/City/EastExit/Guards"
DEFAULT_SOURCE = ROOT / "ArtSource/City/EastExit/Guards"
RIFLE_UPPER = Vector((0, -.015, .24))
RIFLE_LOWER = Vector((0, .018, -.28))
CLIPS = (("Idle", 7.5), ("Walk", 1.5), ("Listen", 5.5), ("Break", 10.0))


@dataclass(frozen=True)
class DutyPerson:
    name: str
    age: int
    height: float
    width: float
    depth: float
    head_width: float
    hair_length: float
    palette: dict


PEOPLE = (
    DutyPerson("EastGuardSenior", 40, 1.82, 1.02, 1.04, 1.05, .0,
               {"skin":"B49A84", "hair":"514A42", "hair_dark":"383731", "hair_light":"665D50",
                "coat":"4E5C53", "coat_edge":"6A7264", "trousers":"444A44", "shirt":"99978A",
                "leather":"423D31", "sole":"282B28", "metal":"777568", "white":"FFFFFF"}),
    DutyPerson("EastGuardJunior", 28, 1.90, .85, .88, .87, .033,
               {"skin":"BDA48C", "hair":"574735", "hair_dark":"3F382E", "hair_light":"716149",
                "coat":"596260", "coat_edge":"727B73", "trousers":"4C5250", "shirt":"AAA595",
                "leather":"453F35", "sole":"292C2A", "metal":"77776D", "white":"FFFFFF"}),
)


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), default=list).encode()).hexdigest()


def publish(path, data, validate_only=False):
    if validate_only:
        if not path.is_file() or path.read_bytes() != data:
            raise RuntimeError("Deterministic asset differs: " + str(path))
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    return hashlib.sha256(data).hexdigest()


def publish_json(path, value, validate_only):
    publish(path, (json.dumps(value, indent=2) + "\n").encode(), validate_only)


def write_metas(directory):
    for asset in directory.iterdir():
        if asset.suffix not in (".fbx", ".json", ".png"):
            continue
        meta = asset.with_suffix(asset.suffix + ".meta")
        if not meta.exists():
            key = hashlib.sha256(asset.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]
            meta.write_text("fileFormatVersion: 2\nguid: " + key + "\n", encoding="utf8")


def face_tile(person, expression=0, mouth=0, private=0):
    """Different painted adult faces, retaining the shared 32-cell expression map."""
    c = PixelCanvas(64, 64)
    skin = tuple(bytes.fromhex(person.palette["skin"]))
    senior = person.age == 40
    for y in range(64):
        for x in range(64):
            cheek = 4 * math.exp(-((x - (25 if senior else 28)) / 21)**2 - ((y - 30) / 24)**2)
            edge = (7 if senior else 11) * (abs(x - 31.5) / 32)**2
            grain = ((x * 11 + y * 7) % 19 == 0) - ((x * 7 + y * 13) % 23 == 0)
            c.put(x, y, tuple(max(0, min(255, round(v + cheek - edge + grain))) for v in skin) + (255,))
    hair = tuple(bytes.fromhex(person.palette["hair"])) + (255,)
    for x in range(64):
        line = (4 + round(max(0, abs(x - 32) - 18) * .43) if senior else
                5 + round(max(0, 31 - x) * .13) + round(1.4 * math.sin(x * .16)))
        for y in range(line):
            c.put(x, y, hair)
    brow = (74, 68, 57, 255) if senior else (84, 69, 50, 255)
    shadow = (143, 121, 101, 255) if senior else (151, 127, 105, 255)
    for index, cx in enumerate((20, 43)):
        by = 20 + (index if senior else 0) + (1 if expression == 4 else 0)
        if expression == 3:
            by -= 1 + index
        c.line(cx - 7, by + (1 if senior else 0), cx - 1, by - 1, brow, 2 if senior else 1)
        c.line(cx - 1, by - 1, cx + 6, by, brow, 2 if senior else 1)
        ey = 28 + index
        if expression == 2:
            c.line(cx - 6, ey, cx + 5, ey + 1, brow)
        else:
            h = 1 if expression in (1, 4) else 2
            c.ellipse(cx, ey, 6, h, (193, 186, 168, 255))
            eye = (108, 120, 112, 255) if senior else (115, 105, 74, 255)
            c.ellipse(cx + (1 if not senior else 0), ey, 2, h, eye)
            c.line(cx, ey - h, cx, ey + h, (54, 58, 48, 255))
            c.put(cx - 1, ey - 1, (208, 204, 181, 255))
            c.line(cx - 6, ey - 2, cx + 5, ey - 2, shadow)
        c.line(cx - 5, ey + 4, cx + 5, ey + 4, shadow)
        if senior:
            c.line(cx + (-8 if index == 0 else 6), ey + 1, cx + (-10 if index == 0 else 9), ey + 4, shadow)
    # Broad blunt nose versus a narrower bridge: all features remain drawings.
    c.line(31, 29, 28 if senior else 30, 39, shadow)
    c.line(34, 30, 36 if senior else 35, 39, (190, 161, 133, 255))
    c.ellipse(32, 40, 5 if senior else 3, 2, (175, 139, 110, 255))
    c.line(27 if senior else 29, 41, 30, 41, (113, 92, 73, 255))
    c.line(35, 41, 37 if senior else 35, 40, (113, 92, 73, 255))
    if senior:
        # Sparse stubble follows the jaw and upper lip, leaving cheeks legible.
        for y in range(44, 63):
            for x in range(6, 59):
                if (y > 53 or abs(x - 32) > 17 or 44 <= y < 47 and 22 < x < 43) and (x * 7 + y * 11) % 7 == 0:
                    c.put(x, y, (116, 105, 90, 255))
        for y in (10, 13):
            c.line(21, y, 42, y + 1, (163, 141, 120, 255))
    else:
        c.line(14, 37, 16, 44, (171, 148, 124, 255))
        c.line(49, 37, 47, 44, (171, 148, 124, 255))
    lip, dark = (154, 118, 101, 255), (85, 65, 52, 255)
    if mouth == 0:
        offset = (-1 if private == 1 else 1 if private == 2 else 0)
        c.line(24 if senior else 26, 50, 32, 50, dark)
        c.line(32, 50, 41 if senior else 39, 50 + offset, dark)
        c.line(28, 52, 37, 52, lip)
    else:
        rx, ry = ((7, 2), (6, 4), (4, 4), (8, 3), (7, 2))[mouth - 1]
        c.ellipse(32, 50, rx + 1, ry + 1, lip)
        c.ellipse(32, 50, rx, ry, dark)
        if mouth in (2, 4, 5):
            c.line(32 - rx + 2, 49 - ry // 2, 32 + rx - 2, 49 - ry // 2, (202, 192, 171, 255))
    return c


def paint_face(person, path, validate_only):
    atlas = PixelCanvas(512, 256)
    tiles = [face_tile(person, i // 6, i % 6) for i in range(30)]
    tiles += [face_tile(person, private=1), face_tile(person, expression=3, private=2)]
    for i, tile in enumerate(tiles):
        for y in range(64):
            start = (((i // 8) * 64 + y) * 512 + (i % 8) * 64) * 4
            atlas.pixels[start:start + 256] = tile.pixels[y * 256:(y + 1) * 256]
    return publish(path, atlas.png_bytes(), validate_only)


def paint_outfit(person, path, validate_only):
    atlas = PixelCanvas(256, 256)
    atlas.rect(0, 0, 256, 256, (255, 255, 255, 255))
    for y in range(256):
        for x in range(256):
            if x < 128 and y < 192:
                weave = ((x * 7 + y * 11) % 9) - 4
                wear = 10 * math.exp(-((y - 174) / 10)**2) + 8 * math.exp(-((x % 64 - 4) / 3)**2)
                value = round(241 + weave - wear)
            elif x >= 128 and y < 128:
                value = 225 + (x * 13 + y * 3) % 20
            elif x >= 128 and y >= 128:
                value = 238 + (x * 3 + y * 5) % 13
            else:
                value = 244
            atlas.put(x, y, (value, value, max(0, value - 2), 255))
    for offset in (0, 64):
        for x in (offset + 7, offset + 56):
            atlas.line(x, 8, x, 182, (199, 198, 183, 255))
            for y in range(10, 178, 6):
                atlas.line(x + 2, y, x + 2, y + 2, (225, 226, 211, 255))
        for y in (110, 175):
            atlas.line(offset + 9, y, offset + 54, y + 1, (218, 218, 199, 255))
    # Broken, long woven fibres on the strap/hair cell, without insignia or text.
    for x in range(133, 255, 6):
        for y in range(6, 122):
            atlas.put(x + round(2 * math.sin(y * .06 + x)), y, (195, 194, 179, 255))
    # A neutral corner is reserved for skin, hardware and per-part colours.
    atlas.rect(0, 240, 14, 256, (255, 255, 255, 255))
    return publish(path, atlas.png_bytes(), validate_only)


class GuardBuilder(receiver.ReceiverBuilder):
    def __init__(self, person):
        super().__init__()
        self.person = person
        self.carry = None
        self.sling_path = None
        self.hand_contact = None

    def glasses(self):
        # Face quality shares the production surface, not the receiver's identity.
        pass

    def add(self, name, geometry, bone, color, role="clothing"):
        aliases = {"shirt":"coat", "shirt_edge":"coat_edge", "pants":"trousers",
                   "shoe":"leather", "belt":"leather", "buckle":"metal", "skin":"skin"}
        return super().add(name, geometry, bone, aliases.get(color, color), role)

    def map_point(self, point):
        x, y, z = point
        h = self.person.height / (1.96 + self.person.hair_length)
        face = max(0, min(1, (z - 1.60) / .10))
        width = self.person.width * (1 - face) + self.person.head_width * face
        return Vector((x * width, y * self.person.depth, z * h))

    def head(self):
        super().head()
        for part in list(self.result.parts):
            if part.role == "hair":
                self.result.parts.remove(part)
                bpy.data.objects.remove(part.obj, do_unlink=True)
        # Close cropped mature hair and a longer, sideways-swept younger cut.
        length = self.person.hair_length
        self.add("HAIR_Crown", base.make_ellipsoid((0, .015, 1.917 + length * .40),
                  (.115, .119 + length * .30, .043 + length * .60), 24, 10), "head", "hair", "hair")
        angles = [math.radians(66 + i * 228 / 20) for i in range(21)]
        verts = []
        for row in range(7):
            t = row / 6
            for a in angles:
                low = 1.745 + .019 * abs(math.cos(a)) - length * .75
                z = low + (1.94 + length * .33 - low) * t
                rr = .115 * math.sqrt(max(.20, 1 - ((z - 1.82) / .18)**2)) + .013
                verts.append((rr * math.sin(a), .001 - (rr + .005) * math.cos(a), z))
        faces = [(row * 21 + i, row * 21 + i + 1, (row + 1) * 21 + i + 1, (row + 1) * 21 + i)
                 for row in range(6) for i in range(20)]
        self.add("HAIR_Nape", (verts, faces), "head", "hair_dark", "hair")
        if length:
            for i in range(9):
                x = (i - 4) * .022
                self.add("HAIR_SweptLock" + str(i), receiver.tube_path(
                    ((x + .025, -.018, 1.936), (x + .012, -.066, 1.958 + length * .45),
                     (x - .022, -.104, 1.941), (x - .033, -.112, 1.897 - i % 2 * .009)),
                    (.017, .016, .011, .003), 8), "head", "hair_light" if i % 4 == 0 else "hair", "hair")

    def sleeve(self, side):
        upper, lower = "upper_arm." + side, "forearm." + side
        a = Vector(base.BONE_BY_NAME[upper].head)
        b = Vector(base.BONE_BY_NAME[lower].head)
        c = Vector(base.BONE_BY_NAME[lower].tail)
        points = [a.lerp(b, t) for t in (.0, .04, .12, .25, .42, .62, .82, 1)]
        # The sewn inner rings sit under the body shell. A narrow cap exactly
        # at the arm pivot reads as a separate shoulder ball once the arm hangs.
        inset = Vector((-.05 if side == "L" else .05, 0, .012))
        points[0] += inset
        points[1] += inset * .6
        points += [b.lerp(c, t) for t in (.12, .3, .5, .7, .85, 1)]
        radii = (.046, .065, .085, .095, .100, .094, .087, .071,
                 .077, .073, .066, .057, .051, .044)
        sleeve = self.add("CLO_DutySleeve." + side, receiver.tube_path(points, radii, 20), upper, "coat")
        for vg in list(sleeve.vertex_groups):
            sleeve.vertex_groups.remove(vg)
        groups = {n:sleeve.vertex_groups.new(name=n) for n in (upper, lower, "chest")}
        for vertex in sleeve.data.vertices:
            row = vertex.index // 20
            elbow = max(0, min(1, (row - 5.5) / 3))
            chest = (1.0,.60,.20,.05)[row] if row<4 else 0.0
            groups[upper].add([vertex.index], (1 - elbow) * (1 - chest), "REPLACE")
            groups[lower].add([vertex.index], elbow, "REPLACE")
            if chest:
                groups["chest"].add([vertex.index], chest * (1 - elbow), "REPLACE")
        # Seat the sewn rings under the coat in the actual hanging pose, then
        # invert the shared skin transform back into rest geometry. Keeping the
        # innermost edge on chest prevents a floating circular shoulder cap.
        rig=self.result.rig;base.reset_pose(rig);base.apply_pose(rig,base.CITIZEN_HANGING_ARMS)
        bpy.context.view_layer.update()
        skin_matrices={name:rig.pose.bones[name].matrix@rig.data.bones[name].matrix_local.inverted()
                       for name in (upper,lower,"chest")}
        for row in range(3):
            entries=[]
            for vertex in list(sleeve.data.vertices)[row*20:(row+1)*20]:
                skin=Matrix(((0,0,0,0),)*4)
                for weight in vertex.groups:
                    skin+=skin_matrices[sleeve.vertex_groups[weight.group].name]*weight.weight
                point=sleeve.matrix_world@vertex.co
                entries.append((vertex,skin,skin@point))
            center=sum((point for _,_,point in entries),Vector())/len(entries)
            seam=Vector((.205 if side=="L" else -.205,0,1.535))
            target=center.lerp(seam,(1,.62,.20)[row])
            for vertex,skin,point in entries:
                vertex.co=sleeve.matrix_world.inverted()@skin.inverted()@(point+target-center)
        base.reset_pose(rig);bpy.context.view_layer.update()
        direction = (c - b).normalized()
        self.add("CLO_Cuff." + side, base.make_frustum_between(c - direction * .037, c + direction * .005,
                 .050, .047, 24, .86), lower, "coat_edge")
        self.hand(side)

    def hand(self, side):
        super().hand(side)
        if side != "R":
            return
        bone="hand.R";a=Vector(base.BONE_BY_NAME[bone].head);b=Vector(base.BONE_BY_NAME[bone].tail)
        q=(b-a).to_track_quat("Z","Y")
        def world(point):
            return tuple(a+q@(Vector(point)*(1.17*receiver.HAND_SCALE)))
        for i in range(4):
            x=-(i-1.5)*.020;length=(.051,.059,.055,.042)[i]
            points=[world((x,0,.074)),world((x,-.003,.091)),world((x,-.015,.08+length*.58)),
                    world((x,-.032,.08+length*.54)),world((x,-.043,.077+length*.20))]
            geometry=fixed_axis_tube(points,tuple(r*receiver.HAND_SCALE for r in (.0105,.010,.0085,.0075,.006)),
                                     q@Vector((1,0,0)),6)
            old=next(p for p in self.result.parts if p.obj.name=="GEO_Finger"+str(i)+".R")
            self.result.parts.remove(old);bpy.data.objects.remove(old.obj,do_unlink=True)
            self.add("GEO_Finger"+str(i)+".R",geometry,bone,"skin","body_detail")

    def coat(self):
        senior = self.person.age == 40
        waist = .213 if senior else .190
        stations = ((1.045, waist, .139, .001), (1.07, waist + .007, .146, -.003),
            (1.115, waist + .010, .148, -.006), (1.17, waist + .012, .151, -.010),
            (1.225, waist + .013, .158, -.011), (1.28, .239, .164, -.014),
            (1.335, .249, .169, -.014), (1.39, .254, .177, -.014),
            (1.445, .255, .179, -.013), (1.495, .252, .167, -.009),
            (1.54, .246, .149, -.004), (1.575, .223, .120, -.008),
            (1.603, .184, .105, -.010), (1.627, .112, .090, -.012),
            (1.65, .099, .088, -.012))
        coat = self.add("CLO_DutyJacket", base.make_vertical_shell(stations, 32), "chest", "coat")
        self.weight_height(coat, ((1.11, "pelvis"), (1.27, "spine"), (1.48, "chest")))
        # Closed, sewn front versus a quiet open-neck insert with two turned collars.
        top = 1.628 if senior else 1.50
        placket = self.add("CLO_FrontPlacket", kit.chamfered_box((0, -.187, (1.08 + top) / 2),
                         (.045, .014, top - 1.08), .003), "chest", "coat_edge")
        self.weight_height(placket, ((1.11, "pelvis"), (1.27, "spine"), (1.48, "chest")))
        for i in range(5 if senior else 4):
            z = 1.12 + i * .108
            obj = self.add("CLO_Fastener" + str(i), base.make_ellipsoid((.001, -.199, z), (.009, .004, .009), 8, 4),
                           "chest", "metal")
            self.weight_height(obj, ((1.11, "pelvis"), (1.27, "spine"), (1.48, "chest")))
        for sign in (-1, 1):
            self.add("CLO_Collar" + str(sign), receiver.tube_path(
                ((sign * .098, -.052, 1.645), (sign * .090, -.111, 1.608),
                 (sign * (.042 if senior else .075), -.147, 1.563 if senior else 1.510)),
                (.026, .029, .012), 10, ovals=(.45, .45, .4)), "chest", "coat_edge")
            # Welt pockets follow the round coat front, without free-standing boxes.
            x = sign * .143
            for suffix, z, width in (("Chest", 1.435, .114), ("Lower", 1.195, .125)):
                obj = self.add("CLO_" + suffix + "Pocket" + str(sign), kit.chamfered_box((x, -.159, z),
                              (width, .015, .029 if suffix == "Lower" else .046), .004), "chest", "coat_edge")
                self.weight_height(obj, ((1.11, "pelvis"), (1.27, "spine"), (1.48, "chest")))
        if not senior:
            self.add("CLO_ShirtNeck", base.make_vertical_shell(((1.58, .084, .082, -.015),
                     (1.64, .084, .081, -.014), (1.68, .079, .075, -.013)), 24), "neck", "shirt")
        # A rear yoke and two underarm seams are authored solid cloth edging.
        self.add("CLO_RearYoke", receiver.tube_path(((-.18, .112, 1.55), (-.09, .14, 1.52),
                  (.09, .14, 1.52), (.18, .112, 1.55)), (.007,) * 4, 6), "chest", "coat_edge")

    def boots(self, side):
        ankle = base.BONE_BY_NAME["foot." + side].head
        for name, rows, color in (
            ("GEO_DutyBoot", ((.023,.076,.142,-.069),(.047,.079,.146,-.07),(.075,.078,.138,-.065),
                 (.107,.071,.113,-.045),(.142,.064,.084,-.009),(.185,.063,.069,.004),(.225,.063,.066,.007)), "leather"),
            ("GEO_BootWelt", ((.006,.081,.148,-.071),(.021,.084,.150,-.071),(.036,.081,.148,-.071)), "sole"),
            ("GEO_BootHeel", ((0,.061,.047,.016),(.025,.064,.049,.016)), "sole")):
            obj = self.add(name + "." + side, base.make_vertical_shell(rows, 24), "foot." + side, color, "footwear")
            for vertex in obj.data.vertices:
                vertex.co.x += ankle[0]
        for i in range(5):
            self.add("GEO_BootLace" + str(i) + "." + side,
                receiver.tube_path(((ankle[0] - .025, -.12 + i * .01, .106 + i * .016),
                                    (ankle[0] + .025, -.12 + i * .01, .112 + i * .016)), (.0025, .0025), 6),
                "foot." + side, "sole", "footwear")

    def remap_anatomy(self):
        rig = self.result.rig
        base.reset_pose(rig)
        bpy.context.view_layer.update()
        anchors = {name:(anchor.parent_bone, anchor.matrix_world.copy()) for name, anchor in self.result.anchors.items()}
        for part in self.result.parts:
            obj = part.obj
            points = [self.map_point(obj.matrix_world @ vertex.co) for vertex in obj.data.vertices]
            origin = self.map_point(obj.matrix_world.translation)
            obj.location = origin
            for vertex, point in zip(obj.data.vertices, points):
                vertex.co = point - origin
        bpy.context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        authored = {bone.name:bone for bone in RAW_SKELETON}
        for bone in rig.data.edit_bones:
            # A connected child's head changes when its parent's tail moves.
            # Read immutable authored points, or the same joint is scaled twice.
            bone.head = self.map_point(authored[bone.name].head)
            bone.tail = self.map_point(authored[bone.name].tail)
        bpy.ops.object.mode_set(mode="OBJECT")
        rig.select_set(False)
        base.SKELETON = tuple(replace(b, head=tuple(self.map_point(b.head)), tail=tuple(self.map_point(b.tail))) for b in RAW_SKELETON)
        base.BONE_BY_NAME = {b.name:b for b in base.SKELETON}
        bpy.context.view_layer.update()
        for name, (bone, matrix) in anchors.items():
            anchor = self.result.anchors[name]
            anchor.matrix_world = Matrix.Translation(self.map_point(matrix.translation)) @ matrix.to_quaternion().to_matrix().to_4x4()

    def carry_equipment(self):
        # Both sockets retain an identity source basis. The prop's ANCHOR_Carry
        # is aligned to the imported socket, so no guessed Euler/unit corrections.
        self.carry = self.map_point((-.355, .075, 1.38))
        self.create_bone_anchor("SOCKET_ShoulderCarry", "chest", self.carry, (0, 0, 1))
        upper, lower = self.carry + RIFLE_UPPER, self.carry + RIFLE_LOWER
        points = [upper, self.map_point((-.27,.12,1.575)), self.map_point((-.23,.015,1.603)),
                  self.map_point((-.235,-.115,1.575)), self.map_point((-.265,-.187,1.445)),
                  self.map_point((-.305,-.187,1.265)), lower]
        self.sling_path = [tuple(point) for point in points]
        self.hand_contact = Vector(points[4]) + Vector((-.008, -.018, -.016))
        # Woven strap is its own closed strip, supported at both actual gun rings.
        # It belongs to chest like its socket, separate from replaceable clothing.
        geometry = ribbon(self.sling_path, .034, .005)
        self.add("GEO_WeaponSling", geometry, "chest", "leather", "accessory")
        buckle = points[5] * .72 + points[4] * .28
        self.add("GEO_SlingBuckle", kit.chamfered_box(tuple(buckle + Vector((0,-.005,0))), (.043,.010,.044),.004),
                 "chest", "metal", "accessory")
        self.create_bone_anchor("ANCHOR_SlingHand", "chest", self.hand_contact, (0,-1,0))

    def uv(self, part):
        if part.obj.name == "GEO_FaceSurface":
            return
        obj, mesh = part.obj, part.obj.data
        uv = mesh.uv_layers.new(name="UVMap")
        points = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
        low = [min(p[a] for p in points) for a in range(3)]
        high = [max(p[a] for p in points) for a in range(3)]
        for polygon in mesh.polygons:
            for loop in polygon.loop_indices:
                point = points[mesh.loops[loop].vertex_index]
                u = (point.x - low[0]) / max(.001, high[0] - low[0])
                v = (point.z - low[2]) / max(.001, high[2] - low[2])
                if part.role in ("body", "body_detail") or part.palette_name in ("metal", "white"):
                    coordinate = (.02, .02)
                elif part.role == "hair" or part.obj.name == "GEO_WeaponSling":
                    coordinate = ((132 + u * 118) / 256, (132 + v * 118) / 256)
                elif part.role == "footwear":
                    coordinate = ((132 + u * 118) / 256, (4 + v * 118) / 256)
                else:
                    coordinate = ((3 + u * 57 + (64 if polygon.normal.y > 0 else 0)) / 256, (67 + v * 183) / 256)
                uv.data[loop].uv = coordinate

    def build(self):
        base.SKELETON = RAW_SKELETON
        base.BONE_BY_NAME = {bone.name:bone for bone in RAW_SKELETON}
        self.reset_scene()
        collection = bpy.data.collections.new("EXPORT_" + self.person.name)
        bpy.context.scene.collection.children.link(collection)
        base.PALETTE.update({name:hex_to_linear_rgba(color) for name,color in self.person.palette.items()})
        material = self.create_shared_material()
        root = bpy.data.objects.new("ROOT_Player", None)
        collection.objects.link(root)
        rig = self.create_armature(collection, root)
        self.result = base.BuildResult(root, rig, collection, material)
        self.head()
        self.trousers()
        self.coat()
        for side in ("L", "R"):
            self.sleeve(side)
            self.boots(side)
        self.remap_anatomy()
        self.carry_equipment()
        bpy.context.view_layer.update()
        for part in self.result.parts:
            self.uv(part)
        return self.result


def ribbon(points, width, thickness):
    verts = []
    for i, point in enumerate(points):
        direction = (Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(0, i - 1)])).normalized()
        transverse = Vector((1, 0, 0))
        across = (transverse - direction * transverse.dot(direction)).normalized()
        normal = direction.cross(across).normalized()
        for a, b in ((-1,-1),(1,-1),(1,1),(-1,1)):
            verts.append(tuple(Vector(point) + across * a * width / 2 + normal * b * thickness / 2))
    faces = [(3,2,1,0)]
    for i in range(len(points) - 1):
        for j in range(4):
            a = i * 4 + j
            b = i * 4 + (j + 1) % 4
            faces.append((a,b,b+4,a+4))
    faces.append(tuple(range((len(points)-1)*4, len(points)*4)))
    return verts, faces


def fixed_axis_tube(points,radii,across,sides):
    """A finger bend keeps one transverse axis, avoiding track-quaternion flips."""
    vertices=[]
    for i,(point,radius) in enumerate(zip(points,radii)):
        tangent=(Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])).normalized()
        right=(Vector(across)-tangent*Vector(across).dot(tangent)).normalized()
        up=tangent.cross(right).normalized()
        for j in range(sides):
            angle=j*math.tau/sides
            vertices.append(tuple(Vector(point)+radius*(right*math.cos(angle)+up*math.sin(angle))))
    faces=[tuple(reversed(range(sides)))]
    for i in range(len(points)-1):
        for j in range(sides):
            a=i*sides+j;b=i*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
    faces.append(tuple((len(points)-1)*sides+j for j in range(sides)))
    return vertices,faces


def contact_pose(builder, name, phase):
    result, person = builder.result, builder.person
    walk = name == "Walk"
    pose = resident.action_pose("Walk" if walk else "Idle", phase)
    wave = math.sin(phase * math.tau)
    pulse = math.sin(phase * math.pi)**2
    amplitude = .6 if person.age == 40 else 1.0
    pose["spine"] = base.BonePose(rotation_degrees=(.7 + pulse * .4, 0, wave * .35 * amplitude))
    pose["chest"] = base.BonePose(rotation_degrees=(-.5 + wave * .22, 0, -wave * .23 * amplitude))
    if name == "Listen":
        pose["neck"] = base.BonePose(rotation_degrees=(-.5 * pulse, 0, -3 * pulse))
        pose["head"] = base.BonePose(rotation_degrees=(-1.4 * pulse, 0, -5 * pulse * amplitude))
    elif name == "Break":
        pose["head"] = base.BonePose(rotation_degrees=(pulse * .8, 0, pulse * (5 if person.age == 40 else -8)))
        pose["spine"] = base.BonePose(rotation_degrees=(.8, 0, pulse * amplitude * 1.8))
    if walk:
        for bone, value in list(pose.items()):
            if bone.startswith(("thigh.", "shin.", "foot.")):
                pose[bone] = base.BonePose(rotation_degrees=tuple(v * (.78 if person.age == 40 else .86) for v in value.rotation_degrees),
                                           location_m=value.location_m, target_direction=value.target_direction)
    rig = result.rig
    base.reset_pose(rig)
    base.apply_pose(rig, pose)
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    low = min(base.evaluated_part_min_z(p, deps) for p in result.parts if p.bone.startswith("foot."))
    pelvis = rig.pose.bones["pelvis"]
    matrix = pelvis.matrix.copy()
    matrix.translation.z -= low
    pelvis.matrix = matrix
    bpy.context.view_layer.update()
    chest_skin = rig.pose.bones["chest"].matrix @ rig.data.bones["chest"].matrix_local.inverted()
    target = chest_skin @ builder.hand_contact
    hand = rig.pose.bones["hand.R"]
    hand.matrix = (Matrix.Translation(hand.head.copy()) @ Matrix.Rotation(math.pi,4,"X") @
                   hand.matrix.to_quaternion().to_matrix().to_4x4())
    bpy.context.view_layer.update()
    resident.solve_grips(rig, {"R":target})
    return target


def build_actions(builder):
    rig, person = builder.result.rig, builder.person
    max_ground = max_grip = max_loop = 0
    curves = []
    for short, duration in CLIPS:
        name = person.name + short
        action = bpy.data.actions.new(name)
        action.use_fake_user = action.use_frame_range = action.use_cyclic = True
        frames = round(duration * FPS)
        action.frame_start, action.frame_end = 0, frames
        rig.animation_data_create().action = action
        previous, endpoints = {}, []
        for frame in range(frames + 1):
            target = contact_pose(builder, short, frame / frames)
            max_grip = max(max_grip, (rig.pose.bones["SOCKET_Grip.R"].head - target).length)
            deps = bpy.context.evaluated_depsgraph_get()
            max_ground = max(max_ground, abs(min(base.evaluated_part_min_z(p, deps) for p in builder.result.parts if p.bone.startswith("foot."))))
            for bone in rig.pose.bones:
                bone.rotation_mode = "QUATERNION"
                if bone.name in previous:
                    bone.rotation_quaternion.make_compatible(previous[bone.name])
                previous[bone.name] = bone.rotation_quaternion.copy()
                for channel in ("location", "rotation_quaternion", "scale"):
                    bone.keyframe_insert(channel, frame=frame, group=bone.name)
            if frame in (0, frames):
                endpoints.append({bone.name:[round(v,7) for row in bone.matrix for v in row] for bone in rig.pose.bones})
        max_loop = max(max_loop, max(abs(a-b) for n in endpoints[0] for a,b in zip(endpoints[0][n], endpoints[1][n])))
        for curve in base.iter_action_fcurves(action):
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
            curves.append((name, curve.data_path, curve.array_index, [[round(k.co.x,6),round(k.co.y,7)] for k in curve.keyframe_points]))
        rig.animation_data.action = None
        print("Authored " + name, flush=True)
    base.reset_pose(rig)
    if max_ground > .002 or max_grip > .002 or max_loop > .002:
        raise RuntimeError(f"Guard motion contact failed: ground={max_ground}, grip={max_grip}, loop={max_loop}")
    return {"generator":Path(__file__).name, "version":VERSION, "bone_count":len(rig.data.bones),
            "fps":FPS, "root_motion":False,
            "clips":[{"name":person.name+n, "duration_seconds":s, "loop":True} for n,s in CLIPS],
            "validation":{"max_ground_error_m":round(max_ground,7), "max_grip_error_m":round(max_grip,7),
                          "max_loop_endpoint_error":round(max_loop,7), "curve_signature":digest(curves)}}


def model_manifest(builder, atlas_hash, face_hash):
    result, person = builder.result, builder.person
    base.reset_pose(result.rig)
    metrics = resident.measured(result)
    if not 7500 <= metrics["triangle_count"] <= 11000:
        raise RuntimeError("Guard body detail budget changed: " + str(metrics["triangle_count"]))
    closed, inward = [], []
    uv_records = []
    for part in result.parts:
        mesh = part.obj.data
        if any(abs(sum(g.weight for g in vertex.groups) - 1) > .00001 for vertex in mesh.vertices):
            raise RuntimeError("Non-normalized skin weights: " + part.obj.name)
        uses = Counter(tuple(sorted((face.vertices[i], face.vertices[(i+1)%len(face.vertices)]))) for face in mesh.polygons for i in range(len(face.vertices)))
        if all(value == 2 for value in uses.values()):
            volume = 0
            for face in mesh.polygons:
                points = [mesh.vertices[i].co for i in face.vertices]
                for i in range(1,len(points)-1):
                    volume += points[0].dot(points[i].cross(points[i+1])) / 6
            if volume <= 0:
                inward.append(part.obj.name)
            closed.append(part.obj.name)
        if not mesh.uv_layers.active:
            raise RuntimeError("Missing painted UV: " + part.obj.name)
        uv_records.append((part.obj.name, [[round(v.uv.x,7),round(v.uv.y,7)] for v in mesh.uv_layers.active.data]))
    if inward:
        raise RuntimeError("Inward solid meshes: " + str(inward))
    slots = {"jacket":[], "trousers":[], "belt":[], "boots":[]}
    for part in result.parts:
        if part.role not in ("clothing", "footwear", "headwear"):
            continue
        name = part.obj.name
        slot = "boots" if part.role == "footwear" else "trousers" if name == "CLO_WorkTrousers" else "belt" if "Belt" in name else "jacket"
        slots[slot].append(name)
    payload = {"generator":Path(__file__).name, "version":VERSION, "role":person.name,
        "anatomy_standard":"NpcHumanV2", "height_m":person.height, "height_scale":1.0,
        "bone_count":len(result.rig.data.bones), "body_bone_count":31, **metrics,
        "triangle_budget":[7500,11000], "closed_mesh_count":len(closed),
        "profile":{"age":person.age,"width_factor":person.width,"head_width_factor":person.head_width,
                   "hair_length_source_m":person.hair_length},
        "atlas_sha256":atlas_hash, "face_atlas_sha256":face_hash,
        "surface_signature":digest(uv_records), "geometry_signature":resident.geometry_signature(result),
        "outfit":{"id":"east_guard_"+("senior" if person.age==40 else "junior")+"_duty", "atlas":person.name+"Atlas.png",
                  "parts":[{"slot":slot,"renderers":names} for slot,names in slots.items() if names]},
        "face":{"renderer":"GEO_FaceSurface", "texture":person.name+"FaceAtlas.png",
                "width":512,"height":256,"columns":8,"rows":4,"cell_size":64,
                "uv_contract":"local_0_1_runtime_cell_scale_offset"},
        "carry":{"socket":"SOCKET_ShoulderCarry", "bone":"chest", "prop":"EastGuardRifle",
                 "prop_anchor":"ANCHOR_Carry", "socket_rest_blender":[round(v,7) for v in builder.carry],
                 "socket_source_rotation_xyzw":[0,0,0,1], "sling_renderers":["GEO_WeaponSling","GEO_SlingBuckle"],
                 "sling_path_blender":[[round(v,7) for v in p] for p in builder.sling_path],
                 "hand_contact_anchor":"ANCHOR_SlingHand", "hand_contact_blender":[round(v,7) for v in builder.hand_contact],
                 "attachment_rule":"Align imported ANCHOR_Carry to imported SOCKET_ShoulderCarry, preserve file-unit basis"},
        "parts":[{"name":p.obj.name,"role":p.role,"color":list(p.color),"triangles":base.triangulated_count(p.obj.data)} for p in result.parts]}
    payload["signature"] = digest(payload)
    return payload


def rifle_geometry():
    """One worn, unbranded service rifle silhouette; no internal moving mechanism."""
    parts = {"Metal":[], "Furniture":[], "Dark":[]}
    def add(role, geometry):
        vertices,faces=geometry;volume=0.0
        for face in faces:
            a=Vector(vertices[face[0]])
            for i in range(1,len(face)-1):
                volume+=a.dot(Vector(vertices[face[i]]).cross(Vector(vertices[face[i+1]])))/6
        if volume<=0:
            raise RuntimeError("Inward weapon component: "+role)
        parts[role].append(geometry)
    add("Metal", kit.chamfered_box((0,0,.015), (.071,.073,.315),.009))
    add("Metal", kit.chamfered_box((0,-.019,.193), (.053,.039,.15),.007))
    # The barrel is upright in Blender; Unity axis conversion makes it local Y.
    add("Metal", base.make_frustum_between((0,0,.18),(0,0,.61),.012,.010,12,1))
    add("Metal", base.make_frustum_between((0,-.026,.15),(0,-.026,.37),.013,.011,10,1))
    add("Metal", kit.chamfered_box((0,0,.605), (.029,.029,.052),.005))
    # Solid old stock with a narrow wrist and a broad worn butt, rather than a cube.
    stock = base.make_vertical_shell(((-.37,.035,.061,.024),(-.35,.037,.060,.025),
             (-.25,.034,.052,.025),(-.18,.025,.033,.022),(-.13,.020,.027,.010)),12)
    add("Furniture", stock)
    add("Dark", kit.chamfered_box((0,.024,-.373),(.078,.125,.014),.004))
    add("Furniture", base.make_vertical_shell(((.16,.039,.046,.005),(.20,.041,.045,.005),
             (.27,.039,.041,.006),(.32,.033,.036,.006)),12))
    add("Furniture", receiver.tube_path(((0,.026,-.09),(0,.066,-.15),(0,.095,-.205)),(.034,.032,.024),10,ovals=(.65,.65,.60)))
    # Curved magazine: an exterior mass only, no cartridge or firing system.
    magazine = receiver.tube_path(((0,.016,-.07),(0,.049,-.155),(0,.075,-.225),(0,.12,-.273)),
                                 (.032,.032,.030,.027),10,ovals=(.53,.58,.58,.60))
    add("Metal", magazine)
    for side in (-1,1):
        for z in (-.015,.04,.10):
            add("Metal", kit.chamfered_box((side*.039,0,z),(.008,.015,.015),.003))
        add("Metal", receiver.tube_path(((side*.028,.068,-.055),(side*.028,.097,-.087),
                                        (side*.028,.077,-.117)),(.006,)*3,6))
    add("Metal", kit.chamfered_box((0,-.014,.535),(.043,.026,.025),.004))
    add("Metal", kit.chamfered_box((0,-.040,.55),(.008,.015,.035),.002))
    for anchor in (RIFLE_UPPER,RIFLE_LOWER):
        add("Metal", receiver.tube_path((tuple(anchor+Vector((-.023,0,0))),tuple(anchor+Vector((0,-.01,0))),
                                        tuple(anchor+Vector((.023,0,0)))),(.005,)*3,8))
    return {role:kit.merge_all(geometries) for role,geometries in parts.items()}


def build_rifle(model_dir, source_dir, validate_only):
    parts = rifle_geometry()
    records = []
    for role, geometry in parts.items():
        low, high = kit.bounds(geometry)
        records.append({"name":"GEO_Rifle"+role,"role":role,"triangle_count":kit.triangle_count(geometry),
                        "bounds_min_blender":[round(v,7) for v in low],"bounds_max_blender":[round(v,7) for v in high]})
    low,high = kit.bounds(kit.merge_all(parts.values()))
    atlas=PixelCanvas(256,256)
    for y in range(256):
        for x in range(256):
            if x<128:
                value=218+(x*7+y*11)%17
                if x<5 or x>122:value=246
                if (x*13+y*7)%113==0:value=244
            else:
                value=222+round(10*math.sin(x*.53+4*math.sin(y*.025)))+(x*7+y*3)%9
            atlas.put(x,y,(value,value,value,255))
    for x,y in ((13,48),(84,99),(55,193),(113,156),(160,67),(203,181)):
        atlas.line(x,y,x+8,y+1,(247,247,239,255))
    atlas.rect(0,0,4,4,(255,255,255,255))
    atlas_hash=publish(model_dir/"EastGuardRifleAtlas.png",atlas.png_bytes(),validate_only)
    payload = {"generator":Path(__file__).name,"version":VERSION,"role":"EastGuardRifle",
        "units":"metres", "source_forward":"-Y", "source_up":"+Z",
        "bounds_min":[round(v,7) for v in low],"bounds_max":[round(v,7) for v in high],
        "mesh_count":len(parts),"triangle_count":sum(p["triangle_count"] for p in records),
        "anchors":{"ANCHOR_Carry":[0,0,0],"ANCHOR_SlingUpper":list(RIFLE_UPPER),"ANCHOR_SlingLower":list(RIFLE_LOWER)},
        "parts":records,"texture":"EastGuardRifleAtlas.png","atlas_sha256":atlas_hash,
        "gameplay":False,"colliders":False,"rigidbodies":False,
        "geometry_signature":digest(parts),"shared_instances":2}
    publish_json(model_dir/"EastGuardRifle.json",payload,validate_only)
    if validate_only:
        return payload
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system="METRIC"
    bpy.context.scene.unit_settings.scale_length=1
    root=bpy.data.objects.new("ROOT_EastGuardRifle",None)
    bpy.context.scene.collection.objects.link(root)
    for role,geometry in parts.items():
        mesh=bpy.data.meshes.new("GEO_Rifle"+role+"_Mesh")
        mesh.from_pydata(geometry[0],[],geometry[1]);mesh.update(calc_edges=True)
        obj=bpy.data.objects.new("GEO_Rifle"+role,mesh)
        bpy.context.scene.collection.objects.link(obj);obj.parent=root
        uv=mesh.uv_layers.new(name="UVMap")
        low,high=kit.bounds(geometry)
        for polygon in mesh.polygons:
            axes=(1,2) if abs(polygon.normal.x)>.6 else (0,2) if abs(polygon.normal.y)>.6 else (0,1)
            for loop in polygon.loop_indices:
                p=mesh.vertices[mesh.loops[loop].vertex_index].co
                u=(p[axes[0]]-low[axes[0]])/max(.001,high[axes[0]]-low[axes[0]])
                v=(p[axes[1]]-low[axes[1]])/max(.001,high[axes[1]]-low[axes[1]])
                uv.data[loop].uv=((4+u*120+(128 if role=="Furniture" else 0))/256,(4+v*248)/256)
    for name,point in payload["anchors"].items():
        anchor=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(anchor)
        anchor.parent=root;anchor.location=point
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(model_dir/"EastGuardRifle.fbx"),use_selection=True,
        object_types={"EMPTY","MESH"},axis_forward="-Z",axis_up="Y",add_leaf_bones=False,
        bake_anim=False,bake_space_transform=False,use_custom_props=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(source_dir/"EastGuardRifle.blend"),check_existing=False)
    return payload


def preview(builder, model_dir, source_dir, selected_views=None):
    result,person=builder.result,builder.person
    contact_pose(builder,"Idle",0)
    nodes=result.material.node_tree.nodes;links=result.material.node_tree.links
    shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
    info=next(n for n in nodes if n.type=="OBJECT_INFO")
    texture=nodes.new("ShaderNodeTexImage")
    texture.image=bpy.data.images.load(str(model_dir/(person.name+"Atlas.png")));texture.interpolation="Closest"
    mix=nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY";mix.inputs[0].default_value=1
    links.new(texture.outputs["Color"],mix.inputs[1]);links.new(info.outputs["Color"],mix.inputs[2]);links.new(mix.outputs[0],shader.inputs["Base Color"])
    face=next(p.obj for p in result.parts if p.obj.name=="GEO_FaceSurface")
    material=result.material.copy();material.name="PREVIEW_Face"
    nodes=material.node_tree.nodes;links=material.node_tree.links
    tex=nodes.new("ShaderNodeTexImage");tex.image=bpy.data.images.load(str(model_dir/(person.name+"FaceAtlas.png")));tex.interpolation="Closest"
    coords=nodes.new("ShaderNodeTexCoord");mapping=nodes.new("ShaderNodeVectorMath");mapping.operation="MULTIPLY_ADD"
    mapping.inputs[1].default_value=(.125,.25,1);mapping.inputs[2].default_value=(0,.75,0)
    links.new(coords.outputs["UV"],mapping.inputs[0]);links.new(mapping.outputs["Vector"],tex.inputs["Vector"])
    links.new(tex.outputs["Color"],next(n for n in nodes if n.type=="BSDF_PRINCIPLED").inputs["Base Color"])
    face.data.materials.clear();face.data.materials.append(material)
    socket=result.anchors["SOCKET_ShoulderCarry"]
    for role,geometry in rifle_geometry().items():
        mesh=bpy.data.meshes.new("PREVIEW_Rifle"+role);mesh.from_pydata(geometry[0],[],geometry[1]);mesh.update()
        obj=bpy.data.objects.new("PREVIEW_Rifle"+role,mesh);bpy.context.scene.collection.objects.link(obj)
        obj.matrix_world=socket.matrix_world.copy()
        mat=bpy.data.materials.new("PREVIEW_RifleMat"+role);mat.diffuse_color={"Metal":(.13,.15,.14,1),"Furniture":(.23,.18,.12,1),"Dark":(.04,.05,.04,1)}[role]
        mat.use_nodes=True;mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=mat.diffuse_color
        mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value=.82
        mesh.materials.append(mat)
    scene=bpy.context.scene;scene.view_settings.view_transform="Standard"
    bg=scene.world.node_tree.nodes.get("Background");bg.inputs["Color"].default_value=(.22,.25,.23,1);bg.inputs["Strength"].default_value=.65
    for loc,power,size in (((-3,-4,4),650,4),((3,-1,3),420,4),((1,3,4),600,3)):
        data=bpy.data.lights.new("PREVIEW_Softbox","AREA");data.energy=power;data.shape="DISK";data.size=size
        obj=bpy.data.objects.new("PREVIEW_Softbox",data);scene.collection.objects.link(obj);obj.location=loc
        obj.rotation_euler=(Vector((0,0,1.1))-obj.location).to_track_quat("-Z","Y").to_euler()
    camera=bpy.data.cameras.new("PREVIEW_Camera");obj=bpy.data.objects.new("PREVIEW_Camera",camera);scene.collection.objects.link(obj);scene.camera=obj
    camera.type="ORTHO";scene.render.engine="CYCLES";scene.cycles.samples=12
    scene.render.resolution_x=900;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    for label,location,target,scale in (("Body",(-2.1,-4,2.1),(0,0,.98),2.3),
        ("Carry",(-2.4,-3,1.8),(-.14,-.03,1.40),.92),
        ("Face",(.65,-3,1.78),(0,-.01,person.height-.16),.47)):
        if selected_views and label not in selected_views.split(","):
            continue
        obj.location=location;obj.rotation_euler=(Vector(target)-obj.location).to_track_quat("-Z","Y").to_euler();camera.ortho_scale=scale
        scene.render.filepath=str(source_dir/(person.name+label+".png"));bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=DEFAULT_MODELS)
    parser.add_argument("--source-dir",type=Path,default=DEFAULT_SOURCE)
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--no-preview",action="store_true")
    parser.add_argument("--rifle-only",action="store_true")
    parser.add_argument("--preview-only",action="store_true")
    parser.add_argument("--preview-views")
    parser.add_argument("--actor",choices=[p.name for p in PEOPLE])
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if not args.validate_only:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
    for person in PEOPLE:
        if args.rifle_only:
            break
        if args.actor and args.actor!=person.name:
            continue
        atlas=args.model_dir/(person.name+"Atlas.png")
        face=args.model_dir/(person.name+"FaceAtlas.png")
        ah=paint_outfit(person,atlas,args.validate_only);fh=paint_face(person,face,args.validate_only)
        builder=GuardBuilder(person)
        result=builder.build()
        if args.preview_only:
            preview(builder,args.model_dir,args.source_dir,args.preview_views)
            continue
        manifest=model_manifest(builder,ah,fh)
        print(person.name+" geometry: "+str(manifest["triangle_count"])+" triangles",flush=True)
        publish_json(args.model_dir/(person.name+".json"),manifest,args.validate_only)
        if not args.validate_only:
            base.export_fbx(args.model_dir/(person.name+".fbx"),result)
        actions=build_actions(builder)
        publish_json(args.model_dir/(person.name+"Actions.json"),actions,args.validate_only)
        if not args.validate_only:
            base.export_animation_fbx(args.model_dir/(person.name+"Actions.fbx"),result)
            bpy.context.preferences.filepaths.save_version=0
            bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/(person.name+".blend")),check_existing=False)
        print(person.name+" contacts: "+json.dumps(actions["validation"]),flush=True)
    if args.preview_only:
        return
    rifle=build_rifle(args.model_dir,args.source_dir,args.validate_only)
    if not args.validate_only:
        write_metas(args.model_dir)
    print("CITY EAST GUARDS ART CONTRACT OK: deterministic bodies/faces/outfits/carry loops; separate "+str(rifle["triangle_count"])+"-triangle prop",flush=True)


if __name__=="__main__":
    main()
