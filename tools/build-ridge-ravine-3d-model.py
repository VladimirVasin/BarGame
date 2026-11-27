#!/usr/bin/env python3
"""Surveyed cut-away apartment, shoring and fallen fabric for the summit ravine."""
import argparse
import importlib.util
import json
import math
import sys
from pathlib import Path

sys.dont_write_bytecode = True
TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("court", TOOLS / "build-city-residential-courtyard-3d-model.py")
court = importlib.util.module_from_spec(spec)
spec.loader.exec_module(court)
port = court.port
OUT = TOOLS.parent / "Assets/Resources/City/Hillside"
MANIFEST = "RidgeRavineSurvey.json"
# West ends of the surviving floor at successive Z stations, back to street.
EDGE = [(-6.05, -4.6), (-6.14, -3.2), (-5.57, -2.8), (-5.72, -1.8),
        (-4.87, -.9), (-5.1, .35), (-4.26, 1.35), (-4.41, 2.35),
        (-3.73, 2.76), (-3.87, 4.60)]
RIGHT, STOREY = 6.65, 2.96
FOOTPRINT = EDGE + [(RIGHT, 4.6), (RIGHT, -4.6)]
WELL_LEFT, WELL_BACK, TURN, WELL_FRONT = 3.91, -2.92, -1.53, 1.29


class Part:
    def __init__(self, name):
        self.root = port.empty(name)
        self.groups = {}

    def group(self, name, role):
        if (name, role) not in self.groups:
            self.groups[name, role] = court.geometry(role)
        return self.groups[name, role]

    def box(self, name, role, centre, dimensions, bevel=.015):
        court.box(self.group(name, role), centre, dimensions, port.EDGE, bevel)

    def turned_box(self, name, role, centre, size, angle):
        verts, faces = port.kit.chamfered_box((0,0,0), port.source(size), .012)
        c,s = math.cos(angle), math.sin(angle)
        x,y,z = centre
        verts = [(x+u*c+v*s,z-u*s+v*c,y+h) for u,v,h in verts]
        port.kit_add(self.group(name,role),(verts,faces),port.WOOD)

    def rod(self, name, role, a, b, radius, sides=8):
        self.group(name, role).rod(a, b, radius, port.METAL, sides)

    def profile(self, name, role, outline, project, lo, hi):
        """Closed polygon extrusion; the two loops can have different batters."""
        verts = [project(a, b, depth) for depth in (lo, hi) for a, b in outline]
        n = len(outline)
        faces = [tuple(reversed(range(n))), tuple(range(n, 2*n))]
        faces.extend((i, (i+1) % n, n + (i+1) % n, n+i) for i in range(n))
        self.group(name, role).add(verts, faces, port.CONCRETE)

    def finish(self, material):
        for (name, role), geom in self.groups.items():
            court.emit(geom, name, self.root, material)
        return self.root


def points(vertices):
    return [dict(x=round(x, 4), y=round(z, 4)) for x, z in vertices]


