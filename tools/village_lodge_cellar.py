"""Closed floor hatch and wall-side utility storage in the old ski lodge.

Unity metres; the floor has an actual opening and the lid a recessed pull.
Runtime owns the inspection camera/question. No geometry opens the cellar.
"""
from __future__ import annotations
import math
import interior_kit as kit
import bar_parts as bp
from village_lodge_props import tube

HATCH_CENTER = (-7.25, .02, -4.20)
HATCH_SIZE = (.90, 1.20)
FRAME_SIZE = (1.12, 1.42)
DOCK = (-5.05, .02, -4.70)
HANDLE = (-6.94, .012, -4.20)
ANCHORS = [dict(kind="SkiLodge", name=name, position=position) for name,position in (
    ("LodgeCellarHatch", HATCH_CENTER),
    ("HatchInteractionDock", DOCK),
    ("HatchFocus", HATCH_CENTER),
    ("HatchHandleFocus", HANDLE),
)]
ANCHORS.append(dict(kind="SkiLodge",name="LodgeCellarHatchHinge",
                    position=(-7.70,.02,-4.20),parent="LodgeCellarHatch"))


def merged(pieces):
    pieces=list(pieces)
    for geometry in pieces:
        assert bp.signed_volume(geometry)>1e-10,"Inward cellar component"
    return kit.merge_all(pieces)


def cut_slab(bounds, opening, bottom, top):
    """Four non-overlapping solids around a true rectangular hole in XZ."""
    left,right,front,back=bounds
    west,east,south,north=opening
    assert left<west<east<right and front<south<north<back
    pieces=[]
    for x0,x1,z0,z1 in ((left,right,front,south),(left,right,north,back),
                        (left,west,south,north),(east,right,south,north)):
        pieces.append(bp.u_box(((x0+x1)/2,(bottom+top)/2,(z0+z1)/2),
                               (x1-x0,top-bottom,z1-z0),0))
    return merged(pieces)


def floor_geometry():
    x,_,z=HATCH_CENTER;w,d=FRAME_SIZE
    return cut_slab((-8.68,8.68,-5.68,5.68),
                    (x-w/2,x+w/2,z-d/2,z+d/2),-.16,.02)


def annulus(profile, segments=10):
    """Closed swept shell; its inside stays genuinely hollow."""
    vertices,faces=kit.lathe(profile,segments)
    faces=faces[:-2];last=(len(profile)-1)*segments
    for i in range(segments):
        nxt=(i+1)%segments
        faces.append((last+i,last+nxt,nxt,i))
    return bp.to_source((vertices,faces))


def add_cellar(add, parts):
    b=bp.u_box;wood=(.305,.26,.195,1);iron=(.205,.215,.205,1)
    x,_,z=HATCH_CENTER
    def prop(name,geometry,surface="Timber",solid=True,tint=None,subject=False,moving=False):
        add("SkiLodge",name,geometry,surface,solid,tint or wood)
        if moving:parts[-1]["parent"]="LodgeCellarHatchHinge"
        elif subject:parts[-1]["parent"]="LodgeCellarHatch"

    frame=cut_slab((x-.56,x+.56,z-.71,z+.71),
                   (x-.456,x+.456,z-.606,z+.606),-.075,.02)
    prop("CellarHatchFrame",frame,tint=(.25,.215,.17,1),subject=True)
    # Four stout boards leave narrow real seams; none overlays a floor face.
    boards=[]
    for i in range(4):
        a=x-.45+i*.225+.002;bnd=a+.221
        if i==3:
            boards.append(cut_slab((a,bnd,z-.60,z+.60),
                                   (HANDLE[0]-.065,HANDLE[0]+.065,z-.07,z+.07),-.055,.02))
        else:boards.append(b(((a+bnd)/2,-.0175,z),(.221,.075,1.20),0))
    prop("CellarHatchLid",merged(boards),moving=True)
    # A thin dark lower rim closes seams without covering the recessed ring.
    backing=b((x,-.066,z),(.90,.018,1.20),0)
    prop("CellarHatchBacking",backing,tint=(.15,.135,.11,1),moving=True)
    well=b((HANDLE[0],-.034,z),(.13,.018,.14),0)
    prop("CellarHatchPullWell",well,"RustedIron",False,iron,moving=True)
    ring=[(HANDLE[0]+.041*math.cos(i*math.tau/8),.011,
           z+.041*math.sin(i*math.tau/8)) for i in range(9)]
    hardware=[tube(ring,.005,4),b((HANDLE[0],.011,z-.047),(.060,.014,.013),0)]
    hinges=[]
    for offset in (-.38,.38):
        hardware.append(b((x-.395,.029,z+offset),(.13,.014,.065),0))
        hinge=bp.u_cylinder((0,0,0),(.034,.046,.034),6)
        hinges += [kit.translated(bp.u_rotated(hinge,(90,0,0)),(x-.452,.03,z+offset)),
                   b((x-.50,.029,z+offset),(.10,.014,.065),0)]
    prop("CellarHatchIronwork",merged(hardware),"RustedIron",False,iron,moving=True)
    prop("CellarHatchFixedHinges",merged(hinges),"RustedIron",False,iron,subject=True)

    # A compact work corner beside the wall, entirely outside the lid/dock.
    bx,bz=-8.31,-3.39
    bucket=merged([annulus([(.125,.020),(.17,.34),(.155,.34),(.112,.044),(.112,.020)],10),
                   bp.u_cylinder((0,.032,0),(.249,.012,.249),10)])
    prop("CellarUtilityBucket",kit.translated(bucket,(bx,0,bz)),"RustedIron",True,(.40,.415,.40,1))
    handle=[(bx+.17*math.cos(i*math.pi/6),.33+.17*math.sin(i*math.pi/6),bz)
            for i in range(7)]
    prop("CellarUtilityBucketHandle",tube(handle,.006,4),"RustedIron",False,iron)
    brush=[b((bx,.104,bz),(.16,.055,.085),0),
           b((bx,.299,bz),(.025,.35,.025),0)]
    prop("CellarUtilityBrush",merged(brush),tint=(.36,.29,.19,1))
    prop("CellarUtilityBrushBristles",b((bx,.069,bz),(.145,.05,.078),0),"Canvas",False,(.24,.235,.18,1))

    # The broom hangs vertically from a bent wall hook; no floating handle.
    mx,mz=-8.53,-4.92
    shaft=bp.u_cylinder((mx,1.055,mz),(.031,.595,.031),6)
    prop("CellarUtilityBroomHandle",shaft,tint=(.36,.28,.175,1))
    head=bp.to_source(kit.prism([(-.105,.24),(.105,.24),(.046,.53),(-.046,.53)],.075))
    prop("CellarUtilityBroomHead",kit.translated(head,(mx,0,mz)),"Canvas",False,(.37,.34,.225,1))
    ties=[b((mx,y,mz),(.16 if y<.4 else .12,.02,.083),0) for y in (.35,.44)]
    hook=tube([(-8.68,1.60,mz),(-8.53,1.60,mz),(-8.51,1.63,mz)],.01,4)
    prop("CellarUtilityBroomBindingAndHook",merged(ties+[hook]),"RustedIron",False,iron)

    cx,cz=-7.08,-5.35
    crate=[b((cx,.065,cz),(.64,.09,.42),0)]
    for y in (.155,.285):
        for side in (-1,1):
            crate += [b((cx,y,cz+side*.203),(.64,.105,.032),0),
                      b((cx+side*.304,y,cz),(.032,.105,.37),0)]
    prop("CellarUtilityStrapCrate",merged(crate),tint=(.325,.27,.195,1))
    straps=[]
    for i in range(3):
        # Broad flat folded belts, not a symbolic coil or collectible object.
        sx=cx-.19+i*.185
        straps += [b((sx,.121,cz),(.045,.022,.27),0),
                   b((sx,.143,cz+.032),(.045,.022,.20),0)]
    prop("CellarUtilitySpareStraps",merged(straps),"Canvas",False,(.235,.245,.205,1))
    buckles=[b((cx-.19+i*.185,.160,cz+.08),(.06,.012,.045),0) for i in range(3)]
    prop("CellarUtilityStrapBuckles",merged(buckles),"RustedIron",False,iron)


