"""Road-owned pigment sources and physical fields; no shared donor is modified.

The proven offline village sampler runs in a private module namespace. This
profile supplies the road's original fifteen palette roles and metre pitches,
while keeping physical slopes independent from ImageGen pigment brightness.
"""
from __future__ import annotations

import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/MountainRoad/Surfaces/Source"
OUTPUT = ROOT / "Assets/Resources/MountainRoad/Surfaces"
DESIGN = "mountain_road_material_surfaces_v1"
VERSION = "1.0.0"
BUDGET = 80 * 1024 * 1024
# Original enum family, pigment cell, original physical repeat, roughness, full colour.
SPECS = (
    ("Asphalt", "Asphalt", 3.5, .95, False),
    ("ForestFloor", "Soil", 5.0, .98, False),
    ("WindSnow", "Snow", 5.0, .95, False),
    ("LayeredStone", "Stone", 6.0, .96, False),
    ("ConiferNeedles", "Needles", 2.5, .98, False),
    ("BarkAndDeadwood", "Bark", 2.5, .97, False),
    ("Concrete", "Concrete", 3.0, .94, False),
    ("RustedIron", "Rust", 1.2, .90, False),
    ("PaintedMetal", "PaintedMetal", 1.2, .76, False),
    ("PaleEnamel", "Enamel", 1.2, .63, False),
    ("Masonry", "Plaster", 2.4, .96, False),
    ("Linoleum", "Enamel", 2.4, .87, False),
    ("Timber", "WeatheredWood", 1.4, .88, False),
    ("WallPaint", "Paint", 2.6, .96, False),
    ("InteriorPaint", "Paint", 2.6, .96, False),
)
GROUND = {"Asphalt", "ForestFloor", "WindSnow", "LayeredStone", "Concrete", "Masonry", "Timber"}
CELL_NAMES = {
    "Mineral": ("Stone", "Concrete", "Plaster", "Asphalt"),
    "Nature": ("Snow", "Soil", "Bark", "Needles"),
    "Finish": ("Rust", "PaintedMetal", "Enamel", "Paint"),
    "Wood": ("WeatheredWood", "Pine", "Oak", "Walnut"),
    "Fabric": ("Linen", "Burgundy", "Paper", "Rubber"),
}
_CANONICAL = {"WindSnow": "Snow", "LayeredStone": "Stone", "ConiferNeedles": "Needles",
              "BarkAndDeadwood": "Bark", "PaleEnamel": "Enamel", "Masonry": "Plaster",
              "InteriorPaint": "WallPaint"}
_spec = importlib.util.spec_from_file_location("road_private_material_fields", ROOT / "tools/village_surface_authoring.py")
_fields = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_fields)
_fields.SOURCE = SOURCE
_fields.OUTPUT = OUTPUT
_fields.DESIGN = DESIGN
_fields.VERSION = VERSION
_fields.BUDGET = BUDGET
_fields.CELL_NAMES = CELL_NAMES
_fields.ALIASES = _CANONICAL
_fields.TABLE = {_CANONICAL.get(row[0], row[0]): (_CANONICAL.get(row[0], row[0]), *row[1:]) for row in SPECS}
_fields.TABLE.update({"Cloth": ("Cloth", "Linen", 1.2, .98, False),
                      "Joinery": ("Joinery", "WeatheredWood", 1.4, .86, False)})
_fields.EXTRAS = {"Rubber": ("Rubber", "Rubber", 1.2, .98, False),
                  "Paper": ("Paper", "Paper", 1.0, .97, False)}

linear = _fields.linear
gamma = _fields.gamma
sources = _fields.sources
sample = _fields.sample
structure = _fields.structure
ground_sample = _fields.ground_sample
provenance = _fields.provenance
spec = _fields.spec


def size_for(family):
    return 2048 if family in ("Asphalt", "LayeredStone", "BarkAndDeadwood") else 1024
