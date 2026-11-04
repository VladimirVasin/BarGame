#!/usr/bin/env python3
"""Fixed-metre passive retaining, residential and rock edges for the City ridge."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import random
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("ridge_kit", ROOT / "tools/build-city-residential-courtyard-3d-model.py")
kit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kit)
port = kit.port
port.SURFACE_TILES.update({"RepairMortar": 2.0, "DampMortar": 2.0, "Rock": 6.0, "Bark": 1.2})
OUT = ROOT / "Assets/Resources/City/Hillside"
MANIFEST = "CityRidgeEdges3D.json"


def run_section(group, left, right, section, color):
    """Two X stations sharing a closed section in Unity (Z,Y)."""
    count = len(section)
    points = [(x, y, z) for x in (left, right) for z, y in section]
    faces = [tuple(reversed(range(count))), tuple(range(count, 2 * count))]
    faces += [(i, (i+1) % count, count + (i+1) % count, count + i) for i in range(count)]
    group.add(points, faces, color)


def retaining_end(material):
    root = port.empty("RidgeRetainingEnd")
    wall, coping, repair, iron = [kit.geometry(s) for s in ("ConcreteWall", "Concrete", "RepairMortar", "SteelDark")]
    # Three pours descend with the bank. Their feet are buried, the wall face
    # batters slightly and water drips from a projecting bevelled coping.
    for start, stop, top in ((-3., -1.10, 1.22), (-1.10, 1.15, .94), (1.15, 3., .64)):
        inset = .006
        run_section(wall, start+inset, stop-inset, [(-.195,-.55),(.19,-.55),(.133,top-.09),(-.195,top-.09)], port.CONCRETE)
        if start == -1.10:
            # A real scarf joint replaces the broken middle of one coping.
            for a,b,section,tint in ((start,-.24,coping,port.EDGE),(-.24,.41,repair,port.ROPE),(.41,stop,coping,port.EDGE)):
                cap(section, a, b, top, tint)
        else:
            cap(coping, start, stop, top, port.EDGE)

    # A narrow channel has a dished invert and raised lips. The body lies
    # under the datum so the small iron grate actually spans a hollow.
    throat_a, throat_b = .90, 1.42
    for start, stop in ((-2.94,-1.10),(-1.09,throat_a),(throat_b,2.78)):
        run_section(coping, start, stop, [(.10,-.20),(.57,-.20),(.57,.07),(.55,.10),
                    (.483,.10),(.46,.063),(.417,.024),(.312,.018),(.240,.050),(.214,.093),(.10,.093)], port.CONCRETE)
    run_section(coping, throat_a, throat_b, [(.10,-.20),(.57,-.20),(.57,.07),(.55,.10),
                (.483,.10),(.46,.064),(.46,-.12),(.227,-.12),(.227,.066),(.214,.093),(.10,.093)], port.CONCRETE)
    for z in (.251,.436):
        kit.box(iron, (1.16,.052,z), (.53,.034,.016), port.DARK, .003)
    for i in range(9):
        kit.box(iron, (throat_a + .020 + .060*i,.083,.343), (.026,.029,.251), port.METAL, .004)
    kit.emit(wall, "BatteredConcrete", root, material)
    kit.emit(coping, "DripCapsAndChannel", root, material)
    kit.emit(repair, "RecastCap", root, material)
    kit.emit(iron, "GrateBars", root, material)
    fill = kit.geometry("Soil")
    # Survey in X: a short shoulder at the wall and a battered toe returning
    # below the old terrain. The bank changes grade on either side of each
    # joint and pinches out at the ends, so soil cannot hang past a bare end.
    stations = [(-2.98,.07,.52),(-2.58,.63,1.7),(-2.12,1.08,2.37),
                (-1.36,1.12,2.65),(-1.11,1.09,2.54),(-1.09,.82,2.50),
                (-.64,.84,2.34),(-.19,.81,2.38),(.36,.83,2.17),
                (1.14,.80,2.15),(1.16,.52,2.08),(1.64,.50,1.86),
                (2.23,.49,1.80),(2.64,.34,1.39),(2.98,.04,.64)]
    vertices = []
    width = 6
    for x, top, reach in stations:
        vertices.extend([(x,-.36,-.185),(x,top,-.185),(x,top-.035,-.40),
                         (x,top*.50,-reach*.54-.12),(x,-.045,-reach),(x,-.36,-reach)])
    faces = [tuple(reversed(range(width))), tuple(range(len(vertices)-width,len(vertices)))]
    for i in range(len(stations)-1):
        for j in range(width):
            a = i*width+j
            b = i*width+(j+1)%width
            faces.extend(((a,b,b+width),(a,b+width,a+width)))
    fill.add(vertices, faces, port.CONCRETE)
    kit.emit(fill, "RetainedBank", root, material)
    return root


def cap(group, start, stop, top, tint):
    run_section(group, start+.004, stop-.004,
                [(-.216,top-.093),(.17,top-.093),(.183,top-.074),(.183,top-.027),
                 (.147,top-.012),(-.212,top),(-.23,top-.017),(-.23,top-.068)], tint)


def grass_blade(group, x, y, z, height, bend, width):
    # A rooted wedge with a midrib and drooping tip; the sheltered grass
    # rises just above the edge. Keep both normals valid at grazing angles.
    dx, dz = bend
    length = math.hypot(dx, dz)
    wx, wz = -dz / length * width, dx / length * width
    group.add([(x-wx,y,z-wz),(x+wx,y,z+wz),
               (x+dx*.30-wx*.65,y+height*.8,z+dz*.30-wz*.65),
               (x+dx*.30+wx*.65,y+height*.8,z+dz*.30+wz*.65),
               (x+dx*.27,y+height*.84,z+dz*.27),(x+dx,y+height,z+dz)],
              [(0,1,4),(1,3,4),(4,2,0),(0,2,3,1),(4,3,5),(2,4,5),(2,5,3)], port.PAINT)


def court_edge(material):
    root = port.empty("RidgeCourtEdge")
    stone, repair, iron, grass = [kit.geometry(s) for s in ("Concrete", "RepairMortar", "SteelDark", "Foliage")]
    section = [(-.265,-.28),(-.03,-.28),(-.03,.234),(-.043,.266),(-.070,.287),(-.238,.29),(-.265,.264)]
    # An old low bedding edge stops before the front door. One short
    # replacement block sits flush between the two original courses.
    for a,b,group,tint in ((-1.90,-.74,stone,port.CONCRETE),(-.73,-.29,repair,port.ROPE),
                           (-.278,.79,stone,port.CONCRETE),(.803,1.90,stone,port.CONCRETE)):
        run_section(group, a, b, section, tint)
    run_section(stone, .75, 1.55, [(-.035,-.18),(.36,-.18),(.36,.028),(.324,.045),(-.035,.066)], port.CONCRETE)
    for x in (.87,1.45):
        kit.box(iron, (x,.067,.18), (.085,.016,.09), port.METAL, .003)
        kit.box(iron, (x,.139,.18), (.032,.157,.032), port.DARK, .004)
    kit.box(iron, (1.16,.218,.18), (.64,.048,.018), port.METAL, .003)
    rng = random.Random(623)
    for index, tuft in enumerate((-1.69,-1.49,-1.16,-.99,-.83,.12,.24,.43)):
        for blade in range(6):
            x = tuft + rng.uniform(-.05,.05)
            z = -.292 + rng.uniform(-.017,.012)
            bend = (rng.uniform(-.11,.11), rng.uniform(-.031,-.011))
            tip = .48 if index == 3 and blade == 2 else rng.uniform(.14,.42)
            grass_blade(grass, x, -.03, z, tip+.03, bend, rng.uniform(.006,.012))
    kit.emit(stone, "BeddingEdgeAndThresholdStone", root, material)
    kit.emit(repair, "ReplacementEdging", root, material)
    kit.emit(iron, "FixedBootScraper", root, material)
    kit.emit(grass, "ShelteredGrass", root, material)
    return root


def slab(group, x, z, rx, rz, base, crown, angle, seed):
    # Coarse local bedding splits into angular flakes with one sheared side.
    # Asymmetric plan and off-centre top keep them out of the cobble grammar.
    rng = random.Random(seed)
    shape = [(-.99,-.35),(-.56,-.94),(.26,-1),(.96,-.51),(1,.36),(.45,.90),(-.49,1),(-1,.44)]
    vertices = []
    cosine, sine = math.cos(angle), math.sin(angle)
    for layer in (0,1,2):
        for sx, sz in shape:
            factor = rng.uniform(.76,.94) if layer == 0 else (rng.uniform(.92,1) if layer == 1 else rng.uniform(.57,.84))
            px, pz = sx*rx*factor, sz*rz*factor
            if layer == 2:
                px -= rx*.12
            y = base if layer == 0 else ((base+crown)*.5 if layer == 1 else crown) + rx*.065*sx - rz*.09*sz
            vertices.append((x+px*cosine-pz*sine, y, z+px*sine+pz*cosine))
    count = len(shape)
    faces = [tuple(reversed(range(count))), tuple(range(2*count,3*count))]
    for row in range(2):
        for i in range(count):
            a = row*count+i
            b = row*count+(i+1)%count
            faces.extend(((a,b,b+count),(a,b+count,a+count)))
    group.add(vertices, faces, port.CONCRETE)


def bearing_talus(material):
    root = port.empty("RidgeBearingTalus")
    rock = kit.geometry("Rock")
    # One exposed shelf, its separated leaf and the tapered fall of chips.
    slab(rock, -.50,-.10,.94,.55,-.45,.493,-.14,81)
    slab(rock, .45,-.09,.67,.37,-.32,.26,-.19,127)
    slab(rock, -.83,.29,.65,.30,-.26,.22,-.10,55)
    slab(rock, -.12,.425,.42,.16,-.21,.15,-.22,67)
    rng = random.Random(326)
    for i in range(17):
        x = rng.uniform(-1.26,1.22)
        z = rng.uniform(.35,.62) if i < 11 else rng.uniform(-.59,-.37)
        rx = rng.uniform(.09,.25)
        slab(rock, x,z,rx,rx*rng.uniform(.38,.73),-.12,rx*rng.uniform(.27,.74),rng.uniform(-.5,.35),i)
    kit.emit(rock, "BedrockAndShaleFlakes", root, material)
    return root


def plinth_repair(material):
    root = port.empty("RidgePlinthRepair")
    mortar, seam = kit.geometry("RepairMortar"), kit.geometry("DampMortar")
    # The old facing remains visible around a trowelled repair. Its edge
    # follows two lost courses, returning irregularly to the footing.
    contour = [(-1.03,.14),(-.965,.13),(-.952,.060),(-.722,.070),(-.695,.009),
               (-.362,0),(-.345,.023),(.020,.018),(.045,.064),(.315,.055),
               (.345,.095),(.692,.090),(.681,.148),(.872,.139),(.883,.22),
               (1.03,.23),(1.02,.419),(.969,.433),(.965,.568),(.663,.557),
               (.641,.662),(.372,.674),(.354,.716),(.105,.712),(.091,.77),
               (-.222,.78),(-.24,.746),(-.44,.753),(-.455,.69),(-.682,.696),
               (-.710,.636),(-.865,.622),(-.879,.473),(-.977,.467),(-.971,.31),(-1.016,.298)]

    def relief(group, outline, rear, face, tint):
        vertices = [(x, y, z) for z in (rear, face) for x, y in outline]
        count = len(outline)
        faces = [tuple(reversed(range(count))), tuple(range(count, 2*count))]
        faces += [(i,(i+1)%count,count+(i+1)%count,count+i) for i in range(count)]
        group.add(vertices, faces, tint)

    relief(mortar, contour, 0, .031, port.CONCRETE)
    # A short damp key at the base of the repair, never an isolated black
    # mark. It meets the lower edge and has a quieter response than metal.
    relief(seam, [(-.514,.016),(-.474,.017),(-.468,.13),(-.478,.183),(-.482,.292),
                  (-.495,.306),(-.495,.195),(-.51,.148)], .030, .036, port.ROPE)
    kit.emit(mortar, "TrowelledPlinth", root, material)
    kit.emit(seam, "BaseSeam", root, material)
    return root


def garden_verge(material):
    root = port.empty("RidgeGardenVerge")
    soil, rock, bark, foliage = [kit.geometry(s) for s in ("Soil", "Rock", "Bark", "Foliage")]
    # An irregular lens of root earth. Its side and end seams disappear
    # below the street datum. No flat raised flowerbed or retaining rim.
    stations = [(-4.79,-.06,.12,.01),(-4.41,-.43,.52,.085),(-3.67,-.84,.77,.17),
                (-2.88,-1.08,.91,.22),(-2.11,-1.29,.98,.23),(-1.25,-1.22,.91,.195),
                (-.33,-1.06,1.035,.16),(.66,-.88,.86,.14),(1.41,-.98,.79,.16),
                (2.55,-.88,.85,.15),(3.25,-.64,.71,.12),(4.05,-.41,.44,.07),(4.79,-.02,.14,.006)]
    vertices = []
    for x, back, front, crown in stations:
        centre = (back+front)*.5
        vertices.extend([(x,-.55,back),(x,-.075,back),(x,crown*.72,(back+centre)*.5),
                         (x,crown,centre),(x,crown*.52,(front+centre)*.5),
                         (x,-.05,front),(x,-.55,front)])
    width = 7
    faces = [tuple(reversed(range(width))), tuple(range(len(vertices)-width,len(vertices)))]
    for i in range(len(stations)-1):
        for j in range(width):
            a = i*width+j
            b = i*width+(j+1)%width
            faces.extend(((a,b,b+width),(a,b+width,a+width)))
    soil.add(vertices, faces, port.WOOD)

    # Some bedding stones remain in line at the public edge. The joints
    # break twice where the bank runs down into the surrounding earth.
    for n,(x,z,size,crown,turn) in enumerate(((-4.22,.41,.28,.14,-.28),(-3.67,.62,.40,.24,-.18),
            (-2.87,.75,.49,.27,-.04),(-2.10,.79,.33,.22,.03),(-1.58,.75,.25,.16,.08),
            (-.65,.89,.25,.18,-.23),(-.06,.92,.37,.23,.06),(.57,.84,.32,.19,.16),
            (1.77,.72,.21,.16,-.15),(2.25,.74,.30,.17,.05),(2.94,.70,.40,.21,.12),
            (3.59,.52,.29,.13,.30),(4.20,.26,.18,.095,.34))):
        slab(rock,x,z,size,size*.59,-.32,crown,turn,381+n)
    rng = random.Random(912)
    for x,z,height in ((-3.91,-.03,.19),(-3.26,-.21,.36),(-3.10,.14,.25),(-2.71,.29,.39),
                       (-1.58,.27,.29),(-1.20,-.45,.35),(-.95,-.26,.38),(-.32,.48,.24),
                       (.32,.29,.29),(.82,.43,.35),(1.10,.33,.34),(1.44,.12,.21),
                       (2.22,-.14,.32),(2.46,-.20,.36),(2.82,.20,.27),(3.47,.11,.20),(3.94,-.03,.14)):
        for _ in range(10):
            grass_blade(foliage, x+rng.uniform(-.13,.13),.045,z+rng.uniform(-.09,.09),
                        height*rng.uniform(.88,1.45), (rng.uniform(-.17,.17),rng.uniform(.03,.16)),
                        rng.uniform(.012,.027))

    # Old canes from one stool split into forks of unequal height. Dense
    # individual sprays overlap into a broken crown at the player's distance.
    base_x,base_z = -2.16,-.29
    for i,(dx,dz,height) in enumerate(((-.77,-.10,.99),(-.42,-.44,1.17),(-.07,-.40,1.31),
                                       (.34,-.27,1.14),(.63,.04,1.12),(.33,.31,1.27),
                                       (-.43,.38,1.14),(-.16,.11,1.37))):
        start = (base_x+dx*.10,.13,base_z+dz*.12)
        knee = (base_x+dx*.41,height*.44,base_z+dz*.49)
        fork = (base_x+dx*.67,height*.68,base_z+dz*.70)
        bark.rod(start,knee,.041 if i % 3 == 0 else .030,port.WOOD,7,.026)
        bark.rod(knee,fork,.026,port.WOOD,6,.017)
        for side in (-1,1):
            tx = base_x+dx+side*.125
            tz = base_z+dz+side*(.12 if i % 2 else -.10)
            top = height - (.065 if side == -1 else 0)
            twig = (tx,top,tz)
            bark.rod(fork,twig,.017,port.WOOD,5,.006)
            crown_spray(foliage,twig,rng)

    kit.emit(soil, "RootedSoil", root, material)
    kit.emit(rock, "BrokenBeddingStones", root, material)
    kit.emit(bark, "ForkedBrushwood", root, material)
    kit.emit(foliage, "LeafSpraysAndRoughGrass", root, material)
    return root


def crown_spray(foliage, twig, rng):
    x,y,z = twig
    # A low-poly, irregular core carries the optical density of the small
    # leaves inside each spray. Pointed outer pairs turn out of the clump.
    points = [(x-.02,y-.115,z+.01),(x+.02,y+.145,z-.02)]
    ring = []
    for i in range(9):
        a = math.tau*i/9 + rng.uniform(-.08,.08)
        dx,dz = math.cos(a),math.sin(a)
        reach = rng.uniform(.17,.26)
        p = (x+dx*reach,y+rng.uniform(-.055,.066),z+dz*reach*.76)
        ring.append(p)
        sx,sy,sz = p
        reach2 = rng.uniform(.09,.16)
        wx,wz = -dz*.044,dx*.044
        foliage.add([(sx-dx*.06,sy,sz-dz*.06),(sx+wx,sy+.014,sz+wz),
                     (sx+dx*reach2,sy+rng.uniform(-.045,.064),sz+dz*reach2),
                     (sx-wx,sy-.001,sz-wz),(sx,sy+.033,sz)],
                    [(0,3,2,1),(0,1,4),(1,2,4),(2,3,4),(3,0,4)],port.PAINT)
    faces = []
    for i in range(9):
        a = 2+i
        b = 2+(i+1)%9
        faces.extend(((0,b,a),(1,a,b)))
    foliage.add(points+ring,faces,port.PAINT)


def build():
    material = port.base.material("RidgeEdgeSource")
    roots = [retaining_end(material), court_edge(material), bearing_talus(material), plinth_repair(material),
             garden_verge(material)]
    port.validate_surfaces(roots)
    models = [port.describe(root) for root in roots]
    envelopes = [
        ((-3.,-.55,-2.65),(3.,1.22,.57),.015,1750),
        ((-1.90,-.28,-.34),(1.90,.48,.36),.035,2500),
        ((-1.50,-.45,-.74),(1.50,.58,.74),.28,2200),
        ((-1.03,0,0),(1.03,.78,.036),.002,200),
        ((-4.80,-.55,-1.35),(4.80,1.58,1.20),.20,6200)]
    roles = set()
    for root, model, (lower, upper, tolerance, budget) in zip(roots, models, envelopes):
        for key, target in (("bounds_min",lower),("bounds_max",upper)):
            if any(abs(a-b) > tolerance for a,b in zip(model[key], target)):
                raise ValueError(f"{root.name} outside measured {key}: {model[key]}")
        renderers = [obj for obj in root.children_recursive if obj.type == "MESH"]
        model["material_roles"] = sorted(obj.name.rsplit("__",1)[1].split(".")[0] for obj in renderers)
        if len(set(model["material_roles"])) != len(renderers) or model["triangles"] > budget:
            raise ValueError(f"Ridge edge mesh budget exceeded: {model}")
        if any(a < b-.001 for a,b in zip(model["bounds_min"],lower)) or any(
                a > b+.001 for a,b in zip(model["bounds_max"],upper)):
            raise ValueError(f"Ridge edge escaped its placement envelope: {model}")
        roles.update(model["material_roles"])
    return roots, {"design_id":"city_ridge_passive_edges_v1", "generator":Path(__file__).name,
        "axes":"Unity metres, X along edge, +Z to low side, ground at root Y=0",
        "semantic_uv_tiles_metres":{role:port.SURFACE_TILES[role] for role in sorted(roles)}, "models":models}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=OUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    port.base.reset()
    roots, data = build()
    if args.validate_only:
        if json.loads((args.model_dir / MANIFEST).read_text()) != data:
            raise ValueError("Ridge edge manifest differs")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            port.base.export(root, args.model_dir / (root.name+".fbx"))
        (args.model_dir / MANIFEST).write_text(json.dumps(data, indent=2)+"\n")
    port.base.reset()
    if build()[1] != data:
        raise ValueError("Ridge edge deterministic rebuild differs")
    print("CITY RIDGE: measured stepped concrete, household edge and dry rock with verified metric UVs")


if __name__ == "__main__":
    main()
