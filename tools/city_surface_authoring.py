"""City-owned ImageGen pigments and independent, metre-space physical fields.

No existing City, Road, Village or Home source asset is changed.  The private
village sampler supplies filtered pigment lookup only; this profile owns its
24 physical definitions and the source-aligned City sheet catalogue.
"""
from __future__ import annotations

import importlib.util
import hashlib
import json
import math
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/City/Surfaces/Source"
OUTPUT = ROOT / "Assets/Resources/City/Surfaces"
DESIGN = "city_material_surfaces_v1"
VERSION = "1.0.0"
BUDGET = 224 * 1024 * 1024
CELL_NAMES = {
    "Masonry": ("Brick", "Plaster", "Concrete", "Stone"),
    "Ground": ("Asphalt", "Soil", "Gravel", "Sand"),
    "Finish": ("Rust", "PaintedMetal", "Galvanized", "Enamel"),
    "Wood": ("WeatheredWood", "DarkWood", "PaintedWood", "Roof"),
    "Fabric": ("Cloth", "Canvas", "Paper", "Rubber"),
    "Nature": ("Bark", "Needles", "Grass", "Moss"),
}
SPECS = (
    ("Brick", "Brick", 2.4, .91, False), ("Plaster", "Plaster", 2.4, .96, False),
    ("Concrete", "Concrete", 3.0, .94, False), ("Stone", "Stone", 2.4, .91, False),
    ("Asphalt", "Asphalt", 3.5, .94, False), ("Soil", "Soil", 4.0, .99, False),
    ("Gravel", "Gravel", 2.2, .96, False), ("Sand", "Sand", 2.6, .97, False),
    ("Rust", "Rust", 1.2, .92, False), ("PaintedMetal", "PaintedMetal", 1.2, .76, False),
    ("Galvanized", "Galvanized", 1.2, .57, False), ("Enamel", "Enamel", 1.2, .52, False),
    ("WeatheredWood", "WeatheredWood", 1.4, .88, False), ("DarkWood", "DarkWood", 1.4, .83, False),
    ("PaintedWood", "PaintedWood", 1.4, .82, False), ("Roof", "Roof", 2.4, .94, False),
    ("Cloth", "Cloth", 1.6, .98, False), ("Canvas", "Canvas", 1.6, .98, False),
    ("Paper", "Paper", .9, .97, False), ("Rubber", "Rubber", 1.2, .97, False),
    ("Bark", "Bark", 1.2, .97, False), ("Needles", "Needles", 1.6, .98, False),
    ("Grass", "Grass", 3.0, .985, False), ("Moss", "Moss", 3.0, .99, False),
)
TABLE = {row[0]: row for row in SPECS}
ALIASES = {"Timber": "WeatheredWood", "Wood": "WeatheredWood", "RustedIron": "Rust",
           "Metal": "PaintedMetal", "Masonry": "Brick", "LayeredStone": "Stone",
           "Foliage": "Grass", "Linen": "Cloth", "PaleEnamel": "Enamel"}
GROUND = {"Asphalt", "Soil", "Gravel", "Sand", "Stone", "Concrete", "Brick", "WeatheredWood", "Grass", "Moss", "Roof"}
_module = importlib.util.spec_from_file_location("city_private_pigment_sampler", ROOT / "tools/village_surface_authoring.py")
_pigment = importlib.util.module_from_spec(_module)
_module.loader.exec_module(_pigment)
_pigment.SOURCE = SOURCE
_pigment.CELL_NAMES = CELL_NAMES
linear, gamma = _pigment.linear, _pigment.gamma


