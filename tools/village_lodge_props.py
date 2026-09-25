"""Measured shelter furnishings and two independently operated lodge doors.

Coordinates are Unity metres. Geometry remains passive; the lodge controller
owns door endpoints, the lantern light and the three interaction docks.
"""
from __future__ import annotations
import math
import interior_kit as kit
import bar_parts as bp

DOOR_HINGE_Z = -6.10
DOOR_NAMES = ("LodgeDoorLeft", "LodgeDoorRight")
# Keep these metre bounds in AlpineVillageExpansionPlan: the path terminates
# at the outer chamfer, while the imported sill owns the doorway surface.
THRESHOLD_OUTER_Z = -6.28
THRESHOLD_INNER_Z = -5.66
THRESHOLD_WIDTH = 2.60
THRESHOLD_TOP = .07
THRESHOLD_CHAMFER = .01
DOOR_BOTTOM = .085
DOOR_TOP = 2.68
COT_CENTER = (-7.61, .4)
COT_SIZE = (2.1, 1.0)
COUNTER_CENTER = (8.28, 1.0)
LANTERN_BASE = (3.68, .78, -.32)
ANCHORS = []
for name, sign in zip(DOOR_NAMES, (-1, 1)):
    ANCHORS += [dict(kind="SkiLodge", name=name+"Hinge", position=(sign*1.3, 0, DOOR_HINGE_Z)),
                dict(kind="SkiLodge", name=name+"Handle", position=(sign*.16, 1.05, -6.115), parent=name+"Hinge"),
                dict(kind="SkiLodge", name=name+"InsideHandle", position=(sign*.16, 1.05, -5.855), parent=name+"Hinge")]
ANCHORS += [dict(kind="SkiLodge", name="CotInteractionDock", position=(-5.86,.02,.4)),
            dict(kind="SkiLodge", name="KettleInteractionDock", position=(7.18,.02,2.5)),
            dict(kind="SkiLodge", name="LanternInteractionDock", position=(3.68,.02,-1.45)),
            dict(kind="SkiLodge", name="LanternLightDock", position=(3.68,1.10,-.32))]


def counter_position(position):
    """Turn the former rental counter's contents toward the room at the wall."""
    x,y,z=position
    return COUNTER_CENTER[0]+z-1.0,y,COUNTER_CENTER[1]-(x-4.7)


def counter_geometry(geometry):
    vertices,faces=geometry
    return [counter_position(v) for v in vertices],faces


def merged(pieces):
    pieces=list(pieces)
    for piece in pieces:
        assert bp.signed_volume(piece)>1e-10, "Inward lodge furnishing component"
    return kit.merge_all(pieces)


def lathe(profile, at=(0,0,0), segments=16):
    return kit.translated(bp.to_source(kit.lathe(profile,segments)),at)


def tube(points, radius=.012, sides=7, radii=None):
    """A continuous capped curved tube with measured, unbroken handle bends."""
    vertices=[];faces=[]
    for i,p in enumerate(points):
        before=points[max(0,i-1)];after=points[min(len(points)-1,i+1)]
        tangent=[after[j]-before[j] for j in range(3)]
        length=math.sqrt(sum(a*a for a in tangent));tangent=[a/length for a in tangent]
        guide=(0,0,1) if abs(tangent[2])<.9 else (0,1,0)
        u=(tangent[1]*guide[2]-tangent[2]*guide[1],tangent[2]*guide[0]-tangent[0]*guide[2],tangent[0]*guide[1]-tangent[1]*guide[0])
        length=math.sqrt(sum(a*a for a in u));u=[a/length for a in u]
        v=(tangent[1]*u[2]-tangent[2]*u[1],tangent[2]*u[0]-tangent[0]*u[2],tangent[0]*u[1]-tangent[1]*u[0])
        r=radius if radii is None else radii[i]
        for j in range(sides):
            a=j*math.tau/sides
            vertices.append(tuple(p[k]+r*(math.cos(a)*u[k]+math.sin(a)*v[k]) for k in range(3)))
    faces.extend((tuple(reversed(range(sides))),tuple((len(points)-1)*sides+j for j in range(sides))))
    for i in range(len(points)-1):
        for j in range(sides):
            n=(j+1)%sides;a=i*sides;b=(i+1)*sides
            faces.append((a+j,a+n,b+n,b+j))
    result=vertices,faces
    assert bp.signed_volume(result)>1e-10, "Inward curved tube"
    return result


