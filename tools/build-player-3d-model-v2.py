#!/usr/bin/env python3
"""Build Bar Promenade's production Hero V2 model.

Hero V2 owns the adult proportions, lean low-poly body, UV-driven expression
face and complete 41-action bank. Shared rig, action, export and validation
helpers live in ``player_3d_model_common.py`` so this remains the only runnable
hero model generator.

Run with Blender 5.0:

    blender --background --factory-startup \
      --python tools/build-player-3d-model-v2.py

The no-argument invocation writes the production source and Unity assets.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import os
import sys
import traceback
import warnings
from pathlib import Path
from typing import Sequence

import bpy
from mathutils import Vector


REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import atlas_kit  # noqa: E402  (after the sys.path fix)

COMMON_AUTHORING_PATH = REPO_ROOT / "tools" / "player_3d_model_common.py"
V2_GENERATOR_VERSION = "1.4.0"
TORSO_SKIN_MESHES = ("GEO_Torso", "CLO_JacketBody")
TORSO_SKIN_BONES = ("pelvis", "spine", "chest")
# Metres at the canonical 1.75 m height. Both garment and shirt use exactly
# the same field, so their layers cannot part company when the waist bends.
TORSO_BLEND_BANDS = ((0.965, 1.075), (1.135, 1.285))
RUN_ACTION_NAME = "Run"
RUN_DURATION_SECONDS = 0.75
RUN_SOURCE_FRAME_COUNT = 18
RUN_SOURCE_FPS = 24
# Eight columns: the left half holds the nine faces, the right half their
# soiled twins at column + 4, so the atlas is twice as wide as it is tall.
ATLAS_COLUMNS = 8
ATLAS_ROWS = 4
ATLAS_CELL_SIZE = 64
ATLAS_WIDTH = ATLAS_COLUMNS * ATLAS_CELL_SIZE
ATLAS_HEIGHT = ATLAS_ROWS * ATLAS_CELL_SIZE
FACE_ATLAS_SOILED_COLUMN_OFFSET = 4
CLOTHING_ATLAS_SIZE = 256

DEFAULT_OUTPUT = (
    REPO_ROOT
    / "ArtSource"
    / "PlayerV2"
    / "Blender"
    / "PlayerCharacter3DV2.blend"
)
DEFAULT_PREVIEW = (
    REPO_ROOT
    / "ArtSource"
    / "PlayerV2"
    / "Preview"
    / "PlayerCharacter3DV2.png"
)
DEFAULT_EXPRESSION_SHEET = (
    REPO_ROOT
    / "ArtSource"
    / "PlayerV2"
    / "Preview"
    / "PlayerFaceExpressions.png"
)
DEFAULT_FACE_ATLAS = (
    REPO_ROOT
    / "Assets"
    / "Player3D"
    / "V2"
    / "Textures"
    / "PlayerFaceAtlas.png"
)
DEFAULT_CLOTHING_ATLAS = (
    REPO_ROOT
    / "Assets"
    / "Player3D"
    / "V2"
    / "Textures"
    / "PlayerClothingAtlas.png"
)
DEFAULT_MANIFEST = (
    REPO_ROOT
    / "Assets"
    / "Player3D"
    / "V2"
    / "Models"
    / "PlayerCharacter3DV2.json"
)
DEFAULT_FBX = (
    REPO_ROOT
    / "Assets"
    / "Player3D"
    / "V2"
    / "Models"
    / "PlayerCharacter3DV2.fbx"
)
DEFAULT_ANIMATION_FBX = (
    REPO_ROOT
    / "Assets"
    / "Player3D"
    / "V2"
    / "Animations"
    / "PlayerCharacter3DV2Animations.fbx"
)
DEFAULT_PORTRAIT = (
    REPO_ROOT
    / "Assets"
    / "Resources"
    / "Player"
    / "Player3DV2Portrait.png"
)
DEFAULT_HEAD_FRONT = (
    REPO_ROOT
    / "ArtSource"
    / "PlayerV2"
    / "Preview"
    / "PlayerCharacter3DV2HeadFront.png"
)
DEFAULT_HEAD_THREE_QUARTER = (
    REPO_ROOT
    / "ArtSource"
    / "PlayerV2"
    / "Preview"
    / "PlayerCharacter3DV2HeadThreeQuarter.png"
)
DEFAULT_LOWER_BODY_CLOSEUP = (
    REPO_ROOT
    / "ArtSource"
    / "PlayerV2"
    / "Preview"
    / "PlayerCharacter3DV2LowerBody.png"
)

# Bottom-left pixel sub-rects in the one static 256x256 clothing atlas.
# Each atlas-bound renderer owns exactly one region and carries pre-baked UV0.
CLOTHING_REGIONS = {
    "JacketBody": ("CLO_JacketBody", 0, 128, 128, 128),
    "JacketSleeveLeft": ("CLO_JacketSleeve.L", 128, 192, 64, 64),
    "JacketSleeveRight": ("CLO_JacketSleeve.R", 192, 192, 64, 64),
    "BandageLeft": ("CLO_Bandage.L", 128, 128, 64, 64),
    "JeansPelvis": ("GEO_Pelvis", 192, 128, 64, 64),
    "JeansThighLeft": ("GEO_Thigh.L", 0, 64, 64, 64),
    "JeansThighRight": ("GEO_Thigh.R", 64, 64, 64, 64),
    "JeansShinLeft": ("GEO_Shin.L", 128, 64, 64, 64),
    "JeansShinRight": ("GEO_Shin.R", 192, 64, 64, 64),
    "BootLeft": ("GEO_Foot.L", 0, 0, 64, 64),
    "BootRight": ("GEO_Foot.R", 64, 0, 64, 64),
    "JacketForearmRight": ("CLO_JacketForearm.R", 128, 0, 64, 64),
}
CLOTHING_RENDERER_REGIONS = {
    renderer: (name, x, y, width, height)
    for name, (renderer, x, y, width, height) in CLOTHING_REGIONS.items()
}


def load_common_authoring():
    spec = importlib.util.spec_from_file_location(
        "bp_player_model_common",
        COMMON_AUTHORING_PATH,
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(
            "Cannot load shared player authoring module from "
            f"{COMMON_AUTHORING_PATH}"
        )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


common = load_common_authoring()
V2_REQUIRED_ACTIONS = (*common.REQUIRED_ACTIONS, RUN_ACTION_NAME)

V2_PALETTE_HEX = dict(common.PALETTE_HEX)
V2_PALETTE_HEX.update(
    {
        # Faded dark olive-drab field jacket.  Film-specific insignia/text are
        # deliberately absent; the ochre right-shoulder patch remains ours.
        "Jacket": "4A4B37",
        "JacketDark": "2F3025",
        "JacketEdge": "62634A",
        "JeansEdge": "202A3A",
        "Patch": "876C3C",
    }
)
for obsolete_material in tuple(
    name for name in V2_PALETTE_HEX if name.startswith("Stra")
):
    V2_PALETTE_HEX.pop(obsolete_material)


def resolve_path(path: Path) -> Path:
    if not path.is_absolute():
        path = REPO_ROOT / path
    return path.resolve()


def parse_args() -> tuple[common.BuildConfig, Path, Path, Path, Path, Path, Path]:
    user_args: list[str] = []
    if "--" in sys.argv:
        user_args = sys.argv[sys.argv.index("--") + 1 :]

    parser = argparse.ArgumentParser(
        description="Generate Bar Promenade's production Hero V2."
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--preview", type=Path, default=DEFAULT_PREVIEW)
    parser.add_argument("--portrait", type=Path, default=DEFAULT_PORTRAIT)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--fbx", type=Path, default=DEFAULT_FBX)
    parser.add_argument(
        "--animation-fbx",
        type=Path,
        default=DEFAULT_ANIMATION_FBX,
    )
    parser.add_argument("--face-atlas", type=Path, default=DEFAULT_FACE_ATLAS)
    parser.add_argument(
        "--clothing-atlas",
        type=Path,
        default=DEFAULT_CLOTHING_ATLAS,
    )
    parser.add_argument(
        "--expression-sheet",
        type=Path,
        default=DEFAULT_EXPRESSION_SHEET,
    )
    parser.add_argument("--head-front", type=Path, default=DEFAULT_HEAD_FRONT)
    parser.add_argument(
        "--head-three-quarter",
        type=Path,
        default=DEFAULT_HEAD_THREE_QUARTER,
    )
    parser.add_argument(
        "--lower-body-closeup",
        type=Path,
        default=DEFAULT_LOWER_BODY_CLOSEUP,
    )
    parser.add_argument("--glb", type=Path)
    parser.add_argument("--height", type=float, default=1.75)
    parser.add_argument("--seed", type=int, default=17301)
    parser.add_argument(
        "--pose",
        choices=("relaxed", "apose"),
        default="apose",
    )
    args = parser.parse_args(user_args)
    if not 1.40 <= args.height <= 2.10:
        parser.error("--height must be between 1.40 and 2.10 metres")

    config = common.BuildConfig(
        output=resolve_path(args.output),
        preview=resolve_path(args.preview),
        portrait=resolve_path(args.portrait),
        manifest=resolve_path(args.manifest),
        glb=resolve_path(args.glb) if args.glb is not None else None,
        fbx=resolve_path(args.fbx),
        animation_fbx=resolve_path(args.animation_fbx),
        height=args.height,
        seed=args.seed,
        pose=args.pose,
    )
    return (
        config,
        resolve_path(args.face_atlas),
        resolve_path(args.expression_sheet),
        resolve_path(args.clothing_atlas),
        resolve_path(args.head_front),
        resolve_path(args.head_three_quarter),
        resolve_path(args.lower_body_closeup),
    )


# The PNG canvas moved verbatim into the shared `tools/atlas_kit.py` so the
# City pedestrian generator paints its atlases with the same code.
png_chunk = atlas_kit.png_chunk
PixelCanvas = atlas_kit.PixelCanvas


SKIN = (174, 141, 123, 255)
SKIN_LIGHT = (188, 151, 130, 255)
SKIN_SHADOW = (103, 80, 75, 255)
SKIN_DARK = (57, 45, 46, 255)
EYE_WHITE = (143, 135, 128, 255)
EYE_DULL = (114, 111, 108, 255)
HAIR = (8, 8, 11, 255)
SOCKET = (126, 96, 89, 255)
UNDER_EYE = (111, 82, 78, 255)
CHEEK = (183, 145, 125, 255)
LIP = (88, 58, 58, 255)
# What the drink left on his chin: a wet ochre band under the lip, darker
# where it ran, a few pale crumbs. Skin reads ~150 in luma, the soil ~85, so
# the band survives the composite's grain at 640x360.
SOIL = (96, 84, 46, 255)
SOIL_DARK = (66, 56, 30, 255)
SOIL_PALE = (150, 140, 84, 255)


def draw_face_tile(
    canvas: PixelCanvas,
    column: int,
    top_row: int,
    expression: str,
    soiled: bool = False,
) -> None:
    ox = column * ATLAS_CELL_SIZE
    oy = top_row * ATLAS_CELL_SIZE
    canvas.rect(ox, oy, ox + 64, oy + 64, SKIN)

    # Broad hand-painted planes, deliberately large enough to survive 640x360.
    canvas.rect(ox, oy, ox + 2, oy + 64, SKIN_SHADOW)
    canvas.rect(ox + 62, oy, ox + 64, oy + 64, SKIN_SHADOW)
    canvas.rect(ox + 14, oy + 8, ox + 49, oy + 10, SKIN_LIGHT)
    canvas.line(ox + 10, oy + 55, ox + 20, oy + 60, SKIN_SHADOW, 2)
    canvas.line(ox + 20, oy + 60, ox + 44, oy + 61, SKIN_SHADOW, 1)
    canvas.line(ox + 44, oy + 60, ox + 54, oy + 55, SKIN_SHADOW, 2)
    canvas.line(ox + 12, oy + 24, ox + 29, oy + 25, SOCKET, 2)
    canvas.line(ox + 35, oy + 25, ox + 52, oy + 24, SOCKET, 2)
    canvas.line(ox + 28, oy + 17, ox + 31, oy + 39, SKIN_SHADOW)
    canvas.line(ox + 33, oy + 17, ox + 34, oy + 38, SKIN_LIGHT)
    canvas.line(ox + 30, oy + 39, ox + 36, oy + 40, SKIN_DARK)
    canvas.put(ox + 29, oy + 40, SKIN_DARK)
    canvas.put(ox + 37, oy + 40, SKIN_DARK)
    canvas.line(ox + 8, oy + 39, ox + 22, oy + 35, CHEEK, 2)
    canvas.line(ox + 42, oy + 35, ox + 56, oy + 39, CHEEK, 2)
    canvas.line(ox + 10, oy + 42, ox + 23, oy + 40, SKIN_SHADOW)
    canvas.line(ox + 41, oy + 40, ox + 54, oy + 42, SKIN_SHADOW)
    for y in range(49, 60):
        for x in range(11, 53):
            selector = x * 11 + y * 7 + column * 13 + top_row * 17
            if selector % 31 == 0:
                canvas.put(ox + x, oy + y, SKIN_DARK)
            elif selector % 19 < 2:
                canvas.put(ox + x, oy + y, SKIN_SHADOW)

    eye_y = 26
    eye_heights = {
        "Watchful": 5,
        "HalfBlink": 2,
        "ClosedBlink": 0,
        "Tense": 2,
        # The drink's faces: lids that will not stay up, eyes that will
        # not focus, a jaw that hangs, and the wince of the floor coming.
        "Drowsy": 1,
        "Glazed": 3,
        "Slack": 2,
        "Grimace": 1,
    }
    eye_height = eye_heights.get(expression, 3)
    dull_eyes = expression in ("Tense", "Grimace", "Drowsy")

    # Flat brows and heavy upper lids communicate exhaustion, not pleading.
    if expression == "Tense":
        canvas.line(ox + 13, oy + 20, ox + 27, oy + 22, HAIR, 1)
        canvas.line(ox + 37, oy + 22, ox + 51, oy + 20, HAIR, 1)
    elif expression == "Grimace":
        # Knitted: the inner ends drawn down and together.
        canvas.line(ox + 13, oy + 20, ox + 28, oy + 24, HAIR, 1)
        canvas.line(ox + 36, oy + 24, ox + 51, oy + 20, HAIR, 1)
    elif expression == "Drowsy":
        # Low and flat, almost on the lids.
        canvas.line(ox + 13, oy + 23, ox + 27, oy + 23, HAIR, 1)
        canvas.line(ox + 37, oy + 24, ox + 51, oy + 23, HAIR, 1)
    elif expression == "Slack":
        # One brow up, the other where it fell: nothing is being held.
        canvas.line(ox + 13, oy + 19, ox + 27, oy + 20, HAIR, 1)
        canvas.line(ox + 37, oy + 23, ox + 51, oy + 22, HAIR, 1)
    else:
        canvas.line(ox + 13, oy + 21, ox + 27, oy + 21, HAIR, 1)
        canvas.line(ox + 37, oy + 22, ox + 51, oy + 21, HAIR, 1)

    for eye_index, (center_x, pupil_shift) in enumerate(((21, 1), (43, 0))):
        left = center_x - 7
        right = center_x + 7
        if eye_height == 0:
            canvas.line(ox + left, oy + eye_y + 2, ox + right, oy + eye_y + 2, SKIN_DARK)
            canvas.line(ox + left + 2, oy + eye_y + 4, ox + right - 2, oy + eye_y + 4, UNDER_EYE)
            continue
        top = eye_y
        bottom = eye_y + eye_height
        if expression == "Glazed" and eye_index == 1:
            # One lid hangs lower than the other.
            top += 1
        canvas.rect(ox + left, oy + top, ox + right + 1, oy + bottom + 1, EYE_DULL if dull_eyes else EYE_WHITE)
        canvas.line(ox + left, oy + top, ox + right, oy + top, SKIN_DARK, 1)
        if expression == "Drowsy":
            # A heavy upper lid: the shadow sits on the eye itself.
            canvas.line(ox + left, oy + top - 1, ox + right, oy + top - 1, SKIN_SHADOW, 1)
        if expression == "HalfBlink":
            canvas.line(ox + left, oy + bottom, ox + right, oy + bottom, SKIN_SHADOW)
        else:
            pupil_x = center_x + pupil_shift + (1 if expression == "Watchful" else 0)
            if expression == "Glazed":
                # The eyes wander apart: each pupil drifts outward.
                pupil_x += -1 if eye_index == 0 else 1
            canvas.rect(ox + pupil_x - 1, oy + top + 1, ox + pupil_x + 2, oy + bottom + 1, HAIR)
        canvas.line(ox + left + 1, oy + bottom + 2, ox + right - 1, oy + bottom + 3, UNDER_EYE)

    mouth_y = 47
    if expression == "Tense":
        canvas.line(ox + 23, oy + mouth_y, ox + 41, oy + mouth_y, LIP, 2)
    elif expression == "Grimace":
        # The corners pulled down, the middle up: a wince.
        canvas.line(ox + 22, oy + mouth_y + 2, ox + 32, oy + mouth_y - 1, LIP, 2)
        canvas.line(ox + 32, oy + mouth_y - 1, ox + 42, oy + mouth_y + 2, LIP, 2)
    elif expression == "Slack":
        # The jaw hangs: a dark slit of open mouth under the lip.
        canvas.line(ox + 22, oy + mouth_y - 1, ox + 42, oy + mouth_y - 1, LIP)
        canvas.rect(ox + 25, oy + mouth_y, ox + 40, oy + mouth_y + 3, SKIN_DARK)
        canvas.line(ox + 25, oy + mouth_y + 3, ox + 40, oy + mouth_y + 3, LIP)
    elif expression == "Drowsy":
        # The mouth has let go at the corners.
        canvas.line(ox + 22, oy + mouth_y + 1, ox + 28, oy + mouth_y, LIP)
        canvas.line(ox + 28, oy + mouth_y, ox + 37, oy + mouth_y, LIP)
        canvas.line(ox + 37, oy + mouth_y, ox + 43, oy + mouth_y + 1, LIP)
        canvas.line(ox + 25, oy + mouth_y + 2, ox + 40, oy + mouth_y + 2, SKIN_SHADOW)
    else:
        canvas.line(ox + 22, oy + mouth_y, ox + 35, oy + mouth_y, LIP)
        canvas.line(ox + 35, oy + mouth_y, ox + 43, oy + mouth_y + 1, LIP)
        canvas.line(ox + 25, oy + mouth_y + 2, ox + 40, oy + mouth_y + 2, SKIN_SHADOW)
    canvas.put(ox + 21, oy + mouth_y, SKIN_SHADOW)
    canvas.put(ox + 44, oy + mouth_y + 1, SKIN_SHADOW)

    if soiled:
        draw_mouth_soil(canvas, ox, oy, column)


def draw_mouth_soil(canvas: PixelCanvas, ox: int, oy: int, column: int) -> None:
    """The soiled twin of a face: the same expression with the drink on it.

    Painted after the mouth so the lips (y=47-48) and Slack's open slit stay
    readable above the band; the soil begins at y=50 and runs down the chin.
    The eyes are never touched - the expression still has to carry.
    """
    canvas.rect(ox + 24, oy + 50, ox + 43, oy + 54, SOIL)
    canvas.rect(ox + 27, oy + 54, ox + 40, oy + 56, SOIL)
    # Where it ran: two drips of unequal length below the band.
    canvas.rect(ox + 30, oy + 56, ox + 32, oy + 59, SOIL_DARK)
    canvas.rect(ox + 37, oy + 56, ox + 38, oy + 58, SOIL_DARK)
    # Smears at the corners of the mouth where the back of a hand went.
    canvas.line(ox + 20, oy + 48, ox + 23, oy + 50, SOIL_DARK, 1)
    canvas.line(ox + 43, oy + 49, ox + 47, oy + 51, SOIL_DARK, 1)
    # Specks across the chin and lower cheeks; the column salts the hash so
    # no two twins carry the same spatter.
    for y in range(46, 59):
        for x in range(17, 49):
            selector = x * 7 + y * 13 + column * 3
            if selector % 17 == 0:
                canvas.put(ox + x, oy + y, SOIL_DARK)
            elif selector % 23 == 0:
                canvas.put(ox + x, oy + y, SOIL_PALE)


# The atlas cells, in python's top-left rows: the five sober faces the
# runtime has always had, then the drink's four, then a soiled twin of every
# one of them four columns to the right. Unity reads rows from the bottom,
# so a python row r is the manifest's row 3 - r.
FACE_ATLAS_CLEAN_CELLS = (
    ("Neutral", 0, 0),
    ("HalfBlink", 1, 0),
    ("ClosedBlink", 2, 0),
    ("Watchful", 0, 1),
    ("Tense", 1, 1),
    ("Drowsy", 2, 1),
    ("Glazed", 3, 1),
    ("Slack", 0, 2),
    ("Grimace", 1, 2),
)
FACE_ATLAS_CELLS = tuple(
    (expression, column, row, False) for expression, column, row in FACE_ATLAS_CLEAN_CELLS
) + tuple(
    (expression, column + FACE_ATLAS_SOILED_COLUMN_OFFSET, row, True)
    for expression, column, row in FACE_ATLAS_CLEAN_CELLS
)


def build_expression_sheet(atlas: PixelCanvas, path: Path) -> None:
    sheet = PixelCanvas(ATLAS_CELL_SIZE * 2 * len(FACE_ATLAS_CELLS), ATLAS_CELL_SIZE * 2)
    source_cells = tuple((column, row) for _, column, row, _ in FACE_ATLAS_CELLS)
    for destination_index, (source_column, source_row) in enumerate(source_cells):
        for y in range(ATLAS_CELL_SIZE):
            for x in range(ATLAS_CELL_SIZE):
                source_offset = (
                    ((source_row * ATLAS_CELL_SIZE + y) * atlas.width)
                    + source_column * ATLAS_CELL_SIZE
                    + x
                ) * 4
                color = tuple(atlas.pixels[source_offset : source_offset + 4])
                dx = destination_index * ATLAS_CELL_SIZE * 2 + x * 2
                dy = y * 2
                sheet.rect(dx, dy, dx + 2, dy + 2, color)
    sheet.write_png(path)


def build_face_atlas(path: Path, expression_sheet_path: Path) -> str:
    canvas = PixelCanvas(ATLAS_WIDTH, ATLAS_HEIGHT)
    # Reserved cells contain a safe weary-neutral fallback instead of alpha.
    for row in range(ATLAS_ROWS):
        for column in range(ATLAS_COLUMNS):
            draw_face_tile(canvas, column, row, "Neutral")
    for expression, column, row, soiled in FACE_ATLAS_CELLS:
        draw_face_tile(canvas, column, row, expression, soiled)
    canvas.write_png(path)
    build_expression_sheet(canvas, expression_sheet_path)
    return hashlib.sha256(path.read_bytes()).hexdigest()


rgba_from_hex = atlas_kit.rgba_from_hex
atlas_rect_bottom_left = atlas_kit.atlas_rect_bottom_left
atlas_line_bottom_left = atlas_kit.atlas_line_bottom_left


def clothing_region(name: str) -> tuple[int, int, int, int]:
    _, x, y, width, height = CLOTHING_REGIONS[name]
    return x, y, width, height


def build_clothing_atlas(path: Path) -> str:
    """Paint all non-silhouette clothing detail into one low-res atlas."""

    canvas = PixelCanvas(CLOTHING_ATLAS_SIZE, CLOTHING_ATLAS_SIZE)
    unused = (20, 20, 18, 255)
    jacket = rgba_from_hex(V2_PALETTE_HEX["Jacket"])
    jacket_dark = rgba_from_hex(V2_PALETTE_HEX["JacketDark"])
    jacket_edge = rgba_from_hex(V2_PALETTE_HEX["JacketEdge"])
    shirt = rgba_from_hex(V2_PALETTE_HEX["Shirt"])
    patch = rgba_from_hex(V2_PALETTE_HEX["Patch"])
    metal = rgba_from_hex(V2_PALETTE_HEX["Metal"])
    jeans = rgba_from_hex(V2_PALETTE_HEX["Jeans"])
    jeans_edge = rgba_from_hex(V2_PALETTE_HEX["JeansEdge"])
    bandage = rgba_from_hex(V2_PALETTE_HEX["Bandage"])
    bandage_dark = rgba_from_hex(V2_PALETTE_HEX["BandageDark"])
    canvas.rect(0, 0, canvas.width, canvas.height, unused)

    # JacketBody is one 128x128 region: left half is front, right half back.
    x, y, width, height = clothing_region("JacketBody")
    atlas_rect_bottom_left(canvas, x, y, x + width, y + height, jacket)
    front_x = x
    back_x = x + width // 2

    # Open M-65-like front: charcoal shirt between two olive panels, zipper
    # tape/open edges, four flat pockets and faded seams. No copied insignia.
    atlas_rect_bottom_left(canvas, front_x + 24, y + 8, front_x + 40, y + 106, shirt)
    for row in range(100, 122):
        expansion = (row - 100) // 3
        atlas_rect_bottom_left(
            canvas,
            front_x + 24 - expansion,
            y + row,
            front_x + 40 + expansion,
            y + row + 1,
            shirt,
        )
    atlas_line_bottom_left(canvas, front_x + 23, y + 7, front_x + 23, y + 111, jacket_dark, 2)
    atlas_line_bottom_left(canvas, front_x + 41, y + 7, front_x + 41, y + 111, jacket_dark, 2)
    for zipper_y in range(10, 108, 6):
        atlas_rect_bottom_left(canvas, front_x + 22, y + zipper_y, front_x + 24, y + zipper_y + 2, metal)
        atlas_rect_bottom_left(canvas, front_x + 40, y + zipper_y, front_x + 42, y + zipper_y + 2, metal)
    atlas_line_bottom_left(canvas, front_x + 7, y + 116, front_x + 22, y + 100, jacket_edge, 2)
    atlas_line_bottom_left(canvas, front_x + 57, y + 116, front_x + 42, y + 100, jacket_edge, 2)
    atlas_line_bottom_left(canvas, front_x + 2, y + 105, front_x + 62, y + 105, jacket_dark)
    for px0, px1, py0, py1 in (
        (5, 27, 65, 91),
        (37, 59, 65, 91),
        (4, 28, 22, 54),
        (36, 60, 22, 54),
    ):
        atlas_rect_bottom_left(canvas, front_x + px0, y + py0, front_x + px1, y + py1, jacket_dark)
        atlas_rect_bottom_left(canvas, front_x + px0 + 2, y + py0 + 2, front_x + px1 - 2, y + py1 - 2, jacket)
        atlas_line_bottom_left(canvas, front_x + px0, y + py1 - 5, front_x + px1 - 1, y + py1 - 5, jacket_edge, 2)
    atlas_line_bottom_left(canvas, front_x + 2, y + 9, front_x + 61, y + 9, jacket_edge)

    # Back yoke and centre seam.
    atlas_line_bottom_left(canvas, back_x + 2, y + 104, back_x + 61, y + 104, jacket_dark, 2)
    atlas_line_bottom_left(canvas, back_x + 32, y + 8, back_x + 32, y + 104, jacket_edge)
    atlas_line_bottom_left(canvas, back_x + 3, y + 9, back_x + 61, y + 9, jacket_edge)

    # Seed-free broken wear reads as faded cloth, not bright camouflage.
    for local_x in range(width):
        for local_y in range(height):
            if (local_x * 17 + local_y * 29) % 431 == 0:
                atlas_rect_bottom_left(canvas, x + local_x, y + local_y, x + local_x + 1, y + local_y + 1, jacket_edge)

    for region_name in ("JacketSleeveLeft", "JacketSleeveRight"):
        sx, sy, sw, sh = clothing_region(region_name)
        atlas_rect_bottom_left(canvas, sx, sy, sx + sw, sy + sh, jacket)
        atlas_line_bottom_left(canvas, sx + 2, sy + 8, sx + sw - 3, sy + 8, jacket_dark)
        atlas_line_bottom_left(canvas, sx + sw // 2, sy + 2, sx + sw // 2, sy + sh - 2, jacket_edge)
        atlas_rect_bottom_left(canvas, sx, sy + sh - 8, sx + sw, sy + sh, jacket_dark)
        atlas_line_bottom_left(canvas, sx, sy + sh - 8, sx + sw - 1, sy + sh - 8, jacket_edge)
    # Our own ochre patch exists only on physical-right (-X) sleeve pixels.
    sx, sy, sw, sh = clothing_region("JacketSleeveRight")
    atlas_rect_bottom_left(canvas, sx + 20, sy + 11, sx + 44, sy + 30, patch)
    atlas_line_bottom_left(canvas, sx + 20, sy + 11, sx + 44, sy + 11, jacket_dark)

    fx, fy, fw, fh = clothing_region("JacketForearmRight")
    atlas_rect_bottom_left(canvas, fx, fy, fx + fw, fy + fh, jacket)
    atlas_line_bottom_left(canvas, fx + fw // 2, fy + 2, fx + fw // 2, fy + fh - 3, jacket_edge)
    for fold_y in (15, 29, 43):
        atlas_line_bottom_left(canvas, fx + 5, fy + fold_y, fx + fw - 7, fy + fold_y + 3, jacket_dark)
    atlas_rect_bottom_left(canvas, fx, fy + fh - 9, fx + fw, fy + fh, jacket_dark)
    atlas_line_bottom_left(canvas, fx, fy + fh - 10, fx + fw - 1, fy + fh - 10, jacket_edge)

    bx, by, bw, bh = clothing_region("BandageLeft")
    atlas_rect_bottom_left(canvas, bx, by, bx + bw, by + bh, bandage)
    for fraction in (0.15, 0.31, 0.48, 0.66, 0.82):
        wrap_y = by + round(fraction * (bh - 1))
        atlas_line_bottom_left(canvas, bx, wrap_y, bx + bw - 1, wrap_y, bandage_dark, 2)
    atlas_line_bottom_left(canvas, bx + 3, by + 2, bx + bw - 5, by + bh - 3, bandage_dark)

    for region_name in (
        "JeansPelvis", "JeansThighLeft", "JeansThighRight",
        "JeansShinLeft", "JeansShinRight",
    ):
        jx, jy, jw, jh = clothing_region(region_name)
        atlas_rect_bottom_left(canvas, jx, jy, jx + jw, jy + jh, jeans)
        atlas_line_bottom_left(canvas, jx + jw // 2, jy + 2, jx + jw // 2, jy + jh - 3, jeans_edge)
        atlas_line_bottom_left(canvas, jx + 2, jy + 4, jx + 2, jy + jh - 4, jeans_edge)
    px, py, pw, ph = clothing_region("JeansPelvis")
    atlas_line_bottom_left(canvas, px + 6, py + 45, px + 26, py + 37, jeans_edge, 2)
    atlas_line_bottom_left(canvas, px + 58, py + 45, px + 38, py + 37, jeans_edge, 2)
    boot = rgba_from_hex(V2_PALETTE_HEX["BootLeather"])
    boot_edge = (62, 54, 45, 255)
    for region_name in ("JeansShinLeft", "JeansShinRight"):
        jx, jy, jw, jh = clothing_region(region_name)
        # Ring-strip V grows knee -> ankle, so the top 13 pixels paint a
        # short boot shaft over the lower 20% of each shin without adding a
        # separate cuff/shaft mesh.
        shaft_bottom = jy + jh - 13
        atlas_rect_bottom_left(canvas, jx, shaft_bottom, jx + jw, jy + jh, boot)
        atlas_line_bottom_left(canvas, jx, shaft_bottom, jx + jw - 1, shaft_bottom, jeans_edge, 2)
        atlas_line_bottom_left(canvas, jx + 5, shaft_bottom + 4, jx + jw - 6, shaft_bottom + 4, boot_edge)

    boot_sole = rgba_from_hex(V2_PALETTE_HEX["BootSole"])
    eyelet = (108, 101, 87, 255)
    for region_name in ("BootLeft", "BootRight"):
        fx, fy, fw, fh = clothing_region(region_name)
        atlas_rect_bottom_left(canvas, fx, fy, fx + fw, fy + fh, boot)
        # Left half: outer side panels, toe cap and integrated sole edge.
        atlas_rect_bottom_left(canvas, fx, fy, fx + fw // 2, fy + 7, boot_sole)
        atlas_line_bottom_left(canvas, fx + 1, fy + 8, fx + fw // 2 - 2, fy + 8, boot_edge, 2)
        atlas_line_bottom_left(canvas, fx + 8, fy + 10, fx + 13, fy + 44, boot_edge, 2)
        atlas_line_bottom_left(canvas, fx + 13, fy + 44, fx + 29, fy + 51, boot_edge)
        atlas_line_bottom_left(canvas, fx + 4, fy + 20, fx + 15, fy + 23, boot_edge)
        # Right half: front/instep panel with readable laces and eyelets.
        front = fx + fw // 2
        atlas_rect_bottom_left(canvas, front, fy, fx + fw, fy + 7, boot_sole)
        atlas_line_bottom_left(canvas, front + 3, fy + 48, front + 29, fy + 48, boot_edge, 2)
        atlas_line_bottom_left(canvas, front + 7, fy + 10, front + 9, fy + 50, boot_edge)
        atlas_line_bottom_left(canvas, front + 25, fy + 10, front + 23, fy + 50, boot_edge)
        for lace_y in (17, 24, 31, 38, 45):
            atlas_rect_bottom_left(canvas, front + 7, fy + lace_y - 1, front + 10, fy + lace_y + 2, eyelet)
            atlas_rect_bottom_left(canvas, front + 22, fy + lace_y - 1, front + 25, fy + lace_y + 2, eyelet)
            atlas_line_bottom_left(canvas, front + 9, fy + lace_y, front + 23, fy + lace_y + 4, boot_edge)
            atlas_line_bottom_left(canvas, front + 23, fy + lace_y, front + 9, fy + lace_y + 4, boot_edge)
        atlas_line_bottom_left(canvas, front + 3, fy + 13, front + 29, fy + 13, boot_edge, 2)

    canvas.write_png(path)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def create_static_atlas_material(name: str, path: Path) -> bpy.types.Material:
    material = bpy.data.materials.new(f"MAT_{name}")
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", DeprecationWarning)
        material.use_nodes = True
    material.diffuse_color = (1.0, 1.0, 1.0, 1.0)
    material["bp_static_atlas"] = True
    material["bp_generator_version"] = V2_GENERATOR_VERSION
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes.get("Principled BSDF")
    if principled is None:
        raise RuntimeError(f"MAT_{name} has no Principled BSDF")
    image = bpy.data.images.load(str(path), check_existing=True)
    image.name = "PlayerClothingAtlas"
    image.colorspace_settings.name = "sRGB"
    texture = nodes.new("ShaderNodeTexImage")
    texture.name = "TEX_PlayerClothingAtlas"
    texture.image = image
    texture.interpolation = "Closest"
    texture.extension = "CLIP"
    uv_node = nodes.new("ShaderNodeUVMap")
    uv_node.uv_map = "UVMap"
    links.new(uv_node.outputs["UV"], texture.inputs["Vector"])
    links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    roughness = principled.inputs.get("Roughness")
    if roughness is not None:
        roughness.default_value = 0.94
    specular = principled.inputs.get("Specular IOR Level") or principled.inputs.get("Specular")
    if specular is not None:
        specular.default_value = 0.14
    return material


def create_face_atlas_material(path: Path) -> bpy.types.Material:
    material = bpy.data.materials.new("MAT_FaceAtlas")
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", DeprecationWarning)
        material.use_nodes = True
    material.diffuse_color = common.hex_to_linear_rgba("AE8D7B")
    material["bp_face_atlas"] = True
    material["bp_atlas_columns"] = ATLAS_COLUMNS
    material["bp_atlas_rows"] = ATLAS_ROWS
    material["bp_generator_version"] = V2_GENERATOR_VERSION

    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes.get("Principled BSDF")
    if principled is None:
        raise RuntimeError("MAT_FaceAtlas has no Principled BSDF")
    image = bpy.data.images.load(str(path), check_existing=False)
    image.name = "PlayerFaceAtlas"
    image.colorspace_settings.name = "sRGB"
    texture = nodes.new("ShaderNodeTexImage")
    texture.name = "TEX_PlayerFaceAtlas"
    texture.image = image
    texture.interpolation = "Closest"
    texture.extension = "CLIP"
    # UVMap remains normalized 0..1 for Unity. Blender preview explicitly uses
    # a second UV layer already transformed into the Neutral atlas cell; this
    # avoids relying on Mapping-node transform order across Blender versions.
    preview_uv = nodes.new("ShaderNodeUVMap")
    preview_uv.name = "UV_PlayerFaceNeutral"
    preview_uv.uv_map = "UVNeutral"
    links.new(preview_uv.outputs["UV"], texture.inputs["Vector"])
    links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    roughness = principled.inputs.get("Roughness")
    if roughness is not None:
        roughness.default_value = 0.92
    specular = principled.inputs.get("Specular IOR Level") or principled.inputs.get("Specular")
    if specular is not None:
        specular.default_value = 0.18
    return material


def make_profiled_segment_geometry(
    start: Vector,
    end: Vector,
    profile: Sequence[tuple[float, float, float]],
    sides: int = 8,
    phase: float = math.pi / 8.0,
) -> tuple[list[Vector], list[tuple[int, ...]]]:
    """One closed limb volume with authored radius/depth along its axis."""

    axis_x, axis_y, _ = common.segment_basis(start, end)
    vertices: list[Vector] = []
    for fraction, radius, depth_scale in profile:
        center = start.lerp(end, fraction)
        for index in range(sides):
            angle = phase + index * math.tau / sides
            vertices.append(
                center
                + axis_x * (math.cos(angle) * radius)
                + axis_y * (math.sin(angle) * radius * depth_scale)
            )
    faces: list[tuple[int, ...]] = [tuple(reversed(range(sides)))]
    for ring_index in range(len(profile) - 1):
        lower = ring_index * sides
        upper = (ring_index + 1) * sides
        for side_index in range(sides):
            following = (side_index + 1) % sides
            faces.append((lower + side_index, lower + following, upper + following, upper + side_index))
    last = (len(profile) - 1) * sides
    faces.append(tuple(last + index for index in range(sides)))
    return vertices, faces


def make_adult_boot_geometry(
    center_x: float,
    ankle_y: float,
    toe_y: float,
    scale: float,
) -> tuple[list[Vector], list[tuple[int, ...]]]:
    """Angular military boot with heel, instep and tapered toe in one mesh."""

    stations = (
        (toe_y, 0.035 * scale, 0.000, 0.032 * scale),
        (toe_y + 0.024 * scale, 0.050 * scale, 0.000, 0.050 * scale),
        (toe_y + 0.070 * scale, 0.063 * scale, 0.000, 0.070 * scale),
        (ankle_y - 0.052 * scale, 0.065 * scale, 0.000, 0.098 * scale),
        (ankle_y + 0.018 * scale, 0.058 * scale, 0.000, 0.156 * scale),
        (ankle_y + 0.073 * scale, 0.050 * scale, 0.012 * scale, 0.170 * scale),
    )
    vertices: list[Vector] = []
    for y, half_width, bottom_z, top_z in stations:
        vertices.extend(
            (
                Vector((center_x - half_width, y, bottom_z)),
                Vector((center_x + half_width, y, bottom_z)),
                Vector((center_x - half_width * 0.88, y, top_z)),
                Vector((center_x + half_width * 0.88, y, top_z)),
            )
        )
    faces: list[tuple[int, ...]] = []
    for station in range(len(stations) - 1):
        current = station * 4
        following = (station + 1) * 4
        faces.extend(
            (
                (current, following, following + 1, current + 1),
                (current + 2, current + 3, following + 3, following + 2),
                (current, current + 2, following + 2, following),
                (current + 1, following + 1, following + 3, current + 3),
            )
        )
    faces.append((0, 1, 3, 2))
    last = (len(stations) - 1) * 4
    faces.append((last, last + 2, last + 3, last + 1))
    return vertices, faces


CLOTHING_ATLAS_REGION_PROP = "bp_clothing_atlas_region"


def uv_region_normalized(name: str, padding_px: float = 1.0) -> tuple[float, float, float, float]:
    x, y, width, height = clothing_region(name)
    return atlas_kit.uv_rect_normalized(x, y, width, height, CLOTHING_ATLAS_SIZE, padding_px)


def assign_ring_strip_uv(
    obj: bpy.types.Object,
    region_name: str,
    sides: int,
    ring_count: int,
) -> None:
    """Seam-aware UV strip for closed frusta/ringed garment volumes."""

    atlas_kit.assign_ring_strip_uv(
        obj,
        uv_region_normalized(region_name),
        sides,
        ring_count,
        region_name,
        CLOTHING_ATLAS_REGION_PROP,
    )


def assign_jacket_body_uv(obj: bpy.types.Object) -> None:
    """Planar front/back halves inside JacketBody's single atlas sub-rect."""

    uv_layer = obj.data.uv_layers.new(name="UVMap")
    obj["bp_clothing_atlas_region"] = "JacketBody"
    x, y, width, height = clothing_region("JacketBody")
    points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    min_x = min(point.x for point in points)
    max_x = max(point.x for point in points)
    min_z = min(point.z for point in points)
    max_z = max(point.z for point in points)
    center_y = sum(point.y for point in points) / len(points)
    half = width // 2
    size = float(CLOTHING_ATLAS_SIZE)
    for polygon in obj.data.polygons:
        centroid_y = sum(points[index].y for index in polygon.vertices) / len(polygon.vertices)
        panel_x = x if centroid_y <= center_y else x + half
        for loop_index in polygon.loop_indices:
            point = points[obj.data.loops[loop_index].vertex_index]
            local_u = (point.x - min_x) / max(1e-6, max_x - min_x)
            local_v = (point.z - min_z) / max(1e-6, max_z - min_z)
            uv_layer.data[loop_index].uv = (
                (panel_x + 2 + local_u * (half - 4)) / size,
                (y + 2 + local_v * (height - 4)) / size,
            )
    uv_layer.active_render = True


