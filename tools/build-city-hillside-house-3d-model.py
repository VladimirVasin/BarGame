#!/usr/bin/env python3
"""Fixed-metre west stair bay and east wash balcony fittings for the upper quarter."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("house_court", ROOT / "tools/build-city-residential-courtyard-3d-model.py")
kit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kit)
port = kit.port
port.SURFACE_TILES.update({"LandingGlass": 1.0, "Brick": .9})
OUT = ROOT / "Assets/Resources/City/Hillside"
MANIFEST = "CityHillsideHouse3D.json"


def stair_bay(mat):
    # Two full storeys of glazed landings above the open domestic vestibule.
    # Close to the glass the stair actually turns into the wall on each floor.
    root = port.empty("GlazedStairBay")
    brick, concrete = kit.geometry("Brick"), kit.geometry("Concrete")
    frame, glass, roof = kit.geometry("SteelDark"), kit.geometry("LandingGlass"), kit.geometry("Roof")
    first_landing = 2.64
    for side in (-1, 1):
        kit.box(brick, (side*.98, 1.32, .53), (.21, 2.64, 1.06), port.RUST)
        for row in range(7):
            kit.box(concrete, (side*.98, .19 + .34*row, 1.06), (.20, .024, .022), port.EDGE, .002)
        kit.box(frame, (side*1.02, 5.66, 1.02), (.065, 6.08, .075), port.METAL)
    for level in range(2):
        bottom = first_landing + level*3.0
        # Each flight has two landings and two short opposing stair runs.
        for i in range(8):
            kit.box(concrete, (-.45 + i*.124, bottom+.12+i*.195, .52), (.135, .20, .78), port.CONCRETE, .007)
            kit.box(concrete, (.47 - i*.124, bottom+1.62+i*.195, .20), (.135, .20, .26), port.CONCRETE, .007)
        kit.box(concrete, (0, bottom+.035, .49), (2.12, .18, 1.04), port.CONCRETE)
        for side in (-1, 1):
            kit.box(brick, (side*.995, bottom+.59, .49), (.15, 1.10, .98), port.RUST)
            kit.box(glass, (side*1.015, bottom+1.98, .57), (.018, 1.66, .76), port.GLASS, .002)
        kit.box(brick, (0, bottom+.54, 1.014), (1.92, .91, .10), port.RUST)
        for x in (-.70, 0, .70):
            kit.box(glass, (x, bottom+1.97, 1.032), (.654, 1.78, .018), port.GLASS, .002)
        for x in (-.35, .35):
            kit.box(frame, (x, bottom+1.97, 1.065), (.052, 1.85, .055), port.METAL)
        for h in (1.06, 2.45, 2.9):
            kit.box(frame, (0, bottom+h, 1.066), (2.04, .055, .055), port.METAL)
        frame.rod((-.57,bottom+1.11,.78),(.50,bottom+2.51,.78),.027,port.METAL,8)
    kit.box(roof, (0, 8.69, .56), (2.24, .16, 1.20), port.DARK, .014)
    # A downpipe falls from the edge of the rain gutter to a real shoe.
    frame.rod((-1.10,8.64,1.12), (1.12,8.64,1.12), .045, port.METAL, 8)
    frame.rod((1.12,8.64,1.12), (1.12,8.48,.14), .042, port.METAL, 8)
    frame.rod((1.12,8.48,.14), (1.12,.23,.14), .042, port.METAL, 8)
    frame.rod((1.12,.23,.14), (1.12,.12,.37), .042, port.METAL, 8)
    for y in (.8, 3.7, 6.6):
        kit.box(frame,(1.12,y,.075),(.15,.06,.14),port.METAL)
    for name, geom in (("StairBrickPiers",brick),("LandingsAndSteps",concrete),("MullionsAndRainwater",frame),
                       ("LandingGlazing",glass),("FoldedBayCap",roof)):
        kit.emit(geom,name,root,mat)
    port.empty("ANCHOR_Pipe",root,(1.12,1.32,.14))
    # The merged concrete also includes low pier trim. Its bounds cannot
    # identify this soffit; export the actual first slab's underside instead.
    port.empty("ANCHOR_EntranceLampCeiling", root, (0, first_landing + .035 - .09, .55))
    return root


def wash_balcony(mat):
    root = port.empty("BalconyAirer")
    steel, timber = kit.geometry("SteelLight"), kit.geometry("Timber")
    # Free-standing folding rack occupies the right half of the 2.5 m deck,
    # leaving the tall apartment door and balcony rail clear.
    for x in (-.56,.56):
        steel.rod((x,0,-.23),(x,.93,.20),.019,port.METAL,8)
        steel.rod((x,0,.23),(x,.93,-.20),.019,port.METAL,8)
        steel.rod((x,.94,-.23),(x,.94,.23),.016,port.METAL,8)
    for z in (-.19,-.095,0,.095,.19):
        steel.rod((-.58,.94,z),(.58,.94,z),.010,port.METAL,6)
    for x in (-.38,.38):
        kit.box(timber,(x,.955,.19),(.029,.062,.032),port.WOOD,.003)
    kit.emit(steel,"FoldingDryingRack",root,mat)
    kit.emit(timber,"TwoPegs",root,mat)
    port.empty("ANCHOR_Linen",root,(0,.94,.19))
    return root


def gallery(mat, model_name, width, floors, passage=False):
    """Two loggias enclose the existing paired balcony doors and their real decks.
    The projecting edges carry no foot traffic. Every ground post stays inside
    the old balcony footprint and the through-house keeps its full headroom."""
    root = port.empty(model_name)
    slab, plaster = kit.geometry("Concrete"), kit.geometry("Plaster")
    wood, dark, light = kit.geometry("Timber"), kit.geometry("SteelDark"), kit.geometry("SteelLight")
    glazing, cap = kit.geometry("LandingGlass"), kit.geometry("Roof")
    for side in (-1, 1):
        x = side*width*.28
        # Different depths distinguish the open washing gallery from the
        # enclosed glazed loggia even when the camera sees both face-on.
        depth = 1.90 if side < 0 else 1.57
        span = 2.73 if side < 0 else 2.60
        for edge in (-1, 1):
            col = x+edge*(span*.5-.12)
            if passage and side == -1 and edge == 1:
                # Move the leg past the pier's separate residential doorway.
                col = -2.54
            port.empty("ANCHOR_Pier_"+str(side)+"_"+str(edge),root,(col,1.38,.92))
            kit.box(plaster,(col,1.38,.92),(.20,2.76,.26),port.CABIN,.018)
            kit.box(slab,(col,.12,.92),(.33,.24,.39),port.EDGE,.012)
            # Corbeled seats, visible from below as the player climbs.
            kit.box(slab,(col,2.54,1.10),(.28,.25,.66),port.EDGE,.013)
            kit.box(slab,(col,2.67,1.28),(.31,.15,.91),port.CONCRETE,.012)
        for storey in range(1, floors+1):
            y = storey*2.8
            # Keep the original 1.2 m deck surface. The cornice begins
            # beyond its edge with an actual drip under the sill.
            kit.box(slab,(x,y-.12,(1.20+depth)/2),(span,.24,depth-1.2),port.CONCRETE,.022)
            kit.box(plaster,(x,y-.18,depth+.019),(span+.025,.125,.040),port.CABIN,.008)
            for edge in (-1,1):
                col = x+edge*(span*.5-.085)
                kit.box(light,(col,y+1.39,1.105),(.115,2.78,.12),port.CABIN,.009)
                kit.box(dark,(col,y+1.39,.115),(.072,2.78,.085),port.METAL,.006)
                kit.box(wood,(col,y+.52,.63),(.080,.94,.92),port.WOOD,.012)
            # Back-painted masonry and resting hands touch the existing
            # perimeter rail. The posts and glass have distinct depth.
            if (storey+side)%3 != 0:
                kit.box(plaster,(x,y+.48,1.225),(span-.20,.80,.10),port.CABIN,.015)
                for off in (-.33,.33):
                    kit.box(light,(x+off,y+.47,1.29),(.04,.76,.035),port.METAL,.004)
            else:
                for n in range(7):
                    kit.box(wood,(x+(n-3)*.30,y+.49,1.23),(.21,.81,.07),port.WOOD,.009)
            kit.box(dark,(x,y+.955,1.24),(span,.08,.15),port.METAL,.009)
            if side < 0 and storey in (1,3):
                # Retrofitted right half-panes overlap opaque sections only
                # in projection. The left sash is open to the weather.
                for n in range(2):
                    xx=x+.20+n*.53
                    kit.box(glazing,(xx,y+1.78,1.06),(.49,1.48,.015),port.GLASS,.002)
                    kit.box(wood,(xx-.267,y+1.77,1.085),(.048,1.54,.054),port.WOOD,.004)
                for yy in (1.015,2.55):
                    kit.box(wood,(x+.46,y+yy,1.085),(1.12,.06,.055),port.WOOD,.004)
            if side > 0 and storey == 2:
                # Fixed timber storage/shutters at one side give a deep
                # silhouette without covering either tall balcony door.
                for k in range(6):
                    kit.box(wood,(x+1.19,y+1.16+k*.14,.65),(.065,.085,.80),port.WOOD,.006)
        roof_y = floors*2.8+2.64
        kit.box(cap,(x,roof_y,depth*.48),(span+.25,.18,depth*1.12),port.DARK,.025)
        kit.box(light,(x,roof_y-.04,depth+.06),(span+.29,.19,.095),port.CABIN,.009)
        for edge in (-1,1):
            col=x+edge*(span*.5-.12)
            dark.rod((col,roof_y-.58,1.08),(col,roof_y-.09,depth-.04),.034,port.METAL,6)
    for name, geom in (("CantileverSlabs",slab),("PiersAndBalustrades",plaster),("RepairedGalleryPanels",wood),
                       ("TieBars",dark),("CreamWindowJambs",light),("VerandaSashes",glazing),("DeepCanopies",cap)):
        kit.emit(geom,name,root,mat)
    return root


def west_roof(mat):
    # A broad offset clerestory and its half-height return step across the
    # existing roof. The tallest roof remains the catalogue stairhead.
    root=port.empty("SteppedClerestory")
    brick, frame, panes, roof = kit.geometry("Brick"), kit.geometry("SteelLight"), kit.geometry("LandingGlass"), kit.geometry("Roof")
    for x, w, h, d in ((1.05,4.42,.88,3.25),(-2.25,2.10,.42,2.55)):
        kit.box(brick,(x,h*.5,0),(w,h,d),port.RUST,.018)
        kit.box(roof,(x,h+.06,0),(w+.19,.12,d+.19),port.DARK,.015)
        if h < .5:
            continue
        for i in range(5):
            xx=x+(i-2)*.71
            kit.box(frame,(xx,.53,d*.5+.032),(.66,.45,.072),port.CABIN,.008)
            kit.box(panes,(xx,.53,d*.5+.080),(.57,.36,.018),port.GLASS,.002)
    for name,geom in (("RoofStepBrickwork",brick),("ClerestoryFrames",frame),("ClerestoryGlass",panes),("SteppedCap",roof)):
        kit.emit(geom,name,root,mat)
    return root


def build():
    material = port.base.material("HillsideHouseAuthoring")
    roots = [stair_bay(material), wash_balcony(material), west_roof(material)]
    for variant, width, floors in ((0,11.602,3),(2,15.0,3),(3,15.5,4),(4,10.0,2)):
        roots.append(gallery(material, "BalconyGallery"+str(variant), width, floors, variant == 3))
    bpy.context.view_layer.update()
    models = [port.describe(model) for model in roots]
    port.validate_surfaces(roots)
    if not (8.7 < models[0]["bounds_max"][1] < 8.8 and .96 < models[1]["bounds_max"][1] < 1.0):
        raise ValueError("Architectural module lost its metre scale")
    if models[0]["triangles"] > 6000 or models[1]["triangles"] > 600:
        raise ValueError("Architectural module exceeds its geometry budget")
    for gallery_model in models[3:]:
        if not (gallery_model["triangles"] < 11000 and 1.9 < gallery_model["bounds_max"][2] < 2.1):
            raise ValueError("Gallery volume or budget changed")
    return roots, {"design_id":"upper_quarter_house_fittings_v1","models":models}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=OUT)
    parser.add_argument("--model", action="append", help="Export only a named model; the full manifest stays validated.")
    parser.add_argument("--validate-only",action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    port.base.reset()
    roots, data = build()
    if args.model and set(args.model) - {root.name for root in roots}:
        raise ValueError("Unknown hillside model selection")
    if args.validate_only:
        if json.loads((args.model_dir/MANIFEST).read_text()) != data:
            raise ValueError("Published hillside house manifest differs")
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        for root in roots:
            if args.model and root.name not in args.model: continue
            port.base.export(root,args.model_dir/(root.name+".fbx"))
        (args.model_dir/MANIFEST).write_text(json.dumps(data,indent=2)+"\n")
    port.base.reset()
    if build()[1] != data:
        raise ValueError("Hillside house geometry or anchors changed between builds")
    print("CITY HILLSIDE HOUSE: deterministic measured bay and balcony airer",flush=True)


if __name__ == "__main__":
    main()