def blanket():
    """Closed cloth thickness, a soft fold and two hanging long edges."""
    # Keep a support row beyond each mattress edge (.45 m). A uniform grid
    # joined the top straight to the hem through the mattress's square sides.
    columns=(-.5175,-.4575,-.39,-.26,-.13,0,.13,.26,.39,.4575,.5175)
    nx,nz=len(columns),14;verts=[];faces=[]
    for bottom in (False,True):
        for z in range(nz):
            v=z/(nz-1)
            for x in range(nx):
                across=columns[x];u=across/1.035+.5
                edge=max(0,abs(across)-.45)/.0675
                # Even the lowest ripple and the cloth underside must clear
                # the mattress crown (.648 m); the outer edges still drape.
                height=.685-.18*edge**1.5+.009*math.sin(u*math.tau*2+v*3)
                height+=.035*math.exp(-((v-.90)/.085)**2)
                verts.append((-3.2+across,height-(.019 if bottom else 0),-.0+v*1.43))
    offset=nx*nz
    for z in range(nz-1):
        for x in range(nx-1):
            a=z*nx+x;face=(a,a+nx,a+nx+1,a+1)
            faces.extend((face,tuple(i+offset for i in reversed(face))))
    border=list(range(nx))+[z*nx+nx-1 for z in range(1,nz)]+list(reversed(range((nz-1)*nx,(nz-1)*nx+nx-1)))+[z*nx for z in reversed(range(1,nz-1))]
    for i,a in enumerate(border):
        b=border[(i+1)%len(border)];faces.append((a,b,b+offset,a+offset))
    result=verts,faces
    if bp.signed_volume(result)<0:result=verts,[tuple(reversed(f)) for f in faces]
    return result