def assign_boot_uv(obj: bpy.types.Object, region_name: str) -> None:
    """Split one boot region between side-panel and front/instep artwork."""

    atlas_kit.assign_box_panel_uv(
        obj,
        clothing_region(region_name),
        CLOTHING_ATLAS_SIZE,
        region_name,
        CLOTHING_ATLAS_REGION_PROP,
    )


def torso_weights(height_m: float) -> dict[str, float]:
    """Three anatomical regions, with only adjacent bones sharing a vertex."""
    for index, (lower, upper) in enumerate(TORSO_BLEND_BANDS):
        if height_m <= lower:
            return {TORSO_SKIN_BONES[index]: 1.0}
        if height_m < upper:
            t = (height_m - lower) / (upper - lower)
            weight = t * t * (3.0 - 2.0 * t)
            return {TORSO_SKIN_BONES[index]: 1.0 - weight,
                    TORSO_SKIN_BONES[index + 1]: weight}
    return {"chest": 1.0}


def subdivide_torso_profiles(profiles: Sequence[Sequence[float]]) -> tuple:
    """Add bending rings without changing the authored silhouette or UV field."""
    result = [tuple(profiles[0])]
    for lower, upper in zip(profiles, profiles[1:]):
        count = math.ceil((upper[0] - lower[0]) / 0.05)
        for index in range(1, count + 1):
            t = index / count
            result.append(tuple(a + (b - a) * t for a, b in zip(lower, upper)))
    return tuple(result)


