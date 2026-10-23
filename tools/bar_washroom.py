"""Fixed-metre pub washroom contract shared by its interior and exterior.

All values are Unity metres.  The toilet's moving bowl/seat/lid/water reuse
the Home authored kit at runtime; only its passive cistern and sink live here.
"""
from __future__ import annotations

import importlib.util
import math
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
MIRROR_X = -2.416
MIRROR_Y = (1.26, 2.10)
MIRROR_Z = (4.70, 5.66)


def _polygon_x(points, x, thickness=.004):
    """A closed irregular plate in the YZ plane, with outward winding."""
    area = sum(a[0] * b[1] - b[0] * a[1]
               for a, b in zip(points, points[1:] + points[:1]))
    points = points if area > 0 else list(reversed(points))
    count = len(points)
    vertices = [(depth, y, z) for depth in (x-thickness*.5, x+thickness*.5)
                for y, z in points]
    faces = [tuple(reversed(range(count))), tuple(range(count, count*2))]
    faces += [(i, (i+1) % count, (i+1) % count + count, i+count) for i in range(count)]
    return vertices, faces


def _stroke_x(points, x, width=.006, thickness=.003):
    segments = []
    for a, b in zip(points, points[1:]):
        dy, dz = b[0]-a[0], b[1]-a[1]
        length = math.hypot(dy, dz)
        ny, nz = -dz / length * width*.5, dy / length * width*.5
        segments.append(_polygon_x([(a[0]+ny,a[1]+nz),(b[0]+ny,b[1]+nz),
                                    (b[0]-ny,b[1]-nz),(a[0]-ny,a[1]-nz)], x, thickness))
    return kit.merge_all(segments)


def _open_bin(center):
    """A dented open wastebasket: inner wall, rolled rim and a real bottom."""
    cx, cy, cz = center
    count = 16
    vertices = []
    for y, radius in ((.025,.165),(.50,.205),(.50,.191),(.045,.151)):
        for i in range(count):
            angle = math.tau*i/count
            dent = 1 - (.09 if i in (3,4) else .035 if i == 11 else 0)
            vertices.append((cx+math.cos(angle)*radius*dent, cy+y, cz+math.sin(angle)*radius))
    faces=[]
    for ring in range(3):
        for i in range(count):
            j=(i+1)%count
            faces.append((ring*count+j,ring*count+i,(ring+1)*count+i,(ring+1)*count+j))
    faces += [tuple(range(3*count,4*count)),tuple(reversed(range(count)))]
    return vertices,faces


