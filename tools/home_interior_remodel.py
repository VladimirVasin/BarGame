"""Measured living-room authoring for the enlarged Home; no bathroom edits."""
from __future__ import annotations

import math
import interior_kit as kit
import bar_parts as bp

SHELL = (-7.0, -4.0, 5.0, 4.0)
LOCKED = (-2.80, .82, 1.46, 3.88)
SLEEP = (-6.86, -1.45, -5.71, 1.10)
SOFA_DEPTH = 1.15
SOFA_DEPTH_SCALE = SOFA_DEPTH / 1.75
TABLE = (-2.65, -.45, -1.20, .75)
KITCHEN = (-6.65, 2.48, -3.28, 3.30)
FRIDGE_X = -4.6645
SEAT_TOP = (-1.925, .48, -1.0)
CHAIR_POSITION = (-1.925, .51, -1.015)
BOOKCASE = (-6.86,-3.76,-5.76,-3.11)
PAINTING_IMAGE_SIZE = (1.34, 1.34 * 941 / 1672, .006)
PAINTING_FRAME_SIZE = (1.40, .82, .045)
PAINTING_FRAME_CENTER = (-.97, 1.98, .745)
PAINTING_IMAGE_CENTER = (-.97, 1.98, .718)
PAINTING_TEXTURE = "Assets/Resources/Home/Textures/HomeWeaverPaintingAlbedo.png"
SHEETS = {
    "RoomWallpaper": (1.9, (.43, .405, .345, 1)),
    "RoomPlanks": (1.6, (.32, .235, .16, 1)),
    "RoomWood": (1.1, (.27, .185, .11, 1)),
    "RoomLaminate": (1.2, (.42, .44, .37, 1)),
    "RoomUpholstery": (.85, (.28, .335, .30, 1)),
}


def turned_sleep_position(position):
    """Original authored local +X footward becomes world +Z, back to the west."""
    x,y,z=position
    return (-6.285-(z+.375)*SOFA_DEPTH_SCALE,y,-.175+(x+5.475))


def turned_bookcase_position(position):
    x,y,z=position
    return (-6.31-(z-1.70),y,-3.435+(x+6.425))


def cuboid(center, size, cut=.008):
    return bp.to_source(bp.u_box(center, size, cut))


def combined(*pieces):
    return kit.merge_all(pieces)


def sink_countertop(size):
    x,y,z=size; hole_x=-.08463; half_x=.385; half_z=.235
    left=hole_x-half_x;right=hole_x+half_x
    return combined(cuboid(((-x*.5+left)*.5,0,0),(left+x*.5,y,z)),
                    cuboid(((right+x*.5)*.5,0,0),(x*.5-right,y,z)),
                    cuboid((hole_x,0,(-z*.5-half_z)*.5),(.77,y,z*.5-half_z)),
                    cuboid((hole_x,0,(z*.5+half_z)*.5),(.77,y,z*.5-half_z)))


def sink_bowl():
    rings=[]
    for width,depth,y,cut in ((.64,.36,-.09,.06),(.85,.55,.07,.045),
                              (.85,.55,.09,.045),(.77,.47,.09,.035),
                              (.57,.27,-.065,.055)):
        rings.append([(x,y,z) for x,z in kit.profile_chamfered(width,depth,cut)])
    vertices=[p for ring in rings for p in ring];faces=[]
    for j in range(len(rings)-1):
        for i in range(8):
            n=(i+1)%8;faces.append((j*8+i,j*8+n,(j+1)*8+n,(j+1)*8+i))
    faces += [tuple(reversed(range(8))),tuple(range(32,40))]
    geom=bp.to_source((vertices,faces))
    if bp.signed_volume(geom)<0:geom=(geom[0],[tuple(reversed(f)) for f in geom[1]])
    return geom