class HeroV2Builder(common.ProductionPlayerBuilderBase):
    def __init__(
        self,
        config: common.BuildConfig,
        face_atlas_path: Path,
        clothing_atlas_path: Path,
    ):
        super().__init__(config)
        self.face_atlas_path = face_atlas_path
        self.clothing_atlas_path = clothing_atlas_path

    def build(self) -> common.BuildResult:
        self.reset_scene()
        collections = self.create_collections()
        materials = {
            name: common.create_material(name, color)
            for name, color in V2_PALETTE_HEX.items()
        }
        materials["FaceAtlas"] = create_face_atlas_material(self.face_atlas_path)
        for name in ("JacketAtlas", "JeansAtlas", "BandageAtlas"):
            materials[name] = create_static_atlas_material(name, self.clothing_atlas_path)
        self.points = self.create_pose_points()
        root = self.create_root(collections["export"])
        bone_specs = self.create_bone_specs()
        rig = self.create_armature(collections["rig"], root, bone_specs)
        self.bone_heads = {spec.name: spec.head for spec in bone_specs}
        self.bone_specs = {spec.name: spec for spec in bone_specs}
        self.result = common.BuildResult(
            root=root,
            rig=rig,
            collections=collections,
            materials=materials,
        )
        self.build_core_anatomy()
        self.build_clothing()
        self.build_face_and_hair()
        self.build_asymmetric_details()
        self.build_actions()
        self.build_presentation()
        self.configure_scene_metadata()
        return self.result

    def build_actions(self) -> None:
        """Rebuild all actions on the articulated torso; retain contact timing.

        Every action already authors the independent pelvis, lumbar and chest
        tracks. Binding the longitudinal regions activates those tracks on the
        actual body; changing their endpoints would displace feet, grips and
        seated/bed contacts shared with the environment.
        """

        super().build_actions()
        if self.result is None:
            raise RuntimeError("BuildResult has not been initialized")
        for action_name in (
            "BarDrinkPickupEnter",
            "BarDrinkSipLoop",
            "BarDrinkReturnExit",
        ):
            self.result.actions[action_name].action[
                "bp_generator_version"] = V2_GENERATOR_VERSION
        relaxed = self.relaxed_pose()
        bone_pose = common.BonePose

        def run_pose(
            vertical_m: float,
            twist_degrees: float,
            roll_degrees: float,
            limbs: dict[str, common.BonePose],
        ) -> dict[str, common.BonePose]:
            # Forward source space is -Y, so positive local X through the
            # pelvis/spine/chest creates the persistent tired forward pitch.
            # Vertical motion belongs to the pelvis, never the root bone.
            return self.merge_pose(
                relaxed,
                {
                    "pelvis": bone_pose(
                        rotation_degrees=(
                            4.5,
                            twist_degrees * 0.35,
                            roll_degrees,
                        ),
                        location_m=(0.0, 0.0, vertical_m),
                    ),
                    "spine": bone_pose(
                        rotation_degrees=(
                            6.5,
                            twist_degrees * 0.40,
                            -roll_degrees * 0.55,
                        )
                    ),
                    "chest": bone_pose(
                        rotation_degrees=(
                            5.5,
                            twist_degrees,
                            roll_degrees * 0.45,
                        )
                    ),
                    "neck": bone_pose(
                        rotation_degrees=(
                            -7.0,
                            -twist_degrees * 0.35,
                            -roll_degrees * 0.20,
                        )
                    ),
                    "head": bone_pose(
                        rotation_degrees=(
                            -5.0,
                            -twist_degrees * 0.30,
                            roll_degrees * 0.15,
                        )
                    ),
                },
                limbs,
            )

        left_contact = run_pose(
            0.0,
            -4.0,
            -2.2,
            {
                "upper_arm.L": bone_pose(
                    target_direction=(0.075, 0.185, -0.245)
                ),
                "upper_arm.R": bone_pose(
                    target_direction=(-0.075, -0.195, -0.225)
                ),
                "forearm.L": bone_pose(rotation_degrees=(-58.0, 6.0, -5.0)),
                "forearm.R": bone_pose(rotation_degrees=(-74.0, -7.0, 5.0)),
                "hand.L": bone_pose(rotation_degrees=(8.0, -8.0, 4.0)),
                "hand.R": bone_pose(rotation_degrees=(10.0, 9.0, -4.0)),
                "thigh.L": bone_pose(rotation_degrees=(-38.0, 0.0, 1.5)),
                "shin.L": bone_pose(rotation_degrees=(18.0, 0.0, 0.0)),
                "foot.L": bone_pose(rotation_degrees=(18.0, 0.0, 0.0)),
                "thigh.R": bone_pose(rotation_degrees=(30.0, 0.0, -1.5)),
                "shin.R": bone_pose(rotation_degrees=(42.0, 0.0, 0.0)),
                "foot.R": bone_pose(rotation_degrees=(-18.0, 0.0, 0.0)),
            },
        )
        left_down = run_pose(
            0.0,
            -2.0,
            -1.2,
            {
                "upper_arm.L": bone_pose(
                    target_direction=(0.072, 0.125, -0.274)
                ),
                "upper_arm.R": bone_pose(
                    target_direction=(-0.072, -0.135, -0.265)
                ),
                "forearm.L": bone_pose(rotation_degrees=(-62.0, 6.0, -5.0)),
                "forearm.R": bone_pose(rotation_degrees=(-70.0, -7.0, 5.0)),
                "thigh.L": bone_pose(rotation_degrees=(-30.0, 0.0, 1.0)),
                "shin.L": bone_pose(rotation_degrees=(34.0, 0.0, 0.0)),
                "foot.L": bone_pose(rotation_degrees=(8.0, 0.0, 0.0)),
                "thigh.R": bone_pose(rotation_degrees=(22.0, 0.0, -1.0)),
                "shin.R": bone_pose(rotation_degrees=(55.0, 0.0, 0.0)),
                "foot.R": bone_pose(rotation_degrees=(-20.0, 0.0, 0.0)),
            },
        )
        right_pass = run_pose(
            0.028,
            1.5,
            0.8,
            {
                "upper_arm.L": bone_pose(
                    target_direction=(0.074, -0.045, -0.294)
                ),
                "upper_arm.R": bone_pose(
                    target_direction=(-0.074, 0.055, -0.291)
                ),
                "forearm.L": bone_pose(rotation_degrees=(-68.0, 6.0, -5.0)),
                "forearm.R": bone_pose(rotation_degrees=(-64.0, -7.0, 5.0)),
                "thigh.L": bone_pose(rotation_degrees=(8.0, 0.0, 1.0)),
                "shin.L": bone_pose(rotation_degrees=(68.0, 0.0, 0.0)),
                "foot.L": bone_pose(rotation_degrees=(-10.0, 0.0, 0.0)),
                "thigh.R": bone_pose(rotation_degrees=(-24.0, 0.0, -1.0)),
                "shin.R": bone_pose(rotation_degrees=(48.0, 0.0, 0.0)),
                "foot.R": bone_pose(rotation_degrees=(15.0, 0.0, 0.0)),
            },
        )
        right_flight = run_pose(
            0.060,
            4.0,
            1.8,
            {
                "upper_arm.L": bone_pose(
                    target_direction=(0.075, -0.195, -0.225)
                ),
                "upper_arm.R": bone_pose(
                    target_direction=(-0.075, 0.185, -0.245)
                ),
                "forearm.L": bone_pose(rotation_degrees=(-78.0, 7.0, -5.0)),
                "forearm.R": bone_pose(rotation_degrees=(-56.0, -6.0, 5.0)),
                "hand.L": bone_pose(rotation_degrees=(10.0, -9.0, 4.0)),
                "hand.R": bone_pose(rotation_degrees=(8.0, 8.0, -4.0)),
                "thigh.L": bone_pose(rotation_degrees=(20.0, 0.0, 1.5)),
                "shin.L": bone_pose(rotation_degrees=(72.0, 0.0, 0.0)),
                "foot.L": bone_pose(rotation_degrees=(-18.0, 0.0, 0.0)),
                "thigh.R": bone_pose(rotation_degrees=(-40.0, 0.0, -1.5)),
                "shin.R": bone_pose(rotation_degrees=(30.0, 0.0, 0.0)),
                "foot.R": bone_pose(rotation_degrees=(22.0, 0.0, 0.0)),
            },
        )
        right_contact = run_pose(
            0.0,
            4.0,
            2.2,
            {
                "upper_arm.L": bone_pose(
                    target_direction=(0.075, -0.195, -0.225)
                ),
                "upper_arm.R": bone_pose(
                    target_direction=(-0.075, 0.185, -0.245)
                ),
                "forearm.L": bone_pose(rotation_degrees=(-74.0, 7.0, -5.0)),
                "forearm.R": bone_pose(rotation_degrees=(-58.0, -6.0, 5.0)),
                "hand.L": bone_pose(rotation_degrees=(10.0, -9.0, 4.0)),
                "hand.R": bone_pose(rotation_degrees=(8.0, 8.0, -4.0)),
                "thigh.L": bone_pose(rotation_degrees=(30.0, 0.0, 1.5)),
                "shin.L": bone_pose(rotation_degrees=(42.0, 0.0, 0.0)),
                "foot.L": bone_pose(rotation_degrees=(-18.0, 0.0, 0.0)),
                "thigh.R": bone_pose(rotation_degrees=(-38.0, 0.0, -1.5)),
                "shin.R": bone_pose(rotation_degrees=(18.0, 0.0, 0.0)),
                "foot.R": bone_pose(rotation_degrees=(18.0, 0.0, 0.0)),
            },
        )
        right_down = run_pose(
            0.0,
            2.0,
            1.2,
            {
                "upper_arm.L": bone_pose(
                    target_direction=(0.072, -0.135, -0.265)
                ),
                "upper_arm.R": bone_pose(
                    target_direction=(-0.072, 0.125, -0.274)
                ),
                "forearm.L": bone_pose(rotation_degrees=(-70.0, 7.0, -5.0)),
                "forearm.R": bone_pose(rotation_degrees=(-62.0, -6.0, 5.0)),
                "thigh.L": bone_pose(rotation_degrees=(22.0, 0.0, 1.0)),
                "shin.L": bone_pose(rotation_degrees=(55.0, 0.0, 0.0)),
                "foot.L": bone_pose(rotation_degrees=(-20.0, 0.0, 0.0)),
                "thigh.R": bone_pose(rotation_degrees=(-30.0, 0.0, -1.0)),
                "shin.R": bone_pose(rotation_degrees=(34.0, 0.0, 0.0)),
                "foot.R": bone_pose(rotation_degrees=(8.0, 0.0, 0.0)),
            },
        )
        left_pass = run_pose(
            0.028,
            -1.5,
            -0.8,
            {
                "upper_arm.L": bone_pose(
                    target_direction=(0.074, 0.055, -0.291)
                ),
                "upper_arm.R": bone_pose(
                    target_direction=(-0.074, -0.045, -0.294)
                ),
                "forearm.L": bone_pose(rotation_degrees=(-64.0, 6.0, -5.0)),
                "forearm.R": bone_pose(rotation_degrees=(-68.0, -7.0, 5.0)),
                "thigh.L": bone_pose(rotation_degrees=(-24.0, 0.0, 1.0)),
                "shin.L": bone_pose(rotation_degrees=(48.0, 0.0, 0.0)),
                "foot.L": bone_pose(rotation_degrees=(15.0, 0.0, 0.0)),
                "thigh.R": bone_pose(rotation_degrees=(8.0, 0.0, -1.0)),
                "shin.R": bone_pose(rotation_degrees=(68.0, 0.0, 0.0)),
                "foot.R": bone_pose(rotation_degrees=(-10.0, 0.0, 0.0)),
            },
        )
        left_flight = run_pose(
            0.060,
            -4.0,
            -1.8,
            {
                "upper_arm.L": bone_pose(
                    target_direction=(0.075, 0.185, -0.245)
                ),
                "upper_arm.R": bone_pose(
                    target_direction=(-0.075, -0.195, -0.225)
                ),
                "forearm.L": bone_pose(rotation_degrees=(-56.0, 6.0, -5.0)),
                "forearm.R": bone_pose(rotation_degrees=(-78.0, -7.0, 5.0)),
                "hand.L": bone_pose(rotation_degrees=(8.0, -8.0, 4.0)),
                "hand.R": bone_pose(rotation_degrees=(10.0, 9.0, -4.0)),
                "thigh.L": bone_pose(rotation_degrees=(-40.0, 0.0, 1.5)),
                "shin.L": bone_pose(rotation_degrees=(30.0, 0.0, 0.0)),
                "foot.L": bone_pose(rotation_degrees=(22.0, 0.0, 0.0)),
                "thigh.R": bone_pose(rotation_degrees=(20.0, 0.0, -1.5)),
                "shin.R": bone_pose(rotation_degrees=(72.0, 0.0, 0.0)),
                "foot.R": bone_pose(rotation_degrees=(-18.0, 0.0, 0.0)),
            },
        )

        self._create_action(
            RUN_ACTION_NAME,
            "locomotion",
            RUN_DURATION_SECONDS,
            True,
            RUN_SOURCE_FRAME_COUNT,
            RUN_SOURCE_FPS,
            (
                (0.0, left_contact),
                (0.125, left_down),
                (0.25, right_pass),
                (0.375, right_flight),
                (0.5, right_contact),
                (0.625, right_down),
                (0.75, left_pass),
                (0.875, left_flight),
                (1.0, left_contact),
            ),
            interpolation="BEZIER",
        )
        if self.result is None:
            raise RuntimeError("BuildResult has not been initialized")
        run_action = self.result.actions[RUN_ACTION_NAME].action
        run_action["bp_generator_version"] = V2_GENERATOR_VERSION
        run_action["bp_event_count"] = 0
        run_action["bp_gait_style"] = "heavy_weary"
        run_action["bp_landmark_count"] = 8
        run_action["bp_short_flight"] = True
        for record in self.result.actions.values():
            record.action["bp_torso_skin"] = "pelvis_spine_chest_v1"
            record.action["bp_generator_version"] = V2_GENERATOR_VERSION

    def skin_torso(self, obj: bpy.types.Object) -> None:
        obj.vertex_groups.clear()
        groups = {name: obj.vertex_groups.new(name=name) for name in TORSO_SKIN_BONES}
        scale = self.config.height / 1.75
        for vertex in obj.data.vertices:
            # add_part stores vertices relative to obj.location. Its matrix is
            # not dependency-graph evaluated yet during construction.
            height = (obj.location.z + vertex.co.z) / scale
            for bone, weight in torso_weights(height).items():
                groups[bone].add([vertex.index], weight, "REPLACE")
        obj["bp_skin_contract"] = "pelvis_spine_chest_v1"
        obj["bp_generator_version"] = V2_GENERATOR_VERSION

    def create_root(self, collection: bpy.types.Collection) -> bpy.types.Object:
        root = super().create_root(collection)
        root.name = "ROOT_PlayerV2"
        root["bp_generator_version"] = V2_GENERATOR_VERSION
        root["bp_design"] = "Hero V2 lean weary adult survival-horror production"
        root["bp_design_version"] = "HeroV2"
        root["bp_runtime_integrated"] = True
        return root

    def create_pose_points(self) -> dict[str, Vector]:
        # Adult survival-horror envelope: a 0.2335 m authored head at 1.75 m
        # reads as 7.49 heads tall; the 0.418 m girdle stays human rather than
        # toy-like after the cranium's X/Y volume reduction.
        points = {
            "hip.L": self.v(0.092, 0.010, 0.878),
            "hip.R": self.v(-0.092, -0.004, 0.878),
            "knee.L": self.v(0.088, -0.014, 0.485),
            "knee.R": self.v(-0.088, 0.010, 0.485),
            "ankle.L": self.v(0.096, -0.022, 0.095),
            "ankle.R": self.v(-0.096, 0.014, 0.095),
            "toe.L": self.v(0.096, -0.212, 0.045),
            "toe.R": self.v(-0.096, -0.180, 0.045),
            "shoulder.L": self.v(0.210, -0.004, 1.424),
            "shoulder.R": self.v(-0.208, 0.005, 1.418),
        }
        if self.config.pose == "apose":
            points.update(
                {
                    "elbow.L": self.v(0.455, -0.012, 1.250),
                    "wrist.L": self.v(0.680, -0.020, 1.075),
                    "hand.L": self.v(0.763, -0.024, 1.025),
                    "elbow.R": self.v(-0.453, -0.008, 1.244),
                    "wrist.R": self.v(-0.678, -0.016, 1.069),
                    "hand.R": self.v(-0.761, -0.020, 1.019),
                }
            )
        else:
            points.update(
                {
                    "elbow.L": self.v(0.270, -0.022, 1.096),
                    "wrist.L": self.v(0.258, -0.052, 0.817),
                    "hand.L": self.v(0.263, -0.062, 0.722),
                    "elbow.R": self.v(-0.264, -0.012, 1.086),
                    "wrist.R": self.v(-0.252, -0.045, 0.804),
                    "hand.R": self.v(-0.258, -0.057, 0.709),
                }
            )
        return points

    def create_bone_specs(self) -> list[common.BoneSpec]:
        p = self.points
        B = common.BoneSpec
        specs = [
            B("root", self.v(0, 0, 0), self.v(0, 0, 0.18), deform=False),
            B("pelvis", self.v(0, 0.008, 0.835), self.v(0, 0.004, 1.015), "root"),
            B("spine", self.v(0, 0.004, 1.015), self.v(0, 0.000, 1.205), "pelvis", True),
            B("chest", self.v(0, 0.000, 1.205), self.v(0, -0.010, 1.410), "spine", True),
            B("neck", self.v(0, -0.010, 1.410), self.v(0, -0.025, 1.485), "chest", True),
            B("head", self.v(0, -0.025, 1.485), self.v(0, -0.047, 1.675), "neck", True),
            B("clavicle.L", self.v(0, -0.008, 1.410), p["shoulder.L"], "chest", deform=False),
            B("upper_arm.L", p["shoulder.L"], p["elbow.L"], "clavicle.L", True),
            B("forearm.L", p["elbow.L"], p["wrist.L"], "upper_arm.L", True),
            B("hand.L", p["wrist.L"], p["hand.L"], "forearm.L", True),
            B("clavicle.R", self.v(0, -0.008, 1.410), p["shoulder.R"], "chest", deform=False),
            B("upper_arm.R", p["shoulder.R"], p["elbow.R"], "clavicle.R", True),
            B("forearm.R", p["elbow.R"], p["wrist.R"], "upper_arm.R", True),
            B("hand.R", p["wrist.R"], p["hand.R"], "forearm.R", True),
            B("thigh.L", p["hip.L"], p["knee.L"], "pelvis"),
            B("shin.L", p["knee.L"], p["ankle.L"], "thigh.L", True),
            B("foot.L", p["ankle.L"], p["toe.L"], "shin.L", True),
            B("thigh.R", p["hip.R"], p["knee.R"], "pelvis"),
            B("shin.R", p["knee.R"], p["ankle.R"], "thigh.R", True),
            B("foot.R", p["ankle.R"], p["toe.R"], "shin.R", True),
            B("face.eye.L", self.v(0.039, -0.122, 1.606), self.v(0.039, -0.122, 1.620), "head"),
            B("face.eye.R", self.v(-0.039, -0.122, 1.606), self.v(-0.039, -0.122, 1.620), "head"),
            B("face.brow.L", self.v(0.066, -0.119, 1.644), self.v(0.018, -0.127, 1.642), "head"),
            B("face.brow.R", self.v(-0.066, -0.119, 1.642), self.v(-0.018, -0.127, 1.641), "head"),
            B("face.mouth", self.v(-0.032, -0.133, 1.538), self.v(0.036, -0.133, 1.537), "head"),
        ]
        grip_l = p["wrist.L"].lerp(p["hand.L"], 0.72)
        grip_r = p["wrist.R"].lerp(p["hand.R"], 0.72)
        specs.extend(
            (
                B("SOCKET_Grip.L", grip_l, grip_l + self.v(0, -0.055, 0), "hand.L", deform=False),
                B("SOCKET_Grip.R", grip_r, grip_r + self.v(0, -0.055, 0), "hand.R", deform=False),
                B("SOCKET_Cigarette.R", grip_r + self.v(0, -0.010, 0.012), grip_r + self.v(0, -0.085, 0.012), "hand.R", deform=False),
                B("SOCKET_Bottle.R", grip_r, grip_r + self.v(0, 0, -0.085), "hand.R", deform=False),
                B("SOCKET_Vessel.L", grip_l, grip_l + self.v(0, 0, -0.085), "hand.L", deform=False),
                B("SOCKET_Mouth", self.v(0.002, -0.141, 1.538), self.v(0.002, -0.201, 1.538), "head", deform=False),
            )
        )
        return specs

    def build_core_anatomy(self) -> None:
        p = self.points
        head_rings = tuple(
            (self.d(z), self.d(rx), self.d(ry), self.d(y_offset))
            for z, rx, ry, y_offset in (
                # Lower face keeps its vertical room and jaw taper; the
                # cranium above the eyes takes most of the X/Y/Z reduction.
                (1.4880, 0.032, 0.038, -0.002),
                (1.5060, 0.052, 0.057, -0.007),
                (1.5360, 0.073, 0.069, -0.012),
                (1.5790, 0.086, 0.079, -0.015),
                (1.6230, 0.088, 0.081, -0.013),
                (1.6720, 0.084, 0.079, -0.007),
                (1.7090, 0.060, 0.056, -0.003),
                (1.7215, 0.016, 0.016, 0.000),
            )
        )
        self.add_part(
            "GEO_Head",
            common.make_ringed_ellipsoid(self.v(0, -0.018, 0), head_rings, 12),
            "Skin",
            "core",
            "head",
            "Body",
            "body_part",
            origin=self.bone_heads["head"],
        )
        neck = self.add_part(
            "GEO_Neck",
            common.make_frustum_between(
                self.v(0, -0.008, 1.455), self.v(0, -0.020, 1.522),
                self.d(0.074), self.d(0.0635), 8, 0.84, 0.0,
            ),
            "SkinShadow", "core", "neck", "Body", "body_part",
        )
        neck["bp_base_width_m"] = 0.148
        neck["bp_top_width_m"] = 0.127
        torso_rings = tuple(
            (self.d(z), self.d(rx), self.d(ry), self.d(y_offset))
            for z, rx, ry, y_offset in subdivide_torso_profiles((
                (0.878, 0.145, 0.086, 0.012),
                (0.970, 0.166, 0.094, 0.010),
                (1.105, 0.170, 0.101, 0.004),
                (1.250, 0.181, 0.108, -0.004),
                (1.350, 0.187, 0.109, -0.008),
                (1.415, 0.170, 0.095, -0.010),
            ))
        )
        torso = self.add_part(
            "GEO_Torso",
            common.make_ringed_ellipsoid(self.v(0, 0, 0), torso_rings, 10),
            "Shirt", "core", "chest", "Body", "body_part",
        )
        torso["bp_waist_half_width_m"] = 0.166
        torso["bp_chest_half_width_m"] = 0.187
        self.skin_torso(torso)
        pelvis_rings = tuple(
            (self.d(z), self.d(rx), self.d(ry), self.d(y_offset))
            for z, rx, ry, y_offset in (
                (0.775, 0.105, 0.074, 0.018),
                (0.820, 0.130, 0.085, 0.017),
                (0.878, 0.140, 0.090, 0.015),
                (0.935, 0.132, 0.082, 0.013),
                (0.972, 0.112, 0.072, 0.011),
            )
        )
        pelvis = self.add_part(
            "GEO_Pelvis",
            common.make_ringed_ellipsoid(self.v(0, 0, 0), pelvis_rings, 10),
            "JeansAtlas", "core", "pelvis", "Body", "body_part",
        )
        assign_ring_strip_uv(pelvis, "JeansPelvis", 10, len(pelvis_rings))

        for side, sign in (("L", 1.0), ("R", -1.0)):
            anatomical = "Left" if side == "L" else "Right"
            upper_sprite = f"{anatomical}UpperArm"
            lower_sprite = f"{anatomical}LowerArm"
            upper_leg_sprite = f"{anatomical}UpperLeg"
            lower_leg_sprite = f"{anatomical}LowerLeg"
            shoulder = p[f"shoulder.{side}"]
            elbow = p[f"elbow.{side}"]
            wrist = p[f"wrist.{side}"]
            hand_tail = p[f"hand.{side}"]
            hip = p[f"hip.{side}"]
            knee = p[f"knee.{side}"]
            ankle = p[f"ankle.{side}"]
            clothed_upper_start = shoulder.lerp(elbow, 0.12)
            self.add_part(
                f"GEO_UpperArm.{side}",
                common.make_frustum_between(clothed_upper_start, elbow, self.d(0.047), self.d(0.043), 8, 0.86),
                "SkinShadow", "core", f"upper_arm.{side}", upper_sprite, "body_part", anatomical,
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                common.make_frustum_between(elbow, wrist, self.d(0.048), self.d(0.034), 8, 0.86),
                "Skin", "core", f"forearm.{side}", lower_sprite, "body_part", anatomical,
            )
            hand_axis = hand_tail - wrist
            hand_rotation = hand_axis.to_track_quat("Z", "Y")
            self.add_part(
                f"GEO_Hand.{side}",
                common.make_ellipsoid_geometry(
                    (wrist + hand_tail) * 0.5,
                    self.v(0.038, 0.029, 0.060),
                    8, 4, hand_rotation,
                ),
                "Skin", "core", f"hand.{side}", lower_sprite, "body_part", anatomical,
            )
            thumb_start = wrist.lerp(hand_tail, 0.30) + self.v(sign * 0.020, -0.004, -0.002)
            thumb_end = wrist.lerp(hand_tail, 0.70) + self.v(sign * 0.036, -0.010, -0.015)
            self.add_part(
                f"GEO_Thumb.{side}",
                common.make_frustum_between(thumb_start, thumb_end, self.d(0.017), self.d(0.012), 6, 0.82),
                "SkinShadow", "core", f"hand.{side}", lower_sprite, "body_detail", anatomical,
                origin=wrist,
            )
            thigh = self.add_part(
                f"GEO_Thigh.{side}",
                make_profiled_segment_geometry(
                    hip, knee,
                    (
                        (0.0, self.d(0.083), 0.87),
                        (0.22, self.d(0.089), 0.89),
                        (0.68, self.d(0.070), 0.86),
                        (1.0, self.d(0.057), 0.84),
                    ),
                ),
                "JeansAtlas", "core", f"thigh.{side}", upper_leg_sprite, "body_part", anatomical,
            )
            assign_ring_strip_uv(thigh, f"JeansThigh{anatomical}", 8, 4)
            shin = self.add_part(
                f"GEO_Shin.{side}",
                make_profiled_segment_geometry(
                    knee, ankle,
                    (
                        (0.0, self.d(0.058), 0.84),
                        (0.18, self.d(0.064), 0.86),
                        (0.42, self.d(0.072), 0.88),
                        (0.72, self.d(0.056), 0.84),
                        (1.0, self.d(0.044), 0.82),
                    ),
                ),
                "JeansAtlas", "core", f"shin.{side}", lower_leg_sprite, "body_part", anatomical,
            )
            assign_ring_strip_uv(shin, f"JeansShin{anatomical}", 8, 5)
            foot = self.add_part(
                f"GEO_Foot.{side}",
                make_adult_boot_geometry(
                    ankle.x,
                    ankle.y,
                    p[f"toe.{side}"].y,
                    self.scale,
                ),
                "JeansAtlas", "core", f"foot.{side}", lower_leg_sprite, "body_part", anatomical,
                origin=ankle,
            )
            assign_boot_uv(foot, f"Boot{anatomical}")

    def build_clothing(self) -> None:
        p = self.points
        # One silhouette mesh replaces lapels, collars, panels and pockets.
        # The field jacket stays nearly parallel from hem through chest; only
        # the short top surface slopes into a narrow integrated collar.  This
        # avoids the old inverted-triangle/superhero torso read.
        profiles = subdivide_torso_profiles((
            (0.805, 0.168, 0.104, 0.017, 0.000),
            (0.895, 0.169, 0.110, 0.014, 0.000),
            (0.970, 0.170, 0.114, 0.010, 0.000),
            (1.115, 0.176, 0.119, 0.002, 0.000),
            (1.285, 0.185, 0.122, -0.006, 0.000),
            (1.360, 0.190, 0.116, -0.010, 0.000),
            (1.427, 0.195, 0.108, -0.012, 0.000),
            # The final ring is the actual open neckline, not a raised tube.
            # Its short shoulder plane meets the lowered head/neck seam.
            (1.477, 0.088, 0.071, -0.014, 0.000),
        ))
        sides = 12
        vertices: list[Vector] = []
        for base_z, radius_x, radius_y, y_offset, lift in profiles:
            for index in range(sides):
                angle = index * math.tau / sides
                vertices.append(
                    self.v(
                        math.cos(angle) * radius_x,
                        y_offset + math.sin(angle) * radius_y,
                        base_z + lift * (1.0 - abs(math.cos(angle))),
                    )
                )
        faces: list[tuple[int, ...]] = [tuple(reversed(range(sides)))]
        for ring_index in range(len(profiles) - 1):
            lower = ring_index * sides
            upper = (ring_index + 1) * sides
            for side_index in range(sides):
                following = (side_index + 1) % sides
                faces.append((lower + side_index, lower + following, upper + following, upper + side_index))
        # The top polygon closes the low-poly shell; the broader neck volume
        # intersects its centre and leaves the visible portion reading as the
        # collar's top facing rather than a separate detail mesh.
        last = (len(profiles) - 1) * sides
        faces.append(tuple(last + index for index in range(sides)))
        jacket = self.add_part(
            "CLO_JacketBody",
            (vertices, faces),
            "JacketAtlas", "clothing", "chest", "Body", "clothing",
        )
        jacket["bp_hem_half_width_m"] = 0.168
        jacket["bp_waist_half_width_m"] = 0.169
        jacket["bp_chest_half_width_m"] = 0.190
        jacket["bp_yoke_half_width_m"] = 0.195
        jacket["bp_shoulder_slope_height_m"] = 0.025
        jacket["bp_yoke_rise_m"] = 0.050
        assign_jacket_body_uv(jacket)
        self.skin_torso(jacket)

        for side in ("L", "R"):
            anatomical = "Left" if side == "L" else "Right"
            shoulder = p[f"shoulder.{side}"]
            elbow = p[f"elbow.{side}"]
            arm_axis = (elbow - shoulder).normalized()
            # Start 8 mm back along the arm axis so the jacket overlaps the
            # anatomical shoulder cap.  This prevents skin wedges while the
            # sloped ring still avoids a separate shoulder-pad volume.
            sleeve_start = shoulder - arm_axis * self.d(0.008)
            sleeve_end = elbow.lerp(p[f"wrist.{side}"], 0.02)
            sleeve = self.add_part(
                f"CLO_JacketSleeve.{side}",
                make_profiled_segment_geometry(
                    sleeve_start,
                    sleeve_end,
                    (
                        (0.0, self.d(0.032), 0.82),
                        (0.14, self.d(0.058), 0.86),
                        (0.30, self.d(0.061), 0.87),
                        (0.70, self.d(0.057), 0.86),
                        (1.0, self.d(0.053), 0.84),
                    ),
                    sides=10,
                ),
                "JacketAtlas",
                "clothing",
                f"upper_arm.{side}",
                f"{anatomical}UpperArm",
                "clothing",
                anatomical,
            )
            assign_ring_strip_uv(sleeve, f"JacketSleeve{anatomical}", 10, 5)
            sleeve["bp_sleeve_coverage"] = "shoulder_to_elbow"
            sleeve["bp_shoulder_overlap_m"] = 0.008

        elbow = p["elbow.R"]
        wrist = p["wrist.R"]
        forearm = self.add_part(
            "CLO_JacketForearm.R",
            make_profiled_segment_geometry(
                elbow,
                wrist,
                (
                    (0.0, self.d(0.052), 0.86),
                    (0.52, self.d(0.045), 0.85),
                    (1.0, self.d(0.036), 0.83),
                ),
                sides=10,
            ),
            "JacketAtlas",
            "clothing",
            "forearm.R",
            "RightLowerArm",
            "clothing",
            "Right",
        )
        assign_ring_strip_uv(forearm, "JacketForearmRight", 10, 3)
        forearm["bp_sleeve_coverage"] = "elbow_to_wrist"

    def build_asymmetric_details(self) -> None:
        # The left forearm keeps one near-flush silhouette shell. All five
        # wraps are pigment in BandageLeft, not stacked torus-like meshes.
        elbow = self.points["elbow.L"]
        wrist = self.points["wrist.L"]
        start = elbow
        end = wrist
        bandage = self.add_part(
            "CLO_Bandage.L",
            make_profiled_segment_geometry(
                start,
                end,
                (
                    (0.0, self.d(0.052), 0.86),
                    (0.52, self.d(0.044), 0.85),
                    (1.0, self.d(0.036), 0.84),
                ),
                sides=10,
            ),
            "BandageAtlas",
            "clothing",
            "forearm.L",
            "LeftLowerArm",
            "signature_detail",
            "Left",
        )
        assign_ring_strip_uv(bandage, "BandageLeft", 10, 3)
        bandage["bp_sleeve_coverage"] = "elbow_to_wrist"

    def _build_face_surface(self) -> None:
        # A curved UV patch follows the head's front planes; it is not a flat
        # billboard. A 0.8 mm offset avoids z-fighting while preserving profile.
        rows = (
            (1.490, 0.030, 0.036),
            (1.512, 0.049, 0.054),
            (1.542, 0.068, 0.066),
            (1.582, 0.081, 0.082),
            (1.624, 0.086, 0.088),
            (1.664, 0.080, 0.082),
            (1.694, 0.055, 0.062),
        )
        columns = (-0.92, -0.61, -0.30, 0.0, 0.30, 0.61, 0.92)
        vertices: list[Vector] = []
        uv_by_vertex: list[tuple[float, float]] = []
        for row_index, (z, radius_x, depth) in enumerate(rows):
            for column_index, normalized_x in enumerate(columns):
                x = normalized_x * radius_x
                curve = 1.0 - 0.34 * normalized_x * normalized_x
                # The central vertices form the bridge/tip as part of this
                # same skinned surface. There is no separate stuck-on nose.
                nose_projection = 0.0
                if row_index == 3:
                    nose_projection = 0.019 * max(0.0, 1.0 - abs(normalized_x) * 1.7)
                elif row_index == 4:
                    nose_projection = 0.010 * max(0.0, 1.0 - abs(normalized_x) * 1.8)
                y = -0.032 - depth * curve - nose_projection - 0.0008
                vertices.append(self.v(x, y, z))
                uv_by_vertex.append(
                    (
                        column_index / (len(columns) - 1),
                        row_index / (len(rows) - 1),
                    )
                )
        faces: list[tuple[int, ...]] = []
        width = len(columns)
        for row_index in range(len(rows) - 1):
            for column_index in range(width - 1):
                lower = row_index * width + column_index
                upper = (row_index + 1) * width + column_index
                faces.append((lower, lower + 1, upper + 1, upper))
        face = self.add_part(
            "GEO_FaceSurface",
            (vertices, faces),
            "FaceAtlas", "details", "head", "Body", "facial_atlas",
            origin=self.bone_heads["head"],
        )
        uv_layer = face.data.uv_layers.new(name="UVMap")
        for loop in face.data.loops:
            uv_layer.data[loop.index].uv = uv_by_vertex[loop.vertex_index]
        preview_uv_layer = face.data.uv_layers.new(name="UVNeutral")
        for loop in face.data.loops:
            u, v = uv_by_vertex[loop.vertex_index]
            preview_uv_layer.data[loop.index].uv = (u * 0.25, v * 0.25 + 0.75)
        face.data.uv_layers.active = uv_layer
        uv_layer.active_render = True
        face["bp_face_atlas_renderer"] = True
        face["bp_uv_contract"] = "local_0_1_runtime_cell_scale_offset"

    def build_face_and_hair(self) -> None:
        self._build_face_surface()
        for side, sign in (("L", 1.0), ("R", -1.0)):
            anatomical = "Left" if side == "L" else "Right"
            self.add_part(
                f"GEO_Ear.{side}",
                common.make_ellipsoid_geometry(
                    self.v(sign * 0.090, -0.020, 1.615),
                    self.v(0.013, 0.011, 0.030), 6, 3,
                ),
                "SkinShadow", "details", "head", "Body", "body_detail", anatomical,
            )
        hair_profiles = (
            (1.630, 0.089, 0.093, -0.005),
            (1.662, 0.090, 0.092, -0.005),
            (1.694, 0.084, 0.085, -0.004),
            (1.724, 0.062, 0.061, -0.002),
            # A forward-biased crown ring keeps the scalp fully covered from
            # the slightly elevated gameplay/portrait cameras.
            (1.742, 0.028, 0.036, -0.010),
        )
        hair_sides = 12
        hair_vertices: list[Vector] = []
        for z, radius_x, radius_y, y_offset in hair_profiles:
            for index in range(hair_sides):
                angle = index * math.tau / hair_sides
                hair_vertices.append(
                    self.v(
                        math.cos(angle) * radius_x,
                        -0.018 + y_offset + math.sin(angle) * radius_y,
                        z,
                    )
                )
        bottom_index = len(hair_vertices)
        hair_vertices.append(self.v(0.0, -0.023, 1.630))
        top_index = len(hair_vertices)
        hair_vertices.append(self.v(-0.006, -0.038, 1.750))
        hair_faces: list[tuple[int, ...]] = []
        for ring_index in range(len(hair_profiles) - 1):
            lower = ring_index * hair_sides
            upper = (ring_index + 1) * hair_sides
            for side_index in range(hair_sides):
                following = (side_index + 1) % hair_sides
                hair_faces.append((lower + side_index, lower + following, upper + following, upper + side_index))
        last_ring = (len(hair_profiles) - 1) * hair_sides
        for side_index in range(hair_sides):
            following = (side_index + 1) % hair_sides
            hair_faces.append((bottom_index, following, side_index))
            hair_faces.append((top_index, last_ring + side_index, last_ring + following))
        self.add_part(
            "GEO_HairCap",
            (hair_vertices, hair_faces),
            "Hair", "details", "head", "Body", "hair",
        )
        self.add_part(
            "GEO_HairBack",
            common.make_ellipsoid_geometry(
                self.v(0, 0.036, 1.655),
                self.v(0.084, 0.052, 0.073),
                10,
                4,
            ),
            "Hair", "details", "head", "Body", "hair",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"GEO_HairTemple.{side}",
                common.make_ellipsoid_geometry(
                    self.v(sign * 0.074, -0.004, 1.655),
                    self.v(0.014, 0.025, 0.058),
                    8,
                    3,
                ),
                "Hair", "details", "head", "Body", "hair",
                "Left" if side == "L" else "Right",
            )
        tuft_specs = (
            ((-0.052, -0.078, 1.704), (-0.063, -0.112, 1.658), 0.021),
            ((-0.009, -0.088, 1.714), (-0.016, -0.124, 1.660), 0.027),
            ((0.035, -0.083, 1.706), (0.047, -0.116, 1.650), 0.026),
            ((0.071, -0.034, 1.675), (0.075, -0.057, 1.638), 0.014),
        )
        for index, (base_tuple, tip_tuple, radius) in enumerate(tuft_specs, 1):
            jitter = self.v(
                self.rng.uniform(-0.003, 0.003),
                self.rng.uniform(-0.002, 0.002),
                self.rng.uniform(-0.0015, 0.0015),
            )
            base_point = self.v(*base_tuple) + jitter
            tip = self.v(*tip_tuple) + jitter * 0.35
            tip.z = min(tip.z, self.d(1.75))
            self.add_part(
                f"GEO_HairTuft.{index:02d}",
                common.make_frustum_between(base_point, tip, self.d(radius), self.d(radius * 0.52), 6, 0.74, 0.0),
                "HairHighlight" if index == 2 else "Hair",
                "details", "head", "Body", "hair",
            )

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-player-3d-model-v2.py"
        scene["bp_generator_version"] = V2_GENERATOR_VERSION
        scene["bp_character_height_m"] = self.config.height
        scene["bp_pose"] = self.config.pose
        scene["bp_seed"] = self.config.seed
        scene["bp_runtime_integrated"] = True
        scene["bp_design_version"] = "HeroV2"
        scene["bp_design_source"] = "ai/player-art-spec.md + city story/art bibles"
        scene["bp_lineage_reference"] = "ArtSource/Player/PlayerDirectionalTurntable.png"
        scene["bp_face_atlas"] = str(self.face_atlas_path.relative_to(REPO_ROOT)).replace("\\", "/")
        scene["bp_clothing_atlas"] = str(self.clothing_atlas_path.relative_to(REPO_ROOT)).replace("\\", "/")


