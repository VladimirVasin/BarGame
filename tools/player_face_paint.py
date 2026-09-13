"""The hero's shared deterministic 64 px painted identity and expressions.

Skin planes and quiet stubble stay fixed through blinking and speech. The
mouth inspection retains the production enamel markers and every soiled twin.
This module paints pixels only; geometry, UVs and atlas layouts have other owners.
"""
from __future__ import annotations

import math

from atlas_kit import PixelCanvas

CELL = 64
SKIN = (174, 141, 123, 255)
SKIN_SHADOW = (130, 99, 88, 255)
SKIN_DARK = (57, 45, 46, 255)
LIP = (112, 76, 71, 255)
TEETH = (175, 165, 143, 255)
INSPECTION_TEETH = (210, 204, 181, 255)
TEETH_SHADOW = (128, 119, 105, 255)
SOIL = (96, 84, 46, 255)
SOIL_DARK = (66, 56, 30, 255)
SOIL_PALE = (150, 140, 84, 255)


def _plane(x, y, cx, cy, rx, ry):
    return math.exp(-((x - cx) / rx) ** 2 - ((y - cy) / ry) ** 2)


def draw_identity(canvas: PixelCanvas, ox=0, oy=0):
    """Soft, stable pigment describes the weary face without contour diagrams."""
    for y in range(CELL):
        for x in range(CELL):
            light = 5 * _plane(x, y, 27, 16, 22, 22) + 3 * _plane(x, y, 33, 36, 3, 10)
            cheek = 3 * (_plane(x, y, 15, 36, 9, 7) + _plane(x, y, 49, 36, 9, 7))
            hollow = 7 * (_plane(x, y, 11, 45, 7, 9) + _plane(x, y, 53, 45, 7, 9))
            tired = 13 * (_plane(x, y, 21, 31, 7, 3.5) + _plane(x, y, 43, 32, 7, 3.5))
            nose = 8 * _plane(x, y, 29, 34, 2.5, 7)
            beard = 7 * _plane(x, y, 32, 58, 22, 7)
            grain = int((x * 11 + y * 7) % 17 == 0) - int((x * 7 + y * 13) % 23 == 0)
            # Match the bare cranial material at the UV boundary; the mesh owns its outline.
            edge = min(1., x / 5., (63 - x) / 5.)
            change = (light - hollow - tired - nose - beard + grain) * edge
            color = tuple(round(v + change + (cheek * edge if channel == 0 else 0))
                          for channel, v in enumerate(SKIN[:3])) + (255,)
            canvas.put(ox + x, oy + y, color)
    # A short, softly lit bridge and two restrained nostrils, not a line from brow to lip.
    canvas.line(ox + 30, oy + 31, ox + 29, oy + 36, (156, 121, 105, 255))
    canvas.line(ox + 29, oy + 36, ox + 30, oy + 39, (144, 108, 94, 255))
    canvas.line(ox + 33, oy + 33, ox + 34, oy + 37, (186, 151, 129, 255))
    canvas.line(ox + 29, oy + 40, ox + 30, oy + 40, (111, 79, 70, 255))
    canvas.line(ox + 35, oy + 40, ox + 36, oy + 40, (122, 89, 77, 255))
    canvas.line(ox + 31, oy + 41, ox + 34, oy + 41, (153, 116, 98, 255))
    canvas.put(ox + 32, oy + 44, (147, 108, 94, 255))
    # A sparse, untrimmed day's stubble: fixed across cells, with no hard beard perimeter.
    for y in range(46, 63):
        for x in range(10, 54):
            if y < 52 and 22 < x < 43:
                continue
            density = _plane(x, y, 32, 58, 22, 8)
            selector = (x * 31 + y * 17 + x * y) % 97
            if selector < 9 * density:
                canvas.put(ox + x, oy + y, (119, 96, 88, 255))
            elif selector < 16 * density:
                canvas.put(ox + x, oy + y, (147, 119, 106, 255))


