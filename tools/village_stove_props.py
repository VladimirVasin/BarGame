"""Metre-authored stove action anchors, thermal flame volumes and pocket lighter."""
from __future__ import annotations
import math
import json
from pathlib import Path
import interior_kit as kit
import bar_parts as bp


ANCHORS = [
    dict(kind="SkiLodge", name="StoveDoorHinge", position=(-.335,.755,-.473)),
    dict(kind="SkiLodge", name="StoveHandleGrip", position=(.245,.76,-.53), parent="StoveDoorHinge"),
    dict(kind="SkiLodge", name="StoveLogDock", position=(0,.6264,-.02)),
    dict(kind="SkiLodge", name="StoveFireDock", position=(0,.65,-.02)),
    dict(kind="SkiLodge", name="StoveLighterDock", position=(.10,.618,-.085)),
    dict(kind="SkiLodge", name="StoveCameraDock", position=(0,1.05,-1.18)),
    dict(kind="SkiLodge", name="StoveEntryDock", position=(0,.02,-1.40)),
    dict(kind="SkiLodge", name="StoveExitDock", position=(0,.02,-1.40)),
    dict(kind="Lighter", name="LighterLidHinge", position=(-.0185,.043,0)),
    dict(kind="Lighter", name="FlintWheel", position=(.008,.058,0)),
    dict(kind="Lighter", name="LighterLeverPivot", position=(.01,.048,0)),
    dict(kind="Lighter", name="FlameTip", position=(-.0055,.063,0)),
    dict(kind="Lighter", name="LighterGrip", position=(0,.024,0)),
]


def flame(tongues, front, sides=8):
    """The same UV1/color thermal contract as the shared mother's-house shader."""
    levels=(0,.10,.23,.39,.57,.73,.88,1)
    widths=(.55,.94,1,.84,.61,.39,.17,.014)
    vertices=[];faces=[];uv=[];colors=[]
    for x,y,z,height,width,depth,lean,curl,phase,heat in tongues:
        rings=[]
        for v,spread in zip(levels,widths):
            cx=x+lean*v**1.25+curl*math.sin(v*math.pi)*v
            cz=z+depth*.17*math.sin(v*math.pi*1.4+phase*math.tau)*v
            ring=[]
            for side in range(sides):
                angle=math.tau*side/sides
                shoulder=1+.10*math.sin(angle*3+phase*math.tau+v*4)
                ring.append(len(vertices))
                vertices.append((cx+math.cos(angle)*width*.5*spread*shoulder,
                                 y+v*height,cz+math.sin(angle)*depth*.5*spread))
                uv.append((.5+.5*math.cos(angle),v))
                colors.append((phase,heat,1 if front else 0,1))
            rings.append(ring)
        faces.extend((tuple(rings[0]),tuple(reversed(rings[-1]))))
        for low,high in zip(rings,rings[1:]):
            for side in range(sides):
                following=(side+1)%sides
                faces.append((low[following],low[side],high[side],high[following]))
    return (vertices,faces),uv,colors


def lighter_lid():
    """Closed metal thickness around a real open underside, with softened corners."""
    vertices=[]
    def ring(width,depth,y):
        x=width*.5;z=depth*.5;c=.0018
        outline=[(-x+c,-z),(x-c,-z),(x,-z+c),(x,z-c),
                 (x-c,z),(-x+c,z),(-x,z-c),(-x,-z+c)]
        start=len(vertices)
        vertices.extend((px,y,pz) for px,pz in outline)
        return list(range(start,start+8))
    low=ring(.036,.014,.0436);high=ring(.036,.014,.0635)
    top=ring(.033,.011,.065)
    inner_low=ring(.033,.011,.0436);inner_high=ring(.033,.011,.0635)
    faces=[tuple(reversed(top)),tuple(inner_high)]
    def strip(a,b):
        for side in range(8):
            following=(side+1)%8
            faces.append((a[following],a[side],b[side],b[following]))
    strip(low,high);strip(high,top)
    strip(inner_high,inner_low);strip(inner_low,low)
    return vertices,faces


