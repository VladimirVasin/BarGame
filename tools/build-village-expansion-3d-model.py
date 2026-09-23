#!/usr/bin/env python3
"""Passive, fixed-metre remains of the village ski base and the old downhill road."""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp

VERSION = "1.0.0"
DESIGN = "village_forest_ski_base_old_road_v1"
COLORS = {"Timber": (.29,.255,.205,1), "Masonry": (.49,.485,.445,1),
          "LayeredStone": (.32,.345,.34,1), "RustedIron": (.30,.255,.21,1),
          "WindSnow": (.83,.85,.84,1), "Asphalt": (.24,.255,.255,1),
          "Glass": (.40,.44,.43,.16)}

def box(p,s,c=.01): return bp.u_box(p,s,c)
def merge(parts): return kit.merge_all(parts)
def at(g,p): return kit.translated(g,p)
def rotate(g,e): return bp.u_rotated(g,e)
def u(g): return bp.to_source(g)

def roof(width,depth,wall,rise,thickness):
    """Two thick roof slopes: closed solids, with the ridge along X."""
    half=depth*.5; length=math.hypot(half,rise)
    return merge([at(rotate(box((0,0,0),(width,thickness,length),.015),
                          (side*math.degrees(math.atan2(rise,half)),0,0)),
                     (0,wall+rise*.5,side*half*.5)) for side in (-1,1)])

def gable(depth,rise,thickness):
    # Shared prism's section lies in source XZ and extrudes source Y.
    raw=kit.prism([(-depth*.5,0),(depth*.5,0),(0,rise)],thickness)
    verts=[(y,z,x) for x,y,z in raw[0]]
    return verts,raw[1]

def beam_between(a,b,width=.05):
    direction=Vector(b)-Vector(a);mid=(Vector(a)+Vector(b))*.5
    geometry=u(kit.beam(direction.length,width,width))
    rotation=Vector((0,0,1)).rotation_difference(direction.normalized())
    return [tuple(rotation @ Vector(v)+mid) for v in geometry[0]],geometry[1]