def draw_upper_face(canvas: PixelCanvas, expression="Neutral", ox=0, oy=0):
    """Shared brow/lid/iris painting for ordinary and dialogue expressions."""
    brow = (48, 41, 41, 255)
    brow_soft = (112, 86, 76, 255)
    lid = (70, 54, 51, 255)
    for side, center in enumerate((21, 43)):
        by, inner_y = ((21, 21) if side == 0 else (22, 21))
        height = {"HalfBlink": 1, "ClosedBlink": 0, "Watchful": 3, "Tense": 1,
                  "Drowsy": 1, "Slack": 1, "Grimace": 1}.get(expression, 2)
        if expression == "Tense":
            by, inner_y = 20, 22
        elif expression == "Grimace":
            by, inner_y = 20, 24
        elif expression == "Drowsy":
            by, inner_y = 24, 24
        elif expression in ("Slack", "Skeptical"):
            by, inner_y = (19, 20) if side == 0 else (23, 23)
            if expression == "Skeptical":
                height = 3 if side == 0 else 1
        elif expression == "Emphasis":
            by, inner_y = (18, 19) if side == 0 else (19, 18)
            height = 3
        outer, inner = (center - 7, center + 6) if side == 0 else (center + 7, center - 6)
        canvas.line(ox + outer, oy + by + 1, ox + center, oy + by, brow_soft)
        canvas.line(ox + outer + (1 if side == 0 else -1), oy + by,
                    ox + center, oy + by, brow)
        canvas.line(ox + center, oy + by, ox + inner, oy + inner_y, brow)
        canvas.put(ox + outer, oy + by, brow_soft)
        ey = 28 + side
        if expression == "Glazed" and side:
            ey += 1
            height = 1
        if not height:
            canvas.line(ox + center - 6, oy + ey, ox + center, oy + ey + 1, lid)
            canvas.line(ox + center, oy + ey + 1, ox + center + 6, oy + ey, lid)
            continue
        white = (139, 131, 123, 255) if expression in ("Drowsy", "Tense", "Grimace") else (161, 151, 137, 255)
        canvas.ellipse(ox + center, oy + ey, 6, height, white)
        pupil = center + (1 if side == 0 else 0) + (1 if expression == "Watchful" else 0)
        if expression == "Glazed":
            pupil += -2 if side == 0 else 2
        canvas.ellipse(ox + pupil, oy + ey, 2, height, (95, 92, 83, 255))
        canvas.line(ox + pupil, oy + ey - height + 1, ox + pupil, oy + ey + height, (41, 39, 39, 255))
        if height > 1:
            canvas.put(ox + pupil - 1, oy + ey - 1, (146, 137, 122, 255))
        # The heavy upper lid tapers at both corners; the lower lid has no black outline.
        canvas.line(ox + center - 6, oy + ey - height + 1, ox + center - 2, oy + ey - height, lid)
        canvas.line(ox + center - 2, oy + ey - height, ox + center + 5, oy + ey - height, lid)
        canvas.put(ox + center + 6, oy + ey, (116, 87, 77, 255))
        canvas.line(ox + center - 3, oy + ey + height + 1,
                    ox + center + 3, oy + ey + height + 1, (150, 116, 103, 255))


def draw_dialogue_upper_face(canvas: PixelCanvas, state: str):
    """Repaint just the upper region from the same identity before posing it."""
    if state == "Rest":
        return
    base = PixelCanvas(CELL, CELL)
    draw_identity(base)
    expression = {"HalfBlink": "HalfBlink", "Blink": "ClosedBlink"}.get(state, state)
    draw_upper_face(base, expression)
    canvas.pixels[:44 * CELL * 4] = base.pixels[:44 * CELL * 4]


def restore_mouth_background(canvas: PixelCanvas, x0=19, y0=44, x1=48, y1=56):
    """Opening the mouth preserves the same painted jaw instead of exposing a flat rectangle."""
    base = PixelCanvas(CELL, CELL)
    draw_identity(base)
    for y in range(y0, y1):
        start, end = (y * CELL + x0) * 4, (y * CELL + x1) * 4
        canvas.pixels[start:end] = base.pixels[start:end]


def draw_face_tile(canvas: PixelCanvas, column: int, top_row: int,
                   expression: str, soiled: bool = False) -> None:
    ox, oy = column * CELL, top_row * CELL
    draw_identity(canvas, ox, oy)
    draw_upper_face(canvas, expression, ox, oy)
    draw_expression_mouth(canvas, ox, oy, column, expression, soiled)


