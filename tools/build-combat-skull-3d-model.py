"""Passive anatomical skulls partitioned by the existing sixteen head sectors.

The original head fracture owns structural removal. These closed, source-bound
bone pieces never supply another head, face, animation rig or damage authority.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("combat_body_art",Path(__file__).with_name("build-combat-body-3d-model.py"))
body=importlib.util.module_from_spec(spec);spec.loader.exec_module(body)


def blender(point):
    return Vector((point[0],point[2],point[1]))


def build(kind,out,publish=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source_path=ROOT/body.SOURCES[kind]
    bpy.ops.import_scene.fbx(filepath=str(source_path),use_anim=False)
    originals=[o for o in bpy.context.scene.objects if o.type=="MESH"]
    source=next(o for o in originals if o.name=="GEO_Head")
    arm=next(o for o in bpy.context.scene.objects if o.type=="ARMATURE")
    head_contract=json.loads((ROOT/"Assets/Resources/CombatGore/CombatGore3D.json").read_text(encoding="utf-8"))
    head=head_contract["models"][kind]
    centre=blender(head["head_center_unity_m"])
    low=blender(head["head_bounds_unity_m"]["min"]);high=blender(head["head_bounds_unity_m"]["max"])
    half=(high-low)*.5;band=head["band_height_unity_m"]
    pieces=[body.Anatomy() for _ in range(16)]

    def sector(point):
        angle=math.atan2(point.y-centre.y,point.x-centre.x)%math.tau
        return min(7,int(angle*8/math.tau))+(8 if point.z>=band else 0)

    def add(art,owner=None):
        if not art.vertices:return
        point=sum(art.vertices,Vector())/len(art.vertices)
        pieces[sector(point) if owner is None else owner].add(art.vertices,art.faces)

    def tube(points,radius,owner=None,sides=8):
        art=body.Anatomy();art.tube(points,radius,sides);add(art,owner)

    dome=centre+Vector((0,0,half.z*.215))
    radii=Vector((half.x*.88,half.y*.88,half.z*.74))
    brow=centre.z+half.z*.23
    heights=sorted(set([dome.z+radii.z*t for t in (-.84,-.64,-.40,-.16,.12,.36,.58,.77,.91,.985)]
                       +[band,brow]))
    for angle_index in range(40):
        first=math.tau*angle_index/40;last=math.tau*(angle_index+1)/40
        angle=(first+last)*.5
        for z0,z1 in zip(heights,heights[1:]):
            # A real anterior opening leaves the orbital and nasal cavities
            # readable; the cranium does not put a smooth wall behind the eyes.
            if 5*math.pi/4<angle<7*math.pi/4 and (z0+z1)*.5<brow:continue
            outer=[]
            for theta,z in ((first,z0),(last,z0),(last,z1),(first,z1)):
                width=math.sqrt(max(.00001,1-((z-dome.z)/radii.z)**2))
                outer.append(Vector((dome.x+radii.x*width*math.cos(theta),
                                     dome.y+radii.y*width*math.sin(theta),z)))
            inner=[dome+(point-dome)*.90 for point in outer]
            art=body.Anatomy();art.add(outer+inner,[(0,1,2,3),(7,6,5,4),
                (0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)])
            add(art,angle_index//5+(8 if (z0+z1)*.5>=band else 0))
    cap=body.Anatomy();cap.ball(dome+Vector((0,0,radii.z*.965)),
                              (half.x*.16,half.y*.16,radii.z*.035),12,5);add(cap)
    front=centre.y-half.y*.865
    eye_z=centre.z+half.z*.07
    for side in (-1,1):
        eye_x=centre.x+side*half.x*.36
        orbit=[Vector((eye_x+half.x*.27*math.cos(math.tau*j/16),front,
                       eye_z+half.z*.17*math.sin(math.tau*j/16))) for j in range(17)]
        for a,b in zip(orbit,orbit[1:]):tube([a,b],half.x*.051)
        # Zygomatic arch connects the orbit's outer rim to the side of the skull.
        cheek=[Vector((centre.x+side*half.x*x,centre.y+half.y*y,centre.z+half.z*z))
               for x,y,z in ((.63,-.78,-.09),(.78,-.45,-.18),(.77,.08,-.06))]
        for a,b in zip(cheek,cheek[1:]):tube([a,b],half.x*.061)
        bridge=[Vector((centre.x+side*half.x*x,front+half.y*y,centre.z+half.z*z))
                for x,y,z in ((.065,.0,.22),(.11,-.025,-.12),(.045,-.035,-.27))]
        for a,b in zip(bridge,bridge[1:]):tube([a,b],half.x*.041)
    # Upper jaw and separate mandible, open behind the teeth and below the nose.
    maxilla=[Vector((centre.x+half.x*.61*math.cos(t),centre.y-half.y*.77*math.sin(t),
                     centre.z-half.z*.38)) for t in [math.pi*j/12 for j in range(13)]]
    for a,b in zip(maxilla,maxilla[1:]):tube([a,b],half.x*.068)
    jaw=[]
    for j in range(17):
        t=math.pi*j/16
        jaw.append(Vector((centre.x+half.x*.65*math.cos(t),centre.y-half.y*.79*math.sin(t),
                           centre.z-half.z*(.43+.34*math.sin(t)))))
    for a,b in zip(jaw,jaw[1:]):tube([a,b],half.x*.071)
    for side in (-1,1):
        tube([jaw[0 if side==1 else -1],
              Vector((centre.x+side*half.x*.70,centre.y+half.y*.045,centre.z-half.z*.11))],half.x*.067)
    for row in (0,1):
        for tooth in range(12):
            theta=math.pi*(tooth+.5)/12
            point=Vector((centre.x+half.x*.54*math.cos(theta),centre.y-half.y*.78*math.sin(theta),
                          centre.z-half.z*(.435 if row==0 else .615)))
            art=body.Anatomy();art.ball(point,(half.x*.042,half.y*.043,half.z*.043),6,4);add(art)
    outputs=[]
    for index,art in enumerate(pieces):
        if not art.vertices:raise RuntimeError("Skull is missing retained sector "+str(index))
        for point in art.vertices:
            if any(point[a]<low[a]-.001 or point[a]>high[a]+.001 for a in range(3)):
                raise RuntimeError("Skull escaped its production head envelope "+kind+" "+str(body.unity(point)))
        obj=body.simple_mesh(f"SkullSector{index}__GEO_Head",source,art.vertices,art.faces,"head")
        outputs.append(obj)
    measurements=[body.measure(obj,True) for obj in outputs]
    for original in originals:bpy.data.objects.remove(original,do_unlink=True)
    arm.animation_data_clear()
    for bone in arm.pose.bones:bone.matrix_basis.identity()
    if publish:
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(filepath=str(out/("Skull"+kind+".fbx")),use_selection=True,
            object_types={"EMPTY","ARMATURE","MESH"},axis_forward="-Z",axis_up="Y",add_leaf_bones=False,
            bake_anim=False,use_armature_deform_only=False,use_mesh_modifiers=False,mesh_smooth_type="FACE")
    print("SKULL ANATOMY",kind,"16 closed source-bound sectors",flush=True)
    return {"source":body.SOURCES[kind],"source_sha256":body.digest(source_path.read_bytes()),
            "head_contract_sha256":body.digest((ROOT/"Assets/Resources/CombatGore/CombatGore3D.json").read_bytes()),
            "head_center_unity_m":head["head_center_unity_m"],"band_height_unity_m":band,
            "head_bounds_unity_m":head["head_bounds_unity_m"],"meshes":measurements}


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--output-dir",type=Path,default=ROOT/"Assets/Resources/CombatGore")
    parser.add_argument("--validate-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    out=args.output_dir;out.mkdir(parents=True,exist_ok=True)
    old=json.loads((out/"CombatSkull3D.json").read_text(encoding="utf-8")) if args.validate_only else None
    if old:
        for file,expected in old["files"].items():
            if body.digest((out/file).read_bytes())!=expected:raise RuntimeError("Published skull changed "+file)
    models={kind:build(kind,out,not args.validate_only) for kind in body.SOURCES}
    rebuilt={kind:build(kind,out) for kind in body.SOURCES}
    if models!=rebuilt:raise RuntimeError("Skull differs across independent source rebuilds")
    manifest={"version":1,"generator":"tools/build-combat-skull-3d-model.py","test_only":True,
              "sector_count":16,"sector_owner":"CombatHeadDestruction existing retained/removed mask",
              "sector_angle_axes":"atan2(Unity Z,Unity X), matching CombatGore3D",
              "material":"CombatGore/BoneSurface","models":models,
              "files":{name:body.digest((out/name).read_bytes()) for name in ("SkullHero.fbx","SkullNpc.fbx")}}
    if args.validate_only:
        if manifest!=old:raise RuntimeError("Skull geometry/source/skin/sector contract changed")
        print("COMBAT SKULL CLOSED ANATOMY, PRODUCTION ENVELOPE, SOURCE SKIN, SECTOR ALIGNMENT AND DETERMINISM OK",flush=True)
    else:(out/"CombatSkull3D.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")


if __name__=="__main__":main()