def create_parts():
    parts=[]
    def add(kind,name,g,surface,solid=True,tint=None):
        # Validate EVERY component before merging; a positive total can hide an inverted piece.
        vol=bp.signed_volume(g)
        assert vol>1e-9,(kind,name,"inward or degenerate solid",vol)
        parts.append(dict(kind=kind,name=name,mesh="GEO_Expansion_"+kind+"_"+name,
                          surface=surface,solid=solid,tint=tint or COLORS[surface],geometry=g))
    lodge="SkiLodge"
    add(lodge,"Floor",box((0,-.07,0),(17.36,.18,11.36),.018),"Timber")
    add(lodge,"Foundation",box((0,-.34,0),(18,.36,12),.045),"LayeredStone")
    front=kit.wall_run(18,3.6,.32,[kit.Opening(-5.4,2.3,2.85,1.15),
        kit.Opening(0,2.6,2.75),kit.Opening(5.4,2.3,2.85,1.15)],.012)
    rear=kit.wall_run(18,3.6,.32,[kit.Opening(-5.4,2.3,2.85,1.15),
        kit.Opening(0,2.3,2.85,1.15),kit.Opening(5.4,2.3,2.85,1.15)],.012)
    sides=kit.wall_run(11.36,3.6,.32,[kit.Opening(-2.7,1.7,2.85,1.15),
        kit.Opening(2.7,1.7,2.85,1.15)],.012)
    add(lodge,"FrontWall",at(u(front),(0,0,-5.84)),"Masonry")
    add(lodge,"BackWall",at(u(rear),(0,0,5.84)),"Masonry")
    for sign in (-1,1):
        add(lodge,"SideWall"+str(sign),at(rotate(u(sides),(0,90,0)),(sign*8.84,0,0)),"Masonry")
        add(lodge,"Gable"+str(sign),at(gable(12,1.4,.32),(sign*8.84,3.6,0)),"Timber")
    add(lodge,"Roof",roof(19.3,13.3,3.48,1.55,.24),"Timber")
    add(lodge,"RoofSnow",roof(19.22,13.22,3.66,1.55,.17),"WindSnow",False)
    # Continuous structural timbers and porch lintel give the long low mass its working character.
    beams=[box((0,3.41,z),(18.3,.24,.22),.025) for z in (-5.9,5.9)]
    for x in (-8.78,-2.95,2.95,8.78):
        beams.extend([box((x,1.78,-6.018),(.19,3.56,.11)),box((x,1.78,6.018),(.19,3.56,.11)),
                      box((x,3.31,0),(.20,.22,11.6))])
    add(lodge,"WallAndCeilingTimbers",merge(beams),"Timber")
    frames=[];glass=[]
    def window(center,width,side=False):
        h=1.7
        frame=merge([box((x,0,0),(.095,h+.19,.13),.006) for x in (-width*.5-.045,width*.5+.045)]+
                    [box((0,y,0),(width,.095,.13),.006) for y in (-h*.5-.045,h*.5+.045)]+
                    [box((0,0,0),(.065,h,.08),.004),box((0,-h*.5-.12,0),(width+.32,.12,.48),.01)])
        pane=box((0,0,0),(width,h,.009),.001)
        if side:frame=rotate(frame,(0,90,0));pane=rotate(pane,(0,90,0))
        frames.append(at(frame,center));glass.append(at(pane,center))
    for z in (-5.84,5.84):
        for x in (-5.4,5.4) if z<0 else (-5.4,0,5.4):window((x,2.0,z),2.3)
    for x in (-8.84,8.84):
        for z in (-2.7,2.7):window((x,2.0,z),1.7,True)
    add(lodge,"WindowFrames",merge(frames),"Timber")
    add(lodge,"WindowGlass",merge(glass),"Glass",False)
    # Doors lie open against the outside wall; no runtime hinge/action is implied.
    doors=[]
    for side in (-1,1):
        for plank in range(6):doors.append(box((side*(1.40+plank*.20),1.32,-6.09),(.188,2.64,.075),.008))
        for y in (.4,2.2):doors.append(box((side*1.9,y,-6.145),(1.18,.075,.035),.004))
    add(lodge,"OpenDoorLeaves",merge(doors),"Timber")
    for x in (-1.72,1.72):add(lodge,"Vestibule"+str(x),box((x,1.4,-4.8),(.16,2.8,2.1)),"Timber")
    # Furniture footprints are mirrored in AlpineVillageExpansionPlan's blockers.
    benches=[]
    for x,z,length in ((-6.4,-.8,6.4),(6.4,-2.2,3.5)):
        benches += [box((x,.46,z),(.65,.095,length)),box((x+(-.25 if x<0 else .25),.82,z),(.075,.6,length))]
        for end in (-1,1):
            benches += [box((x,.23,z+end*(length*.5-.3)),(.58,.46,.12)),
                        box((x,.24,z),(.12,.12,length-.35))]
    add(lodge,"Benches",merge(benches),"Timber")
    racks=[box((0,.20,4.75),(11.8,.14,.8)),box((0,1.65,4.75),(11.8,.14,.55))]
    for i in range(19):racks.append(box((-5.65+i*.625,.96,5.03),(.085,1.88,.085)))
    add(lodge,"EmptyRentalRacks",merge(racks),"Timber")
    # A handful of old skis makes the use legible without labels or trophies.
    skis=[]
    for i in range(5):
        x=-5.1+i*.40
        skis += [box((x,1.18,4.64),(.12,1.94,.045),.016),box((x,.84,4.59),(.16,.16,.07),.008)]
    add(lodge,"RemainingSkis",merge(skis),"RustedIron")
    counter=merge([box((4.7,1.0,1),(3.8,.12,.8),.018),box((4.7,.5,1.31),(3.6,.94,.11)),
                   box((2.89,.48,1),(.11,.96,.65)),box((6.51,.48,1),(.11,.96,.65))])
    add(lodge,"RentalCounter",counter,"Timber")
    # Missing boards, patched skirting and exposed fastenings are passive age, not a recent event.
    patches=[box((x,.35,-6.012),(.52,.24,.04),.004) for x in (-7.4,-3.4,3.0,7.5)]
    add(lodge,"OldPatches",merge(patches),"Timber",True,(.355,.30,.23,1))
    shed="ServiceShed"
    add(shed,"Foundation",box((0,-.15,0),(8,.3,6),.03),"LayeredStone")
    add(shed,"Walls",merge([box((0,1.55,z),(8,3.1,.24)) for z in (-2.88,2.88)]+
        [box((x,1.55,0),(.24,3.1,5.52)) for x in (-3.88,3.88)]),"Timber")
    add(shed,"Roof",roof(8.8,6.8,3.0,.85,.18),"RustedIron")
    add(shed,"Snow",roof(8.76,6.76,3.16,.85,.15),"WindSnow",False)
    add(shed,"Gables",merge([at(gable(6,.76,.24),(x,3.1,0)) for x in (-3.88,3.88)]),"Timber")
    add(shed,"Door",box((0,1.2,-3.03),(2.6,2.4,.11)),"Timber",True,(.235,.24,.215,1))
    for kind in ("LiftBase","LiftTop"):
        add(kind,"Footing",box((0,.2,0),(.7,.4,.7),.04),"LayeredStone")
        iron=[box((0,2.65,0),(.26,4.9,.30)),box((0,4.82,0),(3.8,.24,.3))]
        # A pair of braces, plus the old rope wheel under the cross-arm.
        iron += [beam_between((0,3.4,0),(s*1.65,4.7,0),.09) for s in (-1,1)]
        wheel=bp.u_cylinder((0,4.9,0),(2.6,.085,2.6),16)
        add(kind,"Iron",merge(iron),"RustedIron")
        add(kind,"Wheel",wheel,"RustedIron")
        if kind=="LiftBase":
            add(kind,"DriveHousing",box((0,.8,0),(.65,.85,.65),.07),"RustedIron")
    barrier="RoadBarrier"
    rocks=[]
    for i in range(12):
        x=-5.48+i*.995;h=.50+(i%3)*.08
        rocks.append(box((x,h*.5,0),(1.12,h,.45),.10))
    add(barrier,"StoneBlockage",merge(rocks),"LayeredStone")
    rails=[box((0,.93,.15),(12,.13,.11)),box((0,.62,.15),(12,.12,.10))]
    rails += [box((x,.53,.15),(.12,1.06,.13)) for x in (-5.5,-2.75,0,2.75,5.5)]
    add(barrier,"OldGuardrail",merge(rails),"RustedIron")
    add("RoadSurface","Asphalt",box((0,-.04,0),(1,.08,1),0),"Asphalt",False)
    # Scaled only along the road length; lateral jagged lip remains its authored 5.4m width.
    rubble=[box((x,-.4,z),(w,.8,d),.10) for x,z,w,d in
            ((-2.15,-.08,1.1,1.5),(-.95,-.35,1.3,1.8),(.4,-.05,1.5,1.4),(1.8,.20,1.3,1.8))]
    add("RoadBrokenLip","Foundation",merge(rubble),"LayeredStone",False)
    add("RoadBrokenLip","Asphalt",merge([box((x,.005,z),(w,.1,d),.015) for x,z,w,d in
            ((-2.15,-.08,1.1,1.5),(-.95,-.35,1.3,1.8),(.4,-.05,1.5,1.4),(1.8,.20,1.3,1.8))]),"Asphalt",False)
    return parts