def _dressing(part, box):
    ceramic=bp.rgb(.67,.65,.55)
    dull=bp.rgb(.34,.35,.29)
    paper=bp.rgb(.72,.68,.53)
    # Dented bin and brush occupy the rear-left service margin, outside all action docks.
    part("Wastebasket", _open_bin((-5.08,0.,6.60)), "washroom_bin", "PaintedMetal", dull,
         colliders=[((-5.08,.26,6.60),(.43,.52,.43))])
    brush=kit.merge(bp.u_tapered_cylinder((-4.73,.105,6.83),(.16,.105,.16),.84,16),
                    bp.u_cylinder((-4.73,.36,6.83),(.018,.18,.018),12),
                    bp.u_cylinder((-4.73,.18,6.83),(.095,.055,.095),12))
    part("Toilet Brush",brush,"washroom_brush","PaintedMetal",bp.rgb(.30,.27,.19))
    holder=kit.merge_all([bp.u_box((-3.01,.96,7.105),(.19,.17,.065),.008),
                         bp.u_box((-3.01,.89,7.01),(.20,.025,.17),.006)])
    part("Paper Holder",holder,"washroom_holder","PaintedMetal",dull,shadows=False)
    roll=bp.u_rotated_about(bp.u_cylinder((-3.01,.93,6.98),(.14,.10,.14),16),
                           (0.,0.,90.),(-3.01,.93,6.98))
    part("Paper Roll",kit.merge(roll,bp.u_plate((-3.01,.81,6.90),(.17,.19,.006))),
         "washroom_paper","CeilingPlaster",paper,shadows=False)
    box("Soap Dispenser",(-2.46,1.10,5.96),(.13,.26,.19),"washroom_dispenser",
        "PaintedMetal",bp.rgb(.49,.47,.36),shadows=False)
    box("Soap Dispenser Window",(-2.529,1.09,5.96),(.008,.11,.045),"washroom_dispenser_window",
        "PaintedMetal",bp.rgb(.11,.13,.10),shadows=False)
    # One merged litter mesh; no physics or repeated runtime objects.
    litter=[]
    for index,(x,z,yaw,size) in enumerate(((-5.02,6.57,14,.13),(-5.16,6.70,-23,.16),
            (-4.96,6.73,39,.14),(-5.12,6.43,9,.12),(-4.89,6.34,62,.13),
            (-5.04,6.13,-18,.18),(-4.70,6.56,28,.10),(-5.19,5.82,41,.13))):
        y=.44+(index%3)*.024 if index<4 else .014
        geometry=bp.u_box((0.,0.,0.),(size,.022,size*.7),.009)
        geometry=bp.u_rotated(geometry,(8+index*3,yaw,(-1)**index*13))
        litter.append(kit.translated(geometry,(x,y,z)))
    litter += [bp.u_plate((-4.92,.009,6.02),(.11,.009,.28)),
               bp.u_plate((-5.22,.011,5.49),(.16,.012,.13)),
               bp.u_plate((-2.62,.008,6.43),(.09,.01,.22))]
    part("Discarded Paper",kit.merge_all(litter),"washroom_litter","CeilingPlaster",paper,shadows=False)
    # Broken plaster patches expose an irregular brown substrate, never random floating chips.
    scars=kit.merge_all([_polygon_x([(1.28,5.92),(1.50,5.90),(1.59,6.04),(1.52,6.19),
             (1.31,6.24),(1.23,6.10)],-5.386,.008),
        _polygon_x([(2.22,6.58),(2.42,6.63),(2.51,6.80),(2.35,6.93),(2.19,6.84)],-5.386,.008)])
    part("Plaster Scars",scars,"washroom_damage","CeilingPlaster",bp.rgb(.32,.25,.17),shadows=False)
    # Torn unbranded stickers and a text-free crude marker drawing are everyday misuse.
    stickers=kit.merge_all([_polygon_x([(1.54,4.91),(1.76,4.93),(1.75,5.14),
          (1.70,5.17),(1.52,5.12)],-5.381),
        _polygon_x([(1.12,5.53),(1.26,5.51),(1.29,5.73),(1.16,5.75)],-5.38),
        _polygon_x([(1.88,5.35),(2.05,5.39),(2.01,5.57),(1.85,5.53)],-5.38)])
    part("Sticker Cards",stickers,"washroom_stickers","CeilingPlaster",bp.rgb(.71,.62,.39),shadows=False)
    lips=[(1.64,4.965),(1.685,5.015),(1.66,5.052),(1.686,5.083),(1.63,5.12),
          (1.59,5.065),(1.61,5.025),(1.64,4.965)]
    smile=[(1.21,5.55),(1.18,5.58),(1.17,5.64),(1.21,5.69)]
    crude=[(1.53,5.57),(1.50,5.54),(1.46,5.54),(1.43,5.57),(1.44,5.62),
           (1.54,5.64),(1.77,5.66),(1.80,5.69),(1.78,5.73),(1.53,5.72),
           (1.46,5.76),(1.42,5.75),(1.40,5.71),(1.42,5.67)]
    part("Marker Drawings",kit.merge(_stroke_x(lips,-5.375,.007),
        _stroke_x(smile,-5.374,.006),_stroke_x(crude,-5.375,.007)),
        "washroom_doodles","PaintedMetal",bp.rgb(.24,.105,.11),shadows=False)


