#!/usr/bin/env python3
"""Build the two lower City kiosk courts using the kiosk's shared weathered palette."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("ridge_kiosk", HERE / "build-ridge-kiosk-3d-model.py")
ridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ridge)
OUT = HERE.parent / "Assets/Resources/RidgeKiosk"
SOURCE = HERE.parent / "ArtSource/City/RidgeKiosk"


def residential():
    m = ridge.Model()
    marks = {}

    def group(name, position):
        m.group = name
        marks[name] = position

    group("Floor", (0, 0, 0))
    m.box("concrete", (-4.055, -.235, .59), (5.19, .47, 3.66), .017)
    for x in (-6.45, -5.94, -5.43, -4.92, -4.41, -3.90, -3.39, -2.88, -2.37, -1.86):
        m.box("concrete", (x, -.009, -.91), (.49, .018, .57), .007)

    def table(index, x, width):
        z = -.72
        group(f"Table{index}", (x, .75, z))
        m.box("wood", (x, .722, z), (width, .056, .64), .022)
        for dx in (-width / 2 + .10, width / 2 - .10):
            for dz in (-.22, .22):
                m.box("sage", (x + dx, .345, z + dz), (.075, .69, .075), .010)
            m.box("sage", (x + dx, .62, z), (.045, .075, .49), .005)

    table(0, -3.23, .91)
    table(1, -5.36, 1.53)

    def chair(name, x, z, width, finish, yaw=0):
        group(name, (x, .455, z))
        first = len(m.solids)
        m.box("wood", (x, .43, z), (width, .05, .38), .018)
        for dx in (-width / 2 + .06, width / 2 - .06):
            m.box(finish, (x + dx, .205, z - .145), (.055, .41, .055), .008)
            m.box(finish, (x + dx, .41, z + .14), (.055, .82, .055), .008)
        m.box(finish, (x, .77, z + .14), (width, .094, .045), .012)
        m.box("wood", (x, .645, z + .14), (width - .034, .067, .044), .010)
        if yaw:
            cosine, sine = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
            for index in range(first, len(m.solids)):
                surface, (vertices, faces) = m.solids[index]
                rotated = [(x + (px-x)*cosine + (pz-z)*sine, py, z - (px-x)*sine + (pz-z)*cosine)
                           for px, py, pz in vertices]
                m.solids[index] = surface, (rotated, faces)

    chair("Seat0", -5.74, .105, .61, "sage")
    chair("Seat1", -4.99, .105, .44, "iron")
    chair("LooseChair", -4.08, -.18, .40, "sage", yaw=-90)
    table_edge = -3.23 - .91/2
    far_table_edge = -5.36 + 1.53/2
    for amount in (0, .5, 1):
        radians = math.radians(-9*amount)
        for (_, (vertices, _)), part in zip(m.solids, m.groups):
            if part != "LooseChair":
                continue
            for x, _, z in vertices:
                moved_x = -4.08 - .11*amount + (x+4.08)*math.cos(radians) + (z+.18)*math.sin(radians)
                assert far_table_edge + .10 < moved_x < table_edge - .10, "Chair intersects a table during reset"
    group("Cloth", (0, 0, 0))
    m.box("paper", (0, 0, 0), (.20, .025, .14), .010)
    group("Lens", (-2.93, 2.22, 1.09))
    m.box("iron", (-2.93, 2.27, 1.09), (.30, .09, .26), .017)
    m.box(ridge.LAMP, (-2.93, 2.218, 1.09), (.225, .029, .19), .010)
    for dx in (-.62, .62):
        m.box("iron", (-2.24 + dx, 2.36, .81), (1.36, .045, .81), .007)
    return m, marks


def industrial():
    m = ridge.Model()
    marks = {}

    def group(name, position):
        m.group = name
        marks[name] = position

    group("Floor", (0, 0, 0))
    # The through pavement stays level while the lot falls behind the kiosk.
    # One buried concrete foundation follows each leg of the court.
    m.box("concrete", (-3.035, -.59, -.01), (3.13, 1.18, 4.88), .018)
    m.box("concrete", (-1.575, -.59, -4.55), (6.05, 1.18, 4.20), .018)
    group("Table0", (-2.76, 1.04, -5.15))
    m.box("iron", (-2.76, 1.01, -5.15), (1.63, .060, .65), .016)
    for x in (-3.44, -2.08):
        for z in (-5.36, -4.94):
            m.box("kick", (x, .47, z), (.076, .94, .076), .009)
        m.box("kick", (x, .28, -5.15), (.048, .064, .47), .006)
    group("Table1", (.12, 1.06, -5.15))
    m.box("wood", (.12, .50, -5.15), (.49, .91, .49), .024)
    for y in (.084, 1.014):
        radii = ((.625, -.046), (.655, -.026), (.655, .026), (.625, .046))
        points = [(.12 + r * math.cos(a * math.tau / 12), y + dy, -5.15 + r * math.sin(a * math.tau / 12))
                  for r, dy in radii for a in range(12)]
        faces = [tuple(range(12)), tuple(range(36, 48))]
        faces[1] = tuple(reversed(faces[1]))
        faces.extend((k*12+a, (k+1)*12+a, (k+1)*12+(a+1)%12, k*12+(a+1)%12)
                     for k in range(3) for a in range(12))
        m.add("wood", (points, faces))
    m.box("iron", (.12, 1.063, -5.15), (.10, .007, .10), .003)

    group("Crate", (0, 0, 0))
    m.box("sage", (0, .036, 0), (.42, .072, .32), .012)
    for x in (-.20, .20):
        m.box("sage", (x, .12, 0), (.033, .22, .33), .007)
    for z in (-.15, .15):
        m.box("sage", (0, .119, z), (.38, .16, .03), .007)
        m.box("iron", (0, .207, z), (.38, .032, .033), .006)
    group("CrateStand", (-3.84, .73, -.85))
    m.box("kick", (-3.84, .70, -.85), (.67, .06, .80), .012)
    for x in (-4.09, -3.59):
        m.box("iron", (x, .335, -.85), (.072, .67, .59), .010)

    group("Lens", (-3.77, 2.58, -4.02))
    m.box("iron", (-4.19, 1.31, -4.02), (.105, 2.62, .10), .012)
    m.box("iron", (-3.93, 2.65, -4.02), (.61, .082, .11), .011)
    m.box("kick", (-3.77, 2.63, -4.02), (.38, .13, .29), .016)
    m.box(ridge.LAMP, (-3.77, 2.568, -4.02), (.29, .035, .21), .009)
    return m, marks


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=OUT)
    parser.add_argument("--source-dir", type=Path, default=SOURCE)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    payload = {}
    for name, build in (("ResidentialKioskCourt", residential), ("IndustrialKioskCourt", industrial)):
        model, marks = build()
        second, _ = build()
        measured = ridge.measure(model)
        assert measured == ridge.measure(second) and measured["triangles"] < 5500
        payload[name] = {**measured, "anchors": marks}
        if not args.validate_only:
            ridge.emit(name, model, marks, args.model_dir, args.source_dir, OUT / "RidgeKioskPalette.png")
    manifest = json.dumps(payload, indent=2) + "\n"
    target = args.model_dir / "DistrictKioskCourts.json"
    if args.validate_only:
        assert target.read_text() == manifest
    else:
        target.write_text(manifest)
    print("DISTRICT KIOSK COURTS OK")


if __name__ == "__main__":
    main()