def stairwell(part):
    # The east gable carries both concrete half-landings. The narrow strip
    # west of the well remains the through hall between both apartment doors.
    def rail(start, end):
        length = math.dist(start, end)
        bays = math.ceil(length / .93)
        for i in range(bays + 1):
            x, y, z = (a+(b-a)*i/bays for a,b in zip(start,end))
            part.rod("LandingIronwork", "SteelDark", (x,y-.025,z), (x,y+.92,z), .024, 6)
        for height, name, role, radius in ((.46,"LandingIronwork","SteelDark",.018),
                                         (.92,"StairHandrails","Timber",.046)):
            a,b = ((x,y+height,z) for x,y,z in (start,end))
            part.rod(name, role, a, b, radius, 8)

    for floor in range(3):
        base = floor * STOREY
        rail((WELL_LEFT,base,WELL_FRONT), (WELL_LEFT,base,WELL_BACK))
        rail((WELL_LEFT,base,WELL_BACK), (RIGHT-.15,base,WELL_BACK))
        if floor == 2:
            rail((WELL_LEFT,base,WELL_FRONT), (5.22,base,WELL_FRONT))
            break
        half = STOREY / 2
        part.box("HalfLandings", "Concrete", ((RIGHT+WELL_LEFT)/2,base+half-.10,(TURN+WELL_BACK)/2),
                 (RIGHT-WELL_LEFT,.20,TURN-WELL_BACK), .009)
        rail((WELL_LEFT+.045,base+half,TURN), (WELL_LEFT+.045,base+half,WELL_BACK+.045))
        rail((WELL_LEFT+.045,base+half,WELL_BACK+.045), (RIGHT-.16,base+half,WELL_BACK+.045))
        for left,right,z0,z1,y0,hand in (
            (WELL_LEFT,5.21,WELL_FRONT,TURN,base,5.16),
            (5.27,RIGHT,TURN,WELL_FRONT,base+half,5.32)):
            # Cast flights have nine risers, a rounded lip on every tread and
            # a continuous soffit. Their collision skin follows the pitch.
            depth, rise = abs(z1-z0)/9, half/9
            side = [(0,-.16),(9*depth,half-.16),(9*depth,half)]
            for step in reversed(range(9)):
                front,top = step*depth,(step+1)*rise
                side.extend(((front+.015,top),(front,top-.015),(front,step*rise)))
            def section(run, elevation, x):
                return (x, y0+elevation, z0+math.copysign(run,z1-z0))
            part.profile("CastStairTreads","Concrete",side,section,left,right)
            # Use the same underside for clearance above the lower flight.
            ramp = [(0,-.16),(9*depth,half-.16),(9*depth,half),(0,0)]
            part.profile("COL_StairFlights","Plain",ramp,section,left,right)
            rail((hand,y0+.07,z0), (hand,y0+half,z1))
            if left == WELL_LEFT:
                rail((left+.045,y0+.07,z0), (left+.045,y0+half,z1))


