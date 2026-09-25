"""The old lodge's fitted drinks cabinet and two hand-made lounge chairs.

All coordinates are lodge-local Unity metres. The shared sitting owner reads
the measured seat/facing anchors; every piece of the takeable photograph is
parented to its single authored root. Nothing here owns gameplay or light.
"""
from __future__ import annotations
import math
import interior_kit as kit
import bar_parts as bp
from village_lodge_props import tube, lathe
from village_lodge_furniture import placed, merged

CABINET_CENTER = (3.05, 0, -5.35)
CABINET_SIZE = (1.70, .90, .45)
CHAIRS = (((5.95, .48, -4.60), 28), ((8.00, .48, -4.40), -32))
SEAT_SIZE = (.54, .52)
CHAIR_SIZE = (.90, .77)
TABLE_CENTER = (7.05, 0, -4.53)
PHOTO_CENTER = (2.68, 1.025, -5.235)
PHOTO_TILT = -12
PHOTO_DOCK = (2.68, .02, -4.15)
PHOTO_FRONT = (0, math.sin(math.radians(12)), math.cos(math.radians(12)))
PHOTO_UP = (0, math.cos(math.radians(12)), -math.sin(math.radians(12)))
ANCHORS = []
for index, (seat, yaw) in enumerate(CHAIRS, 1):
    face = (math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw)))
    ANCHORS += [dict(kind="SkiLodge", name=f"LodgeLoungeChair{index}Seat", position=seat),
                dict(kind="SkiLodge", name=f"LodgeLoungeChair{index}Facing",
                     position=tuple(seat[i]+face[i] for i in range(3)))]
ANCHORS += [dict(kind="SkiLodge", name=name, position=position) for name, position in (
    ("LodgeGroupPhotograph", PHOTO_CENTER),
    ("LodgeGroupPhotoDock", PHOTO_DOCK),
    ("LodgeGroupPhotoCameraFront", tuple(PHOTO_CENTER[i]+PHOTO_FRONT[i] for i in range(3))),
    ("LodgeGroupPhotoCameraUp", tuple(PHOTO_CENTER[i]+PHOTO_UP[i] for i in range(3))),
)]


def silhouette(points, depth, z=0, lean=0):
    """A closed sculpted profile; lean is an authored backward slope in Y."""
    vertices = [(x, y, z+side*depth*.5+lean*y) for side in (-1, 1) for x, y in points]
    count = len(points)
    faces = [tuple(reversed(range(count))), tuple(range(count, count*2))]
    faces += [(i, (i+1)%count, (i+1)%count+count, i+count) for i in range(count)]
    geometry = vertices, faces
    if bp.signed_volume(geometry) < 0:
        geometry = vertices, [tuple(reversed(face)) for face in faces]
    return geometry