def draw_expression_mouth(canvas, ox, oy, column, expression, soiled):
    inspection = expression in ("TeethInspectHalf", "TeethInspect")
    if soiled and inspection:
        # These lips open below the soil band's y=50 edge. Paint the
        # surrounding chin/cheek traces first so neither row nor the gap is
        # mistaken for grime on the teeth.
        draw_mouth_soil(canvas, ox, oy, column)
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
    elif expression == "TeethDisplay":
        # Straight, slightly parted lips. The eyes and brows stay weary:
        # inspecting the teeth is a small physical gesture, not a smile.
        canvas.line(ox + 22, oy + mouth_y - 2, ox + 42, oy + mouth_y - 2, LIP)
        canvas.rect(ox + 22, oy + mouth_y - 1, ox + 43, oy + mouth_y + 4, SKIN_DARK)
        canvas.rect(ox + 24, oy + mouth_y - 1, ox + 41, oy + mouth_y + 2, TEETH)
        canvas.line(ox + 24, oy + mouth_y + 2, ox + 40, oy + mouth_y + 2, TEETH_SHADOW)
        for x in (28, 32, 36):
            canvas.put(ox + x, oy + mouth_y + 1, TEETH_SHADOW)
        canvas.line(ox + 24, oy + mouth_y + 4, ox + 40, oy + mouth_y + 4, LIP)
    elif expression == "Spit":
        # A compact opening at the same mouth centre, no grimacing eyes.
        canvas.ellipse(ox + 32, oy + mouth_y + 1, 5, 3, LIP)
        canvas.ellipse(ox + 32, oy + mouth_y + 1, 3, 2, SKIN_DARK)
        canvas.line(ox + 24, oy + mouth_y, ox + 26, oy + mouth_y + 1, SKIN_SHADOW)
        canvas.line(ox + 38, oy + mouth_y + 1, ox + 41, oy + mouth_y, SKIN_SHADOW)
    elif expression in ("TeethInspectHalf", "TeethInspect"):
        # Pull the lips apart vertically, leaving their corners level. Two
        # broad ivory rows and a dark gap survive the small PS1 mirror image;
        # the unchanged upper face keeps this an inspection, not a smile.
        full = expression == "TeethInspect"
        left, right = (18, 47) if full else (21, 44)
        bottom = 58 if full else 55
        canvas.rect(ox + left, oy + 44, ox + right, oy + bottom, SKIN_DARK)
        canvas.line(ox + left + 2, oy + 44, ox + right - 3, oy + 44, LIP)
        canvas.line(ox + left + 2, oy + bottom, ox + right - 3, oy + bottom, LIP)
        upper_left, upper_right = (20, 45) if full else (23, 42)
        upper_bottom = 50 if full else 49
        lower_top, lower_bottom = (53, 57) if full else (51, 54)
        canvas.rect(ox + upper_left, oy + 45, ox + upper_right, oy + upper_bottom, INSPECTION_TEETH)
        canvas.rect(ox + upper_left + 2, oy + lower_top,
                    ox + upper_right - 2, oy + lower_bottom, INSPECTION_TEETH)
        canvas.line(ox + upper_left, oy + upper_bottom - 1,
                    ox + upper_right - 1, oy + upper_bottom - 1, TEETH_SHADOW)
        canvas.line(ox + upper_left + 2, oy + lower_top,
                    ox + upper_right - 3, oy + lower_top, TEETH_SHADOW)
        for x in (26, 32, 38):
            canvas.line(ox + x, oy + 47, ox + x, oy + upper_bottom - 1, TEETH_SHADOW)
            canvas.put(ox + x, oy + lower_bottom - 1, TEETH_SHADOW)
    else:
        canvas.line(ox + 22, oy + mouth_y, ox + 35, oy + mouth_y, LIP)
        canvas.line(ox + 35, oy + mouth_y, ox + 43, oy + mouth_y + 1, LIP)
        canvas.line(ox + 25, oy + mouth_y + 2, ox + 40, oy + mouth_y + 2, SKIN_SHADOW)
    canvas.put(ox + 21, oy + mouth_y, SKIN_SHADOW)
    canvas.put(ox + 44, oy + mouth_y + 1, SKIN_SHADOW)

    if soiled and not inspection:
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
