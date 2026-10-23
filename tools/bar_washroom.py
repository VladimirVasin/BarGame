"""Fixed-metre pub washroom contract shared by its interior and exterior.

All values are Unity metres.  The toilet's moving bowl/seat/lid/water reuse
the Home authored kit at runtime; only its passive cistern and sink live here.
"""
from __future__ import annotations

import importlib.util
from pathlib import Path

import bar_parts as bp
import interior_kit as kit

INNER = (-5.4, 4.15, -2.4, 7.15)
THICKNESS = .18
HEIGHT = 3.
OUTER = (-5.58, 3.97, -2.22, 7.33)
DOOR_X, DOOR_Z, DOOR_WIDTH, DOOR_HEIGHT = -4.2, 3.9, 1.1, 2.2
HINGE = (-4.75, 0., DOOR_Z)
LIGHT = (-3.9, 2.65, 5.65)
TOILET_WATER = (-3.85, .4373, 6.35)
CISTERN = (-3.85, .76, 6.79)
FLUSH = (-4.05, 1.14, 6.79)
DOOR_GROUP = "pivot:Bar Washroom Door"


def to_exterior(point):
    x, y, z = point
    return -z - 4.06, y, x


def contract():
    return {
        "clear_bounds_xz_m": list(INNER), "outer_bounds_xz_m": list(OUTER),
        "clear_height_m": HEIGHT, "wall_thickness_m": THICKNESS,
        "door_clear_m": [DOOR_WIDTH, DOOR_HEIGHT],
        "door_hinge_unity_m": list(HINGE), "door_open_yaw_degrees": -90.,
        "door_collider_local_center_m": [.55, 1.1, 0.],
        "door_collider_local_size_m": [1.1, 2.2, .06],
        "light_unity_m": list(LIGHT),
        "toilet_water_unity_m": list(TOILET_WATER),
        "toilet_yaw_degrees": -90.,
        "toilet_passive_kit": "HomeToiletAction/Models",
        "exterior_mapping": "(-room.z - 4.06, room.y, room.x)",
        "exterior_outer_bounds_xz_m": [-OUTER[3] - 4.06, OUTER[0],
                                        -OUTER[1] - 4.06, OUTER[2]],
    }


def split_door_wall(x_min, x_max, z, thickness, height=HEIGHT):
    """Geometry and the same three piers/lintel boxes, with a real opening."""
    left, right = DOOR_X - DOOR_WIDTH * .5, DOOR_X + DOOR_WIDTH * .5
    boxes = [((.5 * (x_min + left), height * .5, z),
              (left - x_min, height, thickness)),
             ((.5 * (right + x_max), height * .5, z),
              (x_max - right, height, thickness)),
             ((DOOR_X, .5 * (DOOR_HEIGHT + height), z),
              (DOOR_WIDTH, height - DOOR_HEIGHT, thickness))]
    geometry = kit.merge_all([bp.u_box(center, size, .008) for center, size in boxes])
    return geometry, boxes


def rear_trim(center_y, height, depth, z):
    left, right = DOOR_X - DOOR_WIDTH * .5 - .055, DOOR_X + DOOR_WIDTH * .5 + .055
    return kit.merge_all([bp.u_box(((low + high) * .5, center_y, z),
                                   (high - low, height, depth), .006)
                          for low, high in ((-5.425, left), (right, 5.425))])