def add_minibar(add, parts):
    b = bp.u_box
    wood = (.265, .193, .135, 1)
    edge = (.195, .140, .103, 1)
    brass = (.40, .32, .175, 1)
    burgundy = (.275, .125, .115, 1)

    def prop(name, geometry, surface="Timber", solid=True, tint=wood, parent=None):
        add("SkiLodge", name, geometry, surface, solid, tint)
        if parent:
            parts[-1]["parent"] = parent

    def cabinet(geometry):
        return kit.translated(geometry, CABINET_CENTER)

    # The low cabinet occupies the solid pier between the vestibule and window.
    # Its lower doors are recessed into rails; the open drinks shelf is real.
    body = [b((0, .077, 0), (1.56, .114, .36), .015),
            b((0, .865, 0), (1.70, .070, .45), .018),
            b((0, .338, -.184), (1.57, .50, .055), .006),
            b((0, .562, 0), (1.59, .036, .385), .007),
            b((0, .827, -.183), (1.56, .055, .055), .007)]
    for sign in (-1, 1):
        body.append(b((sign*.802, .483, 0), (.056, .734, .385), .009))
    prop("LodgeMinibarCabinet", cabinet(merged(body)))
    doors = []
    for x in (-.389, .389):
        # Broad recessed centre and four raised rails keep the cupboard legible.
        doors.append(b((x, .325, .158), (.719, .394, .034), .005))
        for sx in (-1, 1):
            doors.append(b((x+sx*.318, .325, .186), (.069, .42, .035), .005))
        for y in (.143, .507):
            doors.append(b((x, y, .186), (.565, .055, .035), .005))
    prop("LodgeMinibarPanelledDoors", cabinet(merged(doors)), tint=edge)
    handles = []
    for x in (-.08, .08):
        handles.append(tube([(x, .365, .208), (x, .365, .235),
                             (x, .425, .235), (x, .425, .208)], .008, 6))
    prop("LodgeMinibarBrassHandles", cabinet(merged(handles)), "LighterMetal", False, brass)
    # Muted unbranded bottles: individual shoulders, narrow necks and corks.
    bottle_tints = ((.19,.25,.16,1), (.29,.215,.115,1), (.23,.29,.205,1))
    corks = []
    for index in range(7):
        x = -.63+index*.21
        height = (.218, .232, .205)[index%3]
        profile = [(.041,0), (.047,.016), (.047,height*.67),
                   (.021,height*.79), (.017,height-.008), (.021,height)]
        prop(f"LodgeMinibarBottle{index+1}", cabinet(lathe(profile, (x,.580,-.017), 6)),
             "Glass", False, bottle_tints[index%3])
        corks.append(lathe([(.018,0),(.018,.013)],(x,.580+height,-.017),6))
    prop("LodgeMinibarBottleCorks", cabinet(merged(corks)), "Canvas", False, (.40,.31,.20,1))
    # The top keeps enough uncluttered surface around the small photograph.
    loose = [placed(lathe([(.015,0),(.015,.032)], segments=7),
                    (3.40,.916,-5.28), 0),
             placed(bp.u_rotated(lathe([(.013,0),(.013,.030)],segments=7),(90,0,0)),
                    (3.47,.914,-5.27))]
    prop("LodgeMinibarLooseCorks", merged(loose), "Canvas", False, (.42,.32,.21,1))
    tool = [b((3.61,.916,-5.28),(.12,.024,.03),.007)]
    spiral = [(3.61+.012*math.cos(i*math.tau/6), .911+.005*math.sin(i*math.tau/6),
               -5.27+i*.004) for i in range(13)]
    prop("LodgeMinibarCorkscrewHandle", merged(tool), tint=edge, solid=False)
    prop("LodgeMinibarCorkscrew", tube(spiral,.003,4), "LighterMetal",False,brass)

    # Raised headrest, pronounced wings, shaped arms and cabriole feet. The
    # actual central seat remains open for the shared production hero action.
    for index, (seat, yaw) in enumerate(CHAIRS, 1):
        at = (seat[0], 0, seat[2])
        prefix = f"LodgeLoungeChair{index}"
        frame = []
        for x in (-.325, .325):
            for z in (-.265, .245):
                sign = 1 if x > 0 else -1
                frame.append(tube([(x+sign*.023,.060,z+.010),
                                   (x+sign*.045,.072,z+.022),
                                   (x-sign*.025,.235,z-.013),
                                   (x,.385,z)], sides=6, radii=(.041,.046,.027,.048)))
        frame += [b((0,.354,0),(.73,.065,.59),.012)]
        crown = [(-.35,.47,-.265),(-.40,1.02,-.338),(-.29,1.265,-.370),
                 (-.17,1.34,-.392),(0,1.37,-.40),(.17,1.34,-.392),
                 (.29,1.265,-.370),(.40,1.02,-.338),(.35,.47,-.265)]
        frame.append(tube(crown,.023,6))
        for sign in (-1,1):
            frame.append(tube([(sign*.353,.40,.245),(sign*.383,.62,.285),
                               (sign*.373,.688,.225),(sign*.366,.713,-.18)],
                              sides=6,radii=(.023,.026,.034,.032)))
        prop(prefix+"Wood", placed(merged(frame), at, yaw))
        upholstery = [b((0,.425,0),(.54,.110,.52),.022),
            silhouette([(-.31,.49),(.31,.49),(.355,1.01),(.285,1.235),
                        (.17,1.31),(0,1.34),(-.17,1.31),(-.285,1.235),(-.355,1.01)],
                       .085,z=-.215,lean=-.115)]
        for sign in (-1,1):
            upholstery += [placed(bp.u_rotated(b((0,0,0),(.12,.43,.215),.018),
                                              (0,-sign*12,-sign*7)),
                                  (sign*.345,1.015,-.237)),
                           b((sign*.365,.713,.014),(.14,.070,.44),.020)]
        prop(prefix+"Upholstery", placed(merged(upholstery), at, yaw), "Canvas", True, burgundy)
        # Thin stitched piping follows the seat edge without excessive tufts.
        piping = tube([(-.247,.478,.235),(.247,.478,.235),(.247,.478,-.235),
                       (-.247,.478,-.235),(-.247,.478,.235)],.004,5)
        prop(prefix+"Piping", placed(piping, at, yaw), "Canvas", False, (.41,.24,.185,1))
        if index == 2:
            cloth = [b((.365,.756,.003),(.15,.016,.36),.006),
                     b((.433,.591,.003),(.016,.322,.36),.005)]
            prop("LodgeLoungeThrow",placed(merged(cloth),at,yaw),"Canvas",False,(.365,.335,.253,1))
            stripes = [b((.443,.590,z),(.005,.29,.013),0) for z in (-.10,.03,.12)]
            prop("LodgeLoungeThrowWeave",placed(merged(stripes),at,yaw),"Canvas",False,(.255,.265,.219,1))

    # Small occasional table: profiled top, turned pedestal, three swept feet.
    top = lathe([(.323,.512),(.35,.532),(.35,.550),(.338,.570)], TABLE_CENTER, 16)
    stem = lathe([(.095,.12),(.064,.16),(.045,.24),(.069,.33),(.061,.39),
                  (.036,.445),(.050,.513)],TABLE_CENTER,8)
    legs = []
    for yaw in (0,120,240):
        leg = tube([(0,.205,0),(0,.12,.12),(0,.073,.27),(0,.055,.305)],
                   sides=6,radii=(.045,.037,.027,.032))
        legs.append(placed(leg,TABLE_CENTER,yaw))
    prop("LodgeLoungeTable",merged([top,stem]+legs))
    # An oval dull-metal tray and two empty tumblers; no new drink transaction.
    tray = kit.scaled(lathe([(.165,0),(.172,.008),(.172,.018),(.159,.020),
                             (.155,.006)],segments=12),(1,1,.73))
    tray_at = (TABLE_CENTER[0],.570,TABLE_CENTER[2])
    prop("LodgeLoungeTray",kit.translated(tray,tray_at),"LighterMetal",False,(.34,.29,.21,1))
    glasses = []
    for offset in (-.073,.073):
        glasses.append(lathe([(.030,0),(.034,.006),(.040,.09),(.035,.094),
                              (.029,.013),(.006,.013)],
                             (TABLE_CENTER[0]+offset,.577,TABLE_CENTER[2]),8))
    prop("LodgeLoungeTumblers",merged(glasses),"Glass",False,(.58,.59,.54,.26))
    # Flat textile edges remain below the walking-step tolerance.
    rug_at=(7.0,.026,-4.31)
    prop("LodgeLoungeRug",b(rug_at,(2.86,.012,2.46),.005),"Canvas",False,(.29,.233,.184,1))
    borders=[]
    for x in (5.66,8.34):borders.append(b((x,.033,-4.31),(.045,.002,2.28),0))
    for z in (-5.45,-3.17):borders.append(b((7,.033,z),(2.68,.002,.045),0))
    prop("LodgeLoungeRugBorder",merged(borders),"Canvas",False,(.39,.30,.205,1))

    # A single collectible root contains image, frame, back and easel. Its face
    # normal/up anchors preserve the genuine backward lean in the close-up.
    def photo_geometry(geometry):
        return kit.translated(bp.u_rotated(geometry,(PHOTO_TILT,0,0)),PHOTO_CENTER)
    rails = [b((x,0,0),(.022,.25,.023),.004) for x in (-.164,.164)]
    rails += [b((0,y,0),(.306,.022,.023),.004) for y in (-.114,.114)]
    rails.append(b((0,0,-.011),(.322,.226,.012),0))
    prop("LodgeGroupPhotographFrame",photo_geometry(merged(rails)),solid=False,
         tint=edge,parent="LodgeGroupPhotograph")
    # Contact-ledged lower edge and one hinged back leg both land on the top.
    support = [b((2.68,.903,-5.210),(.23,.006,.042),.002),
               tube([(2.68,1.108,-5.267),(2.68,.907,-5.425)],.009,6)]
    prop("LodgeGroupPhotographEasel",merged(support),solid=False,
         tint=edge,parent="LodgeGroupPhotograph")
    width,height=.306,.204
    image=b((0,0,.0055),(width,height,.005),0)
    # Looking at the authored +Z face, camera-right is lodge -X. The front
    # must reproduce the source photograph, not mirror its skiers and lift.
    uv=[(.008+.984*(.5-x/width),.008+.984*(y/height+.5)) for x,y,z in image[0]]
    prop("LodgeGroupPhotographImage",photo_geometry(image),"LodgeGroupPhotograph",False,
         (1,1,1,1),"LodgeGroupPhotograph")
    parts[-1]["picture_uv"]=uv