def pipe(points,radius=.014,segments=16):
    vertices=[];faces=[]
    for j,p in enumerate(points):
        a=points[max(0,j-1)];c=points[min(len(points)-1,j+1)]
        tangent=[c[k]-a[k] for k in range(3)];length=math.sqrt(sum(v*v for v in tangent))
        tangent=[v/length for v in tangent]
        ref=(1,0,0) if abs(tangent[0])<.9 else (0,0,1)
        u=(tangent[1]*ref[2]-tangent[2]*ref[1],tangent[2]*ref[0]-tangent[0]*ref[2],tangent[0]*ref[1]-tangent[1]*ref[0])
        length=math.sqrt(sum(v*v for v in u));u=[v/length for v in u]
        v=(tangent[1]*u[2]-tangent[2]*u[1],tangent[2]*u[0]-tangent[0]*u[2],tangent[0]*u[1]-tangent[1]*u[0])
        for i in range(segments):
            angle=math.tau*i/segments
            vertices.append(tuple(p[k]+radius*(u[k]*math.cos(angle)+v[k]*math.sin(angle)) for k in range(3)))
    for j in range(len(points)-1):
        for i in range(segments):
            n=(i+1)%segments;faces.append((j*segments+i,j*segments+n,(j+1)*segments+n,(j+1)*segments+i))
    faces += [tuple(reversed(range(segments))),tuple(range((len(points)-1)*segments,len(points)*segments))]
    geom=bp.to_source((vertices,faces))
    if bp.signed_volume(geom)<0:geom=(geom[0],[tuple(reversed(f)) for f in geom[1]])
    return geom


def table_base():
    parts = []
    for x in (-.59, .59):
        for z in (-.465, .465):
            leg = kit.turned_leg(.73, .045, .030, .038, segments=16)
            parts.append(kit.translated(leg, (x, z, -.365)))
    for z in (-.48, .48):
        parts.append(cuboid((0, .305, z), (1.27, .12, .06)))
    for x in (-.605, .605):
        parts.append(cuboid((x, .305, 0), (.06, .12, .90)))
    return kit.merge_all(parts)


def chair():
    parts = [cuboid((0, -.065, .015), (.54, .07, .47), .018)]
    for x in (-.23, .23):
        for z in (-.245, .25):
            parts.append(cuboid((x, -.295, z), (.08 if z < 0 else .07, .43,
                               .08 if z < 0 else .07), .009))
        parts.append(cuboid((x, .215, -.245), (.08, .59, .08), .012))
    # Two curved-looking transverse rails and three separate back slats.
    for y in (.07, .465):
        parts.append(cuboid((0, y, -.245), (.54, .09, .08), .014))
    for x in (-.13, 0, .13):
        parts.append(cuboid((x, .265, -.245), (.065, .33, .06), .012))
    for x in (-.23, .23):
        parts.append(cuboid((x, -.29, .01), (.04, .04, .44), .005))
    return kit.merge_all(parts)


def painting_frame():
    """Four plain worn wooden rails around the full, uncropped image."""
    width, height, depth = PAINTING_FRAME_SIZE
    image_width, image_height, _ = PAINTING_IMAGE_SIZE
    side = (width-image_width)*.5
    rail = (height-image_height)*.5
    pieces = [cuboid((sign*(width-side)*.5,0,0),(side,height,depth),.0015)
              for sign in (-1,1)]
    pieces += [cuboid((0,sign*(height-rail)*.5,0),(image_width,rail,depth),.0015)
               for sign in (-1,1)]
    return kit.merge_all(pieces)


def cabinet(size):
    x, y, z = size
    t = .045
    parts = [cuboid((s * (x-t)*.5, 0, 0), (t, y, z)) for s in (-1, 1)]
    parts += [cuboid((0, 0, (z-t)*.5), (x-2*t, y, t))]
    for h in (-.5, -.26, 0, .26, .5):
        parts.append(cuboid((0, h*(y-t), 0), (x-2*t, t, z)))
    # Low cupboard fronts leave the upper shelves open.
    for s in (-1, 1):
        parts.append(cuboid((s*x*.235, -y*.34, -z*.5-.004),
                            (x*.44, y*.26, .036), .007))
    # Keep the declared envelope rather than growing the door proud of it.
    return fit(kit.merge_all(parts), size)


def fit(geom, size):
    lo, hi = kit.bounds(geom)
    geom = kit.translated(geom, [-(a+b)*.5 for a,b in zip(lo,hi)])
    return kit.scaled(geom, [s/(b-a) for s,a,b in zip((size[0],size[2],size[1]),lo,hi)])