def house():
    part = Part("RavineHouse")
    obstacles = []
    edges = []

    def solid_rect(x0, z0, x1, z1):
        obstacles.append(dict(points=points([(x0, z0), (x1, z0), (x1, z1), (x0, z1)])))

    def wall(x0, x1, y0, y1, z, depth=.32, name="RenderedFacade", role="Plaster"):
        part.box(name, role, ((x0+x1)/2, (y0+y1)/2, z), (x1-x0, y1-y0, depth))
        if y0 < 1.85 and y1 > .3:
            solid_rect(x0, z-depth/2, x1, z+depth/2)

    def slab(level, edge):
        # Upper slabs recede at different rates. The concrete lip has a real
        # fracture plane and scattered bent reinforcement exposed at the break.
        slit = 0 < level < 3*STOREY
        east = WELL_LEFT if slit else RIGHT
        outlines = [edge + [(east, 4.6), (east, -4.6)]]
        if slit:
            for lo,hi in ((-4.6,WELL_BACK),(WELL_FRONT,4.6)):
                outlines.append([(east,lo),(east,hi),(RIGHT,hi),(RIGHT,lo)])
        for outline in outlines:
            part.profile("FracturedSlabs", "Concrete", outline, lambda x,z,h:(x,h,z),
                         level-.23, level)
        for i in range(1, len(edge), 2):
            x,z = edge[i]
            part.rod("ExposedTies", "SteelRust", (x+.17,level-.13,z), (x-.28,level-.16,z+.03), .021)
            part.rod("ExposedTies", "SteelRust", (x-.28,level-.16,z+.03), (x-.47,level-.31,z+.09), .021)

    def window(x, base, z, width):
        h, y = 1.38, base + 1.52
        for side in (-1, 1):
            part.box("OldSashes", "Timber", (x+side*(width/2-.044),y,z), (.088,h,.13))
        for yy in (y-h/2+.035,y+h/2-.035):
            part.box("OldSashes", "Timber", (x,yy,z), (width,.07,.13))
        part.box("OldSashes", "Timber", (x+.13,y,z), (.046,h,.083))
        part.box("StoneSills", "ConcreteWall", (x,base+.815,z), (width+.16,.085,.48))

    def frontage(left, z, base, apertures):
        start = left
        for x, width, door in apertures:
            a,b = x-width/2,x+width/2
            wall(start,a,base,base+STOREY-.18,z)
            if not door:
                wall(a,b,base,base+.83,z)
                window(x,base,z,width)
            wall(a,b,base+(2.24 if door else 2.21),base+STOREY-.18,z)
            start = b
        wall(start,RIGHT,base,base+STOREY-.18,z)

    def fractured_return(z, base, boundary, full, reverse=False):
        # Brick core protrudes in a toothed scar beyond the finished plaster.
        outline = [(boundary,0),(full,0),(full,STOREY-.18),(boundary+.90,STOREY-.18),
                   (boundary+.91,2.21),(boundary+.58,2.18),(boundary+.61,1.58),
                   (boundary+.24,1.51),(boundary+.29,.95),(boundary+.08,.90)]
        part.profile("BrokenBrickwork", "ConcreteWall", outline,
                     lambda x,y,d: (x,base+y,z+d), -.155,.155)
        for row in range(13):
            y = .1+row*.211
            offset = .08 if y < .95 else .25 if y < 1.53 else .58 if y < 2.20 else .90
            for lane in range(2 if row%3 else 3):
                xx = boundary+offset+.12+lane*.30+(row%2)*.11
                if xx+.15 >= full: continue
                part.box("OxideBricks", "Plaster", (xx,base+y,z+(.155 if reverse else -.155)),
                         (.285,.185,.064), .012)

    for floor, retreat in enumerate((0, .95, 2.55, 3.52)):
        base = floor*STOREY
        edge = [(x+retreat + (.17*math.sin(i*1.8+floor) if floor else 0),z)
                for i,(x,z) in enumerate(EDGE)]
        slab(base, edge)
        edges.append(dict(points=points(edge)))
        if floor == 3:
            part.box("RoofEdge", "Roof", (RIGHT-.03,base+.10,0), (.32,.20,9.48))
            part.box("RoofEdge", "Roof", (4.22,base+.10,4.57), (5.04,.20,.30))
            break
        for z,left,sgn in [(4.6,edge[-1][0],1),(-4.6,edge[0][0],-1)]:
            seam = left+1.40
            holes = [(4.71,1.62,floor==0 and sgn==1)]
            if seam < .0: holes.insert(0,(1.12,1.52,False))
            frontage(seam,z,base,holes)
            fractured_return(z,base,left,seam,sgn==1)
        part.box("BlindGable", "Plaster", (RIGHT,base+STOREY/2,0), (.30,STOREY,9.2))
        # A small return and a lintel leave open doors between hall and each flat.
        for lo,hi in [(-4.6,-2.78),(-1.20,1.34),(2.94,4.6)]:
            part.box("HallPartitions","Plaster",(2.50,base+1.38,(lo+hi)/2),(.18,2.76,hi-lo))
            if floor == 0: solid_rect(2.41,lo,2.59,hi)
        for z in (-1.99,2.14):
            part.box("DoorHeads","Timber",(2.50,base+2.40,z),(.20,.71,1.58))
            for s in (-1,1):
                part.box("DoorLinings","Timber",(2.50,base+1.05,z+s*.78),(.23,2.10,.068))
        wall(1.03,2.50,base,base+2.76,0,.20,"PartyWall","Plaster")
        wall(-.49,1.03,base+2.13,base+2.76,0,.20,"PartyWall","Plaster")
        split = -3.85+retreat
        if split < -1.52:
            fractured_return(0,base,split,min(split+1.32,-.50))
            if split+1.32 < -.50:
                wall(split+1.32,-.50,base,base+2.76,0,.29,"PartyWall","Plaster")
            if floor==0: solid_rect(split,-.16,-.5,.16)
        # Damp, peeled-back wall covering confined to the room side.
        paper = [(max(split+.85,-2.64),.15),(-.55,.15),(-.55,2.53),(-.84,2.53),
                 (-1.02,2.32),(-1.28,2.51),(max(split+.85,-2.64),2.46)]
        if floor < 2:
            part.profile("FadedWallpaper","Plaster",paper,lambda x,y,d:(x,base+y,d),-.174,-.151)
        # Different layers at exposed floor edges and real roof/floor thickness.
        if floor:
            # A waist-high remnant of the masonry parapet follows each torn
            # slab. Its uneven crown leaves a view into the cut from upstairs.
            for i, ((x,z),(xx,zz)) in enumerate(zip(edge,edge[1:])):
                h0 = .90 + .10*math.sin(i*2.3+floor)
                h1 = .90 + .10*math.sin((i+1)*2.3+floor)
                profile = [(0,-.04),(1,-.04),(1,h1-.025),(.97,h1),(.55,h1),
                           (.52,h0-.06),(.47,h0),(.02,h0),(0,h0-.025)]
                part.profile("UpperSillRemnants","ConcreteWall",profile,
                             lambda t,y,d:(x+(xx-x)*t+d,base+y,z+(zz-z)*t),-.06,.12)
            for i in (2,4,6):
                x,z = edge[i]
                part.box("BrokenFloorboards","Timber",(x+.34,base+.028,z+.1),(.85,.058,.18))
    # Floorboards are clipped individually to the loss line. Ends break back
    # from the concrete instead of forming a tidy straight threshold.
    def edge_at(z):
        for (x0,z0),(x1,z1) in zip(EDGE,EDGE[1:]):
            if z<=z1:
                return x0+(x1-x0)*(z-z0)/(z1-z0)
        return EDGE[-1][0]

    for i in range(17):
        z0=-4.42+i*.249
        z1=z0+.244
        left0=edge_at(z0)+.17+.13*math.sin(i*2.7)
        left1=edge_at(z1)+.20+.11*math.cos(i*1.7)
        plank=[(left0,z0),(2.40,z0),(2.40,z1),(left1+.20,z1),(left1+.10,z1-.055),(left1,z1-.07)]
        part.profile("RoomFloorboards","Timber",plank,lambda x,z,y:(x,y,z),-.015,.023)
    # Wallpaper turns into the rear window reveals. Torn margins expose plaster.
    for a,b,lo,hi in [(-3.54,.30,.18,2.55),(.32,1.91,.18,.78),(1.95,2.42,.18,2.52)]:
        strip=[(a,lo+.09),(a+.12,lo),(b,lo),(b,hi-.13),(b-.16,hi),(a+.19,hi),(a+.05,hi-.20)]
        part.profile("FadedWallpaper","Plaster",strip,lambda x,y,d:(x,y,d),-4.435,-4.408)
    part.rod("RainwaterStack","SteelDark",(6.29,.12,4.88),(6.29,8.64,4.88),.080)
    for y in (.38,3.22,5.86,8.49):
        part.box("PipeClips","SteelRust",(6.29,y,4.80),(.24,.09,.20))
    solid_rect(RIGHT-.15,-4.68,RIGHT+.15,4.68)
    solid_rect(-6.05,-4.76,-4.65,-4.44)
    solid_rect(-3.87,4.44,-2.47,4.76)
    # Low, tilted remnants physically guard the exposed floor and leave a view down.
    sill_heights = [.86,.39,.24,.28,.77,1.01,.60,.26,.30,.84]
    for i, ((x,z),(xx,zz)) in enumerate(zip(EDGE,EDGE[1:])):
        h0, h1 = sill_heights[i:i+2]
        outline=[(0,-.08),(1,-.08),(1,h1),(.63,h1+.065),(.51,h0-.075),(0,h0)]
        part.profile("SlumpedSill","ConcreteWall",outline,
                     lambda t,y,d:(x+(xx-x)*t+d-.22*y,y,z+(zz-z)*t),-.09,.10)
        obstacles.append(dict(points=points([(x-.18,z),(x+.10,z),(xx+.10,zz),(xx-.18,zz)])))
    # Kitchen splashback and squat enamel sink on the courtyard side of the broken wall.
    wall(-2.12,-.72,.79,1.56,.193,.047,"KitchenTiles","Plaster")
    for xx in (-2.01,-.84):
        part.box("SinkBrackets","SteelDark",(xx,.60,.42),(.07,.39,.43))
    part.box("EnamelBasin","SteelLight",(-1.43,.73,.46),(1.30,.07,.55))
    for z in (.19,.73):
        part.box("EnamelBasin","SteelLight",(-1.43,.80,z),(1.30,.16,.045))
    for x in (-2.06,-.80):
        part.box("EnamelBasin","SteelLight",(x,.80,.46),(.045,.16,.53))
    part.rod("Taps","Steel",(-1.28,.85,.27),(-1.28,1.13,.27),.025)
    part.rod("Taps","Steel",(-1.28,1.13,.27),(-1.28,1.12,.47),.023)
    solid_rect(-2.10,.14,-.75,.76)
    # Cast radiator below the back window, on two piping spurs.
    for i in range(13):
        part.box("CastRadiator","SteelLight",(.42+i*.11,.48,-4.27),(.083,.55,.22),.036)
    for y in (.30,.67):
        part.rod("RadiatorPipe","SteelDark",(.13,y,-4.28),(1.96,y,-4.28),.036)
    solid_rect(.10,-4.50,2,-4.12)
    for z, length in [(-3.6,1.20),(-1.23,.62),(1.1,.75),(3.6,1.55)]:
        part.box("RoomSkirting","Timber",(2.39,.093,z),(.065,.18,length))
    # One surviving bathroom partition and its broken water service face the
    # missing half of the second storey. The open wardrobe above is still fixed
    # to the other flat, so rooms read separately from across the bridge.
    part.box("KitchenTiles","Plaster",(2.386,STOREY+.98,.05),(.044,1.80,2.04))
    for z in (-.37,.09):
        part.rod("ShearedRisers","SteelDark",(2.30,STOREY+.12,z),(2.30,STOREY+1.13,z),.046)
        part.rod("ShearedRisers","Steel",(2.30,STOREY+1.13,z),(2.10,STOREY+1.13,z),.047)
    part.rod("TowelRail","SteelLight",(2.20,STOREY+1.55,-.65),(2.20,STOREY+1.55,.12),.029)
    for z in (-.65,.12):
        part.rod("TowelRail","SteelLight",(2.20,STOREY+1.55,z),(2.37,STOREY+1.55,z),.028)
    cabinet_y=2*STOREY
    part.box("WardrobeBack","Timber",(1.16,cabinet_y+1.04,-4.35),(1.94,2.06,.07))
    for x in (.21,2.10):
        part.box("WardrobeSides","Timber",(x,cabinet_y+1.04,-4.07),(.075,2.08,.62))
    for y in (.11,.62,1.63,2.06):
        part.box("WardrobeShelves","Timber",(1.15,cabinet_y+y,-4.07),(1.83,.064,.62))
    part.turned_box("SaggingWardrobeDoor","Timber",(.47,cabinet_y+1.02,-3.46),(.89,1.95,.052),-.76)
    # Embedded strip footing under the landward envelope.
    part.box("Footings","ConcreteWall",(RIGHT-.03,-.49,0),(.47,.98,9.3))
    for z,left in [(-4.60,-6.05),(4.60,-3.87)]:
        part.box("Footings","ConcreteWall",((RIGHT+left)/2,-.49,z),(RIGHT-left,.98,.42))
    stairwell(part)
    return part, obstacles, edges


