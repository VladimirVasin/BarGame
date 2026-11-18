"""Sculpted facial planes and expression correctives on the production rig.

The mesh uses the same local 64 px coordinate system as both face atlases.
The mouth socket and the core skeleton remain fixed for physical contacts.
"""

from math import exp

from mathutils import Vector


SHAPES = ("Face.Jaw", "Face.Lids", "Face.WideEyes", "Face.Brows", "Face.Purse")
COLUMNS = (0, 6, 12, 17, 21, 25, 28, 32, 36, 39, 43, 47, 52, 58, 64)
ROWS = (0, 5, 11, 17, 22, 25, 28, 32, 36, 40, 44, 47, 50, 54, 58, 64)
# The original row positions keep the lips, chin, eyes and scalp aligned with
# existing props and expression tiles. Relief is sculpted in between them.
PROFILES = (
    (1.694, .055, .062), (1.664, .080, .082), (1.624, .086, .088),
    (1.582, .081, .082), (1.542, .068, .073), (1.512, .049, .059),
    (1.490, .030, .036),
)


def _bell(x, y, cx, cy, width, height):
    return exp(-((x - cx) / width) ** 2 - ((y - cy) / height) ** 2)


def _point(x, y):
    station = y * (len(PROFILES) - 1) / 64
    row = min(len(PROFILES) - 2, int(station))
    blend = station - row
    z, width, depth = (a + (b - a) * blend for a, b in zip(PROFILES[row], PROFILES[row + 1]))
    side = x / 32 - 1
    edge = min(1., x / 11, (64 - x) / 11, y / 7, (64 - y) / 6)
    relief = (
        .021 * _bell(x, y, 31.4, 37.5, 5.5, 10.) + .009 * _bell(x, y, 32., 40., 6.8, 3.)
        + .005 * _bell(x, y, 32., 47.2, 10., 2.5) + .0035 * _bell(x, y, 32., 56., 11., 4.)
    )
    for eye, offset in ((21, -.6), (43, .3)):
        relief += (
            .007 * _bell(x, y, eye, 22.3 + offset, 8., 3.)
            - .0065 * _bell(x, y, eye, 28.6 + offset, 8., 3.7)
            + .003 * _bell(x, y, eye, 26.7 + offset, 6.6, 1.35)
            + .0075 * _bell(x, y, eye, 34.7 + offset, 8., 3.3)
            - .006 * _bell(x, y, eye, 41.8 + offset, 6.5, 6.)
        )
    return Vector((side * width * .92, -.033 - depth * (1 - .34 * side * side) - edge * relief, z))


def _face_delta(name, x, y):
    area = min(1., x / 9, (64 - x) / 9, (64 - y) / 5)
    eye = 21 if x < 32 else 43
    lid = _bell(x, y, eye, 26.5, 7., 3.6)
    brow = _bell(x, y, eye, 22., 8., 3.8)
    lips = _bell(x, y, 32., 49., 11., 6.5)
    if name == "Face.Jaw":
        weight = max(0., min(1., (y - 44.) / 9.)) * area
        return Vector((0., .002 * weight, -.010 * weight))
    if name == "Face.Lids":
        return Vector((0., -.001 * lid, -.004 * lid))
    if name == "Face.WideEyes":
        return Vector((0., 0., .0034 * lid))
    if name == "Face.Brows":
        return Vector((0., -.001 * brow, .0037 * brow))
    return Vector(((32 - x) * .00030 * lips, -.0024 * lips, 0.))


def author(builder):
    coords = [(x, y) for y in reversed(ROWS) for x in COLUMNS]
    vertices = [builder.v(*_point(x, y)) for x, y in coords]
    width = len(COLUMNS)
    faces = []
    for row in range(len(ROWS) - 1):
        for col in range(width - 1):
            i = row * width + col
            faces.append((i, i + 1, i + 1 + width, i + width))
    face = builder.add_part("GEO_FaceSurface", (vertices, faces), "FaceAtlas", "details", "head",
                            "Body", "facial_atlas", origin=builder.bone_heads["head"])
    face.shape_key_add(name="Basis", from_mix=False)
    for name in SHAPES:
        key = face.shape_key_add(name=name, from_mix=False)
        for vertex, (x, y) in zip(key.data, coords):
            vertex.co += _face_delta(name, x, y) * builder.scale
    return face, [(x / 64, 1. - y / 64) for x, y in coords]


def validate(face):
    keys = face.data.shape_keys.key_blocks
    basis = keys[0]
    for name in SHAPES:
        shape = keys.get(name)
        if shape is None or not any((vertex.co - original.co).length > .001
                                    for vertex, original in zip(shape.data, basis.data)):
            raise RuntimeError(f"Missing sculpted deformation {name}")
