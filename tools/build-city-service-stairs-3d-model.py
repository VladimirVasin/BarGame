#!/usr/bin/env python3
"""Fixed-metre street stair and site fittings. Materials use the City's metre charts."""
import argparse
import json
import runpy
import sys
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
kit = runpy.run_path(str(ROOT / "tools/build-city-terrace-3d-model.py"))
port = kit["port"]
OUTPUT = ROOT / "Assets/Resources/CityTerraces"


def build():
    port.base.reset()
    material = port.base.material("Municipal stair finishes")
    roots = []

    def assembly(name):
        root = port.empty(name)
        roots.append(root)
        return root

    def part(root, role, name, make):
        g = kit["geometry"](role)
        make(g)
        port.obj(g, name, root, material)

    def box(g, p, size, bevel=.005):
        port.chamfer(g, p, size, port.CONCRETE, bevel)

    def flight(g):
        for i in range(6):
            h = (i + 1) * .15
            box(g, (0, (h - .25) / 2, (i + .5) * .32), (2.10, h + .25, .32), .009)

    def rails(g):
        for x in (-.99, .99):
            for y, radius in ((1.08, .028), (.46, .018)):
                g.rod((x, y + .05 * .9 / 1.92, .05), (x, y + 1.87 * .9 / 1.92, 1.87), radius, port.METAL, 8)
            for i in (0, 2, 5):
                z = (i + .5) * .32
                foot = (i + 1) * .15
                box(g, (x, foot + .011, z), (.13, .022, .12))
                g.rod((x, foot, z), (x, 1.08 + z * .9 / 1.92, z), .026, port.METAL, 8)

    root = assembly("ServiceFlight")
    part(root, "Concrete", "Six cast treads", flight)
    part(root, "SteelDark", "Welded guard", rails)
    port.empty("ANCHOR_Upper", root, (0, .9, 1.92))
    root = assembly("LoadingEdge")

    def angle(g, start, end):
        length, mid = end - start, (start + end) / 2
        box(g, (0, -.075, mid), (.022, .17, length))
        box(g, (-.049, .007, mid), (.118, .018, length))
    part(root, "SteelDark", "Worn angle", lambda g: angle(g, 0, 2.04))
    part(root, "Steel", "Replacement angle", lambda g: angle(g, 2.05, 3.30))
    def hardware(g):
        for z in (.18, 1.5, 2.3, 3.07):
            g.rod((.014, -.07, z), (.026, -.07, z), .022, port.METAL, 6)
        for z in (1.79, 1.94):
            g.rod((.012, -.11, z), (.015, -.11, z), .018, port.DARK, 8)
    part(root, "SteelDark", "Anchor seats", hardware)

    root = assembly("OutletShelf")
    def stonework(g):
        # A shallow masonry cheek faces the inspector, with staggered bed joints.
        for row in range(3):
            seams = [0, .33, .96, 1.65] if row == 1 else [0, .61, 1.24, 1.65]
            for a, b in zip(seams, seams[1:]):
                box(g, (-.74, .10 + row * .19, (a + b) / 2 - .82), (.21, .18, b - a - .009), .012)
    part(root, "ConcreteWall", "Outfall masonry", stonework)
    part(root, "Concrete", "Repointed beds", lambda g: box(g, (-.752, .28, 0), (.203, .55, 1.64), .003))
    def grate(g):
        for x in (-.32, .32):
            box(g, (x, .012, 0), (.035, .021, .83))
        for z in (-.40, .40):
            box(g, (0, .012, z), (.64, .021, .035))
        for i in range(10):
            box(g, (0, .010, -.34 + i * .076), (.61, .018, .025), .003)
    part(root, "SteelDark", "Cast grate", grate)

    root = assembly("GableRainShoe")
    def pipe(g):
        g.rod((0, .59, 0), (0, 3.4, 0), .077, port.METAL, 12)
        for y in (.81, 2.48):
            box(g, (.11, y, 0), (.17, .055, .055))
            g.rod((0, y - .026, 0), (0, y + .026, 0), .089, port.METAL, 12)
    part(root, "Steel", "Painted downpipe", pipe)
    def elbow(g):
        for a, b in [((0,.64,0),(0,.35,0)), ((0,.35,0),(-.18,.17,0)), ((-.18,.17,0),(-.35,.12,0))]:
            g.rod(a, b, .081, port.METAL, 12)
        g.rod((0,.59,0),(0,.65,0),.088,port.METAL,12)
    part(root, "SteelLight", "Galvanized shoe", elbow)
    def channel(g):
        for z in (-.12,.12):
            box(g, (-.49,.032,z), (.95,.065,.053), .008)
        box(g, (-.49,.007,0), (.95,.015,.20))
    part(root, "Concrete", "Outlet channel", channel)

    root = assembly("CourtPourJoint")
    def boards(g):
        for i, (start, end) in enumerate(((0,1.14),(1.155,2.10))):
            box(g, (0,.31,(start+end)/2), (.14,.69,end-start), .014)
    part(root, "ConcreteWall", "Retaining pours", boards)
    part(root, "Concrete", "Form tie repair", lambda g: [g.rod((.066,y,z),(.073,y,z),.025,port.CONCRETE,10)
         for y,z in ((.19,.28),(.48,.90),(.19,1.52),(.48,1.86))])
    root = assembly("CourtRepairTread")
    part(root, "Concrete", "Replacement nosing", lambda g: box(g,(0,-.020,.15),(1.85,.050,.302),.006))
    root = assembly("CourtShoeWear")
    part(root, "Concrete", "Courtyard shoe turn", lambda g: box(g,(0,-.010,0),(1.78,.025,1.72),.004))
    root = assembly("CourtReturnRail")
    def return_guard(g):
        for height, radius in ((1.025, .026), (.65, .016), (.28, .016)):
            g.rod((0, height, .035), (0, height, 2.365), radius, port.METAL, 8)
        for z in (.065, 1.20, 2.335):
            box(g, (0, .016, z), (.11, .032, .11))
            g.rod((0, .02, z), (0, 1.025, z), .024, port.METAL, 8)
    part(root, "SteelDark", "Horizontal guard", return_guard)
    root = assembly("StairLampPost")
    def standard(g):
        box(g, (0, .018, 0), (.20, .036, .22))
        g.rod((0, .026, 0), (0, 2.76, 0), .049, port.METAL, 10)
        box(g, (0, 2.735, .23), (.085, .075, .54))
        g.rod((0, 2.40, 0), (0, 2.71, .32), .022, port.METAL, 8)
        box(g, (0, 2.672, .44), (.23, .052, .18))
        for x in (-.072, .072):
            for z in (-.08, .08):
                g.rod((x, .033, z), (x, .052, z), .014, port.DARK, 6)
    part(root, "SteelDark", "Bolted folded lamp bracket", standard)
    roots.append(kit["flight"](material, "GableFlight", 2.60))
    return roots


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    roots = build()
    def measure(roots):
        port.validate_surfaces(roots)
        return [port.describe(r) for r in roots]
    report = measure(roots)
    assert all(m["triangles"] < (6000 if m["name"] == "GableFlight" else 1700) for m in report)
    assert abs(report[0]["bounds_max"][2] - 1.92) < .001
    roots = build()
    assert measure(roots) == report
    path = OUTPUT / "ServiceStairs.json"
    if args.validate_only:
        assert json.loads(path.read_text()) == report
    else:
        for root in roots:
            port.base.export(root, OUTPUT / (root.name + ".fbx"))
        path.write_text(json.dumps(report, indent=2) + "\n")
    print("CITY SERVICE STAIRS: measured metre fittings and deterministic geometry")

if __name__ == "__main__":
    main()