def configure_fixed(fixed):
    for name in tuple(fixed):
        if name.startswith("Home Camera Corner "):
            del fixed[name]
    for name, size, sheet in (
        ("Home Floor", (12,.16,8), "RoomPlanks"),
        ("Home Ceiling", (12.48,.16,8.48), "CeilingPlaster"),
        ("Home Back Wall", (12,3.4,.24), "RoomWallpaper"),
        ("Home Left Wall", (.24,3.4,8), "RoomWallpaper"),
        ("Home Entry Wall Left", (5.70,3.4,.24), "RoomWallpaper"),
        ("Home Scarred Table", (1.45,.10,1.20), "RoomLaminate"),
        ("Home Bed Frame", (2.55,.38,SOFA_DEPTH), "RoomUpholstery"),
        ("Home Bed Crooked Blanket", (1.1475,.12,.315*SOFA_DEPTH_SCALE), "RoomUpholstery"),
        ("Home Battered Cabinet", (.65,2.25,1.10), "RoomWood"),
        ("Home Alarm Clock Nightstand", (.68,.72,.46), "RoomWood"),
        ("Home Alarm Clock Nightstand Top", (.73,.05,.5), "RoomWood"),
    ):
        fixed[name] = (size, sheet)
    for name, (size, sheet) in tuple(fixed.items()):
        if name.startswith("Home Kitchen "):
            fixed[name] = (size, "RoomLaminate")
        elif name.startswith(("Home Entry Wall", "Home Entry Lintel", "Home Entry Door Transom")):
            fixed[name] = (size, "RoomWallpaper")


def add_bindings(b):
    b.add("Home Sleeping Sofa Back", (2.55,.56,.16*SOFA_DEPTH_SCALE), "RoomUpholstery")
    for side in ("Left", "Right"):
        b.add("Home Sleeping Sofa Arm " + side, (.08,.60,SOFA_DEPTH), "RoomUpholstery")
    b.add("Home Dining Table Base", (1.27,.73,1.02), "RoomWood", geometry=table_base())
    b.add("Dining Chair", (.54,1.02,.57), "RoomWood", geometry=chair())
    b.add("Home Weaver Painting Frame", PAINTING_FRAME_SIZE, "RoomWood",
          geometry=painting_frame(), tint=(.20,.15,.105,1))
    b.add("Home Weaver Painting Image", PAINTING_IMAGE_SIZE, "Plain",
          geometry=kit.box((0,0,0),(PAINTING_IMAGE_SIZE[0],PAINTING_IMAGE_SIZE[2],PAINTING_IMAGE_SIZE[1])),
          tint=(1,1,1,1), uv_mode="full_image_xy")
    # Closed revolved shade wall, with an open underside around the real bulb.
    profile=((.29,-.11),(.17,.09),(.065,.11),(.055,.09),(.15,.065),(.27,-.11))
    geometry=kit.lathe(profile,32)
    vertices,faces=geometry;faces=faces[:-2]
    for i in range(32):
        n=(i+1)%32
        faces.append(((len(profile)-1)*32+i,(len(profile)-1)*32+n,n,i))
    b.add("Home Main Lamp Shade",(.58,.22,.58),"PaintedMetal",geometry=(vertices,faces),tint=(.25,.30,.27,1))
    b.add("Home Main Lamp Wire",(.028,.40,.028),"PaintedMetal")