def shore():
    part=Part("RavineShoringBay")
    # Local +Z looks out into the cut; the surveyed placement aligns each bay to the rock.
    # Individual battered courses and an offset concrete stitching panel expose the earlier work.
    for row in range(4):
        count = 4 + (row%2)
        for i in range(count):
            w=3.8/count
            cy=-2.75+(row+.5)*.49
            part.box("CoursedStone","ConcreteWall",(-1.9+(i+.5)*w,cy,.12-row*.105),
                     (w-.018,.482,.84),.044)
    part.box("SpalledRepair","Concrete",(.12,-.59,-.20),(3.52,1.34,.77),.055)
    part.box("RunoffCap","ConcreteWall",(.12,.065,-.17),(3.66,.14,.89),.024)
    for x in (-1.09,1.24):
        part.box("AnchorPlates","SteelRust",(x,-.52,.202),(.41,.43,.07),.012)
        part.rod("AnchorNuts","SteelDark",(x,-.52,.23),(x,-.52,.325),.065,6)
    part.rod("SeepPipe","SteelDark",(-.51,-.96,.12),(-.51,-1.0,.38),.092,10)
    return part


def debris():
    panel=Part("RavineKitchenFragment")
    outline=[(-1.25,0),(1.13,.04),(1.27,.54),(.91,.59),(.96,.88),(.53,.95),
             (.46,1.19),(-.66,1.30),(-.78,1.04),(-1.13,1.04)]
    panel.profile("BrickBacking","ConcreteWall",outline,lambda x,y,d:(x,y,d),-.17,.13)
    panel.profile("KitchenTiles","Plaster",[(-.86,.19),(.86,.19),(.86,.63),(.40,.63),
                  (.40,.95),(-.48,.95),(-.48,.79),(-.86,.79)],lambda x,y,d:(x,y,d),.13,.16)
    rubble=Part("RavineFallenFloor")
    rubble.profile("FracturedSlabs","Concrete",[(-2,-1.17),(.43,-1.36),(1.82,-.73),(1.36,.02),
                   (1.57,.83),(-.31,1.18),(-1.72,.76)],lambda x,z,h:(x,h+.17*x,z),-.22,.10)
    for i in range(4):
        x=-1.6+i*.62
        rubble.rod("BentRebar","SteelRust",(x,.17*x,-.65),(x+.12,.17*x-.19,-1.65),.029)
    # A collapsed window frame lies askew on the slab with one missing lower corner.
    for x in (-1.07,.57):
        rubble.box("FallenSash","Timber",(x,.38,.0),(.09,.10,1.28))
    for z,l,x in [(.62,1.72,-.25),(-.62,1.22,-.50)]:
        rubble.box("FallenSash","Timber",(x,.38,z),(l,.10,.10))
    rubble.box("FallenSash","Timber",(-.16,.37,-.02),(.06,.07,1.22))
    for i,(x,z) in enumerate([(-2.11,-.36),(-1.94,.98),(.87,-1.42),(1.67,-.76),
                               (2.05,-.27),(.53,1.26),(1.31,1.52)]):
        rubble.box("OxideBricks","Plaster",(x,.12+max(0,.18*x),z),
                   (.36+i%3*.13,.23+i%2*.11,.30+i%2*.16),.039)
    # Each fall has different construction and contact geometry. A broken jamb
    # and sill keep their open window; the former upper floor shows the joist ends.
    jamb=Part("RavineWindowWall")
    face=[(-1.56,-.14),(1.21,-.14),(1.56,.12),(1.48,.42),(1.08,.50),
          (1.10,.71),(-.62,.71),(-.62,2.10),(-.90,2.06),(-.90,2.32),
          (-1.34,2.23),(-1.29,1.73),(-1.57,1.65)]
    jamb.profile("TornJamb","Plaster",face,lambda x,y,d:(x,y,d),-.18,.16)
    jamb.box("StoneSills","ConcreteWall",(.20,.745,.03),(1.78,.14,.52),.028)
    jamb.box("OldSashes","Timber",(-.57,1.37,.04),(.093,1.23,.135))
    jamb.box("OldSashes","Timber",(.12,.85,.04),(1.48,.083,.135))
    for row in range(5):
        jamb.box("OxideBricks","Plaster",(-1.43+(row%2)*.06,.07+row*.211,.16),
                 (.32,.18,.064))
    joists=Part("RavineSplinteredFloor")
    joists.profile("FracturedSlabs","Concrete",[(-1.62,-.78),(-.45,-1.18),(1.33,-.96),
                   (1.73,.24),(.63,.86),(-.04,.63),(-.92,1.03),(-1.52,.46)],
                   lambda x,z,y:(x,y,z),-.21,.03)
    for i,length in enumerate((2.38,2.77,2.16,2.48,1.84,1.51)):
        z=-.74+i*.276
        end=1.22 if i<4 else .83
        start=end-length
        outline=[(start,z+.04),(end,z),(end,z+.253),(start+.04,z+.25),
                 (start+.22,z+.19),(start+.09,z+.13),(start+.30,z+.11)]
        joists.profile("SplitFloorboards","Timber",outline,lambda x,z,y:(x,y+.028*x,z),.02,.095)
    for x in (-.87,.37):
        joists.box("FloorBearers","Timber",(x,-.03,.01),(.17,.21,2.52),.018)
    pipe=Part("RavinePipeWall")
    pipe.profile("PipeWallCore","ConcreteWall",[(-1.18,0),(.89,0),(1.13,.30),(.80,.56),
                 (.79,1.15),(.40,1.16),(.34,1.41),(-.37,1.51),(-.61,1.22),(-1.0,1.24)],
                 lambda x,y,d:(x,y,d),-.15,.19)
    pipe.profile("FadedWallpaper","Plaster",[(-.88,.10),(.71,.10),(.70,.81),(.31,.83),
                 (.17,1.18),(-.32,1.26),(-.47,1.00),(-.88,.94)],
                 lambda x,y,d:(x,y,d),.19,.22)
    for a,b in [((-.40,.06,.33),(-.40,1.44,.33)),((-.40,1.44,.33),(-.57,1.71,.29)),
                ((-.57,1.71,.29),(-.62,1.82,.24))]:
        pipe.rod("StackRemnant","SteelRust",a,b,.079,10)
    for y in (.31,1.05):
        pipe.box("PipeClips","SteelDark",(-.40,y,.26),(.26,.085,.15))
    roof=Part("RavineFoldedRoof")
    # Two folds of the same seamed roof sheet remain pinned to a piece of coping.
    roof.profile("Coping","ConcreteWall",[(-1.32,-.26),(1.16,-.36),(1.34,.18),
                 (.93,.33),(-1.22,.28)],lambda x,z,y:(x,y,z),-.13,.21)
    for lo,hi,grade in [(-1.04,.05,.32),(.05,1.08,-.16)]:
        profile=[(-1.32,lo),(1.42,lo),(1.35,hi-.15),(.84,hi),(.48,hi-.12),(-1.19,hi)]
        roof.profile("FoldedRoofing","Roof",profile,
                     lambda x,z,y:(x,y+.48+grade*(z-.05)-.06*x,z),-.025,.025)
        for x in (-.88,-.22,.49,1.04):
            roof.rod("RolledSeams","Roof",(x,.51+grade*(lo-.05)-.06*x,lo),
                     (x,.51+grade*(hi-.14)-.06*x,hi-.09),.026,6)
    collapse=Part("RavineMasonryFall")
    # A fan of fractured masonry beds, with courses still bonded in places.
    fragments=[(-1.63,-.43,.77,.38),(-.83,-.64,.68,.43),(.12,-.46,.88,.52),
               (.76,-.09,.63,.36),(1.38,.39,.70,.41),(-.23,.33,.59,.34),(-1.12,.72,.77,.36)]
    for i,(x,z,w,h) in enumerate(fragments):
        profile=[(-w,-.20),(-.54*w,-.43),(w*.9,-.32),(w,.14),(w*.40,.47),(-w*.81,.30)]
        collapse.profile("BondedMasonry","ConcreteWall",profile,
                         lambda u,v,y:(x+u,y+.12*u+.07*v,z+v),-.17,h)
        for j in range(2+i%2):
            xx=x-w*.53+j*.29
            collapse.box("OxideBricks","Plaster",(xx,h+.04+.12*(xx-x),z),(.279,.15,.29),.023)
    return panel,rubble,jamb,joists,pipe,roof,collapse


