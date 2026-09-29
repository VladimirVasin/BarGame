"""Identity-preserving constructions for the fisherman and the mother.

Geometry remains in the pedestrian library's authored space. Its established
NpcHumanV2 remap is applied exactly once by PedestrianBuilder.add_part.
"""
from __future__ import annotations

import math
from mathutils import Vector
import npc_detail_geometry as detail
from fisherman_detail import beard_geometry

VERSION = "1.0.0"
KEYS = ("lake_fisherman", "mother")


def combine(geometries):
    points, faces = [], []
    for vertices, polygons in geometries:
        offset = len(points)
        points.extend(vertices)
        faces.extend(tuple(i + offset for i in face) for face in polygons)
    return points, faces


def cloth_volume(geometry, folds=0.0, sides=16):
    """Rounded, lightly folded sections inside an authored garment envelope."""
    vertices, _ = geometry
    points = [Vector(p) for p in vertices]
    lo, hi = min(p.z for p in points), max(p.z for p in points)
    bottom = [p for p in points if abs(p.z-lo) < 1e-5]
    top = [p for p in points if abs(p.z-hi) < 1e-5]
    def dimensions(ps):
        low = Vector(tuple(min(p[i] for p in ps) for i in range(3)))
        high = Vector(tuple(max(p[i] for p in ps) for i in range(3)))
        return (low + high) / 2, (high - low) / 2
    c0, r0 = dimensions(bottom); c1, r1 = dimensions(top)
    if min(r0.x, r0.y, r1.x, r1.y, hi-lo) <= 1e-6:
        return geometry
    rings = []
    for t in (0, .08, .26, .55, .83, .94, 1):
        c, r = c0.lerp(c1,t), r0.lerp(r1,t)
        bevel = .95 if t in (0,1) else 1
        ring = []
        for j in range(sides):
            a = math.tau*j/sides
            # Superelliptic sections retain the familiar fuller cloth mass,
            # with rounded corners instead of a square cardboard torso.
            x = math.copysign(abs(math.cos(a))**.68,math.cos(a))
            y = math.copysign(abs(math.sin(a))**.68,math.sin(a))
            fold = 1 + folds * math.cos(a*6 + .6*t) * math.sin(math.pi*t)
            ring.append((c.x+r.x*x*bevel*fold,c.y+r.y*y*bevel*fold,c.z))
        rings.append(ring)
    return detail.loft(rings)


def open_shell(stations, gap=.40, thickness=.006, sides=18):
    """A sewn shell with a real front opening, back surface and bound edges."""
    outer, inner = [], []
    for row,(z, rx, ry, cy) in enumerate(stations):
        row_gap=gap[row] if isinstance(gap,(tuple,list)) else gap
        for j in range(sides+1):
            a = row_gap+(math.tau-2*row_gap)*j/sides
            outer.append(Vector((rx*math.sin(a),cy-ry*math.cos(a),z)))
            inner.append(Vector(((rx-thickness)*math.sin(a),cy-(ry-thickness)*math.cos(a),z)))
    width=sides+1; count=len(outer); faces=[]
    for r in range(len(stations)-1):
        for j in range(sides):
            a=r*width+j; b=a+width
            faces += [(a,a+1,b+1,b),(a+count,b+count,b+1+count,a+1+count)]
        a=r*width; b=a+width
        faces += [(a,b,b+count,a+count),
                  (a+sides,a+sides+count,b+sides+count,b+sides)]
    for j in range(sides):
        faces.append((j,j+count,j+1+count,j+1))
        a=(len(stations)-1)*width+j
        faces.append((a,a+1,a+1+count,a+count))
    return detail.outward((outer+inner,faces))


def ribbon(points,width,thickness):
    return detail.loft([[(x-width*.5,y-thickness*.5,z),
                         (x+width*.5,y-thickness*.5,z),
                         (x+width*.5,y+thickness*.5,z),
                         (x-width*.5,y+thickness*.5,z)] for x,y,z in points])


