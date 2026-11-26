#!/usr/bin/env python3
"""Three City street outfits on the same bind pose and rig as the fair child."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy

spec = importlib.util.spec_from_file_location(
    "child_model", Path(__file__).with_name("build-city-fair-child-3d-model.py"))
child = importlib.util.module_from_spec(spec)
spec.loader.exec_module(child)

OUTFITS = ("BobJacket", "BraidedCoat", "BibOveralls")
FACE = "StreetGirlFace"
MODEL = "StreetGirls"
FINISHES = {
    "BobJacket": (("workJacket", (91, 109, 94)), ("binding", (68, 81, 73)),
                  ("cardigan", (118, 114, 93)), ("flannel", (93, 112, 95)),
                  ("slacks", (66, 67, 66)), ("shoe", (74, 63, 54)),
                  ("looseHair", (74, 54, 40)), ("rubber", (45, 44, 41)),
                  ("hardware", (124, 110, 83))),
    "BraidedCoat": (("woolCoat", (137, 85, 69)), ("binding", (104, 64, 57)),
                    ("cardigan", (92, 78, 68)), ("flannel", (132, 83, 68)),
                    ("workTrousers", (82, 76, 77)), ("winterBoot", (80, 61, 49)),
                    ("boundHair", (52, 41, 37)), ("rubber", (46, 42, 40)),
                    ("hardware", (111, 83, 70))),
    "BibOveralls": (("cardigan", (69, 86, 111)), ("binding", (89, 73, 49)),
                    ("shirt", (102, 105, 104)), ("flannel", (156, 124, 68)),
                    ("rib", (147, 115, 62)), ("workBoot", (86, 70, 56)),
                    ("boundHair", (106, 75, 46)), ("rubber", (47, 46, 43)),
                    ("hardware", (126, 105, 72))),
}
FACES = [
    (43, 85, 64, 85, 5, 15, (87, 96, 71), -1, False),
    (42, 85, 65, 83, 4, 19, (92, 67, 48), 1, True),
    (45, 83, 63, 84, 5, 17, (75, 88, 91), 0, False),
]


def scalp(style):
    sides = 24
    vertices = []
    for row in range(11):
        height = row / 10
        for index in range(sides):
            angle = index * math.tau / sides
            side, depth = math.cos(angle), math.sin(angle)
            front, rear = max(0., depth), max(0., -depth)
            if style == "BobJacket":
                hairline = 1.088 + .143 * front**4 + .011 * side * front - .012 * rear
            else:
                hairline = 1.186 + .055 * front - .075 * rear + .012 * side * front
            y = hairline + (child.HEIGHT - hairline) * height
            # Below the temple the bob follows the haircut, keeping its edge outside the cheeks.
            sample = min(1.292, max(y, 1.147 if style == "BobJacket" else 1.105))
            low, high = next((lo, hi) for lo, hi in zip(child.HEAD_PROFILE, child.HEAD_PROFILE[1:])
                             if sample <= hi[0])
            blend = (sample - low[0]) / (high[0] - low[0])
            width, length, offset = [a + (b-a) * blend for a, b in zip(low[1:], high[1:])]
            lift = .003 + .006 * math.sin(math.pi * height)
            lift += .0015 * math.cos(7 * angle + height) * (1 - height)
            vertices.append(((width + lift) * side, y, offset + (length + lift) * depth))
    faces = [tuple(range(sides)), tuple(reversed(range(10*sides, 11*sides)))]
    for row in range(10):
        for index in range(sides):
            a = row * sides + index
            b = row * sides + (index + 1) % sides
            faces.append((b, a, a + sides, b + sides))
    return vertices, faces


def make_hair(part, outfit):
    part.add(scalp(outfit), "hair", 6)
    if outfit == "BraidedCoat":
        for sign in (-1, 1):
            for link in range(6):
                y = 1.169 - .017 * link
                x = sign * (.087 + .004 * link)
                for side in (-1, 1):
                    center = (x + side*.007, y + side*.004*(-1)**link, -.050-.005*link)
                    strand = child.fair.ellipsoid(center, (.024-.001*link, .028, .029-.002*link), 8, 6)
                    part.add(child.bp.u_rotated_about(strand, (0, 0, side*30), center), "head", 6)
            part.ball((sign*.108, 1.070, -.074), (.025, .010, .023), "head", 1, 10, 5)
    elif outfit == "BibOveralls":
        part.add(child.limb_surface(
            [(0, 1.177, -.079), (0, 1.149, -.114), (.008, 1.101, -.127), (.020, 1.060, -.123)],
            [.029, .049, .043, .011], [.027, .042, .034, .011], sides=12, steps=3), "head", 6)
        part.ball((0, 1.166, -.097), (.032, .026, .024), "head", 1, 10, 6)
        part.add(child.limb_surface(
            [(-.046, 1.251, .052), (-.071, 1.226, .061), (-.083, 1.195, .050)],
            [.021, .016, .004], [.014, .012, .004], sides=8, steps=3), "hair", 6)


def make_outfit(outfit, pattern):
    parts = []
    atlas = outfit + "Atlas"

    def part(role):
        result = child.Part("OUTFIT_" + outfit + "_" + role, atlas, outfit)
        parts.append(result)
        return result

    # Reuse the articulated sleeves and sole geometry with outfit-specific finishes.
    for original in child.make_outfit(pattern):
        role = original.name.split("_", 2)[2]
        if role.startswith(("Sleeve.", "Cuff.", "Boots.")):
            target = part(role)
            for geometry, binding, tile, uv in original.components:
                finish = 0 if role.startswith("Sleeve.") else 1 if role.startswith("Cuff.") else tile
                target.add(geometry, binding, finish, uv)
    make_hair(part("Hair"), outfit)
    part("Trousers").add(child.trousers_surface("Raincoat" if outfit == "BraidedCoat" else "Vest"),
                         "trousers", 4)

    if outfit == "BobJacket":
        profile = [(.696,.111,.076,0), (.707,.117,.078,0), (.755,.114,.076,0),
                   (.847,.124,.074,0), (.936,.142,.075,0), (.982,.143,.065,0),
                   (1.013,.094,.047,0), (1.027,.048,.035,0)]
    elif outfit == "BraidedCoat":
        profile = [(.579,.128,.083,-.004), (.590,.134,.089,-.004), (.638,.132,.085,-.004),
                   (.720,.119,.078,0), (.823,.121,.076,0), (.916,.137,.074,0),
                   (.984,.145,.069,0), (1.019,.098,.051,0), (1.030,.047,.036,0)]
    else:
        profile = [(.687,.106,.069,0), (.706,.109,.074,0), (.769,.108,.071,0),
                   (.840,.121,.070,0), (.918,.134,.070,0), (.982,.139,.064,0),
                   (1.015,.090,.047,0), (1.029,.047,.034,0)]
    long_coat = outfit == "BraidedCoat"
    binding = "raincoat" if long_coat else "torso"
    part("Body").add(child.loft(profile, 24), binding)
    collar = part("Collar")
    collar.add(child.torus_y((0, 1.031, .003), .038, .007, 16, 6, (1.17, 1.3, 1)), "neckblend", 1)
    trim = part("Trim")
    if outfit == "BibOveralls":
        # The bib grows from the trouser waist and fastens to straps over the sweater.
        trim.add(child.loft([(.687,.091,.015,.057), (.734,.088,.015,.064),
                             (.822,.071,.013,.072), (.885,.065,.009,.074)], 16), "torso", 4)
        trim.add(child.loft([(.689,.093,.019,-.054), (.725,.089,.020,-.057),
                             (.775,.074,.012,-.066), (.809,.061,.006,-.066)], 16), "torso", 4)
        trim.box((0,.803,.085), (.086,.067,.012), "torso", 4)
        trim.rod((-.041,.836,.092),(.041,.836,.092),.002, "torso", 3)
        for sign in (-1, 1):
            nodes = [(sign*.050,.854,.084), (sign*.066,.956,.069), (sign*.077,1.022,.018),
                     (sign*.073,1.010,-.037), (sign*.060,.921,-.069), (sign*.049,.785,-.077)]
            trim.add(child.limb_surface(nodes, [.023]*6, [.012]*6, sides=8, steps=3), "torso", 4)
            trim.box((sign*.053,.876,.092), (.025,.019,.006), "torso", 8)
    else:
        trim.add(child.loft([(y,.009 if long_coat else .004,.004,z+depth)
                             for y,_,depth,z in profile[1:-1]], 8), binding, 1)
        for sign in (-1, 1):
            corner = (sign*.048, 1.010, .043)
            lapel = child.bp.u_box(corner, (.058,.037,.013), .006)
            collar.add(child.bp.u_rotated_about(lapel, (26, sign*22, sign*26), corner), "chest", 3)
            trim.rod((sign*.095,.754,.058),(sign*.055,.782,.077),.004,binding,1)
        if long_coat:
            for y, _, depth, z in profile[2:7]:
                trim.ball((.015,y,depth+z+.004), (.019,.019,.010), binding, 8, 10, 6)
        else:
            trim.box((0,.977,.069), (.011,.023,.006), "chest", 8)
    return parts


def recipe():
    body = [part for part in child.make_body() if part.visible]
    body[0].texture = FACE
    return body + [part for outfit, pattern in zip(OUTFITS, ("Vest","Raincoat","Quilted"))
                   for part in make_outfit(outfit, pattern)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--verify-fbx", action="store_true")
    parser.add_argument("--model-dir", type=Path, default=child.MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=child.SOURCE_DIR)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    for outfit, finishes in FINISHES.items():
        child.FINISHES[outfit + "Atlas"] = finishes
    model_dir = args.model_dir
    parts = recipe()
    pigments = {outfit + "Atlas": child.paint_texture(outfit + "Atlas") for outfit in OUTFITS}
    pigments[FACE] = child.face_atlas(FACES)
    textures = dict(pigments, ChildSkin=child.paint_texture("ChildSkin"))
    generator = str(Path(__file__).relative_to(child.ROOT))
    payload = child.manifest(parts, [], textures, OUTFITS, generator, "1.0.0")
    if payload != child.manifest(recipe(), [], textures, OUTFITS, generator, "1.0.0"):
        raise RuntimeError("Street girl geometry is not deterministic")
    if not args.validate_only and not args.verify_fbx:
        for name, canvas in pigments.items():
            canvas.write_png(model_dir / (name + ".png"))
        bpy.ops.wm.read_factory_settings(use_empty=True)
        materials = {name: child.create_material(name, child.MODEL_DIR if name == "ChildSkin" else model_dir)
                     for name in textures}
        root, _, objects, _ = child.build_model(parts, model_dir, materials)
        child.export_model(root, model_dir / (MODEL + ".fbx"))
        (model_dir / (MODEL + "3D.json")).write_text(json.dumps(payload, indent=2)+"\n", encoding="utf-8")
        child.select_outfit(objects, parts, OUTFITS[0])
        for obj in objects:
            obj.hide_set(obj.hide_render)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.file.pack_all()
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / (MODEL + "3D.blend")), check_existing=False)
    if not args.validate_only:
        child.verify_fbx(model_dir, payload, verify_props=False, model_name=MODEL)
    print("CITY STREET GIRLS MODEL CONTRACT OK")
    for outfit in payload["outfits"]:
        print(outfit["name"], outfit["visible_triangles"], "visible triangles")


if __name__ == "__main__":
    main()