read_generated_png = atlas_kit.read_generated_png
png_pixel_bottom_left = atlas_kit.png_pixel_bottom_left


def count_region_color(
    pixels: bytes,
    width: int,
    height: int,
    region_name: str,
    colors: set[tuple[int, int, int, int]],
) -> int:
    x, y, region_width, region_height = clothing_region(region_name)
    return sum(
        png_pixel_bottom_left(pixels, width, height, px, py) in colors
        for py in range(y, y + region_height)
        for px in range(x, x + region_width)
    )


def measure_visual_neck_height(records: dict[str, object]) -> float:
    """Measure neckline-to-jaw attachment instead of chin-tip clearance."""

    head = records["GEO_Head"].obj
    neck = records["GEO_Neck"].obj
    _, jacket_max = common.mesh_bounds_world(records["CLO_JacketBody"].obj)
    target_half_width = float(neck.get("bp_top_width_m", 0.124)) * 0.5
    rings: dict[float, float] = {}
    for vertex in head.data.vertices:
        point = head.matrix_world @ vertex.co
        key = round(point.z, 6)
        rings[key] = max(rings.get(key, 0.0), abs(point.x))
    ordered = sorted(rings.items())
    for (lower_z, lower_radius), (upper_z, upper_radius) in zip(ordered, ordered[1:]):
        if lower_radius <= target_half_width <= upper_radius and upper_radius > lower_radius:
            fraction = (target_half_width - lower_radius) / (upper_radius - lower_radius)
            jaw_attachment_z = lower_z + (upper_z - lower_z) * fraction
            return jaw_attachment_z - jacket_max.z
    raise RuntimeError("Unable to interpolate Hero V2 jaw-to-neck attachment")