def validate(parts):
    # The entry and main circulation must be open in actual authored solids.
    trees=[BVHTree.FromPolygons(*p["geometry"],all_triangles=False) for p in parts
           if p["kind"]=="SkiLodge" and p["solid"]]
    for x in (-1.1,0,1.1):
        for y in (.2,1.1,2.3):
            start=Vector((x,y,-7));end=Vector((x,y,3.7));direction=end-start
            assert not any(t.ray_cast(start,direction.normalized(),direction.length)[0] is not None for t in trees),"Blocked lodge doorway/circulation"
    first=json.dumps(parts,sort_keys=True,separators=(",",":"))
    assert first==json.dumps(create_parts(),sort_keys=True,separators=(",",":")),"Non-deterministic geometry"
    return hashlib.sha256(first.encode()).hexdigest()

def build(parts):
    bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
    root=bpy.data.objects.new("VillageExpansion3D",None);bpy.context.scene.collection.objects.link(root)
    objects=[];rows=[]
    for p in parts:
        g=bp.to_source(p["geometry"]);assert bp.signed_volume(g)>1e-9
        mesh=bpy.data.meshes.new(p["mesh"]);mesh.from_pydata(g[0],[],g[1]);mesh.update()
        uv=mesh.uv_layers.new(name="UVMap")
        for face in mesh.polygons:
            axes=sorted(range(3),key=lambda a:abs(face.normal[a]))[:2]
            for i in face.loop_indices:
                v=mesh.vertices[mesh.loops[i].vertex_index].co;uv.data[i].uv=(v[axes[0]],v[axes[1]])
        obj=bpy.data.objects.new(p["mesh"],mesh);bpy.context.scene.collection.objects.link(obj);obj.parent=root
        mat=bpy.data.materials.new(p["mesh"]+"_Review");mat.diffuse_color=p["tint"];mesh.materials.append(mat)
        objects.append(obj);lo,hi=kit.bounds(p["geometry"])
        row={k:v for k,v in p.items() if k!="geometry"};row.update(bounds_min=lo,bounds_max=hi,triangles=kit.triangle_count(g))
        rows.append(row)
    return objects,rows

