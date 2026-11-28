"""Fixed-metre municipal offices and public vestibule. Origins are the two door thresholds."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("port", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)


class Assembly:
    def __init__(self, name):
        self.root = port.empty(name)
        self.material = port.base.material(name + "Source")
        self.groups = {}

    def surface(self, role):
        if role not in self.groups:
            self.groups[role] = port.Geometry()
            self.groups[role].role = "SteelLight" if role == "LampGlass" else role
        return self.groups[role]

    def box(self, role, center, size, bevel=.024):
        port.chamfer(self.surface(role), center, size, port.CONCRETE, min(bevel, min(size) * .22))

    def rod(self, role, a, b, radius, sides=8):
        self.surface(role).rod(a, b, radius, port.METAL, sides)

    def finish(self):
        for role, geometry in self.groups.items():
            port.obj(geometry, self.root.name + role, self.root, self.material)
        return self.root


def door(a, x, z, front):
    a.box("SteelDark", (x, 1.19, z), (1.40, 2.38, .115))
    a.box("Steel", (x, 1.25, z + front * .063), (1.25, 2.19, .035))
    for edge in (-.75, .75):
        a.box("SteelDark", (x + edge, 1.23, z), (.095, 2.46, .20))
    a.box("SteelDark", (x, 2.44, z), (1.59, .10, .20))
    for y in (.91, 1.19):
        a.rod("SteelLight", (x + .45, y, z + front * .07), (x + .45, y, z + front * .16), .026)
    a.rod("SteelLight", (x + .45, .91, z + front * .16), (x + .45, 1.19, z + front * .16), .027)


def exterior():
    a = Assembly("CityHallExterior")
    # An elevated office slab. Only the southern piers and the entrance core touch the ground.
    a.box("ConcreteWall", (-6.90, 12.03, -6.35), (16.9, 15.4, 11.18), .10)
    for floor in range(5):
        y = 4.05 + floor * 3.18
        a.box("Concrete", (-6.92, y, -6.4), (17.70, .46, 12.32), .065)
        if floor == 4:
            continue
        for column in range(7):
            x = -14.30 + column * 2.32
            a.box("Rubber", (x, y+1.55, -.735), (1.70, 1.63, .105))
            a.box("SteelDark", (x, y+1.56, -.664), (1.51, 1.41, .06))
            a.box("Concrete", (x, y+.72, -.56), (1.85, .14, .42))
            a.box("ConcreteWall", (x+.96, y+1.57, -.50), (.23, 2.68, .67))
            a.box("Steel", (x, y+1.55, -.605), (.048, 1.41, .085))
            a.box("SteelDark", (x, y+1.55, -11.97), (1.48, 1.28, .13))
        for z in (-10.4, -8.3, -4.0, -1.97):
            a.box("SteelDark", (-15.40, y+1.62, z), (.18, 1.28, 1.27))
            a.box("Concrete", (-15.53, y+.94, z), (.41, .13, 1.43))
    a.box("Concrete", (-6.9, 19.95, -6.4), (17.62, .55, 12.18), .07)
    a.box("Roof", (-6.9, 20.28, -6.4), (17.68, .13, 12.26))
    # Concrete shuttering stops at each actual joint. Local service repairs and scuppers.
    for x in (-14, -11.4, -8.8, -6.2, -3.6, -1., 1.05):
        a.box("Concrete", (x, 18.31, -.67), (2.03, 2.42, .23))
    for x in (-13.9, -7.8, -1.7):
        a.box("ConcreteWall", (x, -2.83, -10.98), (.95, 13.4, 1.26), .065)
    a.box("ConcreteWall", (.13, -3.03, -5.65), (2.94, 13.74, 10.65), .055)
    a.box("ConcreteWall", (-13.35, -.92, -10.97), (4.40, 9.62, .76))
    a.box("Concrete", (0, 3.05, .39), (3.46, .37, 2.65))
    a.box("SteelDark", (-.32, 2.817, 1.26), (.82, .092, .31))
    a.box("LampGlass", (-.32, 2.755, 1.26), (.67, .055, .19))
    door(a, 0, -.19, 1)
    a.box("SteelDark", (-1.16, 1.58, -.174), (.65, .31, .088))
    for x in (-1.52, 1.52):
        for i in range(16):
            z = .12 + i * .265
            a.box("SteelDark", (x, 1.13, z), (.047, 2.16, .047))
        for z in (.12, 2.11, 4.11):
            a.box("Steel", (x, 1.24, z), (.103, 2.48, .103))
            a.box("SteelDark", (x, .055, z), (.19, .11, .20))
        for y in (.16, 1.50, 2.16):
            a.box("Steel", (x, y, 2.11), (.068, .068, 4.14))
        a.rod("SteelDark", (x, 2.40, .08), (x, 2.40, 4.18), .015)
        for i in range(12):
            z = .22 + i * .336
            a.rod("SteelDark", (x-.042, 2.32, z-.052), (x+.042, 2.48, z+.052), .012, 5)
            a.rod("SteelDark", (x+.043, 2.33, z+.052), (x-.043, 2.47, z-.052), .012, 5)
    # A hood on the last fence post throws light inward across the people and bars.
    a.box("Steel", (1.52,2.62,4.11), (.087,.38,.087))
    a.box("SteelDark", (1.32,2.81,4.11), (.48,.10,.22))
    a.box("LampGlass", (1.24,2.75,4.11), (.25,.04,.15))
    # West dial faces the street approach. A broad pier interrupts the office ribbon.
    # The northern return cantilevers into view above the street mouth.
    # Its deep fins share the spandrel bands of the main block.
    a.box("ConcreteWall", (-11.82,11.63,1.92), (7.29,15.24,8.06), .09)
    for level in range(5):
        y = 4.05 + level * 3.18
        a.box("Concrete", (-11.82,y,1.90), (7.88,.46,8.62), .065)
        if level < 4:
            for x in (-14.37,-12.34,-10.31):
                a.box("Rubber", (x,y+1.56,6.00), (1.52,1.49,.14))
                a.box("SteelDark", (x,y+1.56,6.09), (1.36,1.30,.07))
                a.box("ConcreteWall", (x+.87,y+1.57,6.05), (.23,2.68,.66))
                a.box("Concrete", (x,y+.77,6.10), (1.58,.13,.42))
    a.box("Roof", (-11.82,19.40,1.9), (7.95,.25,8.68), .045)
    cy, cz = 10.35, 3.45
    a.box("ConcreteWall", (-15.74, 12.08, cz), (.96, 15.25, 3.94), .07)
    a.rod("SteelDark", (-16.29,cy,cz), (-16.13,cy,cz), 1.69, 48)
    a.rod("Plaster", (-16.33,cy,cz), (-16.28,cy,cz), 1.56, 48)
    for n in range(12):
        dy, dz = math.cos(math.tau*n/12), -math.sin(math.tau*n/12)
        inner = 1.17 if n % 3 == 0 else 1.31
        a.rod("Rubber", (-16.36,cy+dy*inner,cz+dz*inner), (-16.36,cy+dy*1.47,cz+dz*1.47), .049)
    # Mount high and off centre: the hood grazes real formwork above the dial.
    a.box("SteelDark", (-16.45,15.12,2.33), (.92,.10,.12))
    a.box("Steel", (-16.91,15.05,2.33), (.24,.20,.57))
    a.box("LampGlass", (-16.92,14.93,2.33), (.20,.055,.44))
    for name, x, length, width in (("HourHand",-16.41,.89,.14), ("MinuteHand",-16.48,1.28,.081)):
        pivot = port.empty(name, a.root, (x,cy,cz))
        g = port.Geometry()
        g.role = "Rubber"
        port.chamfer(g, (0,(length-.14)/2,0), (.037,length+.14,width), port.DARK, .008)
        port.obj(g, name+"Blade", pivot, a.material)
    a.rod("SteelDark", (-16.54, cy, cz), (-16.46, cy, cz), .115, 16)
    return a.finish()


def sling():
    a = Assembly("CityHallSling")
    # Flat folded webbing between the passive prop and the shoulder of its wearer.
    path = [(0,.24,-.015),(-.035,.39,-.05),(-.12,.425,-.12),
            (-.18,.405,-.19),(-.17,.23,-.28),(-.09,-.07,-.16),(0,-.28,.018)]
    strap = a.surface("Fabric")
    for left, right in zip(path,path[1:]):
        from mathutils import Vector
        v, w = Vector(left), Vector(right)
        width = (w-v).cross(Vector((0,0,1))).normalized() * .017
        normal = width.cross(w-v).normalized() * .003
        vertices = [tuple(point + side*width + depth*normal) for point in (v,w)
                    for side,depth in ((-1,-1),(1,-1),(1,1),(-1,1))]
        strap.add(vertices, [(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)], port.ROPE)
    return a.finish()


def interior():
    a = Assembly("CityHallVestibule")
    a.box("Concrete", (0,-.125,3.4), (7.6,.25,7.1))
    a.box("Concrete", (0,3.64,3.4), (7.6,.24,7.1))
    for x in (-3.7,3.7):
        a.box("ConcreteWall", (x,1.80,3.4), (.20,3.6,7.1))
        a.box("SteelDark", (x*.973, .19, 3.4), (.045,.34,6.83))
    for x in (-2.26,2.26):
        a.box("ConcreteWall", (x,1.8,-.075), (3.0,3.6,.18))
    a.box("ConcreteWall", (0,3,-.075), (1.53,1.2,.18))
    door(a, 0, .02, 1)
    a.box("ConcreteWall", (0,1.8,6.85), (7.55,3.6,.20))
    # A single shuttered public counter, a writing ledge and a row of waiting seats.
    a.box("SteelDark", (.53,1.70,6.69), (3.08,1.62,.14))
    for i in range(14):
        a.box("Steel", (.53,1.04+i*.10,6.60), (2.88,.087,.07))
    a.box("Timber", (.53,.94,6.43), (3.25,.10,.56))
    for z in (2.45,3.27,4.09):
        a.box("Timber", (-3.18,.46,z), (.66,.105,.75))
        a.box("Timber", (-3.46,.80,z), (.09,.58,.75))
        a.box("SteelDark", (-3.18,.21,z), (.29,.42,.16))
    # The entrance has an enclosed ceiling strip. A separate hood belongs to the shutter.
    a.box("SteelDark", (-.96,3.49,1.9), (1.4,.12,.38))
    a.box("LampGlass", (-.96,3.41,1.9), (1.28,.055,.29))
    for x in (-.13,1.19):
        a.box("Steel", (x,2.80,6.45), (.07,.09,.62))
    a.box("SteelDark", (.53,2.79,6.13), (1.67,.12,.34))
    a.box("LampGlass", (.53,2.710,6.14), (1.48,.045,.23))
    return a.finish()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", type=Path, default=ROOT/"Assets/Resources/City/Hall")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    args.model_dir.mkdir(parents=True, exist_ok=True)
    for generate in (exterior, interior, sling):
        port.base.reset()
        first = port.describe(generate())
        port.base.reset()
        root = generate()
        measure = port.describe(root)
        assert measure == first and 0 < measure["triangles"] < 52000
        if generate is exterior:
            assert measure["bounds_max"][1] > 20 and measure["bounds_min"][1] < -9
        path = args.model_dir / (root.name + ".json")
        if args.validate_only:
            assert json.loads(path.read_text()) == measure
        else:
            port.base.export(root, path.with_suffix(".fbx"))
            path.write_text(json.dumps(measure, indent=2) + "\n")
    print("CITY HALL MODEL CONTRACT OK")


if __name__ == "__main__":
    main()