def add_decor(b, cloth, dish, cylinder, irregular_patch):
    def add(name, size, position, sheet="RoomWood", *, day=1, last=7,
            geometry=None, tint=None, group="shell", rotation=(0,0,0), collider=False):
        if group == "home.bed.surface-clutter":
            position=turned_sleep_position(position)
            # Scale across the assembled sofa, after each cloth's authored
            # turn, so late bedding never hangs beyond its narrower support.
            unity=bp.u_rotated(bp.to_source(geometry),rotation)
            geometry=bp.to_source(([(x,y,z*SOFA_DEPTH_SCALE) for x,y,z in unity[0]],unity[1]))
            low,high=kit.bounds(geometry)
            size=(high[0]-low[0],high[2]-low[2],high[1]-low[1])
            rotation=(0,-90,0)
        elif group == "home.furniture.bookcase":
            position=turned_bookcase_position(position)
            rotation=(rotation[0],rotation[1]-90,rotation[2])
        elif group == "home.furniture.table":
            position=(position[0]+.70,position[1],position[2]+.10)
        return b.add(name,size,sheet,role="decor",position=position,rotation=rotation,
                     min_day=day,max_day=last,geometry=geometry,tint=tint,
                     group=group,collider=collider)

    # One ordinary opaque story door; its room has no authored contents.
    add("Home Locked Room West Wall",(.18,3.4,3.06),(-2.71,1.7,2.35),"RoomWallpaper",collider=True)
    for suffix,x,width in (("Left",-1.3225,2.955),("Right",1.3325,.255)):
        add("Home Locked Room Front "+suffix,(width,3.4,.18),(x,1.7,.91),"RoomWallpaper",collider=True)
    add("Home Locked Room Lintel",(1.05,1.20,.18),(.68,2.8,.91),"RoomWallpaper",collider=True)
    add("Home Locked Room Door",(1.05,2.20,.08),(.68,1.10,.855),"RoomWood",collider=True)
    # Thin routed trim and skirt carry the room's scale without creating clutter.
    for name, center, size in (
        ("Home West Skirting",(-6.855,.095,0),(.05,.19,7.72)),
        ("Home North Skirting West",(-4.78,.095,3.855),(3.9,.19,.05)),
        ("Home South Skirting West",(-4.13,.095,-3.855),(5.38,.19,.05)),
        ("Home South Skirting East",(3.10,.095,-3.855),(3.60,.19,.05)),
        ("Home Locked Room Skirting",(-1.205,.095,.795),(2.98,.19,.05)),
    ):
        add(name,size,center,collider=True)
    for side,x in (("Left",.105),("Right",1.255)):
        add("Home Locked Room Door Architrave "+side,(.08,2.29,.055),(x,1.145,.785),collider=True)
    add("Home Locked Room Door Architrave Head",(1.23,.08,.055),(.68,2.25,.785))
    add("Home Locked Room Door Recessed Panel",(.85,1.86,.018),(.68,1.10,.801),tint=(.215,.145,.085,1),collider=True)

    # Kitchen hardware follows its cabinets and never overlaps the refrigerator gap.
    for i,x in enumerate((-6.23,-5.68)):
        add(f"Home Kitchen Cabinet Door {i}",(.51,.72,.055),(x,.48,2.445),"RoomLaminate",group="home.furniture.kitchen.left")
        add(f"Home Kitchen Cabinet Pull {i}",(.14,.025,.045),(x,.70,2.395),"PaintedMetal",group="home.furniture.kitchen.left",tint=(.36,.35,.30,1))
        add(f"Home Kitchen Drawer Front {i}",(.51,.13,.055),(x,.875,2.442),"RoomLaminate",group="home.furniture.kitchen.left")
        add(f"Home Kitchen Drawer Pull {i}",(.15,.022,.035),(x,.875,2.395),"PaintedMetal",group="home.furniture.kitchen.left")
    add("Home Kitchen Splashback",(3.38,.61,.028),(-4.96,1.365,3.74),"RoomLaminate",tint=(.48,.49,.42,1))
    for i in range(11):
        add(f"Home Kitchen Splashback Joint {i}",(.009,.57,.003),(-6.55+i*.31,1.365,3.722),"Plain",tint=(.28,.29,.25,1))
    # Two ordinary upper cupboards, with frame joinery and separate shallow doors.
    for i,x in enumerate((-6.29,-5.66)):
        for suffix,c,size in (
            ("Left",(x-.29,2.68,3.51),(.045,.56,.46)),
            ("Right",(x+.29,2.68,3.51),(.045,.56,.46)),
            ("Top",(x,2.9375,3.51),(.58,.045,.46)),
            ("Bottom",(x,2.4225,3.51),(.58,.045,.46)),
            ("Back",(x,2.68,3.7275),(.58,.515,.025)),
            ("Door",(x,2.68,3.2675),(.575,.515,.035)),
        ):
            add(f"Home Kitchen Upper {i} {suffix}",size,c,"RoomLaminate")
        add(f"Home Kitchen Upper {i} Handle",(.025,.15,.04),(x+.19,2.56,3.225),"PaintedMetal")
        add(f"Home Kitchen Upper {i} Recess",(.45,.38,.010),(x,2.68,3.246),"RoomLaminate",tint=(.365,.39,.32,1))
    # Real sink drain and a finite curved faucet; no water or new interaction.
    drain_size=(.075,.006,.075)
    add("Home Kitchen Sink Drain",drain_size,(-6.02938,.948,2.89),"PaintedMetal",geometry=cylinder(drain_size),group="home.furniture.kitchen.left")
    for i in range(4):
        add(f"Home Kitchen Sink Drain Slot {i}",(.003,.001,.050),(-6.04738+i*.012,.9515,2.89),"Plain",group="home.furniture.kitchen.left",tint=(.05,.055,.05,1))
    faucet=pipe(((0,-.155,.14),(0,.07,.14),(0,.135,.10),(0,.15,.035),(0,.145,-.06),(0,.105,-.12)),.014)
    faucet=fit(faucet,(.028,.333,.288))
    add("Home Kitchen Curved Faucet",(.028,.333,.288),(-6.02938,1.2705,3.05),"PaintedMetal",geometry=faucet,group="home.furniture.kitchen.left",tint=(.50,.52,.48,1))
    add("Home Kitchen Faucet Base",(.085,.045,.075),(-6.02938,1.1225,3.19),"PaintedMetal",group="home.furniture.kitchen.left")
    add("Home Kitchen Faucet Lever",(.11,.025,.032),(-5.971,1.20,3.19),"PaintedMetal",group="home.furniture.kitchen.left")
    # The stove is an integrated right cabinet; the underlying front is recessed.
    add("Home Stove Enamel Top",(.72,.035,.69),(-3.685,1.048,2.88),"Enamel",group="home.furniture.kitchen.right",tint=(.57,.565,.49,1))
    add("Home Stove Oven Frame",(.68,.53,.035),(-3.685,.46,2.445),"PaintedMetal",group="home.furniture.kitchen.right")
    add("Home Stove Oven Glass",(.53,.33,.018),(-3.685,.445,2.418),"Plain",group="home.furniture.kitchen.right",tint=(.075,.08,.07,1))
    add("Home Stove Oven Handle",(.53,.035,.055),(-3.685,.705,2.388),"PaintedMetal",group="home.furniture.kitchen.right")
    for i,(x,z) in enumerate(((-3.87,2.71),(-3.50,2.71),(-3.87,3.06),(-3.50,3.06))):
        size=(.22,.025,.22)
        add(f"Home Stove Burner {i}",size,(x,1.077,z),"PaintedMetal",geometry=cylinder(size,((1,-.5),(1,.5),(.72,.5),(.72,-.5))),group="home.furniture.kitchen.right",tint=(.12,.13,.115,1))
        for angle in (0,90):
            add(f"Home Stove Trivet {i} {angle}",(.27,.018,.028),(x,1.10,z),"PaintedMetal",group="home.furniture.kitchen.right",rotation=(0,angle,0),tint=(.12,.13,.115,1))
    for i in range(4):
        size=(.045,.045,.035)
        add(f"Home Stove Control {i}",size,(-3.91+i*.15,.855,2.409),"Plain",tint=(.10,.11,.095,1),group="home.furniture.kitchen.right")
    add("Home Stove Hood Canopy",(.78,.14,.65),(-3.685,2.02,3.10),"PaintedMetal",tint=(.39,.41,.36,1))
    add("Home Stove Hood Flue",(.28,.87,.28),(-3.685,2.525,3.48),"PaintedMetal",tint=(.39,.41,.36,1))
    add("Home Stove Hood Filter",(.61,.014,.44),(-3.685,1.943,3.09),"PaintedMetal",tint=(.18,.21,.18,1))
    for i in range(9):
        add(f"Home Stove Hood Vent {i}",(.022,.003,.35),(-3.925+i*.060,1.934,3.09),"Plain",tint=(.075,.085,.07,1))
    add("Home Kitchen Wall Shelf",(1.35,.065,.32),(-5.93,1.94,3.62),"RoomWood")
    for x in (-6.40,-5.47):
        add("Home Kitchen Shelf Bracket "+str(x),(.035,.27,.23),(x,1.80,3.65),"PaintedMetal")
    for i in range(3):
        size=(.115,.17,.115)
        add(f"Home Kitchen Unmarked Cup {i}",size,(-6.30+i*.19,2.0575,3.60),"Enamel",geometry=cylinder(size,((1,-.5),(1,.5),(.82,.5),(.82,-.38))),tint=(.47,.49,.42,1))
    kettle_size=(.24,.25,.24)
    add("Home Kitchen Kettle Body",kettle_size,(-5.47,2.0975,3.60),"Enamel",geometry=cylinder(kettle_size,((.68,-.5),(.9,-.43),(1,-.17),(.96,.22),(.65,.43),(.52,.5))),tint=(.41,.45,.40,1))
    add("Home Kitchen Kettle Lid",(.135,.025,.135),(-5.47,2.235,3.60),"PaintedMetal",geometry=cylinder((.135,.025,.135)))
    handle=pipe(((-.085,-.08,0),(-.10,.07,0),(-.065,.13,0),(.065,.13,0),(.10,.07,0),(.085,-.08,0)),.012)
    handle=fit(handle,(.224,.234,.024))
    add("Home Kitchen Kettle Handle",(.224,.234,.024),(-5.47,2.24,3.60),"PaintedMetal",geometry=handle,tint=(.14,.16,.135,1))
    add("Home Kitchen Kettle Spout",(.11,.065,.065),(-5.62,2.105,3.60),"PaintedMetal")
    pot_size=(.30,.21,.30)
    add("Home Kitchen Stored Pot",pot_size,(-6.20,3.065,3.50),"Enamel",geometry=cylinder(pot_size,((.84,-.5),(1,-.4),(1,.5),(.89,.5),(.89,-.32))),tint=(.38,.42,.37,1))
    add("Home Kitchen Pot Lid",(.305,.028,.305),(-6.20,3.185,3.50),"PaintedMetal",geometry=cylinder((.305,.028,.305)))
    pan_size=(.27,.055,.27)
    # A hanging pan's bowl faces the room, separate from its metal handle.
    pan_geom=kit.rotated_z(cylinder(pan_size),0)
    unity=bp.to_source(pan_geom)
    pan_geom=bp.to_source(bp.u_rotated(unity,(90,0,0)))
    add("Home Kitchen Hanging Pan",(.27,.27,.055),(-5.21,1.43,3.68),"PaintedMetal",geometry=fit(pan_geom,(.27,.27,.055)),tint=(.20,.22,.19,1))
    add("Home Kitchen Pan Handle",(.045,.29,.025),(-5.21,1.67,3.68),"PaintedMetal")
    # Narrow draining rack stays wholly left of the sink rim.
    for x in (-6.64,-6.48):
        add("Home Kitchen Rack Rail "+str(x),(.012,.045,.40),(x,1.054,2.90),"PaintedMetal",group="home.furniture.kitchen.left")
    for i in range(6):
        x=-6.63+i*.03
        add(f"Home Kitchen Rack Wire {i}",(.008,.09,.35),(x,1.08,2.90),"PaintedMetal",group="home.furniture.kitchen.left")

    # The main pendant remains runtime articulated/emissive under its old bindings.
    add("Home Kitchen Task Fixture",(.56,.065,.105),(-5.20,1.7525,2.50),"PaintedMetal")
    add("Home Kitchen Task Diffuser",(.47,.018,.075),(-5.20,1.72,2.50),"Enamel",tint=(.66,.62,.47,1))

    # Clock and radio keep their roles; ordinary possessions acquire no new lore.
    add("Home Old Radio",(.56,.30,.36),(-6.425,2.40,1.70),"RoomWood",group="home.furniture.bookcase")
    add("Home Radio Dial",(.32,.12,.018),(-6.425,2.41,1.51),"Plain",group="home.furniture.bookcase",tint=(.29,.20,.10,1))
    for i in range(7):
        add(f"Home Radio Speaker Slot {i}",(.014,.105,.004),(-6.65+i*.025,2.41,1.509),"Plain",group="home.furniture.bookcase",tint=(.08,.09,.075,1))

    def rag(name,pos,day,size,group="shell",tint=(.38,.39,.32,1),rotation=0,last=7):
        return add(name,size,pos,"BedLinen",day=day,last=last,geometry=cloth(size),group=group,tint=tint,rotation=(0,rotation,0))
    def bottle(name,pos,day,group="home.furniture.table"):
        size=(.105,.34,.105)
        return add(name,size,pos,"Plain",day=day,geometry=cylinder(size,((.86,-.5),(1,-.46),(1,.13),(.77,.27),(.32,.33),(.32,.47),(.40,.5))),tint=(.16,.22,.135,1),group=group)
    def stain(name,pos,day,size,group="shell",overlay=False):
        p=add(name,size,pos,"Plain",day=day,geometry=irregular_patch(size,sum(map(ord,name))%41),tint=(.16,.13,.095,1),group=group)
        if overlay:p["surface_overlay"]=True
        return p
    add("Home Table Ashtray",(.26,.045,.26),(-3.03,.856,.34),"Enamel",geometry=dish((.26,.045,.26),True),group="home.furniture.table")
    for i in range(2):
        add(f"Home Day1 Stacked Plate {i}",(.25,.025,.25),(-2.30,.843+i*.026,.34),"Enamel",geometry=dish((.25,.025,.25)),last=2,group="home.furniture.table")
    rag("Home Day1 Folded Towel",(-6.1,1.053,3.08),1,(.48,.045,.28),"home.furniture.kitchen.left",last=2)
    for day in range(2,8):
        for i in range(day-1):
            slot=(day-2)*(day-1)//2+i
            bottle(f"Day{day} Table Bottle {i}",(-3.20+(slot%6)*.18,.83+.17+(slot//6)*.035,-.34+(slot//6)*.22),day)
        for i in range(max(1,day-2)):
            size=(.25,.035,.25)
            add(f"Day{day} Kitchen Plate {i}",size,(-6.30+(i%3)*.23,1.05+.035*(day-2),2.68+((day+i)%2)*.29),"Enamel",day=day,geometry=dish(size),group="home.furniture.kitchen.left")
        for i in range(day*2):
            angle=day*1.3+i*2.399
            add(f"Day{day} Ashtray End {i}",(.013,.013,.064),(-3.03+math.cos(angle)*.07,.89+(day-2)*.006,.34+math.sin(angle)*.07),"Plain",day=day,geometry=cylinder((.013,.013,.064)),rotation=(0,math.degrees(angle),0),group="home.furniture.table",tint=(.42,.35,.24,1))
        if day>=3:
            rag(f"Day{day} Sofa Foot Linen",(-4.57,.62+(day-3)*.025,.12),day,(.40,.075,.46),"home.bed.surface-clutter",rotation=day*13)
        if day>=4:
            stain(f"Day{day} Table Spill",(-2.41,.836+(day-4)*.002,.25),day,(.35,.004,.31),"home.furniture.table")
            stain(f"Day{day} Kitchen Spill",(-6.0,1.084+(day-4)*.001,2.84),day,(.57,.006,.41),"home.furniture.kitchen.left")
        if day>=5:
            # These bathroom calendar parts keep their original placement, mesh and tint.
            rag(f"Day{day} Bathroom Towel",(2.94+(day-5)*.025,.05+(day-5)*.065,3.44+(day-5)*.035),day,(.39,.08,.46),tint=(.28,.29,.23,1))
            size=(.46+(day-5)*.09,.004,.51)
            add(f"Day{day} Bathroom Floor Soil",size,(3.17,.029+(day-5)*.001,2.03),"Plain",day=day,geometry=irregular_patch(size,sum(map(ord,f"Day{day} Bathroom Floor Soil"))%41),tint=(.14,.17,.13,1))
    rag("Day5 Sofa Abandoned Duvet",(-5.30,.655,-.38),5,(1.82,.18,1.32),"home.bed.surface-clutter",last=6,rotation=-5)
    rag("Day7 Sofa Crumpled Duvet",(-5.41,.70,-.38),7,(2.05,.28,1.40),"home.bed.surface-clutter")
    rag("Day6 Sofa Dirty Sheet",(-5.04,.76,-.54),6,(1.02,.17,.82),"home.bed.surface-clutter",rotation=17)
    for i,(day,pos,size) in enumerate(((4,(-.42,.003,-1.35),(.76,.004,.62)),(5,(1.12,.004,-.28),(1.18,.004,.58)),(6,(.18,.005,-2.75),(1.18,.004,.43)),(6,(2.38,.005,-1.90),(.54,.004,.90)),(7,(.31,.006,-1.28),(1.43,.004,1.12)),(7,(1.67,.006,-.86),(.82,.004,1.37)))):
        stain(f"Day{day} Floor Tracked Grime {i}",pos,day,size,overlay=True)
    for i,(x,z) in enumerate(((.63,-1.49),(1.53,-.16),(.41,-2.47),(2.24,-2.59),(-.36,-.57),(1.42,-1.92),(2.35,-.65),(.95,-3.22))):
        p=rag(f"Day{5+i//3} Floor Flattened Paper {i}",(x,.015,z),5+i//3,(.24,.018,.19),tint=(.45,.39,.26,1),rotation=i*47)
        p["surface_overlay"]=True


def preview_poses():
    poses={"Home Floor":(-1,-.08,0),"Home Back Wall":(-1,1.7,4),
            "Home Left Wall":(-7,1.7,0),"Home Bed Frame":(-5.475,.22,-.375),
            "Home Bed Mattress":(-5.475,.47,-.375),"Home Pillow":(-6.299,.6035,-.375),
            "Home Bed Crooked Blanket":(-5.025,.60,.17),
            "Home Sleeping Sofa Back":(-5.475,.80,.380),
            "Home Sleeping Sofa Arm Left":(-6.71,.60,-.375),
            "Home Sleeping Sofa Arm Right":(-4.24,.60,-.375),
            "Home Scarred Table":(-1.925,.78,.15),"Home Dining Table Base":(-1.925,.365,.15),
            "Dining Chair":CHAIR_POSITION,
            "Home Weaver Painting Frame":PAINTING_FRAME_CENTER,
            "Home Weaver Painting Image":PAINTING_IMAGE_CENTER,
            "Home Battered Cabinet":(-6.425,1.125,1.70),
            "Home Cabinet Shelf 1":(-6.475,.46,1.227),"Home Cabinet Shelf 2":(-6.475,1.08,1.227),
            "Home Alarm Clock Nightstand":(-6.285,.36,-1.84),
            "Home Alarm Clock Nightstand Top":(-6.285,.745,-1.84),
            "Alarm Clock Body":(-6.285,.853,-1.84),"Alarm Clock Face":(-6.2094,.853,-1.84),
            "Alarm Clock Snooze":(-6.294,.9448,-1.84),
            "Home Kitchen Counter Left":(-5.94475,.48,2.89),"Home Kitchen Top Left":(-5.94475,.98,2.89),
            "Home Kitchen Counter Right":(-3.68475,.48,2.89),"Home Kitchen Top Right":(-3.68475,.98,2.89),
            "Home Refrigerator Cabinet Left":(-5.1395,1.12,2.9),"Home Refrigerator Cabinet Right":(-4.1895,1.12,2.9),
            "Home Refrigerator Cabinet Top":(-4.6645,2.125,2.9),"Home Refrigerator Cabinet Bottom":(-4.6645,.115,2.9),
            "Home Refrigerator Cabinet Back":(-4.6645,1.12,3.2025),"Home Refrigerator Door Enamel":(-4.6645,1.12,2.47)}
    for name in sleep_binding_names():
        poses[name]=turned_sleep_position(poses[name])
    # Keep the folded strip clear of the unchanged sleeper's shoulder/hand
    # corridor; it is folded against the back, rather than underneath him.
    poses["Home Bed Crooked Blanket"]=(-6.725,.60,.275)
    for name in bookcase_binding_names():
        poses[name]=turned_bookcase_position(poses[name])
    return poses


def sleep_binding_names():
    return ("Home Bed Frame","Home Bed Mattress","Home Pillow","Home Bed Crooked Blanket",
            "Home Sleeping Sofa Back","Home Sleeping Sofa Arm Left","Home Sleeping Sofa Arm Right")


def preview_rotations():
    rotations={name:(0,-90,0) for name in (*sleep_binding_names(),*bookcase_binding_names(),
            "Home Alarm Clock Nightstand","Home Alarm Clock Nightstand Top",
            "Alarm Clock Body","Alarm Clock Face","Alarm Clock Snooze")}
    rotations["Home Bed Crooked Blanket"]=(0,-90,-2)
    return rotations


def bookcase_binding_names():
    return ("Home Battered Cabinet","Home Cabinet Shelf 1","Home Cabinet Shelf 2")