def measure_ring_width(obj: bpy.types.Object, sides: int, ring_index: int) -> float:
    """Return the world-X silhouette width of one authored profile ring."""

    start = ring_index * sides
    points = [
        obj.matrix_world @ obj.data.vertices[index].co
        for index in range(start, start + sides)
    ]
    return max(point.x for point in points) - min(point.x for point in points)


def measure_relaxed_arm_landmarks(result: common.BuildResult) -> dict[str, float]:
    """Evaluate Relaxed without changing the bind pose left for export."""

    scene = bpy.context.scene
    animation_data = result.rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = scene.frame_current
    values: dict[str, float] = {}
    try:
        animation_data.action = result.actions["Relaxed"].action
        scene.frame_set(0)
        bpy.context.view_layer.update()
        for side in ("L", "R"):
            hand = result.rig.pose.bones[f"hand.{side}"]
            wrist = result.rig.matrix_world @ hand.head
            fingertip = result.rig.matrix_world @ hand.tail
            values[f"wrist_{side.lower()}_z_m"] = wrist.z
            values[f"fingertip_{side.lower()}_z_m"] = fingertip.z
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        if previous_action is None:
            for pose_bone in result.rig.pose.bones:
                pose_bone.rotation_mode = "QUATERNION"
                pose_bone.location = (0.0, 0.0, 0.0)
                pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
                pose_bone.scale = (1.0, 1.0, 1.0)
        bpy.context.view_layer.update()
    return values


