"""A wall-mounted board, knotted net, chalk stripe and one child-sized rubber ball."""
import hashlib
import json
import math
from pathlib import Path
import runpy
import sys

import bpy

TOOLS = Path(__file__).resolve().parent
kit = runpy.run_path(str(TOOLS / "build-city-playground-3d-model.py"))
misc, role = kit["misc"], kit["role"]
OUT = TOOLS.parent / "Assets/Resources/City/Playground/RidgeBasketball.fbx"
RIM = .245
STANDOFF = .68
BALL = .118


def ring_path(radius, height, depth, count):
    return [(radius*math.cos(i*math.tau/count), height, depth+radius*math.sin(i*math.tau/count))
            for i in range(count)]


def loop(points, thickness, sides=7):
    return [misc.recipe_tube(a, b, thickness, sides=sides)
            for a, b in zip(points, points[1:]+points[:1])]


def parts():
    box, tube = misc.recipe_box, misc.recipe_tube
    def part(name, wood, *pieces):
        for shape in pieces:
            if misc.signed_volume(shape) <= 0:
                raise ValueError("Inverted basketball geometry: " + name)
        material = "Masonry_Timber" if wood else "Residential_PaintedMetal"
        return role(misc.make_part("Basketball", 0, material, *pieces), name, 1.4 if wood else 1.2, wood)

    board = [box(dx, -.10+i*.246, -.065, length, .238, .069, chamfer=.012)
             for i,(dx,length) in enumerate(((0,1.08),(.012,1.106),(-.009,1.084)))]
    # Backboard has two small wall battens, a welded saddle and visible fixing heads.
    board += [box(x,.145,-.024,.075,.68,.045,chamfer=.007) for x in (-.40,.40)]
    rim = loop(ring_path(RIM,0,-STANDOFF,40), .020)
    mounts = [box(0,.086,-.111,.26,.24,.027,chamfer=.008)]
    for x in (-.09,.09):
        mounts += [tube((x,0,-.105),(x,0,-.466),.021),
                   tube((x,.17,-.11),(x,.012,-.437),.015)]
    bolts = [tube((x,h,-.091),(x,h,-.112),.015,sides=6)
             for x in (-.40,.40) for h in (-.10,.146,.392)]
    # Roll the complete rigid wall assembly through half a turn around its
    # wall-normal mounting axis. The standoff still projects into the yard.
    def turn_over(pieces):
        return [([(-x, depth, -height) for x, depth, height in vertices], faces)
                for vertices, faces in pieces]
    board, rim, bolts = turn_over(board), turn_over(rim), turn_over(mounts + bolts)
    net = []
    nodes = 10
    for row in range(3):
        def knot(i, level):
            angle = math.tau*(i+.5*(level%2))/nodes
            radius = (.24,.207,.17,.147)[level]
            return (radius*math.cos(angle), -.108*level, radius*math.sin(angle))
        for i in range(nodes):
            for j in (i,i+(1 if row%2 else -1)):
                net.append(tube(knot(i,row),knot(j,row+1),.0052,sides=5))
    profile = []
    for j in range(15):
        angle = .016 + (math.pi-.032)*j/14
        profile.append((-BALL*math.cos(angle),0,0,BALL*math.sin(angle),BALL*math.sin(angle),0))
    skin = misc.ring_solid_z(tuple(profile),32)
    seams = []
    for turn in (0,math.pi/2):
        points = []
        for i in range(48):
            a = math.tau*i/48
            points.append((BALL*math.sin(a)*math.cos(turn), BALL*math.cos(a), BALL*math.sin(a)*math.sin(turn)))
        seams += loop(points,.0024,5)
    seams += loop(ring_path(BALL,0,0,48),.0024,5)
    chalk = [box(x,0,z,w,.002,.035,chamfer=.0006) for x,z,w in
             ((-.46,0,.25),(-.208,.002,.223),(.12,-.001,.39),(.414,.004,.171))]
    return {"Board": [part("BoardTimber",True,*board),part("HoopSteel",False,*rim),part("BoltHeads",False,*bolts)],
            "Net": [part("CordKnots",False,*net)],
            "Ball": [part("BallRubber",False,skin), part("BallChannels",False,*seams)],
            "Stripe": [part("ChalkDust",False,*chalk)]}


def main():
    source, _ = misc.reset_scene()
    root = bpy.data.objects.new("BasketballGear",None)
    source.objects.link(root)
    manifest = {}
    for group, meshes in parts().items():
        pivot = bpy.data.objects.new(group,None)
        source.objects.link(pivot)
        pivot.parent = root
        spec = misc.AssemblySpec("Basketball",0,tuple(meshes))
        for mesh in meshes:
            misc.create_part_object(mesh,spec,pivot,source)
        manifest[group] = {"bounds_blender_m": misc.combined_bounds(meshes),
                           "sha256": hashlib.sha256(repr([(m.geometry,m.authored_uvs) for m in meshes]).encode()).hexdigest()}
    data = json.dumps(manifest,indent=2,sort_keys=True)+"\n"
    if "--validate-only" in sys.argv:
        assert OUT.with_suffix(".json").read_text() == data
    else:
        # Keep nested pivots intact through FBX axis conversion.
        export = runpy.run_path(str(TOOLS / "build-city-fair-3d-model.py"))["export"]
        export(root, OUT)
        OUT.with_suffix(".json").write_text(data)
    print("Ridge basketball geometry and metre bounds verified.")


if __name__ == "__main__":
    main()
