#!/usr/bin/env python3
"""Deterministic passive, fixed-metre household work props for Alpine Village.

Authoring is in Unity metres (+Z is the working/front side); export swaps Y/Z
and reverses winding. No behaviour, collision, text, lights or animation.
Each prop has its own ground origin except Log (centre), StationLid (hinge)
and StationStrap (upper fastening). The three top logs are separate instances.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp

VERSION = "1.1.0"
DESIGN_ID = "village_life_work_props_v1"
EXPECTED_PROP_COUNT = 17
STAGE1_SIGNATURE = "5ebf8898e36129174945a3f8e01f7a09c23bfca220e137c3efc3bcc32940eabf"
COLORS = {"Wood": (.24, .185, .14, 1), "Repair": (.36, .29, .215, 1),
          "Bark": (.22, .19, .16, 1), "Endgrain": (.47, .36, .25, 1),
          "Iron": (.25, .235, .21, 1), "Cloth": (.36, .345, .28, 1),
          "Snow": (.82, .845, .83, 1)}
SURFACES = {"Wood": "Timber", "Repair": "Timber", "Bark": "BarkAndDeadwood",
            "Endgrain": "Timber", "Iron": "RustedIron", "Cloth": "Cloth", "Snow": "WindSnow"}


def box(center, size, bevel=.008):
    return bp.u_box(center, size, min(bevel, min(size) * .23))


def beam(start, end, width, depth=None):
    start, end = Vector(start), Vector(end)
    delta = end - start
    geometry = box((0, 0, 0), (width, delta.length, depth or width))
    rotation = Vector((0, 1, 0)).rotation_difference(delta.normalized())
    return ([tuple(rotation @ Vector(v) + (start + end) * .5) for v in geometry[0]], geometry[1])


def cylinder(center, diameter, height, sides=9):
    return bp.u_cylinder(center, (diameter, height * .5, diameter), sides)


def wood_log(center, length=.46, diameter=.12):
    body = kit.translated(bp.u_rotated(cylinder((0, 0, 0), diameter, length), (0, 0, 90)), center)
    ends = []
    for side in (-1, 1):
        ends.append(kit.translated(bp.u_rotated(cylinder((0, 0, 0), diameter * .82, .006),
                                               (0, 0, 90)),
                                   (center[0] + side * (length * .5 + .001), center[1], center[2])))
    return body, kit.merge_all(ends)


def shovel_blade():
    """A closed, faceted pressed-metal scoop, curved across its real width."""
    columns, rows = 7, 4
    front = []
    for row in range(rows):
        for column in range(columns):
            x = -.23 + .46 * column / (columns - 1)
            y = row * .092 + .017 * (abs(x) / .23) ** 2
            z = .025 + .061 * (abs(x) / .23) ** 2 + .022 * (row / (rows - 1)) ** 2
            front.append((x, y, z))
    vertices = front + [(x, y, z - .018) for x, y, z in front]
    back_offset = len(front)
    faces = []
    for row in range(rows - 1):
        for column in range(columns - 1):
            a = row * columns + column
            face = (a, a + 1, a + columns + 1, a + columns)
            faces.append(face)
            faces.append(tuple(v + back_offset for v in reversed(face)))
    border = (list(range(columns)) + [r * columns + columns - 1 for r in range(1, rows)] +
              list(range(rows * columns - 2, (rows - 1) * columns - 1, -1)) +
              [r * columns for r in range(rows - 2, 0, -1)])
    for a, b in zip(border, border[1:] + border[:1]):
        faces.append((b, a, a + back_offset, b + back_offset))
    return vertices, faces


def make_props():
    props = []

    def add(kind, groups, anchors=None, origin="ground"):
        parts = []
        for role, solids in groups.items():
            # Check each constituent, not just a union whose total can mask
            # one inverted component. Closed edges are checked after joining.
            for solid in solids:
                assert bp.signed_volume(solid) > 1e-10, (kind, role, bp.signed_volume(solid))
            parts.append(dict(mesh=f"GEO_{kind}_{role}", role=role, surface=SURFACES[role],
                              tint=COLORS[role], geometry=kit.merge_all(solids)))
        props.append(dict(kind=kind, origin=origin, parts=parts, anchors=anchors or {}))

    wood, repair, snow = [], [], []
    for x in (-1.21, 1.21):
        for z in (-.49, .47):
            wood.append(box((x, .98, z), (.13, 1.96, .13)))
        wood.append(beam((x, 1.4, -.49), (x, 1.91, .0), .08))
    for y in (.3, 1.1, 1.75):
        wood.append(box((0, y, -.51), (2.48, .13, .07)))
    for x in (-.99, -.64, -.28, .08, .43, .79, 1.12):
        wood.append(box((x, 1.0, -.55), (.28, 1.72, .04)))
    wood.append(beam((-1.18, .2, -.59), (1.18, 1.83, -.59), .10, .035))
    for x in (-1.3, -.98, -.65, -.32, .01, .34, .67, 1.0, 1.31):
        roof = bp.u_rotated(box((0, 0, 0), (.315, .048, 1.3)), (-4.5, 0, 0))
        wood.append(kit.translated(roof, (x, 2.025, 0)))
    wood.append(box((0, 1.95, .50), (2.56, .13, .10)))
    repair.append(box((-.43, .76, -.588), (.14, .84, .03)))
    repair.append(box((.71, 1.69, -.598), (.59, .14, .035)))
    cap = bp.u_tapered_cylinder((0, 0, 0), (2.77, .051, 1.07), .88, 14)
    cap_vertices = []
    for i, (x, y, z) in enumerate(cap[0]):
        # Unequal wind-softened edges, with a deeper deposit away from the
        # exposed lip. This is one snow mass, not white rectangular roof tiles.
        x *= 1.0 - .027 * ((i * 7 + 2) % 4)
        z *= 1.0 - .035 * ((i * 5 + 1) % 3)
        cap_vertices.append((x, y + 2.095 + z * .0787, z - .06))
    snow.append((cap_vertices, cap[1]))
    add("WoodShelter", {"Wood": wood, "Repair": repair, "Snow": snow},
        {"Stack": (0, 0, -.12), "Work": (0, 0, 1.02)})

    bark, ends, rails = [], [], []
    for z in (-.27, .27):
        rails.append(box((0, .045, z), (1.75, .09, .10)))
    for row in range(7):
        for column in range(3):
            for depth in range(3):
                center = ((column - 1) * .55 + (.018 if row % 2 else 0),
                          .15 + row * .089, (depth - 1) * .17)
                body, cap = wood_log(center, .51 - ((column + row) % 3) * .012, .142)
                bark.append(body)
                ends.append(cap)
    add("LogStack", {"Bark": bark, "Endgrain": ends, "Wood": rails},
        {f"Log{i}": ((i - 1) * .55, .815, .17) for i in range(3)})
    body, cap = wood_log((0, 0, 0))
    add("Log", {"Bark": [body], "Endgrain": [cap]},
        {"Grip": (0, 0, 0)}, origin="centre")

    basket, reinforcement = [box((0, .019, 0), (.48, .038, .29))], []
    # Open slatted body; small alternating strips read as old wicker without
    # closing the visible opening or filling it with a solid brown box.
    for side in (-1, 1):
        for i in range(8):
            x = -.245 + i * .070
            basket.append(beam((x * .85, .035, side * .13), (x, .29, side * .17), .025, .022))
        for i in range(5):
            z = -.14 + i * .07
            basket.append(beam((side * .22, .035, z * .9), (side * .28, .29, z), .024, .022))
        for row in range(4):
            y = .072 + row * .061
            ratio = y / .29
            basket.append(box((0, y, side * (.13 + .04 * ratio)), (.45 + .11 * ratio, .024, .019)))
            basket.append(box((side * (.22 + .06 * ratio), y, 0), (.019, .024, .27 + .07 * ratio)))
        basket.append(box((0, .30, side * .172), (.578, .030, .030)))
        basket.append(box((side * .277, .30, 0), (.030, .030, .36)))
        # Side handles, clear openings: hand contact is their upper bar centre.
        for z in (-.095, .095):
            reinforcement.append(beam((side * .279, .29, z), (side * .29, .43, z * .77), .027))
        reinforcement.append(beam((side * .29, .43, -.073), (side * .29, .43, .073), .027))
    reinforcement.append(box((-.15, .08, .171), (.11, .028, .027)))
    add("Basket", {"Wood": basket, "Repair": reinforcement},
        {"LeftGrip": (-.29, .43, 0), "RightGrip": (.29, .43, 0), "Rest": (0, 0, 0),
         "Content0": (0, .19, -.11), "Content1": (0, .19, .02), "Content2": (0, .30, -.045)})

    stand = []
    for x in (-.26, .26):
        for z in (-.18, .18):
            stand.append(beam((x, 0, z), (x * .88, .34, z * .88), .065))
    for x in (-.225, 0, .225):
        stand.append(box((x, .3575, 0), (.213, .045, .51)))
    stand.append(box((0, .14, 0), (.54, .06, .07)))
    add("BasketStand", {"Wood": stand, "Repair": [box((.24, .21, .19), (.04, .15, .02))]},
        {"BasketRest": (0, .38, 0)})

    stump = bp.u_tapered_cylinder((0, .21, 0), (.41, .21, .43), .91, 11)
    top = cylinder((0, .421, 0), .366, .006, 11)
    add("ChoppingBlock", {"Bark": [stump], "Endgrain": [top]},
        {"AxeRest": (0, .422, 0)})
    axe_handle = beam((0, 0, 0), (.035, .61, 0), .033, .030)
    axe_head = box((-.025, .54, 0), (.22, .075, .045))
    blade = beam((-.11, .505, 0), (-.15, .59, 0), .025, .10)
    add("Axe", {"Wood": [axe_handle], "Iron": [axe_head, blade]},
        {"Grip": (.015, .25, 0), "Blade": (-.14, .55, 0)})

    runners, deck = [], []
    for x in (-.29, .29):
        runners.append(beam((x, .035, -.63), (x, .035, .52), .045, .08))
        runners.append(beam((x, .035, .52), (x, .17, .72), .045, .07))
        for z in (-.42, .35):
            deck.append(beam((x, .075, z), (x, .25, z), .055))
    for x in (-.27, -.09, .09, .27):
        deck.append(box((x, .264, -.05), (.16, .045, 1.04)))
    for z in (-.43, .34):
        deck.append(box((0, .21, z), (.7, .075, .06)))
    add("UtilitySled", {"Wood": deck, "Iron": runners,
                        "Repair": [box((-.09, .292, .23), (.17, .016, .16))]},
        {"Load": (0, .2865, -.05), "Pull": (0, .17, .72)})

    crate, metal, repair = [], [], []
    for x in (-.33, .33):
        for z in (-.23, .23):
            crate.append(box((x, .23, z), (.07, .46, .07)))
    crate.append(box((0, .443, 0), (.88, .054, .67)))
    for y in (.51, .622, .738):
        for z in (-.285, .285):
            crate.append(box((0, y, z), (.83, .10, .035)))
        for x in (-.402, .402):
            crate.append(box((x, y, 0), (.033, .10, .59)))
    for x in (-.34, .34):
        for z in (-.314, .314):
            repair.append(box((x, .63, z), (.07, .34, .032)))
    for x in (-.25, .25):
        metal.append(box((x, .79, -.315), (.075, .04, .04)))
    add("StationCrate", {"Wood": crate, "Repair": repair, "Iron": metal},
        {"LidHinge": (0, .80, -.30), "StrapRoot": (0, .80, .315),
         "WorkLeft": (-.22, .80, .29), "WorkRight": (.22, .80, .29),
         "Interior": (0, .47, 0)})
    lid = [box(((i - 2) * .172, .017, .315), (.166, .034, .645)) for i in range(5)]
    add("StationLid", {"Wood": lid,
                       "Repair": [box((x, .047, .315), (.06, .025, .60)) for x in (-.29, .29)],
                       "Iron": [box((x, .005, 0), (.075, .024, .065)) for x in (-.25, .25)]},
        {"Hinge": (0, 0, 0), "LeftGrip": (-.22, .035, .61), "RightGrip": (.22, .035, .61)},
        origin="hinge; open by negative Unity X rotation")
    add("StationStrap", {"Cloth": [beam((0, .016, -.11), (0, 0, 0), .10, .014),
                                       beam((0, 0, 0), (0, -.25, .022), .10, .014)],
                         "Iron": [box((0, -.17, .034), (.075, .035, .023))]},
        {"Grip": (0, -.19, .034), "Fastener": (0, 0, 0)}, origin="upper fastening")

    # Stage two appends to the original catalog; the original eleven recipes,
    # origins and contacts are pinned by STAGE1_SIGNATURE below.
    shovel_wood = [cylinder((0, .601, 0), .035, .742, 10),
                   beam((-.088, .946, 0), (-.088, 1.05, 0), .028),
                   beam((.088, .946, 0), (.088, 1.05, 0), .028),
                   beam((-.088, 1.05, 0), (.088, 1.05, 0), .033)]
    shovel_metal = [shovel_blade(), cylinder((0, .293, 0), .047, .11, 10),
                    beam((-.087, .946, 0), (.087, .946, 0), .024)]
    add("Shovel", {"Wood": shovel_wood, "Iron": shovel_metal,
                   "Repair": [cylinder((0, .664, 0), .041, .034, 10)]},
        {"RightGrip": (0, 1.05, 0), "MainGrip": (0, 1.05, 0),
         "LeftGrip": (0, .65, 0), "MidGrip": (0, .65, 0),
         "BladeRest": (0, 0, .025), "Hook": (0, .90, 0)}, origin="blade bottom")

    open_basket = next(p for p in props if p["kind"] == "Basket")
    basket_wood = next(p["geometry"] for p in open_basket["parts"] if p["role"] == "Wood")
    basket_repair = next(p["geometry"] for p in open_basket["parts"] if p["role"] == "Repair")
    closed_lid = [box((0, .319, 0), (.558, .038, .338), .008)]
    for x in (-.21, -.07, .07, .21):
        closed_lid.append(box((x, .341, 0), (.11, .012, .319), .003))
    add("ClosedBasket", {"Wood": [basket_wood] + closed_lid,
                         "Repair": [basket_repair, box((.16, .352, .025), (.065, .011, .13), .002)],
                         "Cloth": [box((-.062, .354, 0), (.095, .016, .33), .003),
                                   box((-.062, .272, .181), (.094, .14, .012), .002)]},
        {"LeftGrip": (-.29, .43, 0), "RightGrip": (.29, .43, 0), "Rest": (0, 0, 0)})

    gate_posts, post_iron = [], []
    for x in (-.08, 1.15):
        gate_posts.append(box((x, .59, 0), (.14, 1.18, .16), .016))
        gate_posts.append(bp.u_rotated_about(box((x, 1.181, 0), (.156, .045, .174)),
                                             (-8, 0, 0), (x, 1.181, 0)))
        post_iron.append(box((x, .11, .084), (.122, .16, .018)))
    for y in (.29, .87):
        post_iron.append(box((-.005, y, 0), (.08, .048, .068)))
    # Short ordinary fence returns tie the working gate to its yard. They
    # extend away from the opening and never bridge the passable centre.
    for side_center, outer in ((-.36, -.605), (1.43, 1.675)):
        gate_posts.append(box((outer, .455, 0), (.10, .91, .12)))
        for y in (.27, .69):
            gate_posts.append(box((side_center, y, -.012), (.48, .070, .065)))
        for across in (-.15, 0, .15):
            gate_posts.append(box((side_center + across, .47, .018), (.085, .75, .035)))
    add("GatePosts", {"Wood": gate_posts, "Iron": post_iron,
                      "Repair": [box((1.15, .49, .085), (.121, .32, .026))]},
        {"Hinge": (0, 0, 0), "Latch": (1.065, .82, .065),
         "Passage": (.54, 0, 0), "Work": (.54, 0, .65)})

    leaf_wood, leaf_iron = [], []
    for i in range(7):
        x = .095 + i * .145
        height = .95 - (.016 if i % 3 == 1 else 0)
        leaf_wood.append(box((x, .1 + height * .5, 0), (.126, height, .043), .006))
    for y in (.30, .87):
        leaf_wood.append(box((.53, y, -.037), (1.025, .10, .043)))
        leaf_iron.append(box((.105, y, .031), (.22, .040, .020)))
    leaf_wood.append(beam((.065, .235, -.067), (1.0, .92, -.067), .076, .035))
    for y in (.76, .88):
        leaf_iron.append(box((.97, y, .047), (.043, .032, .050)))
    leaf_iron.append(beam((.97, .76, .065), (.97, .88, .065), .022))
    add("GateLeaf", {"Wood": leaf_wood, "Iron": leaf_iron,
                     "Repair": [box((.53, .545, .025), (.115, .42, .026))]},
        {"Hinge": (0, 0, 0), "Handle": (.97, .82, .065),
         "RightGrip": (.97, .82, .065), "LeftGrip": (.53, .87, .02)},
        origin="left hinge at ground; closed leaf extends along Unity +X")

    rack_wood, hooks = [], []
    for x in (-.215, .215):
        rack_wood.append(box((x, .525, -.045), (.068, 1.05, .08)))
        rack_wood.append(box((x, .030, .10), (.095, .06, .47)))
    for y in (.21, .87):
        rack_wood.append(box((0, y, -.01), (.50, .075, .075)))
    rack_wood.append(box((0, .05, .30), (.51, .10, .064)))
    for x in (-.052, .052):
        hooks.append(beam((x, .895, .025), (x, .88, .185), .016))
        hooks.append(beam((x, .88, .185), (x, .925, .185), .016))
    add("ShovelRack", {"Wood": rack_wood, "Iron": hooks,
                       "Repair": [box((.22, .7, .003), (.045, .17, .018))]},
        {"ShovelRest": (0, 0, .15), "Hook": (0, .90, .15)})

    mat = [box((0, .014, 0), (.90, .028, .55), .006)]
    for i in range(13):
        mat.append(box((-.415 + i * .069, .030, 0), (.048, .01, .515), .002))
    add("PorchMat", {"Cloth": mat}, {"Step": (0, .035, 0)})
    return props


def signature(props):
    payload = json.dumps((VERSION, DESIGN_ID, props), sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(payload.encode()).hexdigest()


def stage1_signature(props):
    payload = json.dumps(("1.0.0", DESIGN_ID, props[:11]), sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(payload.encode()).hexdigest()


def validate(props):
    assert len(props) == EXPECTED_PROP_COUNT and len({p["kind"] for p in props}) == EXPECTED_PROP_COUNT
    assert stage1_signature(props) == STAGE1_SIGNATURE, "Stage-one geometry/contacts changed"
    total = 0
    for prop in props:
        for part in prop["parts"]:
            vertices, faces = part["geometry"]
            assert all(math.isfinite(c) for v in vertices for c in v)
            edges = {}
            for face in faces:
                for a, b in zip(face, face[1:] + face[:1]):
                    edges[(a, b)] = edges.get((a, b), 0) + 1
            assert all(n == edges.get((b, a), 0) for (a, b), n in edges.items()), part["mesh"]
            assert bp.signed_volume(bp.to_source(part["geometry"])) > 1e-9, part["mesh"]
            total += sum(len(face) - 2 for face in faces)
    assert total < 32000, total
    assert signature(props) == signature(make_props()), "Determinism mismatch"
    by_kind = {p["kind"]: p for p in props}
    assert by_kind["Basket"]["anchors"]["LeftGrip"] == (-.29, .43, 0)
    assert by_kind["BasketStand"]["anchors"]["BasketRest"][1] == .38
    assert by_kind["StationCrate"]["anchors"]["LidHinge"] == (0, .8, -.3)
    for hand in ("LeftGrip", "RightGrip"):
        assert by_kind["ClosedBasket"]["anchors"][hand] == by_kind["Basket"]["anchors"][hand]
    assert by_kind["Shovel"]["anchors"]["RightGrip"] == (0, 1.05, 0)
    assert by_kind["Shovel"]["anchors"]["LeftGrip"] == (0, .65, 0)
    assert by_kind["GateLeaf"]["anchors"]["Handle"] == (.97, .82, .065)
    return total


def make_mesh(part, parent):
    geometry = bp.to_source(part["geometry"])
    mesh = bpy.data.meshes.new(part["mesh"])
    mesh.from_pydata(geometry[0], [], geometry[1])
    mesh.update(calc_edges=True)
    uv = mesh.uv_layers.new(name="UVMap")
    for face in mesh.polygons:
        axis = max(range(3), key=lambda i: abs(face.normal[i]))
        axes = (0, 1) if axis == 2 else ((0, 2) if axis == 1 else (1, 2))
        for loop in face.loop_indices:
            point = mesh.vertices[mesh.loops[loop].vertex_index].co
            uv.data[loop].uv = (point[axes[0]], point[axes[1]])
    material = bpy.data.materials.get(part["role"]) or bpy.data.materials.new(part["role"])
    material.diffuse_color = part["tint"]
    mesh.materials.append(material)
    obj = bpy.data.objects.new(part["mesh"], mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    return obj


def manifest(props, count):
    rows = []
    for prop in props:
        geom = kit.merge_all(part["geometry"] for part in prop["parts"])
        low, high = kit.bounds(geom)
        rows.append(dict(kind=prop["kind"], origin=prop["origin"], bounds_min=low, bounds_max=high,
                         anchors=[dict(name=n, position=p) for n, p in prop["anchors"].items()],
                         parts=[dict(mesh=part["mesh"], role=part["role"], surface=part["surface"],
                                     tint=part["tint"], bounds_min=kit.bounds(part["geometry"])[0],
                                     bounds_max=kit.bounds(part["geometry"])[1],
                                     triangles=sum(len(f) - 2 for f in part["geometry"][1]))
                                for part in prop["parts"]]))
    return dict(generator="tools/build-village-life-props-3d-model.py", generator_version=VERSION,
                design_id=DESIGN_ID, build_signature=signature(props), stage1_signature=stage1_signature(props),
                scale_mode="fixed_metres",
                uv_mode="projected_metres", forward="Unity +Z", root_scale=1,
                colliders=False, lights=False, cameras=False, animation_count=0,
                prop_count=len(props), mesh_count=sum(len(p["parts"]) for p in props),
                triangle_count=count, props=rows)


def preview(path, groups):
    positions = [(-3, -1.5, 0), (-.5, 0, 0), (1.0, 1.0, .10), (2.0, 1.0, 0),
                 (3.1, 0, 0), (-.6, -2.2, 0), (.4, -2.2, 0),
                 (1.7, -2.2, 0), (3.1, -2.2, 0), (3.1, -2.5, .8), (3.1, -1.885, .8),
                 (-2.8, 1.15, 0), (-1.8, 1.15, 0), (-.4, 2.25, 0), (-.4, 2.25, 0),
                 (1.65, 3.10, 0), (3.1, 2.7, 0)]
    for group, position in zip(groups, positions):
        group.location = position
    cam_data = bpy.data.cameras.new("Review Camera")
    cam = bpy.data.objects.new("Review Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = (8, 12, 9)
    cam.rotation_euler = (Vector((0, .0, .4)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 12.8
    scene = bpy.context.scene
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.background_type = "WORLD"
    scene.world.color = (.11, .12, .13)
    scene.render.resolution_x = 1500
    scene.render.resolution_y = 940
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Resources/VillageLife")
    parser.add_argument("--source-dir", type=Path, default=ROOT / "ArtSource/VillageLife")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    props = make_props()
    count = validate(props)
    data = manifest(props, count)
    if args.validate_only:
        existing = json.loads((args.model_dir / "VillageLifeProps3D.json").read_text(encoding="utf-8"))
        assert existing == json.loads(json.dumps(data)), "Published manifest differs from regeneration"
    else:
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        groups = []
        for prop in props:
            root = bpy.data.objects.new(prop["kind"], None)
            bpy.context.scene.collection.objects.link(root)
            groups.append(root)
            for part in prop["parts"]:
                make_mesh(part, root)
            for name, (x, y, z) in prop["anchors"].items():
                anchor = bpy.data.objects.new(f"ANCHOR_{prop['kind']}_{name}", None)
                bpy.context.scene.collection.objects.link(anchor)
                anchor.parent = root
                anchor.location = (x, z, y)
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(filepath=str(args.model_dir / "VillageLifeProps3D.fbx"),
            use_selection=True, object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
            apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
            add_leaf_bones=False, bake_anim=False, mesh_smooth_type="FACE")
        (args.model_dir / "VillageLifeProps3D.json").write_text(
            json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "VillageLifeProps3D.blend"))
        if not args.no_preview:
            preview(args.source_dir / "VillageLifeProps3D.png", groups)
    print(f"VILLAGE LIFE PROPS VALIDATION OK: {len(props)} props, {data['mesh_count']} meshes, {count} triangles")
    print(f"Signature: {data['build_signature']}; closed outward solids; repeated signatures match")


if __name__ == "__main__":
    main()