def sources():
    if _pigment._sources is not None:
        return _pigment._sources
    result = {}
    for row in json.loads((SOURCE / "generation.json").read_text(encoding="utf-8-sig"))["images"]:
        path = SOURCE / row["file"]
        if hashlib.sha256(path.read_bytes()).hexdigest() != row["sha256"]:
            raise ValueError("City pigment source changed: " + str(path))
        if tuple(row["cells"]) != CELL_NAMES[row["name"]]:
            raise ValueError("City source cell contract changed: " + row["name"])
        rgb = _pigment.load_rgb(path)
        h, w = rgb.shape[:2]
        for index, name in enumerate(CELL_NAMES[row["name"]]):
            y, x = divmod(index, 2)
            cell = rgb[y*h//2:(y+1)*h//2, x*w//2:(x+1)*w//2]
            inset = max(2, round(min(cell.shape[:2]) * .015))
            base = _pigment.resize_pixels(_pigment.periodic(cell[inset:-inset, inset:-inset]))
            levels = []
            while True:
                levels.append(base.astype(np.float32))
                if base.shape[0] == 1:
                    break
                base = (base[::2,::2]+base[1::2,::2]+base[::2,1::2]+base[1::2,1::2])*.25
            result[name] = levels
    if set(result) != set(TABLE):
        raise ValueError("City requires all 24 source pigments")
    _pigment._sources = result
    return result


def spec(family):
    family = ALIASES.get(family, family)
    if family not in TABLE:
        raise ValueError("Unknown City physical material: " + family)
    return TABLE[family]


def structure(family, u, v, pixel_metres):
    family, _, pitch, rough, _ = spec(family)
    x, y = np.broadcast_arrays(np.asarray(u, np.float32), np.asarray(v, np.float32))
    height = np.zeros(x.shape, np.float32); du = height.copy(); dv = height.copy()

    def wave(nx, ny, amplitude, phase=0.):
        nonlocal height, du, dv
        fx, fy = nx / pitch, ny / pitch
        damping = math.exp(-min(80., (math.pi * float(pixel_metres)) ** 2 * (fx * fx + fy * fy)))
        a = amplitude * damping
        angle = math.tau * (fx * x + fy * y) + phase
        height += a * np.sin(angle)
        du += a * math.tau * fx * np.cos(angle)
        dv += a * math.tau * fy * np.cos(angle)

    if family in ("WeatheredWood", "DarkWood", "PaintedWood", "Bark"):
        wave(35, 1, .00013, .4); wave(73, 2, .000045, 1.2)
        if family in ("Bark", "WeatheredWood"):
            wave(9, 1, .00045 if family == "Bark" else .00022, 1.4)
    elif family == "Roof":
        wave(2, 15, .00065, .3); wave(29, 17, .000055, 1.7)
    elif family in ("Cloth", "Canvas"):
        wave(112 if family == "Cloth" else 72, 0, .000045)
        wave(0, 104 if family == "Cloth" else 80, .000045, 1.2)
    elif family in ("Brick", "Plaster", "Concrete", "Stone", "Asphalt", "Soil", "Gravel", "Sand"):
        amplitude = {"Brick": .00011, "Plaster": .000025, "Concrete": .00013, "Stone": .00022,
                     "Asphalt": .00016, "Soil": .00020, "Gravel": .00045, "Sand": .00011}[family]
        wave(17, 11, amplitude, .7); wave(-23, 31, amplitude * .45, 1.4)
        wave(3, -4, amplitude * .8, 2.2)
    elif family in ("Rust", "PaintedMetal", "Galvanized"):
        amplitude = .000075 if family == "Rust" else .00004 if family == "PaintedMetal" else .000018
        wave(29, 23, amplitude); wave(-37, 43, amplitude * .45, .9)
    elif family in ("Enamel", "Paper", "Rubber"):
        wave(3, 2, .000002 if family == "Enamel" else .000006)
    else:
        wave(9, 13, .000045 if family == "Needles" else .00008, .6)
        wave(19, -7, .000018, 1.4)
    cavity = np.clip(.965 + .025 * np.tanh(height / .00025), .9, 1.)
    wear = .025 * np.tanh(height / .00035)
    response = np.stack((np.clip(.5 + du * .5, 0, 1), np.clip(.5 + dv * .5, 0, 1),
                         np.clip(np.full(x.shape, rough, np.float32) - wear, .45, .995), cavity), axis=-1)
    return height, response.astype(np.float32)


def bar_brick_structure(u, v, pixel_metres):
    """Metric shallow mortar for unique Bar brick charts, with analytic slopes.

    Existing City facade atlases already paint their own brick course rhythm;
    this field belongs only to the new unique Bar charts replacing that image.
    """
    x, y = np.broadcast_arrays(np.asarray(u, np.float32), np.asarray(v, np.float32))
    height, response = structure("Brick", x, y, pixel_metres)
    sigma = math.sqrt(.003 ** 2 + float(pixel_metres) ** 2 / 6)
    dy = np.mod(y + .04, .08) - .04
    row = np.floor(y / .08)
    dx = np.mod(x + np.mod(row, 2) * .12 + .12, .24) - .12
    horizontal = np.exp(-(dy / sigma) ** 2)
    vertical = np.exp(-(dx / sigma) ** 2)
    # The vertical seam vanishes at the horizontal seam, so changing the
    # stagger at each course is continuous with a quiet first derivative.
    mortar = horizontal + (1 - horizontal) * vertical
    amount = .0008 * .003 / sigma
    height -= amount * mortar
    du = -amount * (1 - horizontal) * (-2 * dx / sigma ** 2 * vertical)
    dv = -amount * (-2 * dy / sigma ** 2 * horizontal) * (1 - vertical)
    response[..., 0] = np.clip(response[..., 0] + du * .5, 0, 1)
    response[..., 1] = np.clip(response[..., 1] + dv * .5, 0, 1)
    response[..., 2] = response[..., 2] * (1 - mortar) + .98 * mortar
    response[..., 3] = np.clip(response[..., 3] - mortar * .035, .9, 1)
    return height, response, mortar


def sample(family, u, v, *, part_name="", tint=None, pixel_metres=.01):
    family, cell, pitch, _, _ = spec(family)
    x, y = np.broadcast_arrays(np.asarray(u, np.float32), np.asarray(v, np.float32))
    sources()
    pigment = _pigment.pigment(cell, x / pitch, y / pitch, float(pixel_metres) / pitch)
    height, response = structure(family, x, y, pixel_metres)
    mortar = None
    if family == "Brick" and part_name:
        height, response, mortar = bar_brick_structure(x, y, pixel_metres)
    luminance = np.sum(linear(pigment) * np.asarray((.2126, .7152, .0722)), axis=-1)
    mean = float(np.mean(np.sum(linear(sources()[cell][-1]) * np.asarray((.2126, .7152, .0722)), axis=-1)))
    detail = np.clip(luminance / max(.01, mean), .65, 1.35)
    detail *= 1. + np.tanh(height / .0005) * .018
    base = np.asarray(tint[:3], np.float32) if tint is not None else gamma(np.asarray((.58, .58, .58), np.float32))
    colour = gamma(np.clip(linear(base) * detail[..., None], 0, 1))
    if mortar is not None:
        # Mortar is a separate mineral pigment, not a baked lighting shadow.
        brightness = float(np.sum(linear(base) * np.asarray((.2126, .7152, .0722))))
        mineral = brightness * 1.12 * np.asarray((1.02, .99, .92), np.float32)
        colour = gamma(np.clip(linear(colour) * (1 - mortar[..., None]) + mineral * mortar[..., None], 0, 1))
    return np.clip(colour, 0, 1).astype(np.float32), response


def ground_sample(family, u, v, pixel_metres):
    height, response = structure(family, u, v, pixel_metres)
    return np.stack((np.clip((1 - response[..., 2]) / .1, .2, 1.4),
                     np.clip(.5 + height / .0015, 0, 1), 1 - response[..., 3], np.ones_like(height)), axis=-1).clip(0, 1)


def provenance():
    payload = json.loads((SOURCE/"generation.json").read_text(encoding="utf-8-sig"))
    return {"generator":"image_gen (built-in)", "source_manifest_sha256":hashlib.sha256(
        (SOURCE/"generation.json").read_bytes()).hexdigest(), "images":payload["images"]}


def catalogue():
    """Explicit original owner paths; no texture-name guessing at runtime."""
    rows = []

    def add(key, source, family, pitch, layout="repeat", ground=False, size=1024, source_asset=""):
        rows.append(dict(family=key, source=source, source_name=Path(source_asset).stem if source_asset else "",
                         source_asset=source_asset, material_family=family, meters_per_tile=pitch,
                         layout=layout, ground=ground, size=size))

    city = {
        "CemeteryGranite": ("Stone", 1.4, True), "CemeteryStone": ("Stone", 1.8, True),
        "CemeteryGravel": ("Gravel", 1.6, True), "CemeterySoil": ("Soil", 3., True),
        "FringeConcrete": ("Concrete", 3., True), "FringeForefield": ("Soil", 8., True),
        "FringeMasonry": ("Brick", 2.4, True), "FringeServiceTrack": ("Gravel", 4., True),
        "GroundSoil": ("Soil", 12., True), "MountainRock": ("Stone", 6., True),
        "ParkBark": ("Bark", 1.2, False), "ParkFoliage": ("Grass", 1.6, False),
        "ParkLawn": ("Grass", 3., True), "ParkPaintedMetal": ("PaintedMetal", 1.2, False),
        "ParkPath": ("Gravel", 2.2, True), "ParkPlaza": ("Stone", 2.8, True),
        "ParkStone": ("Stone", 1.5, True), "ParkTimber": ("WeatheredWood", 1.4, True),
        "PoiCloth": ("Cloth", 1.6, False), "PoiPaintedMetal": ("PaintedMetal", 1.4, False),
        "PoiPaper": ("Paper", .9, False), "PoiPaving": ("Concrete", 3., True),
        "PoiTimber": ("WeatheredWood", 1.6, False), "RiverBed": ("Soil", 2., True),
        "RiverIron": ("Rust", 1.2, False), "RiverPaving": ("Stone", 3.2, True),
        "RiverQuay": ("Stone", 2.2, True), "RoadAsphalt": ("Asphalt", 12., True),
        "RoadMarking": ("Enamel", 2., True), "Roof": ("Roof", 4., False),
        "SeacoastConcrete": ("Concrete", 2.2, True), "SeacoastHull": ("PaintedWood", 1.6, False),
        "SeacoastPlank": ("WeatheredWood", 1.2, True), "SeacoastSand": ("Sand", 2.6, True),
        "Sidewalk": ("Concrete", 6., True),
    }
    small = {"Cloth", "Canvas", "Paper", "Rubber", "Grass", "Needles", "Moss"}
    for name, (family, pitch, ground) in city.items():
        add("City" + name, "Textures/City" + name + "Albedo", family, pitch, ground=ground, size=512 if family in small else 1024)
    add("CityEastGroundTransition", "Textures/CityEastGroundTransitionAlbedo", "Soil", 12., "authored_atlas", True)
    for district, primary, secondary in (("OldTown", "Brick", "Stone"), ("Residential", "Plaster", "Concrete"),
                                         ("Industrial", "PaintedMetal", "Brick"), ("Nightlife", "Plaster", "Concrete")):
        add("CityFacade" + district + ("Brick" if district == "OldTown" else "Cool" if district == "Residential" else "Steel" if district == "Industrial" else "Magenta"),
            "Textures/CityFacade" + district + ("Brick" if district == "OldTown" else "Cool" if district == "Residential" else "Steel" if district == "Industrial" else "Magenta") + "Albedo", primary, 2.4, "bay_grid")
        suffix = {"OldTown": "Stone", "Residential": "Warm", "Industrial": "Rust", "Nightlife": "Cyan"}[district]
        add("CityFacade" + district + suffix, "Textures/CityFacade" + district + suffix + "Albedo", "Rust" if district == "Industrial" else secondary, 2.4, "bay_grid")
        for role, family in (("FacadePrimary", primary), ("FacadeSecondary", secondary),
                             ("Plinth", "Stone" if district == "OldTown" else "Concrete" if district in ("Industrial", "Residential") else "Plaster"),
                             ("Roof", "Roof"), ("Metal", "PaintedMetal"),
                             ("WindowFrame", "PaintedWood" if district == "OldTown" else "PaintedMetal")):
            if role == "FacadePrimary" and district == "Residential":
                family = "Concrete"
            elif role == "FacadeSecondary" and district in ("OldTown", "Nightlife"):
                family = "Plaster" if district == "OldTown" else "Brick"
            elif role == "Metal" and district in ("Residential", "Industrial"):
                family = "Galvanized" if district == "Residential" else "Rust"
            layout = "building_side_atlas" if role.startswith("Facade") else "full_face" if role == "Plinth" else "repeat"
            pitch = 4. if role == "Roof" else .8 if role == "WindowFrame" else (1.8 if district in ("Residential", "Industrial") else 1.6) if role == "Metal" else 2.4
            add("Building" + district + role, "Textures/CityBuildingSurfaces/" + district + "/" + role, family, pitch, layout)
    port = {"Concrete": "Concrete", "ConcreteWall": "Concrete", "Deck": "Concrete", "Fabric": "Canvas",
            "PaintedSteel": "PaintedMetal", "RoofMetal": "Galvanized", "Rubber": "Rubber",
            "Tare": "PaintedWood", "Timber": "WeatheredWood", "WarehousePlaster": "Plaster"}
    for name, family in port.items():
        pitch = {"Concrete":2., "ConcreteWall":2., "Deck":1., "Fabric":1., "PaintedSteel":1.5,
                 "RoofMetal":2., "Rubber":.5, "Tare":.75, "Timber":1., "WarehousePlaster":2.}[name]
        add("Port" + name, "City/Port/Textures/Port" + name + "Albedo", family, pitch,
            ground=name in ("Concrete", "ConcreteWall", "Deck"), size=512 if family in small else 1024)
    for name, family, pitch in (("Cardboard", "Paper", 1.), ("Floor", "Concrete", 2.),
                                ("Insulation", "Enamel", 1.5), ("Stainless", "Galvanized", 1.5), ("WashWall", "Enamel", 1.5)):
        add("Cannery" + name, "City/Cannery/Textures/Cannery" + name + "Albedo", family, pitch, ground=name == "Floor", size=512 if family in small else 1024)
    for name, family, pitch in (("BrickPlinth", "Brick", 1.2), ("Concrete", "Concrete", 1.5),
                                ("PaintedMetal", "PaintedMetal", 1.2), ("PaintedWood", "PaintedWood", 1.),
                                ("RoofSlate", "Roof", 2.4), ("StuccoPrimary", "Plaster", 2.4),
                                ("StuccoRepair", "Plaster", 1.8), ("WindowFrame", "PaintedWood", .8)):
        add("PlayerHome" + name, "PlayerHome/ExteriorTextures/PlayerHomeExterior" + name + "Albedo", family, pitch)
    for name, family, layout in (("WallAtlas", "Plaster", "quadrant_atlas"), ("FasciaAtlas", "PaintedMetal", "quadrant_atlas"),
                                 ("BrickAlbedo", "Brick", "repeat"), ("MetalAlbedo", "PaintedMetal", "repeat")):
        add("Supermarket" + name.replace("Albedo", ""), "Supermarket/ExteriorTextures/SupermarketExterior" + name, family, 2.4, layout)
    for name, family in (("Plaster", "Plaster"), ("Stone", "Stone"), ("Wood", "DarkWood"), ("Metal", "PaintedMetal")):
        asset = "Assets/Church/Textures/Church" + name + "Albedo.png"
        add("Church" + name, "", family, 2.4, source_asset=asset)
    add("ArchDeadwood", "Textures/MountainRoadBarkAlbedo", "Bark", 2.5)
    # Arch survival props use these retained shared recipes in City; scope is
    # still the City/Home hierarchy, never the true interiors owning donors.
    add("ArchEnamel", "Home/Textures/HomeEnamelAlbedo", "Enamel", 1.)
    prototypes = json.loads((ROOT/"Assets/City/Models/CityBuildings3D.json").read_text(encoding="utf8"))["prototypes"]
    for row in rows:
        for district in ("OldTown", "Residential", "Industrial", "Nightlife"):
            if row["family"].startswith("Building"+district):
                owners = [p for p in prototypes if p["district"] == district]
                dimensions = np.asarray([(p["frontage_width_m"], p["depth_m"], p["height_m"]) for p in owners])
                row["reference_dimensions_m"] = np.median(dimensions, axis=0).tolist()
                row["owner_dimensions_min_m"] = dimensions.min(axis=0).tolist()
                row["owner_dimensions_max_m"] = dimensions.max(axis=0).tolist()
                row["metric_contract"] = "Shared atlas sampled over district median prototype; variable-size owners retain original UV0, not uniform texel density."
                if row["layout"] == "full_face":
                    heights = [part["bounds_max_source"][2]-part["bounds_min_source"][2] for p in owners for part in p["parts"] if part["role"] == "Plinth"]
                    row["reference_dimensions_m"][2] = float(np.median(heights))
        if row["layout"] == "quadrant_atlas":
            row["reference_dimensions_m"] = [15.5, 15.5, 6.4]
            row["metric_contract"] = "Exact fixed supermarket exterior atlas; 0.006 quadrant inset retained."
    return rows
