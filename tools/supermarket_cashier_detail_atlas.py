#!/usr/bin/env python3
"""Deterministic greyscale detail atlas for both supermarket cashiers."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
from pathlib import Path

import atlas_kit


DETAIL_ATLAS_NAME = "SupermarketCashier3DDetailAtlas.png"
NORMAL_DETAIL_ATLAS_NAME = "SupermarketCashier3DSurfaceAtlas.png"
NORMAL_DETAIL_ATLAS_SIZE = 512
DETAIL_ATLAS_SIZE = 256
DETAIL_ATLAS_UV_INSET_PX = 1
DETAIL_ATLAS_REGION_PROP = "bp_atlas_region"
DETAIL_ATLAS_RESERVED_CELL = (0, 0, 64, 64)
DETAIL_ATLAS_WHITE = (255, 255, 255, 255)
DETAIL_ATLAS_GREY_MIN = 140
DETAIL_ATLAS_GREY_MAX = 236


@dataclass(frozen=True)
class CashierAtlasRegion:
    """One bottom-left pixel sub-rect of the atlas owned by a renderer."""

    name: str
    renderer: str
    x: int
    y: int
    width: int
    height: int
    kind: str
    sides: int = 0
    rings: int = 0


CASHIER_ATLAS_REGIONS = (
    CashierAtlasRegion(
        "VestFront.L", "CLO_VestFront.L", 64, 0, 64, 64, "box"),
    CashierAtlasRegion(
        "VestFront.R", "CLO_VestFront.R", 128, 0, 64, 64, "box"),
    CashierAtlasRegion(
        "VestBack", "CLO_VestBack", 192, 0, 64, 64, "box"),
    CashierAtlasRegion(
        "ShirtBib", "CLO_ShirtBib", 0, 64, 64, 64, "box"),
    CashierAtlasRegion(
        "Collar", "CLO_TightCollar", 64, 64, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Sleeve.L", "GEO_Forearm.L", 128, 64, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Sleeve.R", "GEO_Forearm.R", 192, 64, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Trouser.L", "GEO_Thigh.L", 0, 128, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Trouser.R", "GEO_Thigh.R", 64, 128, 64, 64, "ring", 12, 2),
    CashierAtlasRegion(
        "Sole.L", "CLO_ShoeSole.L", 128, 128, 64, 64, "box"),
    CashierAtlasRegion(
        "Sole.R", "CLO_ShoeSole.R", 192, 128, 64, 64, "box"),
)

# The active clerk has a distinct 512 px sheet. The head owns a full 256 px
# cylindrical island; all other surfaces reuse material cells at useful density.
NORMAL_CASHIER_ATLAS_REGIONS = (
    CashierAtlasRegion("VestFront.L", "CLO_VestFront.L", 0, 0, 128, 256, "surface"),
    CashierAtlasRegion("VestFront.R", "CLO_VestFront.R", 128, 0, 128, 256, "surface"),
    CashierAtlasRegion("VestBack", "CLO_VestBack", 256, 0, 128, 256, "surface"),
    *(CashierAtlasRegion("Shirt."+name, name, 384, 0, 128, 128, "surface") for name in
      ("GEO_Torso", "CLO_ShirtBib", "CLO_TightCollar", "GEO_UpperArm.L", "GEO_UpperArm.R",
       "GEO_Forearm.L", "GEO_Forearm.R")),
    *(CashierAtlasRegion("Trouser."+name, name, 384, 128, 128, 128, "surface") for name in
      ("GEO_Pelvis", "GEO_Thigh.L", "GEO_Thigh.R", "GEO_Shin.L", "GEO_Shin.R")),
    CashierAtlasRegion("HeadSkin", "GEO_Head", 0, 256, 256, 256, "head"),
    *(CashierAtlasRegion("Skin."+name, name, 256, 256, 128, 128, "surface") for name in
      ("GEO_Neck", "GEO_Hand.L", "GEO_Hand.R", "GEO_Thumb.L", "GEO_Thumb.R",
       "FACE_Ear.L", "FACE_Ear.R", "FACE_Nose", "FACE_Mouth")),
    CashierAtlasRegion("CombedHair", "HAIR_FlatCombover", 384, 256, 128, 128, "surface"),
    *(CashierAtlasRegion("Leather."+name, name, 256, 384, 128, 128, "surface") for name in
      ("GEO_Foot.L", "GEO_Foot.R", "CLO_Belt")),
    *(CashierAtlasRegion("Rubber."+name, name, 384, 384, 128, 128, "surface") for name in
      ("CLO_ShoeSole.L", "CLO_ShoeSole.R")),
)


def atlas_regions(normal=False):
    return NORMAL_CASHIER_ATLAS_REGIONS if normal else CASHIER_ATLAS_REGIONS


def paint_normal_cashier_surface_atlas():
    """Age and material at actual UV density, without painted face features."""
    import math
    canvas = atlas_kit.PixelCanvas(NORMAL_DETAIL_ATLAS_SIZE, NORMAL_DETAIL_ATLAS_SIZE)
    canvas.rect(0, 0, canvas.width, canvas.height, DETAIL_ATLAS_WHITE)
    cells = {(r.x, r.y): r for r in NORMAL_CASHIER_ATLAS_REGIONS}

    def hash_noise(x, y, salt=0):
        n=(x*73856093) ^ (y*19349663) ^ (salt*83492791)
        n=(n^(n>>13))*1274126177
        return ((n^(n>>16))&255)/255

    def gauss(u,v,x,y,sx,sy):
        return math.exp(-((u-x)/sx)**2-((v-y)/sy)**2)

    def put(region, col, row, rgb):
        canvas.put(region.x+col, canvas.height-1-region.y-row,
                   tuple(max(0,min(255,round(c))) for c in rgb)+(255,))

    for region in cells.values():
        for row in range(region.height):
            v=row/max(1,region.height-1)
            for col in range(region.width):
                u=col/max(1,region.width-1)
                n=hash_noise(col,row)
                large=math.sin(col*.041+math.sin(row*.026))*math.sin(row*.059)
                if region.kind == "head":
                    front=math.exp(-((u-.5)/.20)**6)
                    cheek=gauss(u,v,.421,.47,.038,.12)+gauss(u,v,.579,.47,.038,.12)
                    stubble=front*max(0,min(1,(.40-v)/.16))*max(0,min(1,v/.08))
                    tone=242+large*5+(n-.5)*9-12*stubble
                    if n < .16 and v < .39 and front>.45:
                        tone-=30*stubble
                    if .12<v<.86 and n>.955:
                        tone-=20
                    rgb=(tone+3*cheek,tone-4*cheek,tone-7*cheek)
                elif region.name.startswith("Skin."):
                    tone=235+large*8+(n-.5)*18
                    if n>.94: tone-=20
                    rgb=(tone+2,tone-2,tone-5)
                elif region.name.startswith("CombedHair"):
                    strands=math.sin(col*2.05+row*.26+math.sin(row*.045))
                    tone=210+13*strands+(n-.5)*17
                    if (col+row//5)%29==0: tone+=28
                    rgb=(tone,tone,tone)
                elif region.name.startswith("Leather."):
                    crease=math.exp(-((v-(.34+.025*math.sin(u*10)))/.015)**2)
                    toe=gauss(u,v,.5,.64,.43,.20)
                    tone=219+large*12+(n-.5)*16-30*crease+15*toe
                    if n>.975: tone-=23
                    rgb=(tone,tone-2,tone-4)
                elif region.name.startswith("Rubber."):
                    tone=205+(n-.5)*24-(14 if row%14<2 else 0)
                    rgb=(tone,tone,tone)
                else:
                    # Two orthogonal thread directions, softer cross fibres,
                    # seam dirt and broad wear survive an ordinary close view.
                    weave=(9 if col%3==0 else -3)+(6 if row%3==0 else -2)
                    tone=219+weave+(n-.5)*12+large*8
                    edge=min(u,1-u)
                    tone-=15*math.exp(-(edge/.045)**2)
                    if region.name.startswith("Vest"):
                        tone+=17*gauss(u,v,.55,.30,.35,.17)
                        tone-=14*gauss(u,v,.50,.67,.35,.13)
                        tone+=10*gauss(u,v,.24,.86,.13,.14)
                    elif region.name.startswith("Shirt"):
                        tone-=22*math.exp(-((v-.72-.025*math.sin(u*9))/.025)**2)
                    else:
                        tone+=13*gauss(u,v,.53,.43,.34,.17)
                    rgb=(tone,tone,tone)
                put(region,col,row,rgb)

    def stroke(region, points, tone, width=1):
        for a,b in zip(points,points[1:]):
            atlas_kit.atlas_line_bottom_left(canvas,
                region.x+round(a[0]*(region.width-1)),region.y+round(a[1]*(region.height-1)),
                region.x+round(b[0]*(region.width-1)),region.y+round(b[1]*(region.height-1)),
                (tone,tone,tone,255),width)

    head=cells[(0,256)]
    # Forehead creases, cheek folds and short crow's feet are skin marks only.
    # The actual eyes/brows/nose/lips remain exclusively separate 3D meshes.
    for height,span,tone in ((.765,.088,179),(.807,.083,185),(.845,.066,194)):
        points=[(.5-span+2*span*t,height+.008*math.cos(t*math.pi*2)) for t in (0,.2,.4,.6,.8,1)]
        stroke(head,[(u,v+.006) for u,v in points],250)
        stroke(head,points,tone)
    for sign in (-1,1):
        for v in (.541,.517):
            stroke(head,[(.5+sign*x,v+.008*math.sin(x*42)) for x in (.046,.058,.072,.084)],183)
        stroke(head,[(.5+sign*.047,.445),(.5+sign*.059,.393),(.5+sign*.064,.340)],185)
        for k in range(3):
            stroke(head,[(.5+sign*.104,.594-k*.010),(.5+sign*.124,.606-k*.019)],184+k*7)
    for region in cells.values():
        if region.name.startswith("Vest"):
            for x in (.065,.935):
                stroke(region,[(x,.035),(x,.965)],161)
                stroke(region,[(x+.012,.035),(x+.012,.965)],245)
                for row in range(10,region.height-10,6):
                    stroke(region,[(x+.024,row/region.height),(x+.024,(row+2)/region.height)],175)
            for v in (.278,.304):
                stroke(region,[(.18,v),(.80,v+.006)],164)
            stroke(region,[(.18,.278),(.18,.205),(.80,.205),(.80,.31)],193)
        elif region.name.startswith("Shirt"):
            stroke(region,[(.08,.02),(.08,.98)],170)
            stroke(region,[(.11,.02),(.11,.98)],239)
        elif region.name.startswith("Skin."):
            for y in (.27,.50,.71):
                stroke(region,[(.25,y),(.43,y-.018),(.63,y+.012),(.80,y-.012)],189)
                stroke(region,[(.27,y+.018),(.60,y+.027),(.78,y+.005)],248)
    # Untextured 3D eyes/buttons use this reserved texel in the shared Blender
    # preview; runtime leaves their base texture unset entirely.
    atlas_kit.atlas_rect_bottom_left(canvas,0,0,4,4,DETAIL_ATLAS_WHITE)
    return canvas


def neutral_grey(value: int):
    if not DETAIL_ATLAS_GREY_MIN <= value <= DETAIL_ATLAS_GREY_MAX:
        raise ValueError(
            f"Atlas ink {value} is outside the "
            f"{DETAIL_ATLAS_GREY_MIN}-{DETAIL_ATLAS_GREY_MAX} band"
        )
    return (value, value, value, 255)


def paint_cashier_detail_atlas():
    """Paint the shared atlas as a stable pure function."""

    canvas = atlas_kit.PixelCanvas(DETAIL_ATLAS_SIZE, DETAIL_ATLAS_SIZE)
    canvas.rect(0, 0, canvas.width, canvas.height, DETAIL_ATLAS_WHITE)
    regions = {region.name: region for region in CASHIER_ATLAS_REGIONS}
    rect = atlas_kit.atlas_rect_bottom_left
    line = atlas_kit.atlas_line_bottom_left

    for name, edge_x in (("VestFront.L", 34), ("VestFront.R", 58)):
        region = regions[name]
        line(
            canvas,
            region.x + edge_x, region.y + 4,
            region.x + edge_x, region.y + 60,
            neutral_grey(168), 2,
        )
        rect(
            canvas,
            region.x + 38, region.y + 40,
            region.x + 54, region.y + 43,
            neutral_grey(180),
        )
        rect(
            canvas,
            region.x + 34, region.y + 16,
            region.x + 62, region.y + 19,
            neutral_grey(205),
        )

    region = regions["VestBack"]
    rect(
        canvas,
        region.x + 4, region.y + 46,
        region.x + 60, region.y + 49,
        neutral_grey(170),
    )
    line(
        canvas,
        region.x + 8, region.y + 30,
        region.x + 56, region.y + 26,
        neutral_grey(196), 2,
    )

    region = regions["ShirtBib"]
    line(
        canvas,
        region.x + 32, region.y + 4,
        region.x + 32, region.y + 60,
        neutral_grey(186), 2,
    )
    for crease_y in (18, 30, 42):
        rect(
            canvas,
            region.x + 36, region.y + crease_y,
            region.x + 58, region.y + crease_y + 2,
            neutral_grey(214),
        )

    region = regions["Collar"]
    for band_y, tone in ((20, 176), (40, 192)):
        rect(
            canvas,
            region.x + 2, region.y + band_y,
            region.x + 62, region.y + band_y + 3,
            neutral_grey(tone),
        )
    rect(
        canvas,
        region.x + 24, region.y + 22,
        region.x + 40, region.y + 41,
        neutral_grey(158),
    )

    for name in ("Sleeve.L", "Sleeve.R"):
        region = regions[name]
        rect(
            canvas,
            region.x + 2, region.y + 14,
            region.x + 62, region.y + 16,
            neutral_grey(190),
        )
        rect(
            canvas,
            region.x + 2, region.y + 48,
            region.x + 62, region.y + 51,
            neutral_grey(172),
        )
        line(
            canvas,
            region.x + 18, region.y + 18,
            region.x + 22, region.y + 46,
            neutral_grey(206), 2,
        )

    for name in ("Trouser.L", "Trouser.R"):
        region = regions[name]
        rect(
            canvas,
            region.x + 2, region.y + 12,
            region.x + 62, region.y + 15,
            neutral_grey(182),
        )
        line(
            canvas,
            region.x + 30, region.y + 16,
            region.x + 34, region.y + 58,
            neutral_grey(210), 2,
        )
        rect(
            canvas,
            region.x + 10, region.y + 20,
            region.x + 54, region.y + 24,
            neutral_grey(198),
        )

    for name in ("Sole.L", "Sole.R"):
        region = regions[name]
        for tread_x in range(region.x + 6, region.x + 58, 8):
            rect(
                canvas,
                tread_x, region.y + 10,
                tread_x + 4, region.y + 54,
                neutral_grey(164),
            )

    return canvas


class DetailAtlasReport:
    """The painted atlas as the manifest and validator see it."""

    def __init__(self, path, sha256, width, height):
        self.path = path
        self.sha256 = sha256
        self.width = width
        self.height = height


def texture_asset_path(path) -> str:
    """Return the atlas as a repo-relative forward-slash asset path."""

    resolved = Path(path).resolve()
    for parent in resolved.parents:
        if (parent / "Assets").is_dir() and (parent / "tools").is_dir():
            return resolved.relative_to(parent).as_posix()
    return resolved.as_posix()


def write_detail_atlas(canvas, path, expected_size=DETAIL_ATLAS_SIZE):
    """Write the atlas and prove the on-disk bytes match the painting."""

    payload = canvas.png_bytes()
    expected = hashlib.sha256(payload).hexdigest()
    path.parent.mkdir(parents=True, exist_ok=True)
    canvas.write_png(path)
    written = hashlib.sha256(path.read_bytes()).hexdigest()
    if written != expected:
        raise RuntimeError(
            f"Detail atlas {path} hashes {written} on disk but "
            f"{expected} in memory"
        )

    width, height, _ = atlas_kit.decode_generated_png(payload, str(path))
    if (width, height) != (expected_size, expected_size):
        raise RuntimeError(
            f"Detail atlas {path} is {width}x{height}, expected "
            f"{expected_size} square"
        )

    return DetailAtlasReport(path, expected, width, height)