def geometry(key,name,original,api,builder=None):
    """Return the refined named part; unrelated designs never call this."""
    side=name[-1] if name.endswith((".L",".R")) else None
    sign=1 if side=="L" else -1
    if key=="lake_fisherman" and name.startswith(("ACC_Beard", "ACC_Moustache")):
        return beard_geometry(name, builder.result)
    if key=="mother" and name=="GEO_FaceSurface":
        # Its old mouth/chin rows sat inside the underlying ellipsoid. Keep
        # the expression grid and face identity while giving the entire patch
        # measured clearance over the actual narrower mapped face width.
        vertices=[]
        for source in original[0]:
            p=Vector(source)
            skull_x=p.x*.80/.86
            ellipsoid=1-(skull_x/.098)**2-((p.z-1.548)/.122)**2
            front=-.034-.092*math.sqrt(max(0.,ellipsoid))
            p.y=min(p.y,front-.004)
            vertices.append(p)
        return vertices,original[1]
    if name.startswith("GEO_Hand."):
        wrist=(sign*(.672 if key=="mother" else .680),-.018,1.078 if key=="mother" else 1.075)
        end=(sign*.752,-.022,1.038)
        parts=detail.hand(wrist,end,side,.90 if key=="mother" else 1.)
        # Mother already owns a separate thumb renderer. The fisherman did
        # not: his complete shaped hand lives under the original hand name.
        return combine([g for n,g in parts.items() if key!="mother" or n!="thumb"])
    if key=="mother" and name.startswith("ACC_Thumb."):
        return detail.hand((sign*.672,-.018,1.078),(sign*.752,-.022,1.038),side,.90)["thumb"]
    if key=="lake_fisherman":
        if name=="CLO_HoodBrim":
            # Bind the upper hood opening with a narrow sewn edge. Even a
            # thin projecting visor becomes an opaque rectangle when this
            # working head tips down, concealing the painted eyes below it.
            points=[Vector((-.093,-.073,1.662)),Vector((-.066,-.087,1.687)),
                    Vector((0,-.093,1.707)),Vector((.066,-.087,1.687)),
                    Vector((.093,-.073,1.662))]
            return combine([detail.segment(a,b,((0,.006,1),(.5,.0065,1),(1,.006,1)),8)
                            for a,b in zip(points,points[1:])])
        if name=="CLO_HoodShell":
            return open_shell(((1.425,.116,.115,.010),(1.480,.147,.145,.012),
                               (1.580,.150,.154,.012),(1.655,.126,.131,.018),
                               (1.688,.094,.130,.012),(1.713,.068,.105,.010),
                               (1.746,.026,.040,.020)),
                              gap=(.82,.82,.82,.68,.40,0,0),thickness=.009,sides=20)
        if name=="CLO_HoodCollar":
            return open_shell(((1.328,.132,.132,0),(1.370,.127,.130,-.003),
                               (1.424,.120,.122,-.006)),gap=.55,thickness=.008,sides=16)
        if name.startswith("CLO_Sleeve."):
            return detail.segment((sign*.208,sign*-.004,1.292),(sign*.470,-.010,1.175),
                ((0,.070,.90),(.18,.081,.94),(.46,.076,.94),(.74,.073,.92),(.90,.066,.92),(1,.064,.9)),14)
        if name.startswith("CLO_SleeveLower."):
            return detail.segment((sign*.470,-.010,1.175),(sign*.680,-.018,1.075),
                ((0,.064,.90),(.18,.066,.91),(.42,.060,.90),(.67,.056,.88),(.89,.051,.88),(1,.046,.85)),14)
        if name.startswith("CLO_SleeveCuff."):
            return detail.segment((sign*.628,-.016,1.102),(sign*.696,-.019,1.066),
                ((0,.053,.9),(.16,.055,.9),(.72,.049,.9),(.88,.050,.9),(1,.046,.9)),14)
        if name.startswith("CLO_TrouserUpper."):
            return detail.segment((sign*.094,.004,.740),(sign*.103,-.012,.362),
                ((0,.091,.96),(.20,.090,.98),(.50,.080,.91),(.77,.072,.88),(1,.069,.90)),14)
        if name.startswith("CLO_BootShaft."):
            return detail.segment((sign*.103,-.012,.356),(sign*.112,-.022,.100),
                ((0,.083,.88),(.07,.087,.88),(.15,.083,.88),(.46,.079,.87),(.81,.067,.84),(1,.066,.84)),14)
        if name in ("CLO_SlickerChest","CLO_SlickerWaist","CLO_SlickerHem","CLO_StormYoke",
                    "CLO_HoodPeak","CLO_HoodBrim"):
            return cloth_volume(original,.025 if name=="CLO_SlickerHem" else .009)
        if name.startswith(("GEO_Boot.","GEO_BootSole.")):
            return cloth_volume(original,0,16)
        if name.startswith("ACC_SlickerSeam."):
            # The existing crosswise seams become a narrow curved welt.
            ps=[Vector(p) for p in original[0]]
            z=sum(p.z for p in ps)/len(ps); y=sum(p.y for p in ps)/len(ps)
            width=max(p.x for p in ps)-min(p.x for p in ps)
            points=[(-width/2+width*j/10,y+.040*(abs(-1+2*j/10)**3),z) for j in range(11)]
            return combine([detail.segment(points[i],points[i+1],((0,.0045,1),(1,.0045,1)),6) for i in range(10)])
    else:
        if name=="CLO_Cardigan":
            return open_shell(((1.062,.202,.139,.012),(1.115,.208,.145,.008),
                               (1.208,.217,.150,.003),(1.285,.216,.149,-.001),
                               (1.318,.207,.146,-.002)),gap=.29,thickness=.008,sides=16)
        if name.startswith("CLO_CardiganPanel."):
            return ribbon([(sign*.061,-.126,1.062),(sign*.063,-.132,1.13),
                           (sign*.066,-.139,1.22),(sign*.065,-.140,1.30)],.031,.012)
        if name.startswith("CLO_Sleeve."):
            return detail.segment((sign*.204,sign*-.004,1.290),(sign*.462,-.010,1.176),
                ((0,.063,.85),(.16,.073,.91),(.40,.073,.90),(.68,.067,.86),(.88,.059,.88),(1,.055,.84)),14)
        if name.startswith("GEO_Forearm."):
            return detail.segment((sign*.462,-.010,1.176),(sign*.672,-.018,1.078),
                ((0,.044,.86),(.16,.046,.85),(.38,.044,.83),(.63,.037,.80),(.85,.031,.79),(1,.027,.78)),14)
        if name.startswith("CLO_Cuff."):
            return detail.segment((sign*.430,-.009,1.192),(sign*.498,-.011,1.162),
                ((0,.061,.87),(.12,.063,.87),(.48,.061,.86),(.84,.058,.86),(1,.053,.85)),14)
        if name.startswith(("CLO_Bodice","CLO_Waist","CLO_Skirt","CLO_Blanket","CLO_Collar")):
            return cloth_volume(original,.020 if "Blanket" in name or "Drape" in name else .008,14)
        if name.startswith(("GEO_Slipper.","GEO_SlipperSole.","HAIR_BunKnot")):
            return cloth_volume(original,0,16)
        if name.startswith("ACC_Button."):
            points=[Vector(p) for p in original[0]]; centre=sum(points,Vector())/len(points)
            return api.make_ellipsoid(centre,(.008,.004,.008),10,5)
    return original