def add_props(add,parts):
    # A rooted pair of layers: the runtime grows them from their authored dock.
    for name,front,tongues in (
        ("FlameBack",False,((-.19,0,.04,.26,.16,.10,-.035,.024,.13,.67),
                            (-.025,0,.06,.39,.17,.12,.038,-.04,.51,.83),
                            (.17,0,.025,.30,.15,.09,-.026,.023,.82,.74))),
        ("FlameFront",True,((-.105,0,-.04,.23,.15,.10,.029,.028,.29,.94),
                           (.075,0,-.035,.29,.15,.09,-.025,-.03,.66,1))),
    ):
        geometry,uv,colors=flame(tongues,front)
        add("StoveFire",name,geometry,"Fire",False)
        parts[-1].update(flame_uv=uv,flame_colors=colors)

    # An unbranded, worn steel flip-top. The cap has a real hollow underside;
    # in its closed rest pose it conceals the perforated chimney and striker.
    add("Lighter","LighterBody",bp.u_box((0,.0215,0),(.036,.043,.014),.002),
        "LighterMetal",False,(.47,.49,.465,1))
    add("Lighter","LighterLid",lighter_lid(),"LighterMetal",False,(.50,.515,.49,1))
    parts[-1]["parent"]="LighterLidHinge"
    lid_link=bp.u_box((-.018,.045,0),(.003,.004,.006),0)
    add("Lighter","LidHingeLeaf",lid_link,"LighterMetal",False,(.39,.41,.385,1))
    parts[-1]["parent"]="LighterLidHinge"
    barrel=bp.u_rotated(bp.u_cylinder((0,0,0),(.004,.0036,.004),8),(90,0,0))
    add("Lighter","HingeBarrel",kit.translated(barrel,(-.0185,.043,0)),"LighterMetal",False,(.40,.42,.395,1))
    # Each flank is a steel lattice with four actual holes, not black decals.
    chimney=[]
    for z in (-.0048,.0048):
        chimney += [bp.u_box((-.0055,y,z),(.016,.0025,.0009),0)
                    for y in (.04725,.05425,.06125)]
        chimney += [bp.u_box((x,.05425,z),(.002,.0165,.0009),0)
                    for x in (-.0125,-.0055,.0015)]
    chimney += [bp.u_box((-.013,.05425,0),(.001,.0165,.0096),0)]
    add("Lighter","Chimney",kit.merge_all(chimney),"LighterMetal",False,(.40,.425,.40,1))
    add("Lighter","Wick",bp.u_cylinder((-.0055,.059,0),(.0032,.003,.0032),8),
        "LighterMetal",False,(.18,.175,.155,1))
    wheel=bp.u_rotated(bp.u_cylinder((0,0,0),(.009,.0038,.009),12),(90,0,0))
    ribs=[]
    for index in range(12):
        angle=index*30
        rib=bp.u_rotated(bp.u_box((.0046,0,0),(.0007,.001,.008),0),(0,0,angle))
        ribs.append(rib)
    add("Lighter","Wheel",kit.translated(kit.merge_all([wheel]+ribs),(.008,.058,0)),
        "LighterMetal",False,(.31,.335,.315,1))
    parts[-1]["parent"]="FlintWheel"
    add("Lighter","Lever",bp.u_box((.01,.048,0),(.009,.007,.008),.001),
        "LighterMetal",False,(.25,.27,.245,1))
    parts[-1]["parent"]="LighterLeverPivot"
    geometry,uv,colors=flame(((-.0055,.063,0,.019,.006,.004,.001,.002,.2,1),),True,6)
    add("Lighter","LighterFlame",geometry,"Fire",False)
    parts[-1].update(flame_uv=uv,flame_colors=colors,hidden=True)


def validate_props(parts):
    for part in parts:
        if part["surface"]!="Fire":continue
        vertices=part["geometry"][0]
        assert len(part["flame_uv"])==len(vertices)==len(part["flame_colors"]),"Missing thermal fields"
        assert all(0<=u<=1 and 0<=v<=1 for u,v in part["flame_uv"]),"Invalid thermal UV1"
        assert not part["solid"],"Fire must never own collision"
    lodge={p["name"]:p for p in parts if p["kind"]=="SkiLodge"}
    for name in ("StoveDoor","StoveHardware","StoveDoorWear"):
        assert lodge[name]["parent"]=="StoveDoorHinge","Door leaves hardware or wear behind"
    for anchor in ANCHORS:
        assert len(anchor["position"])==3 and all(math.isfinite(v) for v in anchor["position"])
    assert len({(a["kind"],a["name"]) for a in ANCHORS})==len(ANCHORS),"Duplicate stove anchor"
    docks={a["name"]:a["position"] for a in ANCHORS if a["kind"]=="SkiLodge"}
    props=json.loads((Path(__file__).resolve().parents[1]/"Assets/Resources/VillageLife/VillageLifeProps3D.json").read_text())
    log=next(p for p in props["props"] if p["kind"]=="Log")
    grate_top=kit.bounds(lodge["StoveGrate"]["geometry"])[1][1]
    assert 0<=docks["StoveLogDock"][1]+log["bounds_min"][1]-grate_top<.001,"Log does not rest on grate"
    for name in ("StoveLogDock","StoveFireDock"):
        dock=docks[name]
        lo,hi=(log["bounds_min"],log["bounds_max"]) if name=="StoveLogDock" else kit.bounds(
            kit.merge_all(p["geometry"] for p in parts if p["kind"]=="StoveFire"))
        assert -.39<dock[0]+lo[0]<dock[0]+hi[0]<.39,"Action prop escapes firebox sides"
        assert .54<dock[1]+lo[1]<dock[1]+hi[1]<1.09,"Action prop escapes firebox floor/ceiling"
        assert -.375<dock[2]+lo[2]<dock[2]+hi[2]<.375,"Action prop escapes firebox front/back"
    lighter=kit.merge_all(p["geometry"] for p in parts if p["kind"]=="Lighter" and not p.get("hidden"))
    lo,hi=kit.bounds(lighter)
    assert -.022<=lo[0]<0<hi[0]<=.019 and lo[1]>=-1e-8 and .0649<=hi[1]<.066,("Pocket lighter metre scale",lo,hi)
    lighter_parts={p["name"]:p for p in parts if p["kind"]=="Lighter"}
    assert lighter_parts["LighterLid"]["parent"]=="LighterLidHinge","Flip-top lid has no authored pivot"
    assert all(p["surface"] in ("LighterMetal","Fire") for p in lighter_parts.values()),"Lighter must be wholly metal"
