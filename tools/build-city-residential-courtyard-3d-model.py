#!/usr/bin/env python3
"""Build the passive, fixed-metre residential courtyard kit.

Run through tools/run-blender.py. Unity +Y is up, +Z faces the court, and
ground is Y=0. Every solid is checked by the shared port/interior kit before
export; model wrappers retain FBX units. Runtime only places these meshes.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("courtyard_port", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)
port.SURFACE_TILES.update({"Soil": 12.0, "Foliage": 1.6, "Lens": 1.0})

DESIGN_ID = "city_residential_courtyard_kit_v1"
VERSION = "1.0.0"
MODEL_DIR = ROOT / "Assets/Resources/City/ResidentialCourtyard"
SOURCE_DIR = ROOT / "ArtSource/City/ResidentialCourtyard"
MANIFEST_NAME = "CityResidentialCourtyard3D.json"
ROLES = ("Concrete", "Steel", "SteelDark", "Timber", "Roof", "Tare", "Soil", "Foliage", "Lens")
NAMES = ("CourtyardBench", "CourtyardPlanter", "CourtyardUtilityShelter", "CourtyardEntranceCanopy", "CourtyardDrain", "CourtyardBasket")
BUDGETS = ((3500, 4), (4000, 4), (9000, 5), (4500, 3), (1600, 2), (2500, 2))
ENVELOPES = (
    ((-.95, 0, -.35), (.95, .90, .35)),
    ((-.65, 0, -.325), (.65, .45, .325)),
    ((-1.25, 0, -.625), (1.25, 2.15, .625)),
    ((-1.15, .04, -.06), (1.15, 2.95, 1.10)),
    ((-.225, 0, -.325), (.225, .025, .325)),
    ((-.28, 0, -.20), (.28, .28, .20)),
)


def geometry(role):
    g = port.Geometry()
    g.role = role
    return g


def box(g, center, size, color, bevel=.009):
    port.chamfer(g, center, size, color, bevel)


def emit(g, name, root, material):
    if g.faces:
        return port.obj(g, name, root, material)
    return None


def anchor(root, name, point):
    port.empty(name, root, point)


def bench(material):
    root = port.empty(NAMES[0])
    timber, steel = geometry("Timber"), geometry("SteelDark")
    # Five separate seat slats, a raked two-slat back and a single repaired
    # plank preserve the useful shape at walking distance.
    for z in (-.24, -.12, 0, .12, .24):
        box(timber, (0, .432, z), (1.90, .056, .095), port.WOOD, .009)
    for y, z in ((.65, -.29), (.85, -.315)):
        box(timber, (0, y, z), (1.90, .10, .07), port.WOOD, .012)
    box(timber, (.54, .471, .12), (.52, .022, .092), port.ROPE, .004)
    for x in (-.73, .73):
        for z in (-.25, .25):
            box(steel, (x, .21, z), (.055, .42, .055), port.METAL)
            box(steel, (x, .018, z), (.16, .036, .20), port.METAL)
        box(steel, (x, .38, 0), (.065, .065, .64), port.METAL)
        steel.rod((x, .34, -.27), (x, .88, -.315), .026, port.METAL, 6)
        for z in (-.35, .35):
            steel.rod((x, .026, z+.012 if z < 0 else z-.012),
                      (x, .026, z+.027 if z < 0 else z-.027), .012, port.DARK, 6)
    emit(timber, "BenchSlatsAndRepair", root, material)
    emit(steel, "BenchBoltedFrame", root, material)
    anchor(root, "ANCHOR_BenchSeat", (0, .46, .05))
    anchor(root, "ANCHOR_BenchDock", (0, 0, .85))
    anchor(root, "ANCHOR_BenchLeftSeat", (-.46, .46, .05))
    anchor(root, "ANCHOR_BenchRightSeat", (.46, .46, .05))
    return root


def leaf(g, base, tip, width):
    x, y, z = base
    tx, ty, tz = tip
    # A thin closed leaf has visible area on both sides and stable normals.
    g.add([(x-width, y, z), (tx, ty, tz), (x+width, y, z), (x, y+.018, z)],
          [(0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3)], port.PAINT)


def planter(material):
    root = port.empty(NAMES[1])
    concrete, soil, foliage = geometry("Concrete"), geometry("Soil"), geometry("Foliage")
    box(concrete, (0, .045, 0), (1.30, .09, .65), port.CONCRETE, .015)
    for x in (-.60, .60):
        box(concrete, (x, .18, 0), (.10, .22, .65), port.CONCRETE, .012)
    for z in (-.275, .275):
        box(concrete, (0, .18, z), (1.12, .22, .10), port.CONCRETE, .012)
    box(soil, (0, .21, 0), (1.09, .07, .43), port.WOOD, .012)
    for index, x in enumerate((-.43, -.16, .14, .40)):
        z = -.04 if index % 2 == 0 else .07
        top = .425 if index != 2 else .45
        foliage.rod((x, .23, z), (x+.018, top-.015, z), .012, port.PAINT, 5)
        leaf(foliage, (x, .30, z), (x-.115, top-.045, z-.075), .038)
        leaf(foliage, (x, .33, z), (x+.105, top, z+.065), .038)
        leaf(foliage, (x, .275, z), (x+.08, .345, z-.09), .034)
    emit(concrete, "PlanterRepairedTrough", root, material)
    emit(soil, "PlanterSoil", root, material)
    emit(foliage, "PlanterLowLeaves", root, material)
    anchor(root, "ANCHOR_PlantingSurface", (0, .245, 0))
    return root


def shelter(material):
    root = port.empty(NAMES[2])
    steel, timber, roof, bin_body = geometry("SteelDark"), geometry("Timber"), geometry("Roof"), geometry("Steel")
    for x in (-1.12, 1.12):
        for z in (-.49, .49):
            box(steel, (x, 1.025, z), (.065, 2.05, .065), port.METAL)
            box(steel, (x, .025, z), (.19, .05, .19), port.METAL)
        box(steel, (x, 1.995, 0), (.075, .075, 1.12), port.METAL)
        steel.rod((x, 1.67, -.49), (x, 1.985, -.18), .025, port.METAL, 6)
    box(roof, (0, 2.095, 0), (2.50, .11, 1.25), port.DARK, .017)
    # Subtle raised seams end at a real folded edge, without a false beam.
    for x in (-.93, -.62, -.31, 0, .31, .62, .93):
        box(roof, (x, 2.145, 0), (.025, .010, 1.20), port.DARK, .003)
    for y in (.28, .65, 1.02, 1.39):
        box(timber, (0, y, -.515), (2.20, .30, .055), port.WOOD, .012)
    for x in (-1.12, 1.12):
        for y in (.28, .65, 1.02):
            box(timber, (x, y, -.05), (.05, .30, .91), port.WOOD, .009)
    # Two real supports keep the same basket visible throughout sorting.
    for x in (-.67, .67):
        box(timber, (x, .8325, .25), (.83, .035, .64), port.WOOD, .009)
        for xx in (x-.34, x+.34):
            box(steel, (xx, .425, .25), (.035, .815, .035), port.METAL, .005)
            steel.rod((xx, .58, .25), (xx, .80, .51), .018, port.METAL, 6)
    # Closed refuse bin under the right support: no scattered waste or label.
    box(bin_body, (.67, .30, .22), (.55, .57, .49), port.PAINT, .035)
    box(bin_body, (.67, .604, .22), (.58, .032, .52), port.PAINT, .012)
    box(steel, (.67, .612, .43), (.17, .016, .025), port.METAL, .005)
    for x in (.47, .87):
        steel.rod((x-.018, .06, .04), (x+.018, .06, .04), .06, port.DARK, 8)
    emit(steel, "UtilityBoltedSupports", root, material)
    emit(timber, "UtilityBoardsAndShelves", root, material)
    emit(roof, "UtilityFoldedRoof", root, material)
    emit(bin_body, "UtilityClosedBin", root, material)
    for name, point in {
        "ANCHOR_BasketShelf": (-.67, .85, .25),
        "ANCHOR_BasketShelfEnd": (.67, .85, .25),
        "ANCHOR_ServiceStart": (-.67, 0, .85),
        "ANCHOR_ServiceWaypoint": (0, 0, 1.25),
        "ANCHOR_ServiceEnd": (.67, 0, .85),
    }.items():
        anchor(root, name, point)
    return root


def canopy(material):
    root = port.empty(NAMES[3])
    steel, roof = geometry("SteelDark"), geometry("Roof")
    # Wall Z=0, usable clearance below the slab is 2.70m. No supporting
    # columns appear in the door route; bracing returns directly to the wall.
    box(roof, (0, 2.825, .52), (2.30, .15, 1.16), port.DARK, .018)
    box(roof, (0, 2.925, 1.065), (2.30, .05, .07), port.DARK, .008)
    for x in (-.80, .80):
        box(steel, (x, 2.50, .025), (.07, .42, .07), port.METAL)
        steel.rod((x, 2.31, .04), (x, 2.74, .89), .025, port.METAL, 6)
        steel.rod((x, 2.73, .04), (x, 2.73, .91), .025, port.METAL, 6)
    # Continuous gutter/downpipe, held against the wall by two clips.
    steel.rod((-1.09, 2.73, 1.055), (1.09, 2.73, 1.055), .038, port.METAL, 8)
    steel.rod((1.08, 2.73, 1.055), (1.08, 2.73, .10), .038, port.METAL, 8)
    steel.rod((1.08, 2.73, .10), (1.08, .14, .10), .038, port.METAL, 8)
    steel.rod((1.08, .14, .10), (1.08, .08, .23), .038, port.METAL, 8)
    for y in (.72, 1.95):
        box(steel, (1.08, y, .035), (.10, .045, .07), port.METAL, .006)
    box(steel, (0, 2.73, .38), (.46, .08, .21), port.METAL, .012)
    emit(roof, "EntranceCanopyRoof", root, material)
    emit(steel, "EntranceCanopyBracesAndDrainpipe", root, material)
    lens = geometry("Lens")
    box(lens, (0, 2.685, .38), (.38, .018, .14), port.ICE, .005)
    emit(lens, "EntranceLampLens", root, material)
    anchor(root, "ANCHOR_CanopyThreshold", (0, 0, .12))
    anchor(root, "ANCHOR_DrainOutfall", (1.08, .04, .25))
    anchor(root, "IndustrialTaskLight_CourtyardEntrance", (0, 2.68, .38))
    return root


def drain(material):
    root = port.empty(NAMES[4])
    frame, bars = geometry("Concrete"), geometry("SteelDark")
    for x in (-.2075, .2075):
        box(frame, (x, .009, 0), (.035, .018, .65), port.CONCRETE, .003)
    for z in (-.3075, .3075):
        box(frame, (0, .009, z), (.38, .018, .035), port.CONCRETE, .003)
    for z in (-.265, -.199, -.133, -.067, 0, .067, .133, .199, .265):
        box(bars, (0, .018, z), (.38, .014, .021), port.METAL, .003)
    emit(frame, "DrainSetFrame", root, material)
    emit(bars, "DrainOpenGrating", root, material)
    anchor(root, "ANCHOR_DrainSurface", (0, .025, 0))
    return root


def basket(material):
    root = port.empty(NAMES[5])
    body = geometry("Tare")
    box(body, (0, .0175, 0), (.54, .035, .38), port.PAINT, .012)
    for y in (.075, .135, .195):
        for x in (-.252, .252):
            box(body, (x, y, 0), (.025, .035, .38), port.PAINT, .005)
        for z in (-.177, .177):
            box(body, (0, y, z), (.48, .035, .026), port.PAINT, .005)
    for x in (-.248, .248):
        for z in (-.173, .173):
            box(body, (x, .13, z), (.026, .23, .026), port.PAINT, .005)
    for x in (-.267, .267):
        for z in (-.085, .085):
            body.rod((x, .19, z), (x, .267, z), .013, port.PAINT, 6)
        body.rod((x, .267, -.085), (x, .267, .085), .013, port.PAINT, 6)
    emit(body, "CarryBasketSlottedShell", root, material)
    anchor(root, "ANCHOR_BasketLeft", (-.28, .235, 0))
    anchor(root, "ANCHOR_BasketRight", (.28, .235, 0))
    anchor(root, "ANCHOR_BasketCarry", (0, 0, 0))
    return root


def build():
    material = port.base.material("ResidentialCourtyardVertexPaint")
    roots = [builder(material) for builder in (bench, planter, shelter, canopy, drain, basket)]
    bpy.context.view_layer.update()
    return roots


def manifest(roots):
    models = []
    for index, root in enumerate(roots):
        measured = port.describe(root)
        renderers = [o for o in root.children_recursive if o.type == "MESH"]
        maximum_triangles, maximum_renderers = BUDGETS[index]
        if measured["triangles"] > maximum_triangles or len(renderers) > maximum_renderers:
            raise RuntimeError("Courtyard mesh budget exceeded: " + root.name)
        for key, expected in zip(("bounds_min", "bounds_max"), ENVELOPES[index]):
            if any(abs(actual-wanted) > .025 for actual, wanted in zip(measured[key], expected)):
                raise RuntimeError(f"Courtyard envelope differs: {root.name} {key} {measured[key]} expected {expected}")
        for part in renderers:
            role = part.name.rsplit("__", 1)[-1].split(".")[0]
            if role not in ROLES or not part.data.uv_layers or not part.data.color_attributes:
                raise RuntimeError("Courtyard lost its finite semantic surfaces: " + part.name)
        measured.update({"renderer_count": len(renderers), "maximum_triangles": maximum_triangles,
                         "maximum_renderers": maximum_renderers,
                         "material_roles": sorted({o.name.rsplit("__", 1)[-1].split(".")[0] for o in renderers})})
        models.append(measured)
    return {"design_id": DESIGN_ID, "version": VERSION, "generator": Path(__file__).name,
            "coordinate_system": "Unity metres +Y up/+Z court; root at ground; swap YZ/reverse winding for Blender",
            "geometry_signature_includes": ["vertices", "faces", "colors", "uv0", "transforms"],
            "material_source": "Existing shared City/Port/Ground/Park materials; no new texture or Light",
            "semantic_uv_tiles_metres": {role: port.SURFACE_TILES[role] for role in ROLES},
            "models": models, "assets": [name+".fbx" for name in NAMES]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    port.base.reset()
    roots = build()
    result = manifest(roots)
    if args.validate_only:
        if json.loads((args.model_dir / MANIFEST_NAME).read_text()) != result:
            raise RuntimeError("Courtyard authored contract differs from committed manifest")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            port.base.export(root, args.model_dir / (root.name+".fbx"))
        (args.model_dir / MANIFEST_NAME).write_text(json.dumps(result, indent=2)+"\n")
        # A compact editable gallery is useful without packing duplicate copies
        # of the shared City textures into this source file.
        for index, root in enumerate(roots):
            root.location = port.source((index*3.3, 0, 0))
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityResidentialCourtyard3D.blend"), check_existing=False)
    port.base.reset()
    if manifest(build()) != result:
        raise RuntimeError("Courtyard deterministic rebuild mismatch")
    print("CITY RESIDENTIAL COURTYARD ART CONTRACT OK: fixed metres, supported shelves, bounded shared surfaces and deterministic geometry", flush=True)
    print(json.dumps({"models": [{k: model[k] for k in ("name", "bounds_min", "bounds_max", "triangles", "renderer_count")} for model in result["models"]]}))


if __name__ == "__main__":
    main()