def apartment_fittings():
    tub=Part("RavineEnamelBath")
    # The bath travelled with part of its floor and wet wall. Its wide foundation
    # seats into the slope on the landward side, leaving a visible broken underside.
    base=[(-1.57,-.87),(-.42,-1.06),(.04,-.88),(.75,-.98),(1.45,-.57),
          (1.51,.61),(.76,.88),(-.25,.84),(-.96,.99),(-1.48,.50)]
    tub.profile("BathroomSlab","Concrete",base,lambda x,z,y:(x,y,z),-.56,.008)
    tub.profile("KitchenTiles","Plaster",[(x*.96,z*.95) for x,z in base],
                lambda x,z,y:(x,y,z),.008,.031)
    tub.profile("BathroomWallFoot","ConcreteWall",[(-1.33,.01),(1.25,.01),(1.20,.53),
                (.74,.61),(.59,.41),(.25,.46),(.11,.88),(-.72,.88),(-.81,.66),(-1.35,.57)],
                lambda x,y,d:(x,y,d),-.78,-.57)
    tub.profile("KitchenTiles","Plaster",[(-1.22,.06),(1.10,.06),(1.10,.45),(.60,.47),
                (.55,.35),(.11,.35),(-.04,.70),(-.63,.70),(-.77,.50),(-1.22,.48)],
                lambda x,y,d:(x,y,d),-.57,-.55)
    # A continuous rolled rim turns down the inside into a visible basin floor.
    # Rings form a hollow casting; the angled ends widen toward the old rim.
    rings=[(.64,.25,.08),(.78,.31,.20),(.89,.379,.58),(.895,.391,.605),
           (.88,.394,.635),(.839,.352,.637),(.829,.341,.605),(.72,.275,.31),(.55,.19,.165)]
    verts=[]
    count=24
    for rx,rz,y in rings:
        for i in range(count):
            a=math.tau*i/count
            u,v=math.cos(a),math.sin(a)
            verts.append((rx*math.copysign(abs(u)**.54,u),y,rz*math.copysign(abs(v)**.54,v)))
    faces=[tuple(reversed(range(count)))]
    for ring in range(len(rings)-1):
        for i in range(count):
            j=(i+1)%count
            faces.append((ring*count+i,ring*count+j,(ring+1)*count+j,(ring+1)*count+i))
    faces.append(tuple(range((len(rings)-1)*count,len(rings)*count)))
    tub.group("ChippedEnamel","SteelLight").add(verts,faces,port.ICE)
    tub.rod("DrainCollar","SteelDark",(.435,.162,.00),(.435,.174,.00),.052,12)
    for x in (-.50,.50):
        tub.box("BathSaddles","SteelRust",(x,.065,0),(.11,.14,.60),.025)
    # Cabinet carcass still contains the runners, a crooked open door and a
    # displaced drawer. Its empty front and interior shelves remain visible.
    cupboard=Part("RavineKitchenCabinet")
    cupboard.profile("FloorUnderCupboard","ConcreteWall",[(-1.23,-.76),(.74,-.81),(1.18,-.40),
                     (1.09,.51),(.58,.87),(-.24,.76),(-1.12,.93)],
                     lambda x,z,y:(x,y,z),-.43,-.018)
    for i,length in enumerate((1.48,1.76,1.84,1.73)):
        cupboard.box("KitchenFloorLaths","Timber",(-.035,-.008,-.45+i*.261),(length,.05,.25))
    cupboard.box("CabinetBack","Timber",(0,.49,-.23),(1.11,.91,.055))
    for x in (-.55,.55):
        cupboard.box("CabinetSides","Timber",(x,.49,0),(.068,.97,.52))
    for y in (.09,.51):
        cupboard.box("CabinetShelves","Timber",(0,y,.005),(1.08,.055,.49))
    cupboard.box("CounterLip","SteelLight",(0,.985,-.012),(1.24,.074,.63))
    cupboard.turned_box("HangingCupboardDoor","Timber",(-.40,.41,.48),(.52,.61,.052),-.91)
    cupboard.turned_box("PaintedDoorPanel","Steel",(-.383,.41,.497),(.39,.47,.026),-.91)
    cupboard.box("DrawerBottom","Timber",(.26,.68,.28),(.49,.047,.51))
    for x in (.027,.489):
        cupboard.box("DrawerSides","Timber",(x,.74,.27),(.038,.14,.51))
    cupboard.box("DrawerFront","Timber",(.26,.777,.540),(.55,.22,.055))
    cupboard.rod("DrawerPull","SteelLight",(.15,.79,.587),(.38,.79,.587),.018)
    for x in (.15,.38):
        cupboard.rod("DrawerPull","SteelLight",(x,.79,.57),(x,.79,.59),.019)
    door=Part("RavineApartmentDoor")
    door.profile("BrokenDoorLeaf","Timber",[(-.43,-1.01),(.42,-1.01),(.42,.93),
                 (.23,1.03),(.11,.98),(-.05,1.025),(-.43,1.03)],lambda x,z,y:(x,y,z),-.04,.025)
    for z,length in [(-.52,.65),(.42,.84)]:
        for x in (-.278,.278):
            door.box("PanelMouldings","Timber",(x,.041,z),(.048,.045,length))
        for end in (-1,1):
            door.box("PanelMouldings","Timber",(0,.043,z+end*length*.5),(.59,.047,.048))
    door.box("LatchPlate","SteelDark",(.32,.037,.03),(.088,.022,.18))
    door.rod("DoorHandle","SteelLight",(.32,.05,.058),(.32,.105,.058),.026)
    door.rod("DoorHandle","SteelLight",(.32,.105,.058),(.18,.105,.058),.026)
    return tub,cupboard,door


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--model-dir",type=Path,default=OUT)
    parser.add_argument("--validate-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])

    def build():
        port.base.reset()
        material=port.base.material("RavineFabric")
        shell, solids, edges=house()
        roots=[part.finish(material) for part in (shell,shore(),*debris(),*apartment_fittings())]
        port.validate_surfaces(roots)
        models=[port.describe(root) for root in roots]
        if models[0]["bounds_max"][1] < 8.9 or sum(m["triangles"] for m in models)>27000:
            raise ValueError("Ravine house envelope or triangle budget")
        return roots,dict(design="ridge_ravine_v2", outline=points(FOOTPRINT), solids=solids,
                          edges=edges, storey=STOREY, stairWell=dict(x=WELL_LEFT,y=WELL_BACK,
                          width=RIGHT-WELL_LEFT,height=WELL_FRONT-WELL_BACK), models=models)

    roots,data=build()
    if args.validate_only:
        if json.loads((args.model_dir/MANIFEST).read_text())!=data:
            raise ValueError("Stale ravine survey")
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        for root in roots: port.base.export(root,args.model_dir/(root.name+".fbx"))
        (args.model_dir/MANIFEST).write_text(json.dumps(data,indent=2)+"\n")
    if build()[1]!=data: raise ValueError("Ravine fabric changed between builds")
    print("RIDGE RAVINE: reproducible shell, open plans, shore and matching fallen fabric",flush=True)


if __name__=="__main__":
    main()