def validate_minibar(parts):
    lodge={p["name"]:p for p in parts if p["kind"]=="SkiLodge"}
    new=[p for name,p in lodge.items() if name.startswith(("LodgeMinibar","LodgeLounge","LodgeGroupPhotograph"))]
    for part in new:
        assert bp.signed_volume(part["geometry"])>1e-10,"Inward minibar part: "+part["name"]
        low,high=kit.bounds(part["geometry"])
        assert low[0]>1.80 and high[0]<8.68 and low[2]>-5.68 and high[2]<-2.9,part["name"]+" escaped its corner"
        assert low[1]>=.019-1e-6,part["name"]+" crosses the lodge floor"
    low,high=kit.bounds(lodge["LodgeMinibarCabinet"]["geometry"])
    assert abs(high[1]-.90)<1e-8 and low[0]>2.19 and high[0]<3.91
    assert high[0]<4.25,"Cabinet occupies the front window"
    for index,(seat,yaw) in enumerate(CHAIRS,1):
        chair=merged(p["geometry"] for p in new if p["name"].startswith(f"LodgeLoungeChair{index}"))
        local=bp.u_rotated(kit.translated(chair,(-seat[0],0,-seat[2])),(0,-yaw,0))
        low,high=kit.bounds(local)
        assert -.45<=low[0]<high[0]<=.45 and -.43<=low[2]<high[2]<=.34,"Chair footprint mismatch"
        assert high[1]<1.40 and low[1]>=.019
    photograph=[p for p in new if p["name"].startswith("LodgeGroupPhotograph")]
    assert len(photograph)==3 and all(not p["solid"] and p.get("parent")=="LodgeGroupPhotograph" for p in photograph)
    image=lodge["LodgeGroupPhotographImage"]
    assert image["surface"]=="LodgeGroupPhotograph" and len(image["picture_uv"])==len(image["geometry"][0])
    assert all(0<u<1 and 0<v<1 for u,v in image["picture_uv"])
    for vertex,(u,v) in zip(image["geometry"][0],image["picture_uv"]):
        assert abs(u-(.008+.984*(.5-(vertex[0]-PHOTO_CENTER[0])/.306)))<1e-9,\
            "Photograph mirrors the source when viewed from its +Z front"
    assert abs(sum(PHOTO_FRONT[i]*PHOTO_UP[i] for i in range(3)))<1e-9

    # Use the same .78 m entry offset as CityBenchSitPlan. Full capsule rings
    # and the approach lane must clear the other chair, table and legacy props.
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    obstacles=[BVHTree.FromPolygons(*p["geometry"],all_triangles=False) for p in parts
               if p["kind"]=="SkiLodge" and p["solid"] and p["name"] not in ("Floor","Foundation")]
    docks=[PHOTO_DOCK]
    for seat,yaw in CHAIRS:
        face=(math.sin(math.radians(yaw)),math.cos(math.radians(yaw)))
        docks.append((seat[0]+face[0]*.78,.02,seat[2]+face[1]*.78))
    for dock in docks:
        for index in range(13):
            angle=index*math.tau/12
            radius=0 if index==12 else .35
            origin=Vector((dock[0]+math.cos(angle)*radius,.10,dock[2]+math.sin(angle)*radius))
            assert not any(tree.ray_cast(origin,Vector((0,1,0)),2.2)[0] is not None for tree in obstacles),"Blocked minibar interaction dock"
        for x_offset in (-.35,0,.35):
            origin=Vector((dock[0]+x_offset,1.1,dock[2]))
            target=Vector((dock[0]+x_offset,1.1,-2.25))
            direction=target-origin
            assert not any(tree.ray_cast(origin,direction.normalized(),direction.length)[0] is not None for tree in obstacles),"Blocked minibar approach lane"