def add_props(add, parts):
    b=bp.u_box;wood=(.285,.25,.205,1);metal=(.19,.195,.18,1)
    def prop(name, geometry, surface="Timber", solid=True, tint=None, parent=None):
        add("SkiLodge",name,geometry,surface,solid,tint)
        if parent:parts[-1]["parent"]=parent

    # A rebated frame occupies the existing opening. The outer hinge axis is
    # beyond the wall, so the 180-degree open leaves never disappear inside it.
    frame=[b((x,1.38,-5.93),(.18,2.76,.35),.015) for x in (-1.39,1.39)]
    frame += [b((0,2.81,-5.93),(2.96,.16,.35),.018),
              b((0,2.6975,-5.8925),(2.60,.095,.075),.006)]
    prop("LodgeDoorFrame",merged(frame),tint=(.315,.275,.22,1))
    # A substantial worn timber, five centimetres above the hall floor. Its
    # ends meet the jambs and its inner edge covers the wall-to-floor reveal.
    # One solid owns the top; no wear card competes with the wood surface.
    threshold_bottom=-.16
    prop("LodgeDoorThreshold",b((0,(THRESHOLD_TOP+threshold_bottom)*.5,
        (THRESHOLD_OUTER_Z+THRESHOLD_INNER_Z)*.5),
        (THRESHOLD_WIDTH,THRESHOLD_TOP-threshold_bottom,
         THRESHOLD_INNER_Z-THRESHOLD_OUTER_Z),THRESHOLD_CHAMFER),
        tint=(.39,.335,.26,1))
    for name,sign in zip(DOOR_NAMES,(-1,1)):
        x=sign*.654;hinge=name+"Hinge"
        # A thick continuous core seals the whole leaf; shallow inset planks,
        # broad end rails and wear reveal joinery without daylight between boards.
        # Trim the lower joinery together so either moving leaf clears the sill.
        panel=[b((x,(DOOR_BOTTOM+DOOR_TOP)*.5,-5.985),
                 (1.288,DOOR_TOP-DOOR_BOTTOM,.10),.010)]
        for j in range(6):
            px=sign*(.12+j*.212)
            panel.append(b((px,1.375,-6.040),(.201,2.33,.018),.004))
        prop(name+"Panel",merged(panel),tint=wood,parent=hinge)
        braces=[]
        for face in (-6.057,-5.917):
            braces += [b((x,y,face),(1.275,.145,.04),.012) for y in (.17,2.55)]
            braces += [b((px,1.355,face),(.105,2.54,.04),.010) for px in (sign*.064,sign*1.244)]
        # A real diagonal internal brace and an old low repair board.
        braces += [bp.u_rotated(b((0,0,0),(.095,2.42,.045),.006),(0,0,sign*24))]
        braces[-1]=kit.translated(braces[-1],(x,1.34,-5.895))
        braces.append(b((x,.48,-6.068),(1.03,.095,.026),.006))
        if sign<0:braces.append(b((0,(DOOR_BOTTOM+DOOR_TOP)*.5,-6.086),
                                (.060,DOOR_TOP-DOOR_BOTTOM,.014),.003))
        prop(name+"Braces",merged(braces),tint=(.32,.275,.215,1),parent=hinge)
        hardware=[]
        for y in (.40,2.29):
            hardware += [bp.u_cylinder((sign*1.3,y,DOOR_HINGE_Z),(.065,.105,.065),8),
                         b((sign*.91,y,-6.089),(.68,.06,.022),.008)]
            for px in (sign*.62,sign*1.14):
                hardware.append(bp.u_cylinder((px,y,-6.108),(.034,.018,.034),8))
        prop(name+"Hardware",merged(hardware),"RustedIron",True,metal,hinge)
        handles=[]
        for face,out in ((-6.075,-1),(-5.895,1)):
            handles += [b((sign*.16,1.05,face),(.080,.285,.018),.007)]
            handles += [tube([(sign*.16,.948,face),(sign*.16,.975,face+out*.038),
                              (sign*.16,1.125,face+out*.038),(sign*.16,1.152,face)],.013)]
        prop(name+"HandleMetal",merged(handles),"RustedIron",True,(.23,.225,.20,1),hinge)

    # Preserve the existing dry cot, moved into the left sleeping zone below.
    cot=[]
    for x in (-3.64,-2.76):
        cot.append(b((x,.435,1),(.095,.145,2.10),.014))
        for z in (.03,1.97):cot.append(b((x,.255,z),(.095,.47,.105),.012))
    for z in (.005,1.995):cot.append(b((-3.2,.435,z),(.96,.145,.085),.012))
    cot += [b((-3.2,.465,.17+i*.205),(.84,.045,.13),.006) for i in range(9)]
    cot += [b((-3.2,.27,1),(.06,.055,1.94),.006),b((-3.2,.73,2.0),(.97,.10,.065),.012)]
    prop("LodgeCotFrame",merged(cot),tint=(.37,.315,.245,1))
    prop("LodgeCotMattress",b((-3.2,.558,1),(.90,.18,1.98),.064),"Canvas",True,(.48,.46,.395,1))
    prop("LodgeCotBlanket",blanket(),"Canvas",False,(.32,.355,.32,1))
    pillow=b((-3.2,.716,1.71),(.68,.15,.40),.062)
    verts,faces=pillow
    pillow=([(x,y+.015*math.sin((x+3.54)*9)*math.sin((z-1.5)*8),z) for x,y,z in verts],faces)
    prop("LodgeCotPillow",pillow,"Canvas",False,(.63,.60,.52,1))
    prop("LodgeCotRepair",b((-3.69,.34,.10),(.014,.24,.145),.003),"RustedIron",False,(.235,.225,.195,1))

    # Kettle: rounded pressed-metal body, tapering curved spout, lid seam,
    # arched bail with insulating grip, set on a stone trivet on the old counter.
    k=(3.30,1.077,.97)
    prop("LodgeKettleTrivet",b((k[0],1.07,k[2]),(.42,.020,.38),.018),"LayeredStone",False,(.315,.32,.30,1))
    body=lathe([(.115,0),(.145,.025),(.163,.085),(.165,.16),(.145,.23),(.102,.265),(.10,.274)],k,20)
    prop("LodgeKettleBody",body,"LighterMetal",False,(.45,.465,.435,1))
    prop("LodgeKettleLid",lathe([(.112,.264),(.117,.278),(.095,.292),(.032,.302)],k,20),"LighterMetal",False,(.49,.50,.455,1))
    prop("LodgeKettleKnob",lathe([(.020,.301),(.025,.319),(.018,.338)],k,12),"RustedIron",False,metal)
    spout=[(k[0]-.125,k[1]+.105,k[2]),(k[0]-.207,k[1]+.16,k[2]),(k[0]-.250,k[1]+.244,k[2]),(k[0]-.274,k[1]+.28,k[2])]
    prop("LodgeKettleSpout",tube(spout,sides=12,radii=(.057,.043,.029,.028)),"LighterMetal",False,(.42,.44,.415,1))
    # Small recessed dark end is a mouth, not a steam effect.
    mouth=bp.u_cylinder((0,0,0),(.043,.003,.043),12)
    mouth=kit.translated(bp.u_rotated(mouth,(0,0,33)),spout[-1])
    prop("LodgeKettleMouth",mouth,"RustedIron",False,(.095,.105,.095,1))
    bail=[(k[0]+.15*math.cos(a),k[1]+.20+.22*math.sin(a),k[2]) for a in [i*math.pi/10 for i in range(11)]]
    prop("LodgeKettleHandle",tube(bail,.014),"RustedIron",False,metal)
    prop("LodgeKettleGrip",tube(bail[4:7],.023),"Timber",False,(.23,.21,.175,1))
    cup=(3.80,1.06,.99)
    # Return down the inner wall for a genuinely hollow cup and thick lip.
    prop("LodgeTeaCup",lathe([(.045,0),(.055,.015),(.065,.11),(.057,.115),(.047,.018),(.010,.018)],cup,16),"LighterMetal",False,(.62,.625,.545,1))
    cp=[(cup[0]+.07+.032*math.sin(a),cup[1]+.06+.040*math.cos(a),cup[2]) for a in [i*math.pi/8 for i in range(9)]]
    prop("LodgeTeaCupHandle",tube(cp,.009,6),"LighterMetal",False,(.58,.59,.51,1))

    # A stable gas reservoir, valve, burner mantle, glass chimney, protective
    # cage and vented cap: the warm source is visibly a usable portable lamp.
    l=(4.70,1.06,1.0)
    tank=lathe([(.125,0),(.15,.035),(.15,.135),(.13,.185),(.075,.205)],l,18)
    prop("LanternGasTank",tank,"LighterMetal",False,(.285,.32,.285,1))
    fittings=[lathe([(.055,.20),(.05,.26),(.065,.285),(.065,.30)],l,14)]
    fittings += [bp.u_cylinder((l[0]+.17,l[1]+.12,l[2]),(.07,.035,.07),10)]
    prop("LanternBurnerAndValve",merged(fittings),"LighterMetal",False,(.32,.325,.285,1))
    glass=lathe([(.111,.30),(.119,.33),(.115,.56),(.104,.585),(.101,.583),(.112,.558),(.116,.332),(.108,.302)],l,18)
    prop("LanternGlass",glass,"Glass",False,(.64,.68,.60,.10))
    prop("LanternMantle",lathe([(.030,.355),(.047,.390),(.045,.466),(.025,.493)],l,16),"Canvas",False,(.66,.63,.50,1))
    cage=[]
    for a in (0,math.pi/2,math.pi,math.pi*1.5):
        x,z=l[0]+.124*math.cos(a),l[2]+.124*math.sin(a)
        cage.append(tube([(x,l[1]+.29,z),(x,l[1]+.59,z)],.006,6))
    cage += [lathe([(.129,y),(.129,y+.016),(.12,y+.016),(.12,y)],l,18) for y in (.30,.57)]
    prop("LanternGuard",merged(cage),"LighterMetal",False,(.245,.255,.22,1))
    cap=lathe([(.13,.585),(.145,.603),(.137,.620),(.087,.655),(.065,.70),(.025,.715)],l,18)
    prop("LanternCap",cap,"LighterMetal",False,(.26,.29,.25,1))
    vents=[b((l[0]+math.cos(a)*.083,l[1]+.656,l[2]+math.sin(a)*.083),(.020,.018,.018),.002) for a in [i*math.tau/12 for i in range(12)]]
    prop("LanternVentRecesses",merged(vents),"RustedIron",False,(.085,.09,.075,1))
    handle=[(l[0]+.16*math.cos(a),l[1]+.62+.27*math.sin(a),l[2]) for a in [i*math.pi/12 for i in range(13)]]
    prop("LanternCarryHandle",tube(handle,.009,7),"LighterMetal",False,(.235,.25,.22,1))
    # A portable pressure lantern, not a metre-tall street fitting. Scaling is
    # baked in authored metres around its tabletop contact, never at runtime.
    for part in parts:
        if part["kind"]=="SkiLodge" and part["name"].startswith("Lantern"):
            vertices,faces=part["geometry"]
            part["geometry"]=([tuple(LANTERN_BASE[i]+(v[i]-l[i])*.75 for i in range(3)) for v in vertices],faces)
        elif part["kind"]=="SkiLodge" and part["name"].startswith("LodgeCot"):
            # The original bed was authored with its pillow at +Z. Its HEAD,
            # rather than its long side, now meets the left wall like the bunks.
            centered=kit.translated(part["geometry"],(3.2,0,-1))
            part["geometry"]=kit.translated(bp.u_rotated(centered,(0,-90,0)),
                (COT_CENTER[0],0,COT_CENTER[1]))
        elif part["kind"]=="SkiLodge" and part["name"].startswith(("LodgeKettle","LodgeTeaCup")):
            part["geometry"]=counter_geometry(part["geometry"])