def add_details(builder,api):
    """Small constructed garment features, each belonging to existing clothing."""
    if builder.spec.key=="lake_fisherman":
        for side,sign in (("L",1),("R",-1)):
            for label,z,height in (("Pocket",.968,.085),("PocketFlap",1.014,.024)):
                g=api.make_tapered_box((sign*.126,-.122,z-height/2),(sign*.126,-.126,z+height/2),
                                      (.126,.017,0),(.122,.016,0))
                builder.add_part("CLO_Slicker"+label+"."+side,cloth_volume(g),"spine","clothing_detail",
                                 "slicker_dark" if label=="PocketFlap" else "slicker")
            for i in range(3):
                # Fine sculpted beard bundles stay inside the established grey
                # chin, without assigning a new face or biographical mark.
                builder.add_part(f"ACC_BeardLock{i}.{side}",detail.segment(
                    (sign*(.017+i*.015),-.139,1.476),(sign*(.013+i*.018),-.129,1.421+i*.004),
                    ((0,.012,.55),(.4,.013,.60),(1,.007,.5)),7),"head","surface_detail","fisher_grey")
    else:
        for side,sign in (("L",1),("R",-1)):
            # Rolled cardigan pocket edge and ear folds are ordinary tailoring
            # and anatomy, not jewellery, disease or a narrative identifier.
            builder.add_part("CLO_CardiganPocket."+side,cloth_volume(api.make_tapered_box(
                (sign*.134,-.130,1.075),(sign*.134,-.144,1.151),(.092,.014,0),(.098,.014,0))),
                "chest","clothing_detail","mother_cardigan_panel")
            builder.add_part("GEO_Ear."+side,api.make_ellipsoid((sign*.096,-.012,1.552),(.016,.017,.032),10,6),
                             "head","body_detail","mother_skin")


def surface_kind(part):
    name=part.obj.name
    palette=part.palette_name
    if "hair" in palette or "fisher_grey"==palette:return "hair"
    if any(k in name for k in ("Boot","Sole","Slipper")):return "leather"
    if name.startswith("CLO_") or "SlickerSeam" in name:return "cloth"
    return "skin_white"