def validate_cellar(parts):
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    lodge={p["name"]:p for p in parts if p["kind"]=="SkiLodge"}
    tree=lambda name:BVHTree.FromPolygons(*lodge[name]["geometry"],all_triangles=False)
    x,y,z=HATCH_CENTER;floor=tree("Floor");lid=tree("CellarHatchLid")
    for dx in (-.4,0,.4):
        for dz in (-.5,0,.5):
            start=Vector((x+dx,.5,z+dz))
            assert floor.ray_cast(start,Vector((0,-1,0)),.8)[0] is None,"Floor seals cellar cutout"
    for dx,dz in ((-.30,-.3),(-.08,.3),(.15,0),(.33,.3)):
        hit=lid.ray_cast(Vector((x+dx,.5,z+dz)),Vector((0,-1,0)),.8)[0]
        assert hit is not None and abs(hit.y-y)<1e-6,"Hatch lid is not flush with floor"
    assert lid.ray_cast(Vector((HANDLE[0],.2,HANDLE[2])),Vector((0,-1,0)),.4)[0] is None,"Pull recess is capped by a lid face"
    assert kit.bounds(lodge["CellarHatchIronwork"]["geometry"])[1][1]<=.05,"Hatch hardware exceeds low walking clearance"
    for dx,dz in ((-.60,0),(.60,0),(0,-.75),(0,.75)):
        hit=floor.ray_cast(Vector((x+dx,.5,z+dz)),Vector((0,-1,0)),.8)[0]
        assert hit is not None and abs(hit.y-y)<1e-6,"Floor is missing beside hatch frame"
    for name in ("CellarUtilityBucket","CellarUtilityStrapCrate"):
        lo,hi=kit.bounds(lodge[name]["geometry"])
        assert .019<=lo[1]<=.026,"Utility prop must rest on the floor"
        assert hi[0]<-6.65 and lo[0]>-8.68 and lo[2]>-5.68,"Utility prop escaped the wall-side corner"
    solids=[tree(name) for name,p in lodge.items() if p["solid"]]
    # A capsule-width route reaches the standing dock from the room/cot aisle.
    for height in (.2,1.1,2.0):
        for offset in (-.32,0,.32):
            start=Vector((DOCK[0]+offset,height,-.4));end=Vector((DOCK[0]+offset,height,DOCK[2]))
            direction=end-start
            assert not any(t.ray_cast(start,direction.normalized(),direction.length)[0] is not None
                           for t in solids),"Cellar corner blocks the cot/dock approach"
    for i in range(8):
        a=i*math.tau/8;start=Vector((DOCK[0]+.34*math.cos(a),.15,DOCK[2]+.34*math.sin(a)))
        assert not any(t.ray_cast(start,Vector((0,1,0)),2)[0] is not None for t in solids),"Occupied cellar interaction dock"
    for name,p in lodge.items():
        if name.startswith("CellarUtility"):
            lo,hi=kit.bounds(p["geometry"])
            assert hi[0]<x-.56 or lo[0]>x+.56 or hi[2]<z-.71 or lo[2]>z+.71,"Utility prop covers the hatch"
