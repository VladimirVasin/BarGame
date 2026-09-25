"""Fixed-metre joinery and quiet household dressing for the old ski lodge.

Everything here is passive authored geometry. Only the measured chair and two
inspection subjects expose anchors; bunks, dining seats and dressing do not.
"""
from __future__ import annotations
import math
import interior_kit as kit
import bar_parts as bp

CHAIR_CENTER = (-1.50, .48, -1.25)
CHAIR_YAW = math.degrees(math.atan2(-CHAIR_CENTER[0], -CHAIR_CENTER[2]))
CHAIR_FACING = (CHAIR_CENTER[0]+1, CHAIR_CENTER[1],
                CHAIR_CENTER[2]+CHAIR_CENTER[2]/CHAIR_CENTER[0])
BUNK_CENTERS = ((-7.63,2.25),(-7.63,4.10))
RUG_CENTER = (-5.77,2.1)
RUG_LENGTH = 4.4
CHEST_CENTER = (-8.35,-1.0)
ANCHORS = [dict(kind="SkiLodge", name=name, position=position) for name,position in (
    ("LodgeChairSeat", CHAIR_CENTER),
    ("LodgeChairFacing", CHAIR_FACING),
    ("LodgePhotoFocus", (8.51,2.02,-.6)),
    ("LodgePhotoDock", (6.7,.02,-.6)),
    ("LodgeSkiFocus", (-4.3,1.25,4.62)),
    ("LodgeSkiDock", (-4.3,.02,3.35)),
    ("LodgeSkiEquipment", (-4.3,0,4.62)),
)]


def placed(geometry, position, yaw=0):
    return kit.translated(bp.u_rotated(geometry,(0,yaw,0)),position)


def merged(pieces):
    pieces=list(pieces)
    for piece in pieces:
        assert bp.signed_volume(piece)>1e-10,"Inward lodge furniture component"
    return kit.merge_all(pieces)


def ellipsoid(center, radius, segments=12, rings=7):
    """Faceted sculpted volume, with closed pole caps and no degenerate faces."""
    vertices=[];faces=[]
    for row in range(1,rings):
        latitude=-math.pi*.5+math.pi*row/rings
        for i in range(segments):
            a=math.tau*i/segments
            vertices.append((center[0]+radius[0]*math.cos(latitude)*math.cos(a),
                             center[1]+radius[1]*math.sin(latitude),
                             center[2]+radius[2]*math.cos(latitude)*math.sin(a)))
    low=len(vertices);vertices.append((center[0],center[1]-radius[1],center[2]))
    high=len(vertices);vertices.append((center[0],center[1]+radius[1],center[2]))
    for i in range(segments):
        n=(i+1)%segments
        faces.append((low,n,i))
        last=(rings-2)*segments
        faces.append((high,last+i,last+n))
    for row in range(rings-2):
        for i in range(segments):
            n=(i+1)%segments;a=row*segments;b=a+segments
            faces.append((a+i,a+n,b+n,b+i))
    result=vertices,faces
    if bp.signed_volume(result)<0:result=vertices,[tuple(reversed(f)) for f in faces]
    return result