def _sink_donor():
    path = Path(__file__).with_name("build-home-brushing-action-3d-model.py")
    spec = importlib.util.spec_from_file_location("bar_washroom_sink_donor", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def wc_letters():
    """Closed Bfont glyphs; face the hall, without a font resource at runtime."""
    import bpy
    curve = bpy.data.curves.new("Bar Washroom WC Authoring", type="FONT")
    curve.body = "WC"
    curve.size = 1.
    curve.align_x = "CENTER"
    curve.resolution_u = 3
    curve.extrude = .006
    curve.bevel_depth = 0.
    temporary = bpy.data.objects.new("Bar Washroom WC Authoring", curve)
    bpy.context.scene.collection.objects.link(temporary)
    evaluated = temporary.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    vertices = [tuple(vertex.co) for vertex in mesh.vertices]
    faces = [tuple(polygon.vertices) for polygon in mesh.polygons]
    evaluated.to_mesh_clear()
    bpy.data.objects.remove(temporary, do_unlink=True)
    bpy.data.curves.remove(curve)
    low = [min(point[axis] for point in vertices) for axis in range(3)]
    high = [max(point[axis] for point in vertices) for axis in range(3)]
    scale = min(.27 / (high[0]-low[0]), .115 / (high[1]-low[1]))
    midpoint = [(a+b)*.5 for a, b in zip(low, high)]
    # Font XY becomes Unity XY; reflecting its extrusion through Z turns
    # the lettering toward the room's -Z side, so rewind every face.
    points = [(-4.2 + (x-midpoint[0])*scale,
               2.40 + (y-midpoint[1])*scale,
               3.682 - (z-midpoint[2]) * .0016 / (high[2]-low[2]))
              for x, y, z in vertices]
    return points, [tuple(reversed(face)) for face in faces]


def build(g, asset, materials):
    def part(suffix, geometry, role, sheet, tint, **kwargs):
        return g.add_part(asset, materials, "Bar Washroom " + suffix,
                          geometry, role, tint, sheet=sheet, **kwargs)

    def box(suffix, center, size, role, sheet, tint, solid=False, **kwargs):
        return part(suffix, bp.u_box(center, size, .008), role, sheet, tint,
                    colliders=[(center, size)] if solid else [], **kwargs)

    ceramic = bp.rgb(.67, .65, .55)
    plaster = bp.rgb(.43, .42, .33)
    metal = bp.rgb(.34, .36, .30)
    xmin, zmin, xmax, zmax = OUTER
    width, depth = xmax - xmin, zmax - 3.9
    center = ((xmin + xmax) * .5, -.12, (3.9 + zmax) * .5)
    box("Floor", center, (width, .24, depth), "washroom_floor", "Ceramic",
        bp.rgb(.31, .30, .25), solid=True)
    box("Ceiling", ((xmin + xmax) * .5, 3.1, (zmin + zmax) * .5),
        (width, .2, zmax - zmin), "washroom_ceiling", "CeilingPlaster", plaster)
    for suffix, center, size in (
        ("Left Wall", (xmin + .09, 1.5, (zmin + zmax) * .5), (.18, 3., zmax-zmin)),
        ("Right Wall", (xmax - .09, 1.5, (zmin + zmax) * .5), (.18, 3., zmax-zmin)),
        ("Rear Wall", ((xmin+xmax) * .5, 1.5, zmax-.09), (width, 3., .18)),
    ):
        box(suffix, center, size, "washroom_wall", "CeilingPlaster", plaster, solid=True)
    front, front_boxes = split_door_wall(xmin, xmax, zmin + .09, .18)
    part("Front Wall", front, "washroom_wall", "CeilingPlaster", plaster,
         colliders=front_boxes)

    # Plain low ceramic splashback, with no text, stains or narrative clues.
    box("Sink Splashback", (-2.405, .97, 5.18), (.025, .5, 1.05),
        "washroom_splashback", "Ceramic", ceramic, shadows=False)
    frame = kit.merge_all([
        bp.u_box((side, 1.14, 3.72), (.08, 2.28, .10), .006)
        for side in (DOOR_X - .59, DOOR_X + .59)
    ] + [bp.u_box((DOOR_X, 2.24, 3.72), (1.26, .08, .10), .006)])
    part("Door Frame", frame, "washroom_door_frame", "DarkWood", bp.ident("DarkWoodTint"))
    box("WC Sign", (-4.2, 2.40, 3.70), (.42, .18, .025),
        "washroom_wc_sign", "PaintedMetal", bp.rgb(.065, .062, .052), shadows=False)
    letters = part("WC Letters", wc_letters(), "washroom_wc_letters", "PaintedMetal",
                   bp.rgb(.80, .78, .67), shadows=False)
    letters["bp_text"] = "WC"
    g.add_anchor(asset, "WashroomSign", "washroom_wc_sign", (-4.2, 2.40, 3.70))
    leaf = bp.to_source(kit.panelled_leaf(DOOR_WIDTH, DOOR_HEIGHT, .06,
                                         stile=.09, panels=3, relief=.012))
    # Like the fan, this group is authored around zero. Runtime places its
    # container at the separately measured hinge, exactly once.
    g.add_part(asset, materials, "Bar Washroom Door Leaf", leaf,
               "washroom_door_leaf", bp.ident("DarkWoodTint"),
               group=DOOR_GROUP, sheet="DarkWood")
    for suffix, offset in (("Hall", -.075), ("Room", .075)):
        handle = kit.merge_all([
            bp.u_box((.91, 1.01, offset), (.12, .025, .025), .005),
            bp.u_box((.96, .99, offset*.6), (.025, .10, .045), .004),
        ])
        g.add_part(asset, materials, "Bar Washroom Door Handle " + suffix,
                   handle, "washroom_door_handle", metal,
                   group=DOOR_GROUP, sheet="AgedBrass", shadows=False)
        g.add_anchor(asset, "WashroomDoorHandle" + suffix, "washroom_door_handle_" + suffix.lower(),
                     (HINGE[0] + .91, 1.01, DOOR_Z + offset))
    g.add_anchor(asset, "WashroomDoorPivot", "washroom_door_pivot", HINGE)
    g.add_anchor(asset, "WashroomDoor", "washroom_door", (DOOR_X, 1.1, DOOR_Z))

    # The existing Home toilet kit remains the single bowl/seat/lid source.
    box("Toilet Cistern", CISTERN, (.748, .72, .24), "washroom_cistern", "Ceramic",
        ceramic, solid=True)
    flush = bp.u_cylinder(FLUSH, (.055, .0125, .055), sides=16)
    g.add_pivoted_part(asset, materials, "Bar Washroom Toilet Flush", flush, FLUSH,
                       "washroom_toilet_flush", metal, sheet="PaintedMetal", shadows=False)
    g.add_anchor(asset, "WashroomToiletWater", "washroom_toilet_water", TOILET_WATER,
                 unity_right=(0., 0., 1.), unity_forward=(-1., 0., 0.))
    g.add_anchor(asset, "WashroomToiletFlush", "washroom_toilet_flush", FLUSH)

    donor = _sink_donor()
    sink = (-2.62, .78, 5.18)
    def sink_geometry(geometry):
        return kit.translated(bp.u_rotated(geometry, (0., 90., 0.)), sink)
    part("Sink Basin", sink_geometry(donor.sink_basin()), "washroom_sink", "Ceramic",
         ceramic, colliders=[(sink, (.35, .20, .85))])
    pedestal = bp.u_tapered_cylinder((-2.62, .36, 5.18), (.26, .36, .26), .7, sides=16)
    part("Sink Pedestal", pedestal, "washroom_sink", "Ceramic", ceramic,
         colliders=[((-2.62, .36, 5.18), (.26, .72, .26))])
    drain = kit.translated(donor.sink_drain(), (-.08, -.056, 0.))
    part("Sink Drain", sink_geometry(drain), "washroom_sink_metal", "PaintedMetal", metal, shadows=False)
    faucet = kit.translated(donor.faucet_body(), (0., .10, .10))
    faucet_handle = kit.translated(donor.faucet_handle(), (0., .295, .10))
    part("Sink Faucet", sink_geometry(kit.merge(faucet, faucet_handle)),
         "washroom_sink_metal", "PaintedMetal", metal, shadows=False)

    box("Light Mount", (-3.9, 2.94, 5.65), (.14, .12, .14),
        "washroom_light_fixture", "PaintedMetal", metal, shadows=False)
    part("Light Shade", bp.u_tapered_cylinder((-3.9, 2.75, 5.65), (.36, .10, .36), .55, sides=16),
         "washroom_light_fixture", "PaintedMetal", bp.rgb(.42, .40, .30), shadows=False)
    part("Light Bulb", bp.u_cylinder(LIGHT, (.11, .035, .11), sides=12),
         "washroom_light_bulb", "", bp.rgb(1., .76, .44), emissive=True, shadows=False)
    g.add_anchor(asset, "WashroomLight", "washroom_light", LIGHT)
    for suffix, position in (("InnerMin", (INNER[0], 0., INNER[1])),
                             ("InnerMax", (INNER[2], HEIGHT, INNER[3]))):
        g.add_anchor(asset, "Washroom" + suffix, "washroom_" + suffix.lower(), position)
    asset.washroom_contract = contract()


def contains(point):
    x, y, z = point
    return (OUTER[0] - .001 <= x <= OUTER[2] + .001 and
            3.9 - .001 <= z <= OUTER[3] + .001 and -.241 <= y <= 3.201)


def validate(asset, problems):
    names = {part.name for part in asset.parts}
    for suffix in ("Floor", "Left Wall", "Right Wall", "Rear Wall", "Front Wall",
                   "Door Leaf", "Door Frame", "WC Sign", "WC Letters", "Toilet Cistern", "Toilet Flush", "Sink Basin"):
        if "Bar Washroom " + suffix not in names:
            problems.append("missing washroom part " + suffix)
    if any("Social Pub Table 3" in name or "Pub Table 3" in name for name in names):
        problems.append("the removed rear social table returned")
    leaf = next(part for part in asset.parts if part.role == "washroom_door_leaf")
    letters = next(part for part in asset.parts if part.role == "washroom_wc_letters")
    if letters.obj.get("bp_text") != "WC" or letters.emissive:
        problems.append("the washroom sign must read exactly WC without emission")
    if leaf.colliders or leaf.group != DOOR_GROUP:
        problems.append("the moving washroom leaf must use only its runtime pivot collider")
    for part in asset.parts:
        for center, size in part.colliders:
            # A capsule at the threshold must see a genuinely empty opening.
            if (abs(center[2] - DOOR_Z) <= size[2]*.5 + .02 and
                center[0]-size[0]*.5 < DOOR_X + .32 and
                center[0]+size[0]*.5 > DOOR_X - .32 and
                center[1]+size[1]*.5 > .1 and center[1]-size[1]*.5 < 1.9):
                problems.append("washroom doorway blocked by " + part.name)