def validate_v2_result(
    config: common.BuildConfig,
    result: common.BuildResult,
    face_atlas_path: Path,
    clothing_atlas_path: Path,
) -> common.ValidationReport:
    bpy.context.view_layer.update()
    errors: list[str] = []
    common.validate_bed_support_contract(result, errors)
    common.validate_bed_sleep_pose(result, errors)
    # The shared fall, lie and rise poses predate the current proportions.
    # Their landmark contacts (hands and knees on the floor at all fours,
    # the low crouch's boots) float on this rig — known debt the runtime's hand
    # and boot IK hides. What is held here is what no rig may do: pass a
    # limb through the floor on any frame, or bend a knee or an elbow the
    # wrong way.
    common.validate_fall_recovery_dense(result, errors)
    records = {record.obj.name: record for record in result.parts}
    if len(records) != len(result.parts):
        errors.append("Export mesh names are not unique")
    for required in (*common.REQUIRED_BODY_OBJECTS, "GEO_FaceSurface"):
        if required not in records:
            errors.append(f"Missing required Hero V2 part {required}")
    forbidden_exact = {
        "CLO_ShirtFront", "CLO_ShoulderCap.L", "CLO_ShoulderCap.R",
        "CLO_JacketCuff.L", "CLO_JacketCuff.R", "CLO_JacketPanel.L",
        "CLO_JacketPanel.R", "ACC_JacketPocket.L", "ACC_JacketPocket.R",
        "ACC_JacketPocketFlap.L", "ACC_JacketPocketFlap.R",
        "ACC_JeansCuff.L", "ACC_JeansCuff.R", "ACC_BootSole.L",
        "ACC_BootSole.R", "CLO_Lapel.L", "CLO_Lapel.R",
        "CLO_CollarBack", "CLO_CollarFront.L", "CLO_CollarFront.R",
        "ACC_ShoulderPatch.R",
    }
    forbidden_found = sorted(forbidden_exact.intersection(records))
    forbidden_found.extend(sorted(name for name in records if name.startswith("ACC_BandageWrap.")))
    if forbidden_found:
        errors.append(f"Decorative clothing geometry must be atlas-painted, found {forbidden_found}")

    expected_atlas_material = {
        "CLO_JacketBody": "MAT_JacketAtlas",
        "CLO_JacketSleeve.L": "MAT_JacketAtlas",
        "CLO_JacketSleeve.R": "MAT_JacketAtlas",
        "CLO_JacketForearm.R": "MAT_JacketAtlas",
        "CLO_Bandage.L": "MAT_BandageAtlas",
        "GEO_Pelvis": "MAT_JeansAtlas",
        "GEO_Thigh.L": "MAT_JeansAtlas",
        "GEO_Thigh.R": "MAT_JeansAtlas",
        "GEO_Shin.L": "MAT_JeansAtlas",
        "GEO_Shin.R": "MAT_JeansAtlas",
        "GEO_Foot.L": "MAT_JeansAtlas",
        "GEO_Foot.R": "MAT_JeansAtlas",
    }
    if set(CLOTHING_RENDERER_REGIONS) != set(expected_atlas_material):
        errors.append("Clothing region table does not match the exact atlas renderer contract")

    expected_bones = set((*common.REQUIRED_BONES, *common.REQUIRED_FACE_BONES, *common.REQUIRED_SOCKET_BONES))
    actual_bones = {bone.name for bone in result.rig.data.bones}
    if actual_bones != expected_bones:
        errors.append(
            f"Hero V2 bones differ from 31-bone contract: missing={sorted(expected_bones-actual_bones)}, extra={sorted(actual_bones-expected_bones)}"
        )
    if set(result.actions) != set(V2_REQUIRED_ACTIONS):
        missing_actions = sorted(set(V2_REQUIRED_ACTIONS) - set(result.actions))
        extra_actions = sorted(set(result.actions) - set(V2_REQUIRED_ACTIONS))
        errors.append(
            "Hero V2 must export exactly its 38-Action contract: "
            f"missing={missing_actions}, extra={extra_actions}"
        )
    for name, record in result.actions.items():
        curves = list(common.iter_action_fcurves(record.action))
        if not curves:
            errors.append(f"Action {name} has no bone curves")
        if any(not curve.data_path.startswith('pose.bones["') for curve in curves):
            errors.append(f"Action {name} contains a non-bone curve")
        if bool(record.action.get("bp_root_motion", True)):
            errors.append(f"Action {name} enables root motion")
        if record.loop and any(
            abs(curve.evaluate(record.action.frame_start) - curve.evaluate(record.action.frame_end)) > 1e-4
            for curve in curves
        ):
            errors.append(f"Loop Action {name} does not close")

    run_record = result.actions.get(RUN_ACTION_NAME)
    if run_record is not None:
        run_action = run_record.action
        run_curves = list(common.iter_action_fcurves(run_action))
        if (
            run_record.category != "locomotion"
            or not run_record.loop
            or abs(run_record.duration_seconds - RUN_DURATION_SECONDS) > 1e-6
            or run_record.source_frame_count != RUN_SOURCE_FRAME_COUNT
            or abs(run_record.source_fps - RUN_SOURCE_FPS) > 1e-6
            or abs(run_action.frame_start) > 1e-6
            or abs(run_action.frame_end - RUN_SOURCE_FRAME_COUNT) > 1e-6
        ):
            errors.append(
                "Run must be a looping locomotion Action authored as "
                "18 source frames / 0.75 s at 24 FPS"
            )
        if any(
            keyframe.interpolation != "BEZIER"
            for curve in run_curves
            for keyframe in curve.keyframe_points
        ):
            errors.append("Run must use auto-clamped Bezier interpolation")
        root_location_curves = [
            curve
            for curve in run_curves
            if curve.data_path == 'pose.bones["root"].location'
        ]
        if len(root_location_curves) != 3:
            errors.append("Run must key all three fixed root-bone axes")
        if any(
            abs(keyframe.co.y) > 1e-6
            for curve in root_location_curves
            for keyframe in curve.keyframe_points
        ):
            errors.append("Run must keep the root bone fixed in place")
        pelvis_height_curve = next(
            (
                curve
                for curve in run_curves
                if curve.data_path == 'pose.bones["pelvis"].location'
                and curve.array_index == 2
            ),
            None,
        )
        expected_landmark_frames = {0, 2, 4, 7, 9, 11, 14, 16, 18}
        if pelvis_height_curve is None:
            errors.append("Run must author pelvis height through all gait phases")
        else:
            pelvis_height_keys = {
                round(keyframe.co.x): keyframe.co.y
                for keyframe in pelvis_height_curve.keyframe_points
            }
            if set(pelvis_height_keys) != expected_landmark_frames:
                errors.append(
                    "Run pelvis keys must retain the exact "
                    "contact/down/pass/up landmark frames"
                )
            elif min(
                pelvis_height_keys[frame]
                for frame in (7, 16)
            ) < 0.055:
                errors.append(
                    "Run flight landmarks must lift the pelvis by at least "
                    "0.055 m"
                )
        if int(run_action.get("bp_event_count", -1)) != 0:
            errors.append("Run must not declare Animation Events")
        if run_action.get("bp_gait_style") != "heavy_weary":
            errors.append("Run must retain the heavy-weary gait contract")
        if int(run_action.get("bp_landmark_count", 0)) != 8:
            errors.append("Run must retain eight contact/down/pass/up landmarks")
        if not bool(run_action.get("bp_short_flight", False)):
            errors.append("Run must retain its short authored flight phase")

    all_minima: list[Vector] = []
    all_maxima: list[Vector] = []
    triangle_count = 0
    for record in result.parts:
        obj = record.obj
        mesh = obj.data
        if obj.type != "MESH" or not mesh.vertices or not mesh.polygons:
            errors.append(f"{obj.name} is not a non-empty mesh")
            continue
        if obj.parent is not result.rig:
            errors.append(f"{obj.name} is not parented to the Hero V2 rig")
        if len(mesh.materials) != 1 or mesh.materials[0] is None:
            errors.append(f"{obj.name} must use exactly one material")
        expected_material = expected_atlas_material.get(obj.name)
        if expected_material is not None:
            actual_material = mesh.materials[0].name if mesh.materials else ""
            if actual_material != expected_material:
                errors.append(f"{obj.name} uses {actual_material}, expected {expected_material}")
            region_name, x, y, width, height = CLOTHING_RENDERER_REGIONS[obj.name]
            if obj.get("bp_clothing_atlas_region") != region_name:
                errors.append(f"{obj.name} lost atlas region metadata {region_name}")
            uv_layer = mesh.uv_layers.get("UVMap")
            if uv_layer is None or not uv_layer.data:
                errors.append(f"{obj.name} has no clothing atlas UV0")
            else:
                inset_min_u = (x + 1) / CLOTHING_ATLAS_SIZE
                inset_min_v = (y + 1) / CLOTHING_ATLAS_SIZE
                inset_max_u = (x + width - 1) / CLOTHING_ATLAS_SIZE
                inset_max_v = (y + height - 1) / CLOTHING_ATLAS_SIZE
                values = [loop.uv for loop in uv_layer.data]
                if (
                    min(value.x for value in values) < inset_min_u - 1e-6
                    or max(value.x for value in values) > inset_max_u + 1e-6
                    or min(value.y for value in values) < inset_min_v - 1e-6
                    or max(value.y for value in values) > inset_max_v + 1e-6
                ):
                    errors.append(f"{obj.name} UV0 escapes the 1px inset of {region_name}")
        armature_modifiers = [modifier for modifier in obj.modifiers if modifier.type == "ARMATURE"]
        if len(armature_modifiers) != 1 or armature_modifiers[0].object is not result.rig:
            errors.append(f"{obj.name} needs one Hero V2 armature modifier")
        group = obj.vertex_groups.get(record.bone)
        if obj.name in TORSO_SKIN_MESHES:
            groups = {group.index: group.name for group in obj.vertex_groups}
            used = set()
            blended = 0
            for vertex in mesh.vertices:
                actual = {groups[item.group]: item.weight for item in vertex.groups}
                height = (obj.matrix_local @ vertex.co).z / (config.height / 1.75)
                expected = torso_weights(height)
                if (set(actual) != set(expected) or
                        any(abs(actual[name] - weight) > 1e-5
                            for name, weight in expected.items())):
                    errors.append(f"{obj.name} vertex {vertex.index} lost its torso skin weights")
                    break
                used.update(actual)
                blended += len(actual) == 2
            if used != set(TORSO_SKIN_BONES) or blended < 20:
                errors.append(f"{obj.name} must articulate all three torso regions with blended rings")
        elif group is None:
            errors.append(f"{obj.name} has no rigid {record.bone} group")
        else:
            for vertex in mesh.vertices:
                assignments = [assignment for assignment in vertex.groups if assignment.group == group.index]
                if len(assignments) != 1 or abs(assignments[0].weight - 1.0) > 1e-5:
                    errors.append(f"{obj.name} vertex {vertex.index} lost its rigid weight")
                    break
        if obj.name != "GEO_FaceSurface":
            try:
                common.validate_manifold(obj)
            except RuntimeError as error:
                errors.append(str(error))
        mesh.calc_loop_triangles()
        triangle_count += len(mesh.loop_triangles)
        minimum, maximum = common.mesh_bounds_world(obj)
        all_minima.append(minimum)
        all_maxima.append(maximum)

    if triangle_count > common.MAX_TRIANGLES:
        errors.append(f"Triangle budget exceeded: {triangle_count} > {common.MAX_TRIANGLES}")
    bounds_min = Vector((min(v.x for v in all_minima), min(v.y for v in all_minima), min(v.z for v in all_minima)))
    bounds_max = Vector((max(v.x for v in all_maxima), max(v.y for v in all_maxima), max(v.z for v in all_maxima)))
    measured_height = bounds_max.z - bounds_min.z
    if abs(bounds_min.z) > config.height * 0.012:
        errors.append(f"Feet miss Z=0 by {bounds_min.z:.4f} m")
    if abs(measured_height - config.height) > config.height * 0.010:
        errors.append(f"Visible height is {measured_height:.4f} m, expected {config.height:.4f} m")

    head_min, head_max = common.mesh_bounds_world(records["GEO_Head"].obj)
    head_height = head_max.z - head_min.z
    head_ratio = measured_height / head_height
    head_width = head_max.x - head_min.x
    if not 0.233 <= head_height <= 0.240:
        errors.append(f"Adult head height must be 0.233-0.240 m, got {head_height:.4f}")
    if not 7.0 <= head_ratio <= 7.5:
        errors.append(f"Adult head ratio must be 7.0-7.5, got {head_ratio:.3f}")
    if not 0.174 <= head_width <= 0.178:
        errors.append(f"Adult head width must be 0.174-0.178 m, got {head_width:.3f}")
    shoulder = result.rig.data.bones["upper_arm.L"].head_local
    opposite_shoulder = result.rig.data.bones["upper_arm.R"].head_local
    shoulder_span = (shoulder - opposite_shoulder).length
    if not 0.410 <= shoulder_span <= 0.430:
        errors.append(f"Adult shoulder joint span must be 0.410-0.430 m, got {shoulder_span:.3f}")
    shoulder_head_ratio = shoulder_span / head_width
    if not 2.30 <= shoulder_head_ratio <= 2.50:
        errors.append(f"Shoulder/head-width ratio must be 2.30-2.50, got {shoulder_head_ratio:.3f}")
    wrist = result.rig.data.bones["forearm.L"].tail_local
    arm_ratio = (wrist - shoulder).length / config.height
    if not 0.25 <= arm_ratio <= 0.34:
        errors.append(f"Arm proportion is outside adult non-uncanny range: {arm_ratio:.3f}")
    neck_min, neck_max = common.mesh_bounds_world(records["GEO_Neck"].obj)
    jacket_min, jacket_max = common.mesh_bounds_world(records["CLO_JacketBody"].obj)
    jacket_obj = records["CLO_JacketBody"].obj
    visible_neck = measure_visual_neck_height(records)
    neck_base_width = neck_max.x - neck_min.x
    neck_top_width = float(records["GEO_Neck"].obj.get("bp_top_width_m", 0.0))
    if not 0.040 <= visible_neck <= 0.050:
        errors.append(f"Visible neckline-to-jaw height must be 0.040-0.050 m, got {visible_neck:.3f}")
    if not 0.145 <= neck_base_width <= 0.150:
        errors.append(f"Neck/trapezius base must be 0.145-0.150 m, got {neck_base_width:.3f}")
    if not 0.125 <= neck_top_width <= 0.130:
        errors.append(f"Neck top width must be 0.125-0.130 m, got {neck_top_width:.3f}")
    if not 0.165 <= jacket_obj.get("bp_hem_half_width_m", 0.0) <= 0.172:
        errors.append("Field-jacket hem half-width must stay boxy at 0.165-0.172 m")
    if not 0.165 <= jacket_obj.get("bp_waist_half_width_m", 0.0) <= 0.172:
        errors.append("Field-jacket waist half-width must stay within 0.165-0.172 m")
    if not 0.180 <= jacket_obj.get("bp_chest_half_width_m", 0.0) <= 0.190:
        errors.append("Field-jacket chest half-width must stay within 0.180-0.190 m")
    if not 0.190 <= jacket_obj.get("bp_yoke_half_width_m", 0.0) <= 0.198:
        errors.append("Field-jacket yoke half-width must stay within 0.190-0.198 m")
    if jacket_obj.get("bp_yoke_half_width_m", 0.0) - jacket_obj.get("bp_hem_half_width_m", 0.0) > 0.035:
        errors.append("Field-jacket sides must remain near-parallel, not inverted-triangular")
    if not 0.020 <= jacket_obj.get("bp_shoulder_slope_height_m", 0.0) <= 0.035:
        errors.append("Field-jacket shoulder slope must remain a restrained 20-35 mm")
    if not 0.045 <= jacket_obj.get("bp_yoke_rise_m", 0.0) <= 0.060:
        errors.append("Open neckline must use one short yoke plane, not a raised collar tube")

    torso_obj = records["GEO_Torso"].obj
    hip_left = result.rig.data.bones["thigh.L"].head_local
    hip_right = result.rig.data.bones["thigh.R"].head_local
    torso_length = ((shoulder.z + opposite_shoulder.z) - (hip_left.z + hip_right.z)) * 0.5
    hip_joint_span = (hip_left - hip_right).length
    if not 0.535 <= torso_length <= 0.550:
        errors.append(f"Shoulder-to-hip torso length must be 0.535-0.550 m, got {torso_length:.3f}")
    if not 0.180 <= hip_joint_span <= 0.190:
        errors.append(f"Pelvis joint span must be 0.180-0.190 m, got {hip_joint_span:.3f}")
    if not 0.165 <= torso_obj.get("bp_waist_half_width_m", 0.0) <= 0.172:
        errors.append("Underlying torso waist half-width must be 0.165-0.172 m")
    if not 0.180 <= torso_obj.get("bp_chest_half_width_m", 0.0) <= 0.190:
        errors.append("Underlying ribcage half-width must be 0.180-0.190 m")

    relaxed_landmarks = measure_relaxed_arm_landmarks(result)
    for side in ("l", "r"):
        wrist_z = relaxed_landmarks[f"wrist_{side}_z_m"]
        fingertip_z = relaxed_landmarks[f"fingertip_{side}_z_m"]
        if not 0.780 <= wrist_z <= 0.900:
            errors.append(f"Relaxed wrist {side.upper()} must reach crotch/upper thigh, got z={wrist_z:.3f}")
        if not 0.660 <= fingertip_z <= 0.790:
            errors.append(f"Relaxed fingertips {side.upper()} must reach mid-thigh, got z={fingertip_z:.3f}")
        if not 0.070 <= wrist_z - fingertip_z <= 0.115:
            errors.append(f"Relaxed hand length landmark is implausible on {side.upper()}")

    hair_records = [record for name, record in records.items() if name.startswith("GEO_Hair")]
    hair_min_x = min(common.mesh_bounds_world(record.obj)[0].x for record in hair_records)
    hair_max_x = max(common.mesh_bounds_world(record.obj)[1].x for record in hair_records)
    if hair_max_x - hair_min_x > head_width + 0.012:
        errors.append("Hair silhouette expands beyond the adult skull envelope")

    for side in ("L", "R"):
        foot_min, foot_max = common.mesh_bounds_world(records[f"GEO_Foot.{side}"].obj)
        foot_length = foot_max.y - foot_min.y
        foot_width = foot_max.x - foot_min.x
        if abs(foot_min.z) > 1e-6:
            errors.append(f"GEO_Foot.{side} must reach ground, got z={foot_min.z:.4f}")
        if not 0.255 <= foot_length <= 0.270:
            errors.append(f"GEO_Foot.{side} length must be 0.255-0.270 m: {foot_length:.3f} m")
        if not 0.115 <= foot_width <= 0.135:
            errors.append(f"GEO_Foot.{side} width is not adult: {foot_width:.3f} m")
        thigh_obj = records[f"GEO_Thigh.{side}"].obj
        shin_obj = records[f"GEO_Shin.{side}"].obj
        upper_thigh_width = measure_ring_width(thigh_obj, 8, 1)
        knee_width = max(
            measure_ring_width(thigh_obj, 8, 3),
            measure_ring_width(shin_obj, 8, 0),
        )
        calf_width = measure_ring_width(shin_obj, 8, 2)
        ankle_width = measure_ring_width(shin_obj, 8, 4)
        if not 0.155 <= upper_thigh_width <= 0.175:
            errors.append(f"GEO_Thigh.{side} upper width is implausible: {upper_thigh_width:.3f} m")
        if not 0.100 <= knee_width <= 0.115:
            errors.append(f"GEO_{side} knee width must remain narrow: {knee_width:.3f} m")
        if not 0.125 <= calf_width <= 0.140:
            errors.append(f"GEO_Shin.{side} calf width is implausible: {calf_width:.3f} m")
        if not 0.075 <= ankle_width <= 0.090:
            errors.append(f"GEO_Shin.{side} ankle width is implausible: {ankle_width:.3f} m")
        if calf_width <= ankle_width * 1.45:
            errors.append(f"GEO_Shin.{side} must widen through the calf before tapering")

    face = records.get("GEO_FaceSurface")
    if face is not None:
        uv_layer = face.obj.data.uv_layers.get("UVMap")
        if uv_layer is None:
            errors.append("GEO_FaceSurface has no UVMap")
        else:
            uv_values = [entry.uv for entry in uv_layer.data]
            if not uv_values or min(uv.x for uv in uv_values) < -1e-5 or max(uv.x for uv in uv_values) > 1.00001 or min(uv.y for uv in uv_values) < -1e-5 or max(uv.y for uv in uv_values) > 1.00001:
                errors.append("GEO_FaceSurface UV0 must stay normalized 0..1")
        average_normal_y = sum(polygon.normal.y for polygon in face.obj.data.polygons) / len(face.obj.data.polygons)
        if average_normal_y > -0.65:
            errors.append("GEO_FaceSurface must face source -Y")

    bandage = bpy.data.objects.get("CLO_Bandage.L")
    if bandage is None or common.object_center_world(bandage).x <= 0:
        errors.append("Bandage must remain on physical left (+X)")
    for sleeve_name in ("CLO_JacketSleeve.L", "CLO_JacketSleeve.R"):
        sleeve = bpy.data.objects.get(sleeve_name)
        if sleeve is None or sleeve.get("bp_sleeve_coverage") != "shoulder_to_elbow":
            errors.append(f"{sleeve_name} must cover shoulder to elbow")
        elif sleeve.get("bp_shoulder_overlap_m", 0.0) < 0.005:
            errors.append(f"{sleeve_name} must overlap the anatomical shoulder seam")
    right_forearm = bpy.data.objects.get("CLO_JacketForearm.R")
    if right_forearm is None or right_forearm.get("bp_sleeve_coverage") != "elbow_to_wrist":
        errors.append("CLO_JacketForearm.R must cover elbow to wrist")
    if bandage is not None and bandage.get("bp_sleeve_coverage") != "elbow_to_wrist":
        errors.append("CLO_Bandage.L must cover elbow to wrist without bare gaps")
    if not face_atlas_path.is_file() or face_atlas_path.stat().st_size < 256:
        errors.append("Hero V2 face atlas was not generated")
    if not clothing_atlas_path.is_file() or clothing_atlas_path.stat().st_size < 256:
        errors.append("Hero V2 clothing atlas was not generated")
    else:
        width, height, pixels = read_generated_png(clothing_atlas_path)
        if (width, height) != (CLOTHING_ATLAS_SIZE, CLOTHING_ATLAS_SIZE):
            errors.append(f"Clothing atlas must be 256x256, got {width}x{height}")
        else:
            patch_color = rgba_from_hex(V2_PALETTE_HEX["Patch"])
            wrap_color = rgba_from_hex(V2_PALETTE_HEX["BandageDark"])
            shirt_color = rgba_from_hex(V2_PALETTE_HEX["Shirt"])
            jacket_dark = rgba_from_hex(V2_PALETTE_HEX["JacketDark"])
            boot_color = rgba_from_hex(V2_PALETTE_HEX["BootLeather"])
            if count_region_color(pixels, width, height, "JacketSleeveRight", {patch_color}) < 100:
                errors.append("Ochre patch pixels are missing from physical-right sleeve region")
            if count_region_color(pixels, width, height, "JacketSleeveLeft", {patch_color}) != 0:
                errors.append("Ochre patch pixels leaked onto physical-left sleeve region")
            if count_region_color(pixels, width, height, "BandageLeft", {wrap_color}) < 250:
                errors.append("Bandage wrap lines are missing from the single left sleeve region")
            if count_region_color(pixels, width, height, "JacketForearmRight", {jacket_dark}) < 150:
                errors.append("Right jacket forearm needs painted cuff and fold pixels")
            if count_region_color(pixels, width, height, "JacketBody", {shirt_color}) < 500:
                errors.append("Open jacket front must reveal a readable charcoal shirt")
            for shin_region in ("JeansShinLeft", "JeansShinRight"):
                if count_region_color(pixels, width, height, shin_region, {boot_color}) < 650:
                    errors.append(f"{shin_region} needs a painted military boot shaft at the ankle")

    if errors:
        raise RuntimeError("Hero V2 validation failed:\n" + "\n".join(f"  - {error}" for error in errors))
    return common.ValidationReport(
        object_count=len(result.export_objects),
        mesh_count=len(result.parts),
        triangle_count=triangle_count,
        action_count=len(result.actions),
        socket_count=len(common.REQUIRED_SOCKET_BONES),
        bounds_min=tuple(round(value, 6) for value in bounds_min),
        bounds_max=tuple(round(value, 6) for value in bounds_max),
    )