def add_furniture(add, parts):
    from village_lodge_props import tube,lathe
    b=bp.u_box
    wood=(.355,.286,.205,1);edge=(.285,.235,.18,1)
    iron=(.205,.205,.185,1);cloth=(.335,.36,.32,1)
    def prop(name, geometry, surface="Timber", solid=True, tint=None, parent=None):
        add("SkiLodge",name,geometry,surface,solid,tint or wood)
        if parent:parts[-1]["parent"]=parent

    # A substantial plank top on two pegged trestles, with real feet/braces.
    table=[]
    for i in range(4):
        table.append(b((4-.39375+i*.2625,.7425,.8),(.2595,.075,2.7),.010))
    for z in (-.27,1.87):
        table += [b((4,.685,z),(.92,.09,.17),.014),
                  b((4,.105,z),(.91,.13,.22),.018)]
        for x in (3.70,4.30):
            leg=bp.to_source(kit.turned_leg(.57,.069,.051,.073,8))
            table.append(placed(leg,(x,.04,z)))
            brace=b((0,0,0),(.09,.45,.09),.008)
            table.append(placed(bp.u_rotated(brace,(0,0,-28 if x<4 else 28)),(x,.43,z)))
    table += [b((4,.22,.8),(.115,.13,2.53),.012),
              b((4,.66,.8),(.13,.09,2.35),.010)]
    prop("LodgeDiningTable",merged(table))
    pegs=[]
    for z in (-.27,1.87):
        for x in (3.70,4.30):
            pegs.append(placed(bp.u_cylinder((0,0,0),(.022,.006,.022),7),(x,.667,z-.091),90))
    prop("LodgeDiningPegs",merged(pegs),solid=False,tint=(.23,.185,.135,1))
    for label,x in (("Left",3.02),("Right",4.98)):
        bench=[b((x+side*.1005,.4275,.8),(.198,.065,2.7),.012) for side in (-1,1)]
        for z in (-.28,1.88):
            for side in (-1,1):
                leg=bp.to_source(kit.turned_leg(.345,.044,.032,.049,8))
                bench.append(placed(leg,(x+side*.135,.02,z)))
            bench += [b((x,.37,z),(.37,.07,.13),.008),b((x,.15,z),(.34,.075,.08),.009)]
        bench.append(b((x,.16,.8),(.075,.075,2.28),.009))
        prop("LodgeDiningBench"+label,merged(bench),tint=(.325,.269,.203,1))

    # Author each bunk with its pillow at +Z, then turn that head end toward
    # the left wall. The two ladders remain on the sides between the beds.
    for index,(bed_x,bed_z) in enumerate(BUNK_CENTERS):
        x=z=0;frame=[]
        def at_bed(geometry):return placed(geometry,(bed_x,0,bed_z),-90)
        for px in (x-.46,x+.46):
            for pz in (z-.99,z+.99):
                frame.append(b((px,1.055,pz),(.075,2.07,.075),.009))
                frame.append(b((px,2.065,pz),(.089,.05,.089),.012))
        for y in (.395,1.405):
            for px in (x-.46,x+.46):frame.append(b((px,y,z),(.075,.145,2.04),.009))
            for pz in (z-.99,z+.99):frame.append(b((x,y,pz),(.91,.145,.075),.009))
            for step in range(10):frame.append(b((x,y+.035,z-.88+step*.196),(.86,.035,.10),.003))
        for y in (.80,1.80,1.985):
            for pz in (z-.99,z+.99):frame.append(b((x,y,pz),(.91,.065,.045),.007))
        # Wall rail and split aisle rail leave a practical ladder opening.
        frame.append(b((x-.46,1.84,z),(.046,.09,1.95),.008))
        frame.append(b((x+.46,1.84,z+.25),(.046,.09,1.45),.008))
        for pz in (z-.86,z-.43):frame.append(b((x+.555,.90,pz),(.056,1.76,.055),.006))
        for y in (.25,.54,.83,1.12,1.41):frame.append(b((x+.555,y,z-.645),(.065,.055,.47),.006))
        prop("LodgeBunkFrame"+str(index),at_bed(merged(frame)),tint=(.30,.265,.212,1))
        bedding=[];pillows=[]
        for level,y in enumerate((.505,1.515)):
            bedding.append(b((x,y,z),(.85,.135,1.92),.038))
            pillows.append(b((x,y+.098,z+.69),(.62,.105,.36),.038))
        prop("LodgeBunkMattresses"+str(index),at_bed(merged(bedding)),"Canvas",True,(.47,.46,.407,1))
        prop("LodgeBunkPillows"+str(index),at_bed(merged(pillows)),"Canvas",True,(.47,.46,.407,1))
        covers=[]
        for y in (.583,1.593):
            covers += [b((x,y,z-.24),(.86,.022,1.41),.008),
                       b((x+.428,y-.045,z-.24),(.02,.105,1.41),.005),
                       b((x,y+.022,z+.39),(.85,.044,.15),.014)]
        prop("LodgeBunkBlankets"+str(index),at_bed(merged(covers)),"Canvas",False,cloth)

    # A low pile with woven borders; closed thickness, no collision step.
    rx,rz=RUG_CENTER
    prop("LodgeSleepingRug",b((rx,.027,rz),(1.12,.012,RUG_LENGTH),.005),"Canvas",False,(.325,.292,.252,1))
    stripes=[]
    for x in (rx-.49,rx+.49):stripes.append(b((x,.034,rz),(.042,.004,RUG_LENGTH-.17),.001))
    for z in (rz-RUG_LENGTH*.5+.095,rz+RUG_LENGTH*.5-.095):stripes.append(b((rx,.034,z),(.99,.004,.052),.001))
    for z in (rz-RUG_LENGTH*.5-.015,rz+RUG_LENGTH*.5+.015):
        for i in range(18):stripes.append(b((rx-.48+i*.056,.025,z),(.012,.006,.09),.001))
    prop("LodgeRugWeave",merged(stripes),"Canvas",False,(.40,.354,.277,1))

    # The original ordinary chair type: turned legs, seat boards, an open
    # spindle back and worn stretcher. Local +Z is the seated facing.
    chair=[]
    for x in (-.19,.19):
        for z in (-.17,.17):
            chair.append(placed(bp.to_source(kit.turned_leg(.39,.033,.024,.034,9)),(x,.02,z)))
        chair += [b((x,.89,-.20),(.043,.38,.045),.008),
                  b((x,.255,0),(.028,.028,.38),.005)]
    for i in range(3):chair.append(b((-.154+i*.154,.4475,0),(.152,.065,.44),.012))
    for z in (-.175,.175):chair.append(b((0,.3825,z),(.40,.065,.043),.007))
    for y in (.205,.73,1.04):chair.append(b((0,y,-.20),(.43,.043,.055),.009))
    for x in (-.12,0,.12):chair.append(b((x,.885,-.20),(.038,.27,.03),.007))
    # Back posts continue into the rear legs without a floating middle gap.
    for x in (-.19,.19):chair.append(b((x,.605,-.185),(.043,.28,.045),.006))
    prop("LodgeStoveChair",placed(merged(chair),(CHAIR_CENTER[0],0,CHAIR_CENTER[2]),CHAIR_YAW),tint=(.345,.282,.21,1))

    # Back against the left wall, front/handle toward the room. The former
    # freestanding placement obstructed the approach to the cot's foot.
    def at_chest(geometry):return placed(geometry,(CHEST_CENTER[0],0,CHEST_CENTER[1]),-90)
    chest=[];cx=cz=0
    for side in (-1,1):
        chest += [b((cx+side*.55,.33,cz),(.10,.56,.65),.012),
                  b((cx,.33,cz+side*.28),(1.03,.56,.08),.010)]
    chest += [b((cx,.07,cz),(1.1,.08,.61),.010),b((cx,.635,cz),(1.20,.08,.66),.016)]
    for x in (cx-.43,cx+.43):chest.append(b((x,.365,cz-.345),(.06,.57,.02),.004))
    prop("LodgeBlanketChest",at_chest(merged(chest)),tint=edge)
    prop("LodgeChestHardware",at_chest(merged([b((cx,.53,cz-.36),(.07,.145,.023),.005),
        tube([(cx-.09,.37,cz-.357),(cx-.09,.32,cz-.40),(cx+.09,.32,cz-.40),(cx+.09,.37,cz-.357)],.009,6)])),
        "RustedIron",False,iron)
    folds=[b((cx+.03,.71,cz+.01),(.79,.07,.45),.020),b((cx+.03,.78,cz+.01),(.78,.065,.44),.021),
           b((cx-.01,.85,cz+.03),(.67,.065,.40),.019)]
    prop("LodgeFoldedBlankets",at_chest(merged(folds)),"Canvas",False,(.39,.405,.35,1))

    # Entry hooks are shallow, on masonry beside the vestibule, never on a leaf.
    prop("LodgeEntryPegBoard",b((-2.85,1.77,-5.60),(1.3,.18,.12),.012),tint=edge)
    hooks=[]
    for i in range(5):
        x=-3.33+i*.24
        hooks.append(tube([(x,1.77,-5.525),(x,1.74,-5.435),(x,1.79,-5.405)],.012,7))
    prop("LodgeEntryHooks",merged(hooks),"RustedIron",False,iron)

    # A small crockery shelf on the unbroken pier beyond the kettle corner.
    shelf=[b((8.49,1.73,4.38),(.36,.055,1.16),.010)]
    for z in (3.94,4.82):
        shelf += [b((8.62,1.55,z),(.045,.36,.06),.006),
                  placed(b((0,0,0),(.035,.30,.06),.006),(8.53,1.60,z),0)]
    prop("LodgeCupShelf",merged(shelf),tint=edge)
    cups=[]
    for i in range(3):
        at=(8.43,1.759,4.01+i*.34)
        cups.append(lathe([(.045,0),(.054,.012),(.063,.12),(.056,.124),(.047,.017),(.009,.017)],at,12))
        cups.append(tube([(at[0]-.06,at[1]+.10,at[2]),(at[0]-.105,at[1]+.10,at[2]),
                          (at[0]-.105,at[1]+.04,at[2]),(at[0]-.06,at[1]+.04,at[2])],.008,6))
    prop("LodgeShelfCups",merged(cups),"LighterMetal",False,(.54,.55,.48,1))

    # Old plain wall pictures, deliberately free of text, heraldry and dates.
    # Atlas UV quadrants: photo TL, village painting TR, forest painting BL.
    def picture(name,center,width,height,yaw,cell):
        rails=[b((x,0,.018),(.064,height+.12,.075),.009) for x in (-width*.5-.032,width*.5+.032)]
        rails += [b((0,y,.018),(width,.064,.075),.009) for y in (-height*.5-.032,height*.5+.032)]
        rails.append(b((0,0,.07),(width+.10,height+.10,.026),.006))
        prop(name+"Frame",placed(merged(rails),center,yaw),solid=False,tint=(.235,.205,.164,1))
        g=b((0,0,0),(width,height,.008),0)
        u0,v0=cell
        uv=[(u0+.01+.48*(x/width+.5),v0+.01+.48*(y/height+.5)) for x,y,z in g[0]]
        prop(name+"Image",placed(g,center,yaw),"LodgePictures",False,(1,1,1,1))
        parts[-1]["picture_uv"]=uv
    picture("LodgePhotograph",(8.55,2.02,-.6),1.02,.71,90,(0,.5))
    picture("LodgeVillagePainting",(3.05,2.22,5.55),1.02,.76,0,(.5,.5))
    picture("LodgeForestPainting",(-3.30,2.16,-5.54),.76,.94,180,(0,0))

    # A dusty ordinary taxidermy mount: faceted neck/skull/muzzle, folded ears,
    # dark glass eyes and branching antlers. No light or interaction component.
    mount=(-2.65,2.20,5.61)
    plaque=ellipsoid((0,0,.008),(.27,.37,.040),14,8)
    prop("LodgeDeerPlaque",placed(plaque,mount),solid=False,tint=(.235,.177,.121,1))
    hide=[ellipsoid((0,-.07,-.13),(.16,.24,.18)),ellipsoid((0,.13,-.29),(.13,.20,.15)),
          ellipsoid((0,.07,-.44),(.092,.105,.17))]
    for sign in (-1,1):
        ear=ellipsoid((0,0,0),(.15,.047,.063),9,5)
        hide.append(placed(bp.u_rotated(ear,(0,0,sign*26)),(sign*.18,.25,-.24)))
    prop("LodgeDeerHead",placed(merged(hide),mount),"Canvas",False,(.37,.31,.235,1))
    pale=[ellipsoid((0,-.005,-.45),(.071,.052,.12),10,5),ellipsoid((0,-.17,-.23),(.08,.11,.055),10,5)]
    prop("LodgeDeerPaleFur",placed(merged(pale),mount),"Canvas",False,(.52,.485,.401,1))
    features=[ellipsoid((0,.085,-.585),(.072,.045,.036),10,5)]
    for sign in (-1,1):features.append(ellipsoid((sign*.109,.178,-.345),(.016,.017,.019),8,4))
    prop("LodgeDeerEyesAndNose",placed(merged(features),mount),"Canvas",False,(.105,.101,.087,1))
    antlers=[]
    for sign in (-1,1):
        antlers.append(tube([(sign*.085,.28,-.24),(sign*.15,.41,-.18),(sign*.24,.57,-.15),
                             (sign*.33,.72,-.16),(sign*.44,.87,-.21)],sides=7,radii=(.031,.027,.021,.014,.003)))
        for points,radii in (([(.16,.43,-.18),(.13,.56,-.31),(.12,.63,-.39)],(.022,.012,.003)),
                             ([(.24,.57,-.15),(.38,.61,-.32),(.42,.70,-.39)],(.018,.010,.003)),
                             ([(.32,.71,-.16),(.27,.84,-.29),(.30,.93,-.33)],(.014,.009,.003))):
            antlers.append(tube([(sign*x,y,z) for x,y,z in points],sides=7,radii=radii))
    prop("LodgeDeerAntlers",placed(merged(antlers),mount),"Canvas",False,(.49,.457,.377,1))

    # Existing skis gain the equipment that belongs beneath their rack.
    boots=[]
    for x in (-4.51,-4.12):
        boots += [b((x,.26,4.61),(.235,.12,.41),.035),
                  b((x,.42,4.71),(.21,.25,.23),.035),
                  b((x,.206,4.60),(.25,.035,.44),.010)]
    prop("LodgeSkiBoots",kit.translated(merged(boots),(0,.082,0)),"Canvas",True,(.255,.265,.235,1),"LodgeSkiEquipment")
    straps=[]
    for x in (-4.51,-4.12):
        for y,z in ((.35,4.59),(.45,4.583)):
            straps.append(b((x,y,z),(.224,.028,.025),.004))
    prop("LodgeSkiBootStraps",kit.translated(merged(straps),(0,.082,0)),"RustedIron",False,iron,"LodgeSkiEquipment")
    poles=[]
    for x in (-3.65,-3.40):
        poles += [tube([(x,.23,4.64),(x+.10,1.81,4.67)],.009,7),
                  b((x+.097,1.76,4.67),(.031,.17,.032),.009),
                  lathe([(.08,0),(.08,.015),(.018,.023),(.013,.01)],(x+.01,.36,4.64),10)]
    prop("LodgeSkiPoles",merged(poles),"RustedIron",False,(.29,.292,.26,1),"LodgeSkiEquipment")