def open_geometry(part):
    """Apply precisely the runtime's open endpoint for authoring validation."""
    parent=part.get("parent","")
    for name,sign in zip(DOOR_NAMES,(-1,1)):
        if parent==name+"Hinge":
            pivot=(sign*1.3,0,DOOR_HINGE_Z)
            centered=kit.translated(part["geometry"],tuple(-v for v in pivot))
            return kit.translated(bp.u_rotated(centered,(0,-sign*180,0)),pivot)
    return part["geometry"]


def validate_props(parts):
    lodge={p["name"]:p for p in parts if p["kind"]=="SkiLodge"}
    assert "OpenDoorLeaves" not in lodge,"Legacy static doors survived"
    threshold=lodge["LodgeDoorThreshold"]
    threshold_lo,threshold_hi=kit.bounds(threshold["geometry"])
    assert threshold["solid"] and threshold["surface"]=="Timber"
    assert abs(threshold_hi[0]-threshold_lo[0]-THRESHOLD_WIDTH)<1e-6
    assert abs(threshold_lo[2]-THRESHOLD_OUTER_Z)<1e-6 and abs(threshold_hi[2]-THRESHOLD_INNER_Z)<1e-6
    assert abs(threshold_hi[1]-THRESHOLD_TOP)<1e-6
    floor_top=kit.bounds(lodge["Floor"]["geometry"])[1][1]
    assert .04-1e-6<=threshold_hi[1]-floor_top<=.05+1e-6,"Lodge sill must remain a small walking step"
    for name,sign in zip(DOOR_NAMES,(-1,1)):
        panel=lodge[name+"Panel"];lo,hi=kit.bounds(panel["geometry"])
        assert panel["solid"] and panel["parent"]==name+"Hinge"
        assert abs((hi[0]-lo[0])-1.288)<1e-6 and abs(lo[1]-DOOR_BOTTOM)<1e-6 and abs(hi[1]-DOOR_TOP)<1e-6
        for suffix in ("Panel","Braces","Hardware","HandleMetal"):
            part=lodge[name+suffix]
            assert kit.bounds(part["geometry"])[0][1]>=threshold_hi[1]+.014,\
                "Door joinery or hardware clips the threshold throughout its swing"
            assert kit.bounds(open_geometry(part))[1][2]<-6.0+1e-6,"Open door entered wall"
    lo,hi=kit.bounds(lodge["LodgeCotFrame"]["geometry"])
    assert -8.68<=lo[0]<-8.64 and hi[0]<=-6.55 and lo[2]>=-.11 and hi[2]<=.91
    pillow_lo,pillow_hi=kit.bounds(lodge["LodgeCotPillow"]["geometry"])
    assert pillow_hi[0]<COT_CENTER[0]-.4 and 0<pillow_lo[0]-lo[0]<.25,\
        "Cot must place its pillow/head end against the left wall"
    assert hi[0]-lo[0]>hi[2]-lo[2],"Cot feet must point into the room"
    # Use the actual surfaces, including the chamfer and hanging cloth sides.
    # Bounds alone cannot reveal a mattress breaking through a soft fold.
    from mathutils.bvhtree import BVHTree
    mattress=BVHTree.FromPolygons(*lodge["LodgeCotMattress"]["geometry"],all_triangles=False)
    cloth=BVHTree.FromPolygons(*lodge["LodgeCotBlanket"]["geometry"],all_triangles=False)
    assert not mattress.overlap(cloth),"Cot blanket intersects the mattress"
    for name in ("LodgeKettleBody","LodgeTeaCup"):
        assert kit.bounds(lodge[name]["geometry"])[0][1]>=1.059,"Counter prop floats through tabletop"
        low,high=kit.bounds(lodge[name]["geometry"])
        assert 7.88<=low[0]<high[0]<=8.68 and -.9<=low[2]<high[2]<=2.9,"Kettle/cup left the wall counter"
    low,high=kit.bounds(lodge["LanternGasTank"]["geometry"])
    assert abs(low[1]-.78)<1e-6 and 3.475<=low[0]<high[0]<=4.525
    assert -.55<=low[2]<high[2]<=2.15,"Lantern must rest entirely on the dining table"
    assert 1.44<kit.bounds(lodge["LanternCarryHandle"]["geometry"])[1][1]<1.46
    assert len({a["name"] for a in ANCHORS})==len(ANCHORS)