def _mirror(part, box):
    ymin,ymax=MIRROR_Y;zmin,zmax=MIRROR_Z
    frame=kit.merge_all([bp.u_box((MIRROR_X-.017,y,(zmin+zmax)*.5),(.032,.035,zmax-zmin+.07),.007)
                       for y in (ymin-.018,ymax+.018)] +
        [bp.u_box((MIRROR_X-.017,(ymin+ymax)*.5,z),(.032,ymax-ymin,.035),.007)
         for z in (zmin-.018,zmax+.018)])
    part("Mirror Frame",frame,"washroom_mirror_frame","PaintedMetal",bp.rgb(.36,.36,.28),shadows=False)
    box("Mirror Fallback",(MIRROR_X+.002,(ymin+ymax)*.5,(zmin+zmax)*.5),
        (.008,ymax-ymin,zmax-zmin),"washroom_mirror_fallback","PaintedMetal",bp.rgb(.15,.18,.15),shadows=False)
    missing=_polygon_x([(ymax,zmax),(ymax-.22,zmax),(ymax-.13,zmax-.13),
                        (ymax-.04,zmax-.19),(ymax,zmax-.15)],MIRROR_X-.004,.007)
    part("Mirror Missing Backing",missing,"washroom_mirror_missing","CeilingPlaster",bp.rgb(.26,.23,.17),shadows=False)
    paths=[[(1.49,5.07),(1.62,5.19),(1.73,5.27),(1.87,5.43),(2.10,5.52)],
           [(1.73,5.27),(1.90,5.24),(2.10,5.16)],
           [(1.73,5.27),(1.59,5.43),(1.47,5.66)],
           [(1.62,5.19),(1.44,5.24),(1.26,5.23)],
           [(1.49,5.07),(1.33,4.87),(1.26,4.83)],
           [(1.73,5.27),(1.77,5.03),(1.83,4.70)],
           [(1.73,5.27),(1.56,5.14),(1.26,5.48)]]
    part("Mirror Cracks",kit.merge_all([_stroke_x(path,MIRROR_X-.009,.004,.003) for path in paths]),
         "washroom_mirror_cracks","PaintedMetal",bp.rgb(.08,.09,.075),shadows=False)
    # Corroded silver follows the perimeter, leaving the centre a real opening.
    edge=kit.merge_all([bp.u_box((MIRROR_X-.008,y,(zmin+zmax)*.5),(.004,.014,zmax-zmin),.001)
                       for y in (ymin+.007,ymax-.007)] +
        [bp.u_box((MIRROR_X-.008,(ymin+ymax)*.5,z),(.004,ymax-ymin,.016),.001)
         for z in (zmin+.008,zmax-.008)])
    part("Mirror Corrosion",edge,"washroom_mirror_corrosion","PaintedMetal",bp.rgb(.18,.15,.10),shadows=False)


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
        "mirror_plane_x_m": MIRROR_X,
        "mirror_opening_yz_m": [*MIRROR_Y, *MIRROR_Z],
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
        ("Rear Wall", ((xmin+xmax) * .5, 1.5, zmax-.09), (width, 3., .18)),
    ):
        box(suffix, center, size, "washroom_wall", "CeilingPlaster", plaster, solid=True)
    # Four solids surround a genuine mirror opening. Its invisible wall collider
    # stays whole so the hero cannot enter the reflected set behind the wall.
    ymin,ymax=MIRROR_Y; mzmin,mzmax=MIRROR_Z
    right=kit.merge_all([bp.u_box((xmax-.09,y,(zmin+zmax)*.5),(.18,h,zmax-zmin),.008)
                        for y,h in ((ymin*.5,ymin),((ymax+3.)*.5,3.-ymax))] +
        [bp.u_box((xmax-.09,(ymin+ymax)*.5,z),(.18,ymax-ymin,d),.008)
         for z,d in (((zmin+mzmin)*.5,mzmin-zmin),((mzmax+zmax)*.5,zmax-mzmax))])
    part("Right Wall",right,"washroom_wall","CeilingPlaster",plaster,
         colliders=[((xmax-.09,1.5,(zmin+zmax)*.5),(.18,3.,zmax-zmin))])
    front, front_boxes = split_door_wall(xmin, xmax, zmin + .09, .18)
    part("Front Wall", front, "washroom_wall", "CeilingPlaster", plaster,
         colliders=front_boxes)

    # Low tile lining uses the same registered texture response as its dirty grout.
    lining=kit.merge_all([bp.u_box((-5.387,.575,5.65),(.026,1.15,3.),.004),
                          bp.u_box((-2.413,.575,5.65),(.026,1.15,3.),.004),
                          bp.u_box((-3.9,.575,7.137),(3.,1.15,.026),.004)])
    part("Lower Wall Tiles",lining,"washroom_tiles","Ceramic",ceramic,shadows=False)
    box("Sink Splashback", (-2.405, .97, 5.18), (.025, .5, 1.05),
        "washroom_splashback", "Ceramic", ceramic, shadows=False)
    frame = kit.merge_all([
        bp.u_box((side, 1.14, 3.72), (.08, 2.28, .10), .006)
        for side in (DOOR_X - .59, DOOR_X + .59)
    ] + [bp.u_box((DOOR_X, 2.24, 3.72), (1.26, .08, .10), .006)])
    part("Door Frame", frame, "washroom_door_frame", "DarkWood", bp.ident("DarkWoodTint"))
    box("WC Sign", (-4.2, 2.40, 3.70), (.42, .18, .025),
        "washroom_wc_sign", "PaintedMetal", bp.rgb(.065, .062, .052), shadows=False)
    letters = part("WC Letters", wc_letters(), "washroom_wc_letters", "",
                   bp.rgb(2.4, 2.05, 1.5), emissive=True, shadows=False)
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

    _dressing(part,box)
    _mirror(part,box)
    # Render-only source patches are hidden by the mirror owner after cloning.
    # They stop exactly at the reflection plane, so negative-scale copies cannot
    # cross back into the real room or overlap its surfaces.
    source_left=xmin; source_right=MIRROR_X
    source_mid=(source_left+source_right)*.5; source_width=source_right-source_left
    for suffix,center,size,sheet,tint in (
        ("Floor",(source_mid,-.12,(3.9+zmax)*.5),(source_width,.24,zmax-3.9),"Ceramic",bp.rgb(.31,.30,.25)),
        ("Ceiling",(source_mid,3.1,(zmin+zmax)*.5),(source_width,.2,zmax-zmin),"CeilingPlaster",plaster),
        ("Rear Wall",(source_mid,1.5,zmax-.09),(source_width,3.,.18),"CeilingPlaster",plaster),
        ("Left Wall",(xmin+.09,1.5,(zmin+zmax)*.5),(.18,3.,zmax-zmin),"CeilingPlaster",plaster),
        ("Hall Floor",((-5.7+MIRROR_X)*.5,-.12,0.),(MIRROR_X+5.7,.24,7.8),"WornPlank",bp.ident("FloorTint")),
        ("Hall Ceiling",((-5.7+MIRROR_X)*.5,3.10,0.),(MIRROR_X+5.7,.20,7.8),"CeilingPlaster",bp.ident("CeilingTint")),
        ("Hall South Wall",((-5.7+MIRROR_X)*.5,1.5,-3.90),(MIRROR_X+5.7,3.,.25),"Wallpaper",bp.ident("WallTint")),
    ):
        box("Mirror Source "+suffix,center,size,"washroom_mirror_source",sheet,tint,shadows=False)
    source_front,_=split_door_wall(source_left,source_right,zmin+.09,.18)
    part("Mirror Source Front Wall",source_front,"washroom_mirror_source","CeilingPlaster",plaster,shadows=False)

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
    if letters.obj.get("bp_text") != "WC" or not letters.emissive:
        problems.append("the washroom sign must read exactly WC with milk-yellow emission")
    for suffix in ("Wastebasket","Discarded Paper","Marker Drawings","Mirror Frame",
                   "Mirror Fallback","Mirror Cracks","Mirror Missing Backing","Lower Wall Tiles"):
        if "Bar Washroom "+suffix not in names:
            problems.append("missing worn washroom detail "+suffix)
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