def preview(path,objects,rows):
    for obj,row in zip(objects,rows):obj.hide_render=row["kind"]!="SkiLodge"
    scene=bpy.context.scene
    camera=bpy.data.objects.new("ReviewCamera",bpy.data.cameras.new("ReviewCamera"));scene.collection.objects.link(camera)
    camera.location=(25,-26,15);camera.rotation_euler=(Vector((0,0,2))-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.lens=43;scene.camera=camera;scene.render.engine="BLENDER_WORKBENCH"
    scene.display.shading.light="STUDIO";scene.display.shading.color_type="MATERIAL"
    scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True;scene.world.color=(.19,.21,.23)
    scene.render.resolution_x=1400;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG";scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/Village/Expansion")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/Village")
    parser.add_argument("--validate-only",action="store_true");parser.add_argument("--no-preview",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    parts=create_parts();signature=validate(parts);objects,rows=build(parts)
    data=dict(generator_version=VERSION,design_id=DESIGN,scale_mode="fixed_metres",uv_mode="projected_metres",
              build_signature=signature,mesh_count=len(rows),triangle_count=sum(p["triangles"] for p in rows),
              colliders=False,lights=False,cameras=False,animation_count=0,parts=rows)
    target=args.model_dir/"VillageExpansion3D.json"
    if args.validate_only:assert json.loads(target.read_text())==json.loads(json.dumps(data)),"Stale expansion manifest"
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(filepath=str(args.model_dir/"VillageExpansion3D.fbx"),use_selection=True,
            object_types={"EMPTY","MESH"},axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",
            bake_space_transform=True,add_leaf_bones=False,bake_anim=False,mesh_smooth_type="FACE")
        target.write_text(json.dumps(data,indent=2)+"\n",encoding="utf-8")
        bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"VillageExpansion3D.blend"))
        if not args.no_preview:preview(args.source_dir/"VillageExpansion3D.png",objects,rows)
    print("VILLAGE EXPANSION VALIDATION OK: closed outward solids, true 2.6m entrance, clear main aisle; "+signature)

if __name__=="__main__":main()