def validate_furniture(parts):
    lodge={p["name"]:p for p in parts if p["kind"]=="SkiLodge"}
    assert "Benches" not in lodge,"The oversized legacy benches must be removed"
    low,high=kit.bounds(lodge["RentalCounter"]["geometry"])
    assert abs(high[0]-8.68)<1e-6 and abs(low[0]-7.88)<1e-6,"Counter must meet the right wall"
    low,high=kit.bounds(lodge["LodgeDiningTable"]["geometry"])
    assert abs(high[1]-.78)<1e-6 and abs((high[2]-low[2])-2.7)<1e-6
    low,high=kit.bounds(lodge["LodgeBlanketChest"]["geometry"])
    assert abs(low[0]+8.68)<1e-6,"Linen chest back must meet the left wall"
    assert abs(high[0]-low[0]-.685)<1e-6 and abs(high[2]-low[2]-1.2)<1e-6
    handle_low,handle_high=kit.bounds(lodge["LodgeChestHardware"]["geometry"])
    assert handle_low[0]>CHEST_CENTER[0],"Chest handle must face the room"
    assert high[0]<-7.9,"Chest must leave the cot foot approach open"
    for index in range(2):
        low,high=kit.bounds(lodge["LodgeBunkFrame"+str(index)]["geometry"])
        assert -8.68<=low[0]<-8.64 and -6.61<high[0]<-6.58 and high[1]<2.11
        assert high[2]<=5.68 and low[1]>=.019,"Bunks must stay within the sleeping corner"
        pillow_low,pillow_high=kit.bounds(lodge["LodgeBunkPillows"+str(index)]["geometry"])
        assert pillow_high[0]<BUNK_CENTERS[index][0]-.4 and 0<pillow_low[0]-low[0]<.25,\
            "Both bunk pillow/head ends must face the left wall"
        assert high[0]-low[0]>high[2]-low[2],"Bunk feet must point into the room"
        dx=max(low[0]+6.7,0,-6.7-high[0]);dz=max(low[2]-5.2,0,5.2-high[2])
        assert math.hypot(dx,dz)>.34,"Bunk occupies the rear sleeping-zone warmth probe"
    low,high=kit.bounds(lodge["LodgeStoveChair"]["geometry"])
    stove_low,stove_high=kit.bounds(lodge["StoveBody"]["geometry"])
    gap_x=max(stove_low[0]-high[0],0,low[0]-stove_high[0])
    gap_z=max(stove_low[2]-high[2],0,low[2]-stove_high[2])
    assert math.hypot(gap_x,gap_z)>.85 and low[1]>=.019,"Chair must keep a physical gap from the stove"
    # Match the shared .44 m seat depth plus .52 m entry-edge clearance.
    # Its grounded dock must clear the iron body with the full player capsule.
    length=math.hypot(CHAIR_CENTER[0],CHAIR_CENTER[2])
    dock=(CHAIR_CENTER[0]*(1-.74/length),CHAIR_CENTER[2]*(1-.74/length))
    dx=max(stove_low[0]-dock[0],0,dock[0]-stove_high[0])
    dz=max(stove_low[2]-dock[1],0,dock[1]-stove_high[2])
    assert math.hypot(dx,dz)>.36,"Chair approach capsule overlaps the stove"
    for name in ("LodgePhotographImage","LodgeVillagePaintingImage","LodgeForestPaintingImage"):
        part=lodge[name]
        assert part["surface"]=="LodgePictures" and not part["solid"]
        assert len(part["picture_uv"])==len(part["geometry"][0])
        assert all(0<u<1 and 0<v<1 for u,v in part["picture_uv"])
    assert all(p.get("parent")=="LodgeSkiEquipment" for p in lodge.values()
               if p["name"] in ("RemainingSkis","LodgeSkiBoots","LodgeSkiBootStraps","LodgeSkiPoles"))
