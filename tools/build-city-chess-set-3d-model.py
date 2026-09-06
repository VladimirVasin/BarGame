#!/usr/bin/env python3
"""Build the park chess set's men: six turned chess pieces and a draught.

Run through Blender 5, for example::

    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-city-chess-set-3d-model.py

The deterministic output is a metre-scale editable .blend, an FBX carrying
one mesh per piece kind, a JSON contract consumed by Unity's importer and a
review render that shows all seven side by side at eye level.

Blender source space is Z-up, metres, and a piece faces +Y. The FBX is
exported with `axis_forward="-Z", axis_up="Y"`, so +Y arrives in Unity as
+Z and the runtime only ever has to yaw a knight about the vertical.

Everything here is a surface of revolution plus a few boxes, because that
is what a turned wooden chess set physically is. The knight is the one
exception and the reason this generator exists at all: a horse cannot be
stated as a stack of discs, and the whole point of a chess set in a game
is that six silhouettes are told apart at a glance.

Sizes are all derived from one number, the drawn board's square. That
board is a park table 1.20 m across, so a proportionate set is large:
the king stands 1.43 squares, a shade under club proportion, which is as
tall as it can be before it starts hiding the man sitting behind it.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Sequence

try:
    import bpy
    from mathutils import Matrix, Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "city_chess_set_v1"
DISPLAY_NAME = "Park Chess Set Men"

# The drawn board's square, restated from CityChessBoardGeometry. Every
# dimension below is a fraction of it, so the set can never be the wrong
# size for the board it stands on.
SQUARE = 0.15

# One ring of ten. Twelve is smoother and reads no better at this size
# under the composite pass, and eight starts to show a decagonal base as
# a hexagon.
SEGMENTS = 10

SOURCE_COLLECTION = "SOURCE_CityChessSet3D"
PRESENTATION_COLLECTION = "PRESENTATION_CityChessSet3D"
ROOT_NAME = "SRC_CityChessSet3D"

MIN_TRIANGLES = 700
MAX_TRIANGLES = 4200

# Turned bone and turned bog oak: the same two values the board's light
# plate and dark inlay carry, so the men belong to the board rather than
# sitting on it. The runtime tints them; these are for the review render
# only.
PREVIEW_LIGHT = (0.615, 0.596, 0.545, 1.0)
PREVIEW_DARK = (0.116, 0.104, 0.098, 1.0)


@dataclass(frozen=True)
class PieceSpec:
    key: str
    mesh_name: str
    display_name: str
    # Both as multiples of the square, so the whole set rescales with
    # the board. The height is the design and is asserted to the tenth
    # of a millimetre; the footprint is a ceiling, because on a knight
    # the widest point is a muzzle rather than a base disc.
    height_squares: float
    max_footprint_squares: float
    # A knight is the only piece with a front. Everything else is a
    # solid of revolution and the runtime may place it at any yaw.
    directional: bool = False

    @property
    def height(self) -> float:
        return self.height_squares * SQUARE

    @property
    def max_footprint(self) -> float:
        return self.max_footprint_squares * SQUARE


# Club proportion, compressed a little at the top so the king does not
# curtain the face of the man behind it. The order is the read: every
# piece is meaningfully taller than the one below it, because height is
# the only channel that survives fog and grayscale at this distance.
PIECES: tuple[PieceSpec, ...] = (
    PieceSpec("pawn", "GEO_Pawn", "Pawn", 0.720, 0.300),
    PieceSpec("rook", "GEO_Rook", "Rook", 0.840, 0.340),
    PieceSpec("knight", "GEO_Knight", "Knight", 1.000, 0.345,
              directional=True),
    PieceSpec("bishop", "GEO_Bishop", "Bishop", 1.147, 0.320),
    PieceSpec("queen", "GEO_Queen", "Queen", 1.293, 0.350),
    PieceSpec("king", "GEO_King", "King", 1.433, 0.360),
    PieceSpec("draught", "GEO_Draught", "Draught", 0.152, 0.390),
)

PIECES_BY_KEY = {piece.key: piece for piece in PIECES}


@dataclass
class BuildResult:
    root: bpy.types.Object
    objects: list[bpy.types.Object] = field(default_factory=list)
    triangles: dict[str, int] = field(default_factory=dict)
    heights: dict[str, float] = field(default_factory=dict)
    radii: dict[str, float] = field(default_factory=dict)


class MeshAccumulator:
    """Vertices and faces in the piece's own space, base centred on the
    origin with `z = 0` on the square it stands on."""

    def __init__(self) -> None:
        self.vertices: list[tuple[float, float, float]] = []
        self.faces: list[tuple[int, ...]] = []

    # -- primitives ---------------------------------------------------

    def add_box(
        self,
        center: Sequence[float],
        size: Sequence[float],
        rotation_x_degrees: float = 0.0,
    ) -> None:
        """An axis-aligned box, optionally pitched about its own X.

        Pitch is the only rotation the set needs: a knight's neck, muzzle
        and ears all lean in the plane it faces, and nothing here is ever
        rolled or yawed at author time.
        """

        half = [component * 0.5 for component in size]
        corners = [
            (-half[0], -half[1], -half[2]),
            (+half[0], -half[1], -half[2]),
            (+half[0], +half[1], -half[2]),
            (-half[0], +half[1], -half[2]),
            (-half[0], -half[1], +half[2]),
            (+half[0], -half[1], +half[2]),
            (+half[0], +half[1], +half[2]),
            (-half[0], +half[1], +half[2]),
        ]
        rotation = Matrix.Rotation(
            math.radians(rotation_x_degrees), 3, "X")
        base = len(self.vertices)
        for corner in corners:
            point = rotation @ Vector(corner) + Vector(center)
            self.vertices.append((point.x, point.y, point.z))

        self.faces.extend(
            [
                (base + 0, base + 3, base + 2, base + 1),
                (base + 4, base + 5, base + 6, base + 7),
                (base + 0, base + 1, base + 5, base + 4),
                (base + 1, base + 2, base + 6, base + 5),
                (base + 2, base + 3, base + 7, base + 6),
                (base + 3, base + 0, base + 4, base + 7),
            ]
        )

    def add_lathe(
        self,
        profile: Sequence[tuple[float, float]],
        segments: int = SEGMENTS,
    ) -> None:
        """Revolve a `(radius, z)` profile about the Z axis.

        A radius of zero at either end closes as a fan rather than as a
        degenerate ring, which is what lets a pawn's head come to a point
        and a base close flat without a separate cap.
        """

        if len(profile) < 2:
            raise ValueError("A lathe profile needs at least two points")

        rings: list[int | list[int]] = []
        for radius, z in profile:
            if radius <= 1e-6:
                rings.append(len(self.vertices))
                self.vertices.append((0.0, 0.0, z))
                continue

            base = len(self.vertices)
            for segment in range(segments):
                angle = (math.tau * segment) / segments
                self.vertices.append(
                    (
                        math.cos(angle) * radius,
                        math.sin(angle) * radius,
                        z,
                    )
                )
            rings.append([base + index for index in range(segments)])

        for index in range(len(rings) - 1):
            lower = rings[index]
            upper = rings[index + 1]
            if isinstance(lower, int) and isinstance(upper, int):
                continue

            if isinstance(lower, int):
                for segment in range(segments):
                    following = (segment + 1) % segments
                    self.faces.append(
                        (lower, upper[following], upper[segment]))
                continue

            if isinstance(upper, int):
                for segment in range(segments):
                    following = (segment + 1) % segments
                    self.faces.append(
                        (lower[segment], lower[following], upper))
                continue

            for segment in range(segments):
                following = (segment + 1) % segments
                self.faces.append(
                    (
                        lower[segment],
                        lower[following],
                        upper[following],
                        upper[segment],
                    )
                )

    def add_extruded_profile(
        self,
        profile: Sequence[tuple[float, float]],
        slices: Sequence[tuple[float, float]],
    ) -> None:
        """Extrude a closed `(y, z)` outline along X.

        `slices` are `(x, scale)` pairs, the scale taken about the
        outline's own centroid, so a run of `0.88, 1, 1, 0.88` gives a
        chamfered solid rather than a card. The two end slices are
        capped with one n-gon each; Unity triangulates them on import.

        This is here for the knight and only the knight. Every other man
        on the board is a turning, and a turning is what `add_lathe`
        does; a horse is a silhouette, and a silhouette is a thing you
        draw once in profile and give a thickness to.
        """

        if len(profile) < 3 or len(slices) < 2:
            raise ValueError("An extrusion needs an outline and two slices")

        centre_y = sum(point[0] for point in profile) / len(profile)
        centre_z = sum(point[1] for point in profile) / len(profile)
        count = len(profile)
        rings: list[list[int]] = []
        for x, scale in slices:
            base = len(self.vertices)
            for y, z in profile:
                self.vertices.append(
                    (
                        x,
                        centre_y + (y - centre_y) * scale,
                        centre_z + (z - centre_z) * scale,
                    )
                )
            rings.append([base + index for index in range(count)])

        for index in range(len(rings) - 1):
            lower = rings[index]
            upper = rings[index + 1]
            for point in range(count):
                following = (point + 1) % count
                self.faces.append(
                    (
                        lower[point],
                        upper[point],
                        upper[following],
                        lower[following],
                    )
                )

        self.faces.append(tuple(rings[0]))
        self.faces.append(tuple(reversed(rings[-1])))

    def add_ring_of_boxes(
        self,
        count: int,
        radius: float,
        center_z: float,
        size: Sequence[float],
    ) -> None:
        """`count` boxes stood evenly around the axis, each turned to face
        outward. Merlons on a rook, points on a coronet."""

        for index in range(count):
            angle = (math.tau * index) / count
            offset = Vector(
                (math.cos(angle) * radius, math.sin(angle) * radius, 0.0))
            rotation = Matrix.Rotation(angle, 3, "Z")
            half = [component * 0.5 for component in size]
            corners = [
                (-half[0], -half[1], -half[2]),
                (+half[0], -half[1], -half[2]),
                (+half[0], +half[1], -half[2]),
                (-half[0], +half[1], -half[2]),
                (-half[0], -half[1], +half[2]),
                (+half[0], -half[1], +half[2]),
                (+half[0], +half[1], +half[2]),
                (-half[0], +half[1], +half[2]),
            ]
            base = len(self.vertices)
            for corner in corners:
                point = rotation @ Vector(corner) + offset
                self.vertices.append(
                    (point.x, point.y, point.z + center_z))
            self.faces.extend(
                [
                    (base + 0, base + 3, base + 2, base + 1),
                    (base + 4, base + 5, base + 6, base + 7),
                    (base + 0, base + 1, base + 5, base + 4),
                    (base + 1, base + 2, base + 6, base + 5),
                    (base + 2, base + 3, base + 7, base + 6),
                    (base + 3, base + 0, base + 4, base + 7),
                ]
            )


# -- the seven pieces ----------------------------------------------------


def scaled(profile: Sequence[tuple[float, float]]) -> list[tuple[float, float]]:
    """A profile authored in squares, in metres."""

    return [(radius * SQUARE, z * SQUARE) for radius, z in profile]


def build_pawn() -> MeshAccumulator:
    """Base, waist, collar, ball. The simplest turning there is, and the
    one whose only job is to be unmistakably the shortest thing here."""

    mesh = MeshAccumulator()
    mesh.add_lathe(scaled([
        (0.000, 0.000),
        (0.290, 0.000),
        (0.290, 0.062),
        (0.235, 0.100),
        (0.148, 0.155),
        (0.098, 0.270),
        (0.094, 0.345),
        (0.150, 0.395),
        (0.150, 0.425),
        (0.092, 0.462),
        (0.163, 0.535),
        (0.168, 0.590),
        (0.128, 0.665),
        (0.000, 0.720),
    ]))
    return mesh


def build_rook() -> MeshAccumulator:
    """A tower. Straight-sided where every other piece is waisted, which
    is the difference the silhouette actually carries, and four merlons
    on the rim rather than a cut ring: the notch is the read."""

    mesh = MeshAccumulator()
    mesh.add_lathe(scaled([
        (0.000, 0.000),
        (0.330, 0.000),
        (0.330, 0.070),
        (0.262, 0.112),
        (0.196, 0.160),
        (0.180, 0.470),
        (0.222, 0.545),
        (0.238, 0.630),
        (0.238, 0.680),
        (0.000, 0.680),
    ]))
    mesh.add_ring_of_boxes(
        4,
        0.196 * SQUARE,
        0.735 * SQUARE,
        (0.150 * SQUARE, 0.110 * SQUARE, 0.210 * SQUARE),
    )
    return mesh


def build_knight() -> MeshAccumulator:
    """The one piece that is not a turning.

    A horse is stated as a pitched neck, a muzzle dropped off the front
    of it, a mane down the back and two ears — six boxes, which at this
    size is exactly enough and one more than would survive the composite
    pass. It faces +Y; the runtime turns it to face the opponent.
    """

    mesh = MeshAccumulator()
    mesh.add_lathe(scaled([
        (0.000, 0.000),
        (0.320, 0.000),
        (0.320, 0.062),
        (0.256, 0.104),
        (0.215, 0.155),
        (0.205, 0.240),
        (0.230, 0.300),
        (0.000, 0.300),
    ]))
    unit = SQUARE
    # The head and neck as one drawn outline, front of the chest round
    # to the withers: chest, throat, jaw, nose, the stop under the brow,
    # forehead, poll, then the crest and the mane down the back. Boxes
    # were tried first and gave a flag on a pole — a horse is a line,
    # and a line has to be drawn rather than stacked.
    outline = [
        (0.140, 0.300),
        (0.125, 0.470),
        (0.175, 0.600),
        (0.270, 0.650),
        (0.320, 0.700),
        (0.300, 0.775),
        (0.195, 0.800),
        (0.150, 0.870),
        (0.080, 0.905),
        (0.020, 0.860),
        (-0.060, 0.845),
        (-0.150, 0.700),
        (-0.215, 0.520),
        (-0.190, 0.360),
        (-0.140, 0.300),
    ]
    mesh.add_extruded_profile(
        [(y * unit, z * unit) for y, z in outline],
        [
            (-0.100 * unit, 0.88),
            (-0.052 * unit, 1.00),
            (0.052 * unit, 1.00),
            (0.100 * unit, 0.88),
        ],
    )
    # Two ears at the poll set the height, and they are what a knight is
    # recognised by from the front, where the profile says nothing.
    for side in (-1.0, 1.0):
        mesh.add_box(
            (side * 0.055 * unit, 0.045 * unit, 0.9230 * unit),
            (0.055 * unit, 0.070 * unit, 0.145 * unit),
            rotation_x_degrees=-14.0,
        )
    return mesh


def build_bishop() -> MeshAccumulator:
    """Tall waist, a mitre that swells before it closes, and a bead on
    top. Against the queen it is the absence of a coronet that reads;
    against the pawn it is nearly half again the height."""

    mesh = MeshAccumulator()
    mesh.add_lathe(scaled([
        (0.000, 0.000),
        (0.313, 0.000),
        (0.313, 0.062),
        (0.250, 0.104),
        (0.166, 0.160),
        (0.108, 0.320),
        (0.100, 0.470),
        (0.156, 0.530),
        (0.156, 0.562),
        (0.098, 0.600),
        (0.178, 0.680),
        (0.190, 0.760),
        (0.152, 0.860),
        (0.078, 0.940),
        (0.055, 0.985),
        (0.088, 1.040),
        (0.070, 1.100),
        (0.000, 1.147),
    ]))
    return mesh


def build_queen() -> MeshAccumulator:
    """The same turning taken taller, then opened into a coronet: eight
    points around a flared band, and a bead over the middle of them."""

    mesh = MeshAccumulator()
    mesh.add_lathe(scaled([
        (0.000, 0.000),
        (0.340, 0.000),
        (0.340, 0.066),
        (0.272, 0.112),
        (0.180, 0.172),
        (0.116, 0.360),
        (0.106, 0.540),
        (0.170, 0.606),
        (0.170, 0.640),
        (0.112, 0.682),
        (0.196, 0.760),
        (0.224, 0.840),
        (0.212, 0.890),
        (0.150, 0.890),
    ]))
    mesh.add_ring_of_boxes(
        8,
        0.196 * SQUARE,
        0.950 * SQUARE,
        (0.078 * SQUARE, 0.078 * SQUARE, 0.140 * SQUARE),
    )
    mesh.add_lathe(scaled([
        (0.000, 1.020),
        (0.058, 1.060),
        (0.075, 1.110),
        (0.062, 1.170),
        (0.036, 1.230),
        (0.048, 1.265),
        (0.000, 1.293),
    ]))
    return mesh


def build_king() -> MeshAccumulator:
    """The tallest turning, closed with a dome instead of a coronet and
    surmounted by a cross. Two boxes carry the whole difference between
    him and the queen, and they carry it at any distance."""

    mesh = MeshAccumulator()
    mesh.add_lathe(scaled([
        (0.000, 0.000),
        (0.350, 0.000),
        (0.350, 0.068),
        (0.280, 0.116),
        (0.186, 0.178),
        (0.120, 0.380),
        (0.110, 0.580),
        (0.176, 0.650),
        (0.176, 0.686),
        (0.116, 0.730),
        (0.200, 0.812),
        (0.230, 0.900),
        (0.222, 0.960),
        (0.188, 1.020),
        (0.120, 1.075),
        (0.052, 1.110),
        (0.062, 1.140),
        (0.000, 1.160),
    ]))
    unit = SQUARE
    mesh.add_box(
        (0.0, 0.0, 1.290 * unit),
        (0.062 * unit, 0.062 * unit, 0.290 * unit),
    )
    mesh.add_box(
        (0.0, 0.0, 1.320 * unit),
        (0.185 * unit, 0.058 * unit, 0.062 * unit),
    )
    return mesh


def build_draught() -> MeshAccumulator:
    """A disc with a milled rim and a turned centre. Wider than any chess
    base and a fifth of a pawn's height, so the two games never read as
    the same game from the path."""

    mesh = MeshAccumulator()
    mesh.add_lathe(scaled([
        (0.000, 0.000),
        (0.330, 0.000),
        (0.367, 0.026),
        (0.367, 0.085),
        (0.340, 0.104),
        (0.360, 0.124),
        (0.334, 0.152),
        (0.206, 0.152),
        (0.206, 0.132),
        (0.000, 0.132),
    ]))
    return mesh


BUILDERS = {
    "pawn": build_pawn,
    "rook": build_rook,
    "knight": build_knight,
    "bishop": build_bishop,
    "queen": build_queen,
    "king": build_king,
    "draught": build_draught,
}


# -- scene plumbing ------------------------------------------------------


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--blend",
        type=Path,
        default=Path("ArtSource/City/Blender/CityChessSet3D.blend"),
    )
    parser.add_argument(
        "--model",
        type=Path,
        default=Path("Assets/City/Models/CityChessSet3D.fbx"),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("Assets/City/Models/CityChessSet3D.json"),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path("ArtSource/City/Blender/CityChessSet3D.png"),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    config = parser.parse_args(arguments)
    for name in ("blend", "model", "manifest", "preview"):
        setattr(config, name, getattr(config, name).resolve())
    return config


def reset_scene() -> tuple[bpy.types.Collection, bpy.types.Collection]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = False
    source = bpy.data.collections.new(SOURCE_COLLECTION)
    presentation = bpy.data.collections.new(PRESENTATION_COLLECTION)
    scene.collection.children.link(source)
    scene.collection.children.link(presentation)
    return source, presentation


def create_material(name: str, rgba: Sequence[float]) -> bpy.types.Material:
    material = bpy.data.materials.new(name)
    material.diffuse_color = tuple(rgba)
    material.use_nodes = True
    material.node_tree.nodes.clear()
    output = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
    shader = material.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Base Color"].default_value = tuple(rgba)
    shader.inputs["Metallic"].default_value = 0.0
    shader.inputs["Roughness"].default_value = 0.62
    material.node_tree.links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return material


def create_object(
    name: str,
    accumulator: MeshAccumulator,
    collection: bpy.types.Collection,
    parent: bpy.types.Object | None,
    material: bpy.types.Material,
    location: Sequence[float] = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(accumulator.vertices, [], accumulator.faces)
    mesh.validate(verbose=False)
    mesh.update()
    for polygon in mesh.polygons:
        polygon.use_smooth = False
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = tuple(location)
    return obj


def triangulated_count(mesh: bpy.types.Mesh) -> int:
    return sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)


def build_set() -> BuildResult:
    source, _ = reset_scene()
    material = create_material("MAT_CityChessSetLight", PREVIEW_LIGHT)
    root = bpy.data.objects.new(ROOT_NAME, None)
    source.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.1

    result = BuildResult(root=root)
    for piece in PIECES:
        accumulator = BUILDERS[piece.key]()
        obj = create_object(
            piece.mesh_name,
            accumulator,
            source,
            root,
            material,
        )
        result.objects.append(obj)
        result.triangles[piece.key] = triangulated_count(obj.data)
        result.heights[piece.key] = max(
            vertex.co.z for vertex in obj.data.vertices)
        result.radii[piece.key] = max(
            math.hypot(vertex.co.x, vertex.co.y)
            for vertex in obj.data.vertices
        )

    return result


def stable(value: float) -> float:
    return round(value + 0.0, 6)


def build_signature(result: BuildResult) -> str:
    digest = hashlib.sha256()
    digest.update(GENERATOR_VERSION.encode("utf-8"))
    digest.update(DESIGN_ID.encode("utf-8"))
    for obj in sorted(result.objects, key=lambda item: item.name):
        digest.update(obj.name.encode("utf-8"))
        for vertex in obj.data.vertices:
            for component in vertex.co:
                digest.update(f"{stable(component):.6f}".encode("utf-8"))
        for polygon in obj.data.polygons:
            for index in polygon.vertices:
                digest.update(str(index).encode("utf-8"))
    return digest.hexdigest()


def validate(result: BuildResult) -> None:
    """Every problem at once rather than the first one.

    Tuning a turned profile is a loop: a raised collar moves the finial,
    a wider mitre moves the footprint. Reporting one failure at a time
    turns a two-minute pass into ten.
    """

    problems: list[str] = []
    total = sum(result.triangles.values())
    if not MIN_TRIANGLES <= total <= MAX_TRIANGLES:
        problems.append(
            f"triangle count {total} is outside "
            f"[{MIN_TRIANGLES}, {MAX_TRIANGLES}]"
        )

    objects = {obj.name: obj for obj in result.objects}
    for piece in PIECES:
        height = result.heights[piece.key]
        if abs(height - piece.height) > 0.0005:
            problems.append(
                f"{piece.display_name} stands {height * 1000:.2f} mm, not "
                f"the declared {piece.height * 1000:.2f} mm"
            )

        radius = result.radii[piece.key]
        if radius > piece.max_footprint + 1e-6:
            problems.append(
                f"{piece.display_name} reaches {radius * 1000:.2f} mm from "
                f"its axis, past the declared "
                f"{piece.max_footprint * 1000:.2f} mm"
            )

        # A man has to stand inside his own square with a visible gap, or
        # a full back rank becomes one continuous wall of wood.
        if radius * 2.0 > SQUARE * 0.80:
            problems.append(
                f"{piece.display_name} is {radius * 2000:.1f} mm across and "
                f"crowds the {SQUARE * 1000:.0f} mm square it stands on"
            )

        lowest = min(
            vertex.co.z
            for vertex in objects[piece.mesh_name].data.vertices
        )
        if abs(lowest) > 1e-6:
            problems.append(
                f"{piece.display_name} does not sit on z=0 "
                f"(lowest {lowest:.6f})"
            )

    # The read is the height ladder. Assert it rather than trust the
    # table above, because a single edited digit would quietly make a
    # bishop shorter than a rook.
    ladder = ["pawn", "rook", "knight", "bishop", "queen", "king"]
    for lower, upper in zip(ladder, ladder[1:]):
        gap = result.heights[upper] - result.heights[lower]
        if gap < 0.012:
            problems.append(
                f"{upper} stands only {gap * 1000:.1f} mm over {lower}; "
                "the two silhouettes will not separate"
            )

    # And the draught must not be mistakable for any of them.
    if result.heights["draught"] > result.heights["pawn"] * 0.35:
        problems.append("the draught is too tall to read as a draught")
    if result.radii["draught"] <= max(result.radii[key] for key in ladder):
        problems.append("the draught must be the widest man on the set")

    if problems:
        raise RuntimeError(
            "Chess set contract violated:\n  - " + "\n  - ".join(problems)
        )


def export_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in result.objects:
        obj.select_set(True)
    result.root.select_set(True)
    bpy.context.view_layer.objects.active = result.root
    # Unlike every other model in this project, these meshes are used by
    # the runtime as bare mesh assets rather than by instantiating the
    # imported hierarchy — fifty-six GameObjects is not a thing to put
    # on a park table. The two exporter defaults that hide inside that
    # hierarchy therefore have to be baked out instead:
    #
    #   `apply_scale_options="FBX_SCALE_ALL"` puts the metre-to-FBX-unit
    #   factor into the file's unit scale rather than onto a root object
    #   with `scale = 100`, and
    #   `bake_space_transform=True` bakes the Z-up-to-Y-up conversion
    #   into the vertices rather than onto that root's rotation.
    #
    # Without them the meshes arrive a hundredth of their size and lying
    # on their backs, and everything still looks right in the model
    # preview, because the root the preview instantiates carries both
    # corrections.
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True,
        add_leaf_bones=False,
        bake_anim=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
    )


def write_manifest(path: Path, result: BuildResult, signature: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-city-chess-set-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "square_size_m": SQUARE,
        "radial_segments": SEGMENTS,
        "triangle_count": sum(result.triangles.values()),
        "pieces": [
            {
                "key": piece.key,
                "mesh": piece.mesh_name,
                "display_name": piece.display_name,
                "height_m": stable(result.heights[piece.key]),
                "radius_m": stable(result.radii[piece.key]),
                "triangle_count": result.triangles[piece.key],
                "directional": piece.directional,
            }
            for piece in PIECES
        ],
        "build_signature": signature,
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary, path)


def render_preview(path: Path, result: BuildResult) -> None:
    """All seven in a row, twice, on a patch of board, from a low
    three-quarter angle. If two silhouettes cannot be told apart here
    they cannot be told apart in the park either."""

    path.parent.mkdir(parents=True, exist_ok=True)
    presentation = bpy.data.collections[PRESENTATION_COLLECTION]
    light_material = create_material("MAT_PreviewLight", PREVIEW_LIGHT)
    dark_material = create_material("MAT_PreviewDark", PREVIEW_DARK)

    pitch = SQUARE * 1.25
    span = (len(PIECES) - 1) * pitch
    # Light in front, dark behind and shifted half a pitch, so neither
    # row hides the other and both silhouettes are read against the
    # board rather than against each other. The knight is turned into
    # profile, which is the only view a horse has ever been read in.
    for row, (material, y, shift) in enumerate(
        (
            (light_material, -pitch * 0.55, 0.0),
            (dark_material, pitch * 0.75, pitch * 0.5),
        )
    ):
        for index, piece in enumerate(PIECES):
            accumulator = BUILDERS[piece.key]()
            obj = create_object(
                f"PREVIEW_{piece.mesh_name}_{row}",
                accumulator,
                presentation,
                None,
                material,
                location=(index * pitch - span * 0.5 + shift, y, 0.0),
            )
            if piece.directional:
                obj.rotation_euler = (0.0, 0.0, math.radians(-90.0))

    board = MeshAccumulator()
    board.add_box(
        (0.0, pitch * 0.1, -0.012),
        (span + pitch * 2.0, pitch * 5.0, 0.024),
    )
    create_object(
        "PREVIEW_Board",
        board,
        presentation,
        None,
        create_material("MAT_PreviewBoard", (0.34, 0.32, 0.29, 1.0)),
    )

    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("CAM_ChessSetPreview")
    camera = bpy.data.objects.new("CAM_ChessSetPreview", camera_data)
    presentation.objects.link(camera)
    camera.location = (0.0, -2.55, 0.46)
    target = Vector((0.0, 0.0, 0.10))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 52
    scene.camera = camera

    for name, location, energy, color in (
        ("Key", (-1.6, -2.0, 1.9), 220.0, (0.80, 0.84, 0.80)),
        ("Rim", (1.8, 1.4, 1.3), 130.0, (0.36, 0.48, 0.44)),
        ("Fill", (0.9, -2.2, 0.6), 60.0, (0.58, 0.56, 0.62)),
    ):
        data = bpy.data.lights.new(f"LIGHT_{name}", "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = 1.8
        light = bpy.data.objects.new(f"LIGHT_{name}", data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (
            target - Vector(location)
        ).to_track_quat("-Z", "Y").to_euler()

    scene.render.resolution_x = 1440
    scene.render.resolution_y = 620
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def main() -> None:
    config = parse_args()
    result = build_set()
    validate(result)
    signature = build_signature(result)

    export_fbx(config.model, result)
    write_manifest(config.manifest, result, signature)
    if not config.no_preview:
        render_preview(config.preview, result)
    save_blend(config.blend)

    print(f"  {DESIGN_ID}: {len(PIECES)} pieces, "
          f"{sum(result.triangles.values())} triangles")
    for piece in PIECES:
        print(
            f"    {piece.display_name:<8} "
            f"{result.heights[piece.key] * 1000:6.1f} mm tall, "
            f"{result.radii[piece.key] * 2000:5.1f} mm across, "
            f"{result.triangles[piece.key]:4d} tris"
        )
    print(f"    Signature: {signature}")
    print(f"    FBX: {config.model}")

    # A second build in the same process, proving the geometry is a pure
    # function of this file rather than of Blender's state.
    rerun = build_set()
    if build_signature(rerun) != signature:
        raise RuntimeError("Non-deterministic chess set build signature")
    print("  Determinism: repeated signatures match")
    print("CITY CHESS SET ART BUILD OK")


if __name__ == "__main__":
    main()