def stable_number(value: float) -> float:
    """Quantize authored numeric data before hashing it."""

    rounded = round(float(value), 6)
    return 0.0 if rounded == 0.0 else rounded


def stable_vector(values: Sequence[float]) -> list[float]:
    return [stable_number(value) for value in values]


def content_signature(
    config: common.BuildConfig,
    result: common.BuildResult,
    face_atlas_sha256: str,
    clothing_atlas_sha256: str,
) -> str:
    """Hash authored model/rig/action content, independent of FBX timestamps.

    Blender's binary writers may include session-specific bookkeeping.  This
    signature instead covers the data that determines the imported character:
    mesh topology and UV0, skin weights, object transforms, skeleton and all
    bone animation keys.  Two clean generator runs must therefore publish the
    same value even when the container file bytes differ.
    """

    part_records: list[dict] = []
    for record in sorted(result.parts, key=lambda item: item.obj.name):
        obj = record.obj
        mesh = obj.data
        group_names = {
            group.index: group.name
            for group in obj.vertex_groups
        }
        uv_layer = mesh.uv_layers.get("UVMap")
        part_records.append(
            {
                "name": obj.name,
                "role": record.role,
                "bone": record.bone,
                "sprite_part": record.sprite_part,
                "side": record.side,
                "material": mesh.materials[0].name,
                "matrix_local": [
                    stable_number(value)
                    for row in obj.matrix_local
                    for value in row
                ],
                "vertices": [
                    stable_vector(vertex.co)
                    for vertex in mesh.vertices
                ],
                "faces": [
                    {
                        "vertices": list(polygon.vertices),
                        "material_index": polygon.material_index,
                        "smooth": polygon.use_smooth,
                    }
                    for polygon in mesh.polygons
                ],
                "uv0": (
                    [stable_vector(loop.uv) for loop in uv_layer.data]
                    if uv_layer is not None
                    else []
                ),
                "weights": [
                    [
                        {
                            "group": group_names[assignment.group],
                            "weight": stable_number(assignment.weight),
                        }
                        for assignment in sorted(
                            vertex.groups,
                            key=lambda item: group_names[item.group],
                        )
                    ]
                    for vertex in mesh.vertices
                ],
            }
        )

    bone_records = []
    for bone in sorted(result.rig.data.bones, key=lambda item: item.name):
        bone_records.append(
            {
                "name": bone.name,
                "parent": bone.parent.name if bone.parent is not None else None,
                "connected": bone.use_connect,
                "deform": bone.use_deform,
                "head": stable_vector(bone.head_local),
                "tail": stable_vector(bone.tail_local),
                "matrix_local": [
                    stable_number(value)
                    for row in bone.matrix_local
                    for value in row
                ],
            }
        )

    action_records = []
    for name, record in sorted(result.actions.items()):
        curves = []
        for curve in sorted(
            common.iter_action_fcurves(record.action),
            key=lambda item: (item.data_path, item.array_index),
        ):
            curves.append(
                {
                    "data_path": curve.data_path,
                    "array_index": curve.array_index,
                    "extrapolation": curve.extrapolation,
                    "keys": [
                        {
                            "co": stable_vector(keyframe.co),
                            "interpolation": keyframe.interpolation,
                        }
                        for keyframe in curve.keyframe_points
                    ],
                }
            )
        action_records.append(
            {
                "name": name,
                "category": record.category,
                "duration_seconds": stable_number(record.duration_seconds),
                "loop": record.loop,
                "source_frame_count": record.source_frame_count,
                "source_fps": stable_number(record.source_fps),
                "frame_start": stable_number(record.action.frame_start),
                "frame_end": stable_number(record.action.frame_end),
                "face_keys": action_face_keys(name),
                "curves": curves,
            }
        )

    payload = {
        "generator": "tools/build-player-3d-model-v2.py",
        "generator_version": V2_GENERATOR_VERSION,
        "design_version": "HeroV2",
        "height_m": stable_number(config.height),
        "seed": config.seed,
        "pose": config.pose,
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "face_atlas_sha256": face_atlas_sha256,
        "clothing_atlas_sha256": clothing_atlas_sha256,
        "palette": dict(sorted(V2_PALETTE_HEX.items())),
        "parts": part_records,
        "bones": bone_records,
        "actions": action_records,
    }
    encoded = json.dumps(
        payload,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def action_face_keys(action_name: str) -> list[dict[str, float | str]] | None:
    single_states = {
        "Face_Neutral": "Neutral",
        "Face_HalfBlink": "HalfBlink",
        "Face_ClosedBlink": "ClosedBlink",
        "Face_Watchful": "Watchful",
        "Face_Tense": "Tense",
        "BedSleepLoop": "ClosedBlink",
        "FallLeft": "Tense",
        "DownLeft": "Tense",
        "FallRight": "Tense",
        "DownRight": "Tense",
    }
    if action_name in single_states:
        return [{"normalized_time": 0.0, "expression": single_states[action_name]}]
    if action_name == "BedEnter":
        return [
            {"normalized_time": 0.0, "expression": "Neutral"},
            {"normalized_time": 0.55, "expression": "HalfBlink"},
            {"normalized_time": 0.72, "expression": "ClosedBlink"},
        ]
    if action_name == "BedExit":
        return [
            {"normalized_time": 0.0, "expression": "ClosedBlink"},
            {"normalized_time": 0.18, "expression": "HalfBlink"},
            {"normalized_time": 0.38, "expression": "Neutral"},
        ]
    if action_name in ("RiseLeft", "RiseRight"):
        return [
            {"normalized_time": 0.0, "expression": "Tense"},
            {"normalized_time": 0.72, "expression": "Watchful"},
            {"normalized_time": 1.0, "expression": "Neutral"},
        ]
    return None


def write_v2_manifest(
    path: Path,
    config: common.BuildConfig,
    result: common.BuildResult,
    report: common.ValidationReport,
    face_atlas_path: Path,
    face_atlas_sha256: str,
    clothing_atlas_path: Path,
    clothing_atlas_sha256: str,
    content_signature_sha256: str,
) -> None:
    # Start with the established model manifest contract, then add only V2 data.
    common.write_manifest(path, config, result, report)
    payload = json.loads(path.read_text(encoding="utf-8"))
    records = {record.obj.name: record for record in result.parts}
    head_min, head_max = common.mesh_bounds_world(records["GEO_Head"].obj)
    head_height = head_max.z - head_min.z
    head_width = head_max.x - head_min.x
    neck_min, neck_max = common.mesh_bounds_world(records["GEO_Neck"].obj)
    visible_neck_height = measure_visual_neck_height(records)
    shoulder_left = result.rig.data.bones["upper_arm.L"].head_local
    shoulder_right = result.rig.data.bones["upper_arm.R"].head_local
    shoulder_joint_span = (shoulder_left - shoulder_right).length
    wrist_left = result.rig.data.bones["forearm.L"].tail_local
    hand_left = result.rig.data.bones["hand.L"]
    hip_left = result.rig.data.bones["thigh.L"].head_local
    hip_right = result.rig.data.bones["thigh.R"].head_local
    torso_length = ((shoulder_left.z + shoulder_right.z) - (hip_left.z + hip_right.z)) * 0.5
    pelvis_joint_span = (hip_left - hip_right).length
    torso_obj = records["GEO_Torso"].obj
    jacket_obj = records["CLO_JacketBody"].obj
    relaxed_landmarks = measure_relaxed_arm_landmarks(result)
    lower_body_metrics: dict[str, float] = {}
    for side, label in (("L", "left"), ("R", "right")):
        foot_min, foot_max = common.mesh_bounds_world(records[f"GEO_Foot.{side}"].obj)
        thigh_obj = records[f"GEO_Thigh.{side}"].obj
        shin_obj = records[f"GEO_Shin.{side}"].obj
        lower_body_metrics.update(
            {
                f"{label}_foot_length_m": round(foot_max.y - foot_min.y, 6),
                f"{label}_foot_width_m": round(foot_max.x - foot_min.x, 6),
                f"{label}_upper_thigh_width_m": round(measure_ring_width(thigh_obj, 8, 1), 6),
                f"{label}_knee_width_m": round(
                    max(
                        measure_ring_width(thigh_obj, 8, 3),
                        measure_ring_width(shin_obj, 8, 0),
                    ),
                    6,
                ),
                f"{label}_calf_width_m": round(measure_ring_width(shin_obj, 8, 2), 6),
                f"{label}_ankle_width_m": round(measure_ring_width(shin_obj, 8, 4), 6),
            }
        )
    payload.update(
        {
            "generator": "tools/build-player-3d-model-v2.py",
            "generator_version": V2_GENERATOR_VERSION,
            "design_version": "HeroV2",
            "design_source": "ai/player-art-spec.md + ai/city-story-bible.md + ai/city-zones-art-bible.md",
            "lineage_reference": "ArtSource/Player/PlayerDirectionalTurntable.png",
            "runtime_integrated": True,
            "torso_skin": {
                "contract": "pelvis_spine_chest_v1",
                "meshes": list(TORSO_SKIN_MESHES),
                "bones": list(TORSO_SKIN_BONES),
                "maximum_influences": 2,
                "blend_bands_m": [list(band) for band in TORSO_BLEND_BANDS],
                "maximum_ring_gap_m": 0.05,
                "action_count": len(result.actions),
                "preserves_authored_contacts": True,
            },
            "content_signature_sha256": content_signature_sha256,
            "material_palette": {
                "MAT_FaceAtlas": "FFFFFF",
                "MAT_JacketAtlas": "FFFFFF",
                "MAT_JeansAtlas": "FFFFFF",
                "MAT_BandageAtlas": "FFFFFF",
            },
            "face_material_contract": {
                "material": "MAT_FaceAtlas",
                "base_color_hex": "FFFFFF",
                "atlas_color": "full_color_rgba",
                "multiply_contract": "_BaseMap multiplied by white _BaseColor",
                "srgb": True,
                "wrap_mode": "Clamp",
                "mipmaps": False,
                "compression": "Uncompressed",
            },
            "face_atlas": {
                "texture_asset": str(face_atlas_path.relative_to(REPO_ROOT)).replace("\\", "/"),
                "renderer": "GEO_FaceSurface",
                "columns": ATLAS_COLUMNS,
                "rows": ATLAS_ROWS,
                "cell_size_px": ATLAS_CELL_SIZE,
                "uv_origin": "bottom_left",
                "filter_mode": "Point",
                "sha256": face_atlas_sha256,
                "cells": [
                    {
                        "expression": expression,
                        "column": column,
                        "row": ATLAS_ROWS - 1 - row,
                        "soiled": soiled,
                    }
                    for expression, column, row, soiled in FACE_ATLAS_CELLS
                ],
            },
            "texture_bindings": [
                {
                    "texture_asset": str(clothing_atlas_path.relative_to(REPO_ROOT)).replace("\\", "/"),
                    "width_px": CLOTHING_ATLAS_SIZE,
                    "height_px": CLOTHING_ATLAS_SIZE,
                    "materials": ["MAT_JacketAtlas", "MAT_JeansAtlas", "MAT_BandageAtlas"],
                    "shader_property": "_BaseMap",
                    "color_space": "sRGB",
                    "filter_mode": "Point",
                    "wrap_mode": "Clamp",
                    "mipmaps": False,
                    "compression": "Uncompressed",
                    "uv_channel": 0,
                    "uv_origin": "bottom_left",
                    "uv_safe_inset_px": 1,
                    "material_tint_hex": "FFFFFF",
                    "sha256": clothing_atlas_sha256,
                    "regions": [
                        {
                            "name": name,
                            "renderer": renderer,
                            "x_px": x,
                            "y_px": y,
                            "width_px": width,
                            "height_px": height,
                        }
                        for name, (renderer, x, y, width, height) in CLOTHING_REGIONS.items()
                    ],
                }
            ],
            "body_contract": {
                "canonical_height_m": config.height,
                "head_crown_to_chin_m": round(head_height, 6),
                "heads_tall": round(config.height / head_height, 4),
                "silhouette": "lean weary adult",
                "bandage_side": "Left",
                "shoulder_patch_side": "Right",
                "jacket": "faded dark olive-drab field jacket; original ochre patch; no copied insignia",
                "face_baseline": "weary flat neutral; no guilt, tears, or theatrical sadness",
            },
            "geometry_simplification": {
                "painted_not_modeled": [
                    "lapels", "collar", "placket", "four pocket panels and flaps",
                    "right shoulder patch", "jacket cuffs",
                    "jeans seams and cuffs", "bandage wraps", "boot shaft panels",
                    "laces", "eyelets", "toe cap", "sole edge",
                ],
                "forbidden_detail_mesh_prefixes": ["ACC_BandageWrap"],
                "bandage_meshes": ["CLO_Bandage.L"],
                "foot_meshes": ["GEO_Foot.L", "GEO_Foot.R"],
            },
            "design_metrics": {
                "height_m": config.height,
                "head_height_m": round(head_height, 6),
                "head_width_m": round(head_width, 6),
                "heads_tall": round(config.height / head_height, 4),
                "shoulder_width_m": round(shoulder_joint_span, 6),
                "shoulder_joint_span_m": round(shoulder_joint_span, 6),
                "visible_neck_height_m": round(visible_neck_height, 6),
                "bare_visible_neck_m": round(visible_neck_height, 6),
                "neck_base_width_m": round(neck_max.x - neck_min.x, 6),
                "neck_top_width_m": round(float(records["GEO_Neck"].obj["bp_top_width_m"]), 6),
                "shoulder_span_head_width_ratio": round(shoulder_joint_span / head_width, 6),
                "shoulder_to_wrist_m": round((wrist_left - shoulder_left).length, 6),
                "shoulder_to_wrist_ratio": round((wrist_left - shoulder_left).length / config.height, 6),
                "hand_bone_length_m": round(hand_left.length, 6),
                "pelvis_height_m": round(result.rig.data.bones["pelvis"].head_local.z, 6),
                "pelvis_joint_span_m": round(pelvis_joint_span, 6),
                "torso_length_m": round(torso_length, 6),
                "torso_waist_half_width_m": round(float(torso_obj["bp_waist_half_width_m"]), 6),
                "torso_chest_half_width_m": round(float(torso_obj["bp_chest_half_width_m"]), 6),
                "jacket_hem_half_width_m": round(float(jacket_obj["bp_hem_half_width_m"]), 6),
                "jacket_waist_half_width_m": round(float(jacket_obj["bp_waist_half_width_m"]), 6),
                "jacket_chest_half_width_m": round(float(jacket_obj["bp_chest_half_width_m"]), 6),
                "jacket_yoke_half_width_m": round(float(jacket_obj["bp_yoke_half_width_m"]), 6),
                "relaxed_left_wrist_z_m": round(relaxed_landmarks["wrist_l_z_m"], 6),
                "relaxed_right_wrist_z_m": round(relaxed_landmarks["wrist_r_z_m"], 6),
                "relaxed_left_fingertip_z_m": round(relaxed_landmarks["fingertip_l_z_m"], 6),
                "relaxed_right_fingertip_z_m": round(relaxed_landmarks["fingertip_r_z_m"], 6),
                **lower_body_metrics,
            },
        }
    )
    for action in payload["actions"]:
        source_action = result.actions[action["name"]].action
        action["event_count"] = int(source_action.get("bp_event_count", 0))
        if action["name"] == RUN_ACTION_NAME:
            action.update(
                {
                    "bone_only": True,
                    "in_place": True,
                    "gait_style": source_action["bp_gait_style"],
                    "landmark_count": int(source_action["bp_landmark_count"]),
                    "short_flight": bool(source_action["bp_short_flight"]),
                }
            )
        keys = action_face_keys(action["name"])
        if keys:
            action["face_keys"] = keys
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def print_report(
    config: common.BuildConfig,
    report: common.ValidationReport,
    face_atlas_path: Path,
    clothing_atlas_path: Path,
    expression_sheet_path: Path,
    head_front_path: Path,
    head_three_quarter_path: Path,
    lower_body_closeup_path: Path,
    content_signature_sha256: str,
) -> None:
    print("BP HERO V2 BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Export objects: {report.object_count}")
    print(f"  Separate mesh parts: {report.mesh_count}")
    print(f"  Triangles: {report.triangle_count}/{common.MAX_TRIANGLES}")
    print(f"  Bones: {len(common.REQUIRED_BONES) + len(common.REQUIRED_FACE_BONES) + len(common.REQUIRED_SOCKET_BONES)}")
    print(f"  Actions: {report.action_count}")
    print(f"  Bounds min: {report.bounds_min}")
    print(f"  Bounds max: {report.bounds_max}")
    print(f"  Content signature: {content_signature_sha256}")
    print(f"  Blend: {config.output}")
    print(f"  FBX: {config.fbx}")
    print(f"  Animation FBX: {config.animation_fbx}")
    print(f"  Manifest: {config.manifest}")
    print(f"  Face atlas: {face_atlas_path}")
    print(f"  Clothing atlas: {clothing_atlas_path}")
    print(f"  Expression sheet: {expression_sheet_path}")
    print(f"  Head front: {head_front_path}")
    print(f"  Head 3/4: {head_three_quarter_path}")
    print(f"  Lower body: {lower_body_closeup_path}")
    print(f"  Portrait: {config.portrait}")
    print(f"  Preview: {config.preview}")


def render_relaxed_preview(path: Path, result: common.BuildResult) -> None:
    animation_data = result.rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = bpy.context.scene.frame_current
    try:
        animation_data.action = result.actions["Relaxed"].action
        bpy.context.scene.frame_set(0)
        bpy.context.view_layer.update()
        common.render_preview(path)
    finally:
        animation_data.action = previous_action
        bpy.context.scene.frame_set(previous_frame)
        if previous_action is None:
            for pose_bone in result.rig.pose.bones:
                pose_bone.rotation_mode = "QUATERNION"
                pose_bone.location = (0.0, 0.0, 0.0)
                pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
                pose_bone.scale = (1.0, 1.0, 1.0)
        bpy.context.view_layer.update()


def render_relaxed_study(
    path: Path,
    result: common.BuildResult,
    camera_location: Vector,
    target: Vector,
    lens: float,
) -> None:
    """Render a deterministic close crop used for silhouette acceptance."""

    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("CAM_HeroV2Study_Data")
    camera = bpy.data.objects.new("CAM_HeroV2Study", camera_data)
    scene.collection.objects.link(camera)
    camera.location = camera_location
    camera_data.lens = lens
    camera_data.sensor_width = 36.0
    common.look_at(camera, target)
    animation_data = result.rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = scene.frame_current
    previous_camera = scene.camera
    previous_filepath = scene.render.filepath
    previous_resolution = (scene.render.resolution_x, scene.render.resolution_y)
    try:
        animation_data.action = result.actions["Relaxed"].action
        scene.frame_set(0)
        scene.camera = camera
        scene.render.resolution_x = 640
        scene.render.resolution_y = 480
        scene.render.filepath = str(path)
        bpy.context.view_layer.update()
        bpy.ops.render.render(write_still=True)
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        scene.camera = previous_camera
        scene.render.filepath = previous_filepath
        scene.render.resolution_x, scene.render.resolution_y = previous_resolution
        bpy.data.objects.remove(camera, do_unlink=True)
        bpy.data.cameras.remove(camera_data)
        if previous_action is None:
            for pose_bone in result.rig.pose.bones:
                pose_bone.rotation_mode = "QUATERNION"
                pose_bone.location = (0.0, 0.0, 0.0)
                pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
                pose_bone.scale = (1.0, 1.0, 1.0)
        bpy.context.view_layer.update()


def main() -> None:
    (
        config,
        face_atlas_path,
        expression_sheet_path,
        clothing_atlas_path,
        head_front_path,
        head_three_quarter_path,
        lower_body_closeup_path,
    ) = parse_args()
    face_atlas_sha256 = build_face_atlas(face_atlas_path, expression_sheet_path)
    clothing_atlas_sha256 = build_clothing_atlas(clothing_atlas_path)
    builder = HeroV2Builder(config, face_atlas_path, clothing_atlas_path)
    result = builder.build()
    report = validate_v2_result(config, result, face_atlas_path, clothing_atlas_path)
    content_signature_sha256 = content_signature(
        config,
        result,
        face_atlas_sha256,
        clothing_atlas_sha256,
    )
    result.root["bp_content_signature_sha256"] = content_signature_sha256
    bpy.context.scene["bp_content_signature_sha256"] = content_signature_sha256
    if config.preview is not None:
        render_relaxed_preview(config.preview, result)
    render_relaxed_study(
        head_front_path,
        result,
        Vector((0.0, -1.28, 1.625)),
        Vector((0.0, -0.020, 1.565)),
        78.0,
    )
    render_relaxed_study(
        head_three_quarter_path,
        result,
        Vector((0.66, -1.22, 1.640)),
        Vector((0.0, -0.015, 1.555)),
        82.0,
    )
    render_relaxed_study(
        lower_body_closeup_path,
        result,
        Vector((0.52, -2.18, 0.58)),
        Vector((0.0, -0.055, 0.39)),
        68.0,
    )
    if config.portrait is not None:
        common.render_inventory_portrait(config.portrait, result)
    if config.glb is not None:
        common.export_glb(config.glb, result)
    if config.fbx is not None:
        common.export_fbx(config.fbx, result)
    if config.animation_fbx is not None:
        common.export_animation_fbx(config.animation_fbx, result)
    if config.manifest is not None:
        write_v2_manifest(
            config.manifest,
            config,
            result,
            report,
            face_atlas_path,
            face_atlas_sha256,
            clothing_atlas_path,
            clothing_atlas_sha256,
            content_signature_sha256,
        )
    common.save_blend(config.output)
    print_report(
        config,
        report,
        face_atlas_path,
        clothing_atlas_path,
        expression_sheet_path,
        head_front_path,
        head_three_quarter_path,
        lower_body_closeup_path,
        content_signature_sha256,
    )


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        raise
