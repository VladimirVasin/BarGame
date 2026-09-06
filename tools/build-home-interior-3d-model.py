#!/usr/bin/env python3
"""Deterministic Blender-authored Home parts and seven calendar-day dressings.

Run Blender --background --factory-startup --python-exit-code 1 --python this-file -- [--validate-only].
Bindings retain the runtime's semantic names, origins, colliders and articulated
hierarchy. Fixed parts use metres. Explicitly enumerated repeated hardware uses
measured, profiled library meshes: its fit is deliberately marked parametric.
No object, noise, texture, text, light or actor is randomly generated at runtime.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Vector, Euler

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp

VERSION = "1.2.0"
SOURCE = ROOT / "ArtSource/Home/Interior"
MODEL = ROOT / "Assets/Home/Interior/Models/HomeInterior3D.fbx"
PITCH = {"Plain": 1, "Wallpaper": 1.9, "CeilingPlaster": 2.8,
         "PlankFloor": 1.6, "DarkWood": 1.1, "WornLaminate": 1.2,
         "Upholstery": .85, "BedLinen": .9, "BathroomTile": 1.2,
         "Enamel": 1, "PaintedMetal": 1, "Concrete": 2.4, "Rug": 1.5}
COLORS = {"Plain": (.30,.27,.21,1), "Wallpaper": (.34,.30,.23,1),
          "CeilingPlaster": (.26,.24,.20,1), "PlankFloor": (.22,.15,.095,1),
          "DarkWood": (.20,.105,.055,1), "WornLaminate": (.30,.27,.18,1),
          "Upholstery": (.20,.28,.24,1), "BedLinen": (.55,.52,.42,1),
          "BathroomTile": (.40,.43,.38,1), "Enamel": (.57,.60,.51,1),
          "PaintedMetal": (.24,.29,.27,1), "Concrete": (.29,.30,.27,1),
          "Rug": (.30,.12,.085,1)}
BUILDER_NAMES = ("HomeInteriorWorldBuilder", "HomeBathroomBuilder",
                 "HomeBalconyWorldBuilder", "HomeAlarmClockBuilder",
                 "HomeRefrigeratorWorldBuilder")


def swap(v):
    return float(v[0]), float(v[2]), float(v[1])


def box(size, cut=.012):
    return kit.chamfered_box((0,0,0), swap(size), cut)


def cylinder(size, profile=None):
    x,y,z = size
    profile = profile or ((.90,-.5),(1,-.46),(1,.46),(.90,.5))
    geometry = kit.lathe([(r*x*.5,h*y) for r,h in profile], 12)
    return kit.scaled(geometry, (1,z/x,1))


def fit_geometry(geometry, size):
    lo,hi = kit.bounds(geometry)
    center = [(a+b)*.5 for a,b in zip(lo,hi)]
    geometry = kit.translated(geometry, [-v for v in center])
    return kit.scaled(geometry, [s/(b-a) for s,a,b in zip(swap(size),lo,hi)])


def frame(size, thickness=.06):
    x,y,z=size
    return kit.merge_all([
        kit.translated(box((thickness,y,z)), swap((sx*(x-thickness)*.5,0,0)))
        for sx in (-1,1)] + [
        kit.translated(box((x-2*thickness,thickness,z)),swap((0,sy*(y-thickness)*.5,0)))
        for sy in (-1,1)])


def cupboard(size):
    x,y,z=size
    t=min(.055,x*.085)
    pieces=[kit.translated(box((t,y,z)),swap((s*(x-t)*.5,0,0))) for s in (-1,1)]
    pieces += [kit.translated(box((x-2*t,y,t)),swap((0,0,(z-t)*.5)))]
    for h in (-.5,.0,.5):
        pieces.append(kit.translated(box((x-2*t,t,z)),swap((0,h*(y-t),0))))
    return kit.merge_all(pieces)


def cloth(size):
    x,y,z=size
    # A closed, irregularly folded slab: no zero-thickness billboards.
    rows=5; cols=6; vertices=[]
    for side in (0,1):
        for iz in range(rows+1):
            for ix in range(cols+1):
                px=(ix/cols-.5)*x; pz=(iz/rows-.5)*z
                crest=(.30+math.sin(ix*1.8+iz*.6)*.20+math.cos(iz*2.2)*.12)*y
                vertices.append((px,pz,crest+(-.50 if side==0 else .28)*y))
    stride=cols+1; layer=(rows+1)*stride; faces=[]
    for iz in range(rows):
        for ix in range(cols):
            a=iz*stride+ix; b=a+1; c=a+stride+1; d=a+stride
            faces.extend(((a,d,c,b),(a+layer,b+layer,c+layer,d+layer)))
    boundary=list(range(stride))+[r*stride+cols for r in range(1,rows+1)]
    boundary += [rows*stride+c for c in range(cols-1,-1,-1)]
    boundary += [r*stride for r in range(rows-1,0,-1)]
    for a,b in zip(boundary,boundary[1:]+boundary[:1]):
        faces.append((a,b,b+layer,a+layer))
    return fit_geometry((vertices,faces),size)


def grid(size,cols,rows):
    x,y,z=size; vertices=[]; faces=[]
    # Unity SW,SE,NW,NE top-corner ordering survives in the manifest; the
    # importer explicitly reindexes vertices by coordinate after FBX import.
    for row in range(rows):
        for col in range(cols):
            a=len(vertices)
            for cx,cz in ((col,row),(col+1,row),(col,row+1),(col+1,row+1)):
                vertices.append(((cx/cols-.5)*x,y*.5,(cz/rows-.5)*z))
            faces.extend(((a,a+2,a+1),(a+1,a+2,a+3)))
    # Separate bottom and four sides match the existing runtime deformation.
    base=kit.box((0,0,0),size)
    for face in base[1]:
        pts=[base[0][i] for i in face]
        if all(abs(p[1]-y*.5)<1e-6 for p in pts):
            continue
        a=len(vertices);vertices.extend(pts);faces.append(tuple(range(a,a+len(pts))))
    return bp.to_source((vertices,faces))


def pillow_grid(size,cols,rows):
    """Two filled cloth shells meet at one thin, pinned perimeter seam.

    The upper shell includes the rounded shoulders. Both height profiles are
    exported so runtime contact samples this shape instead of an imaginary
    plane. The lower shell stays supported by the mattress.
    """
    x,y,z=size; top=[]; bottom=[]
    # A slightly lifted sewn edge separates this filled pillow from the
    # mattress. Crown height and the central lower support stay unchanged.
    seam_top=-y*.18+.0015+.015; seam_bottom=seam_top-.003
    for row in range(rows+1):
        for col in range(cols+1):
            dome=max(0,math.sin(math.pi*col/cols)*math.sin(math.pi*row/rows))**.65
            top.append(seam_top+(y*.5-seam_top)*dome)
            bottom.append(seam_bottom+(-y*.5-seam_bottom)*dome)
    vertices=[]; faces=[]
    for heights,upper in ((top,True),(bottom,False)):
        for row in range(rows):
            for col in range(cols):
                a=len(vertices)
                for cx,cz in ((col,row),(col+1,row),(col,row+1),(col+1,row+1)):
                    vertices.append(((cx/cols-.5)*x,heights[cz*(cols+1)+cx],(cz/rows-.5)*z))
                face=(a,a+2,a+3,a+1)
                faces.append(face if upper else tuple(reversed(face)))
    boundary=[(col,0) for col in range(cols+1)]
    boundary += [(cols,row) for row in range(1,rows+1)]
    boundary += [(col,rows) for col in range(cols-1,-1,-1)]
    boundary += [(0,row) for row in range(rows-1,0,-1)]
    for left,right in zip(boundary,boundary[1:]+boundary[:1]):
        a=len(vertices)
        for (cx,cz),height in ((left,seam_bottom),(right,seam_bottom),(right,seam_top),(left,seam_top)):
            vertices.append(((cx/cols-.5)*x,height,(cz/rows-.5)*z))
        faces.append((a,a+3,a+2,a+1))
    return bp.to_source((vertices,faces)),top,bottom


def dish(size,deep=False):
    profile=((.63,-.5),(.84,-.44),(1,.34),(1,.5),(.86,.5),
             (.73,-.20),(.18,-.26),(.12,-.26)) if deep else (
                 (.55,-.5),(.85,-.35),(1,.28),(1,.5),(.86,.5),(.66,-.04),(.12,-.04))
    return cylinder(size,profile)


def irregular_patch(size,seed=0,wall=False):
    """Closed matte surface fleck with genuinely concave, non-circular edges."""
    if wall:
        outline=[(-.50,.46),(-.38,.50),(-.29,.42),(-.16,.45),(-.13,.27),
                 (-.05,.22),(.05,.37),(.17,.48),(.32,.42),(.48,.47),
                 (.50,.29),(.38,.25),(.35,.13),(.24,.11),(.21,-.30),
                 (.17,-.50),(.12,-.45),(.08,.11),(-.03,.08),(-.12,-.19),
                 (-.18,-.29),(-.22,-.12),(-.24,.11),(-.37,.12),(-.46,.22)]
        # A wall patch is authored in Unity XY, with its thin axis along Z.
        verts=[(x*size[0],y*size[1],depth*size[2])
               for depth in (-.5,.5) for x,y in outline]
    else:
        outline=[]
        for i in range(32):
            angle=math.tau*i/32
            radius=.64+.19*math.sin(3*angle+.7*seed)+.14*math.cos(7*angle+seed)+.09*math.sin(11*angle)
            outline.append((math.cos(angle)*radius*.5,math.sin(angle)*radius*.5))
        verts=[(x*size[0],y*size[2],depth*size[1])
               for depth in (-.5,.5) for x,y in outline]
    n=len(outline)
    faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    geom=(verts,faces)
    if wall:geom=bp.to_source(geom)
    if bp.signed_volume(geom)<0:geom=(geom[0],[tuple(reversed(f)) for f in geom[1]])
    return fit_geometry(geom,size)


class Build:
    def __init__(self):
        bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
        self.root=bpy.data.objects.new("HomeInterior3D",None)
        bpy.context.scene.collection.objects.link(self.root)
        self.parts=[];self.geometry={};self.materials={}
        bpy.context.scene.unit_settings.system="METRIC"
        bpy.context.scene.unit_settings.scale_length=1

    def add(self,name,size,sheet="Plain",*,semantic=None,role="binding",group="shell",
            fit="fixed",aliases=(),patterns=(),position=(0,0,0),rotation=(0,0,0),
            min_day=1,max_day=7,tint=None,geometry=None,collider=False,
            grid_columns=0,grid_rows=0,max_depth=0,
            grid_top_heights=(),grid_bottom_heights=()):
        if any(p["name"]==name for p in self.parts):
            raise ValueError("Duplicate part "+name)
        geometry=geometry or box(size)
        mesh=bpy.data.meshes.new(name+"_Mesh")
        mesh.from_pydata(geometry[0],[],geometry[1]);mesh.update(calc_edges=True)
        uv=mesh.uv_layers.new(name="UVMap")
        for poly in mesh.polygons:
            poly.use_smooth=False
            axis=max(range(3),key=lambda i:abs(poly.normal[i]))
            for loop in poly.loop_indices:
                p=mesh.vertices[mesh.loops[loop].vertex_index].co
                uv.data[loop].uv=((p.y,p.z) if axis==0 else (p.x,p.z) if axis==1 else (p.x,p.y))
                uv.data[loop].uv/=PITCH[sheet]
        if sheet not in self.materials:
            mat=bpy.data.materials.new("PREVIEW_Home_"+sheet)
            mat.diffuse_color=COLORS[sheet];self.materials[sheet]=mat
        mesh.materials.append(self.materials[sheet])
        obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj)
        obj.parent=self.root
        # Mesh origins stay zero; placement is metadata. This avoids importer
        # translation baking, especially at hinged and deforming assemblies.
        lo,hi=kit.bounds(geometry)
        item={"name":name,"semantic_name":semantic or (name if role=="binding" else ""),
              "aliases":list(aliases),"patterns":list(patterns),"role":role,
              "group":group,"sheet":sheet,"tint":list(tint or COLORS[sheet]),
              "fit":fit,"min_day":min_day,"max_day":max_day,
              "position":list(position),"rotation":list(rotation),"size":list(size),
              "bounds_min":list(swap(lo)),"bounds_max":list(swap(hi)),
              "triangles":kit.triangle_count(geometry),"collider":collider,
              "grid_columns":grid_columns,"grid_rows":grid_rows,"max_depth":max_depth,
              "grid_top_heights":list(grid_top_heights),"grid_bottom_heights":list(grid_bottom_heights)}
        for key in ("role","group","sheet","fit","min_day","max_day"):
            obj["bp_"+key]=item[key]
        self.parts.append(item);self.geometry[name]=geometry
        return item


def add_bindings(b):
    # Broad structural masses and furniture are fixed-metre meshes. Chamfers
    # are authored in metres and never inflated by a GameObject size scale.
    fixed={
        "Home Floor":((10,.16,8),"PlankFloor"),
        "Home Ceiling":((10.48,.16,8.48),"CeilingPlaster"),
        "Home Back Wall":((10,3.4,.24),"Wallpaper"),
        "Home Left Wall":((.24,3.4,8),"Wallpaper"),
        "Home Right Wall":((.24,3.4,8),"Wallpaper"),
        "Home Entry Wall Left":((3.70,3.4,.24),"Wallpaper"),
        "Home Entry Wall Right":((3.70,3.4,.24),"Wallpaper"),
        "Home Entry Lintel":((2.60,.76,.24),"Wallpaper"),
        "Home Entry Wall Left Door Infill":((.5,2.64,.24),"Wallpaper"),
        "Home Entry Wall Right Door Infill":((.5,2.64,.24),"Wallpaper"),
        "Home Entry Door Transom Infill":((1.60,.34,.24),"Wallpaper"),
        "Home Entry Rug":((1.55,.03,1.35),"Rug"),
        "Home Exit Door":((1.60,2.30,.12),"DarkWood"),
        "Home Right Wall South Pier":((.24,3.4,.91),"Wallpaper"),
        "Home Right Wall Middle Pier":((.24,3.4,.43),"Wallpaper"),
        "Home Right Wall North Pier":((.24,3.4,3.82),"Wallpaper"),
        "Home Right Wall Window Sill":((.24,1.53,1.48),"Wallpaper"),
        "Home Right Wall Window Lintel":((.24,.77,1.48),"Wallpaper"),
        "Home Right Wall Door Lintel":((.24,1.06,1.36),"Wallpaper"),
        "Home Bed Frame":((2.55,.38,1.75),"DarkWood"),
        "Home Bed Crooked Blanket":((1.1475,.12,.315),"Upholstery"),
        "Home Sofa Base":((1.50,.56,2.02),"Upholstery"),
        "Home Sofa Back":((.25,1.08,2.02),"Upholstery"),
        "Home Sofa Sunken Cushion":((1.05,.16,1.5756),"Upholstery"),
        "Home Scarred Table":((1.45,.12,1.20),"WornLaminate"),
        "Home Table Base Crooked":((.28,.80,.28),"DarkWood"),
        "Home Battered Cabinet":((.65,2.25,1.10),"DarkWood"),
        "Home Cabinet Shelf 1":((.468,.10,.08),"WornLaminate"),
        "Home Cabinet Shelf 2":((.468,.10,.08),"WornLaminate"),
        "Home Camera Corner Junk Base":((1.30,.44,1.40),"DarkWood"),
        "Home Camera Corner Broken Wardrobe Door":((1.014,.12,.952),"DarkWood"),
        "Home Camera Corner Suitcase":((.72,.24,.62),"DarkWood"),
        "Home Camera Corner Old Coat":((.78,.10,.72),"Upholstery"),
        "Home Alarm Clock Nightstand":((.68,.72,.46),"DarkWood"),
        "Home Alarm Clock Nightstand Top":((.73,.05,.5),"DarkWood"),
        "Alarm Clock Body":((.276,.162,.144),"PaintedMetal"),
        "Alarm Clock Face":((.237,.105,.0108),"Plain"),
        "Alarm Clock Snooze":((.096,.021,.045),"Plain"),
        "Home Bathroom West Wall":((.18,3.4,3),"Wallpaper"),
        "Home Bathroom Front Wall Left":((.17,3.4,.18),"Wallpaper"),
        "Home Bathroom Front Wall Right":((1.83,3.4,.18),"Wallpaper"),
        "Home Bathroom Door Lintel":((1.10,1.15,.18),"Wallpaper"),
        "Home Bathroom Door Ajar":((.748,2.12,.08),"DarkWood"),
        "Home Bathroom Tile Floor":((2.90,.024,2.80),"BathroomTile"),
        "Home Bathroom Back Tile":((2.98,1.70,.022),"BathroomTile"),
        "Home Bathroom Right Tile":((.022,1.70,2.88),"BathroomTile"),
        "Home Bathroom Toilet Footprint":((.82,.48,.858),"Enamel"),
        "Home Bathroom Toilet Cistern":((.24,.72,.748),"Enamel"),
        "Home Bathroom Shower Tray":((1.15,.18,1.15),"Enamel"),
        "Home Bathroom Shower Basin":((1.01,.045,1.01),"Enamel"),
        "Home Bathroom Shower Rim Front":((1.15,.09,.05),"Enamel"),
        "Home Bathroom Shower Rim Left":((.05,.09,1.09),"Enamel"),
        "Home Bathroom Sink Basin":((.85,.20,.35),"Enamel"),
        "Home Bathroom Sink Hollow":((.493,.035,.189),"Plain"),
        "Home Bathroom Cracked Mirror":((.66,.88,.024),"Plain"),
        "Home Refrigerator Cabinet Left":((.13,2.24,.76),"Enamel"),
        "Home Refrigerator Cabinet Right":((.13,2.24,.76),"Enamel"),
        "Home Refrigerator Cabinet Top":((.82,.23,.76),"Enamel"),
        "Home Refrigerator Cabinet Bottom":((.82,.23,.76),"Enamel"),
        "Home Refrigerator Cabinet Back":((.82,1.78,.155),"Enamel"),
        "Home Refrigerator Cavity Back Liner":((.82,1.78,.028),"Enamel"),
        "Home Refrigerator Cavity Left Liner":((.028,1.78,.50),"Enamel"),
        "Home Refrigerator Cavity Right Liner":((.028,1.78,.50),"Enamel"),
        "Home Refrigerator Door Enamel":((1.00,2.08,.10),"Enamel"),
        "Home Refrigerator Door Inner Liner":((.88,1.8928,.025),"Enamel"),
        "Home South Exterior Return Wall":((2.95,3.4,.24),"Concrete"),
        "Home North Exterior Return Wall":((2.95,3.4,.24),"Concrete"),
        "Home Balcony Deck":((2.42,.18,3.90),"Concrete"),
        "Home Balcony Threshold":((.58,.09,1.26),"DarkWood"),
        "Home Balcony Outer Rail Cap":((.18,.07,3.96),"PaintedMetal"),
        "Home Balcony South Rail Cap":((2.42,.07,.18),"PaintedMetal"),
        "Home Balcony North Rail Cap":((2.42,.07,.18),"PaintedMetal"),
    }
    # Exact sizes of the counter runs follow the measured refrigerator gap.
    for side,width in (("Left",1.4105),("Right",.8095)):
        fixed["Home Kitchen Counter "+side]=((width,.92,.82),"WornLaminate")
        fixed["Home Kitchen Top "+side]=((width+.04,.10,.90),"WornLaminate")
    for i,width in enumerate((.32,.30,.31,.27),1):
        fixed[f"Home Bathroom Shower Curtain Fold {i}"]=((width,1.82,.03),"BedLinen")
    for i in range(1,4):
        fixed[f"Home Bathroom Shower Curtain Side {i}"]=((.03,1.82,.32),"BedLinen")
    for name,(size,sheet) in fixed.items():
        geom=None
        if "Blanket" in name or "Coat" in name or "Cushion" in name:
            geom=cloth(size)
        if name=="Home Battered Cabinet":geom=cupboard(size)
        if name=="Home Table Base Crooked":
            geom=cylinder(size,((1,-.5),(1,-.46),(.62,-.42),(.52,.38),(.70,.45),(1,.5)))
        b.add(name,size,sheet,geometry=geom)
    for name,size,cols,rows,depth in (
            ("Home Bed Mattress",(2.39,.18,1.57),17,11,.10),):
        b.add(name,size,"BedLinen",role="grid",semantic=name,
              geometry=grid(size,cols,rows),grid_columns=cols,grid_rows=rows,max_depth=depth)
    size=(.62,.14,1.05); cols,rows=8,14
    geometry,top,bottom=pillow_grid(size,cols,rows)
    b.add("Home Pillow",size,"BedLinen",role="grid",semantic="Home Pillow",
          geometry=geometry,grid_columns=cols,grid_rows=rows,max_depth=.070,
          grid_top_heights=top,grid_bottom_heights=bottom,tint=(.58,.55,.47,1))
    b.add("Home Bathroom Toilet Bowl",(.62,.24,.572),"Enamel",
          geometry=dish((.62,.24,.572),True))
    b.add("Home Bathroom Toilet Seat",(.54,.05,.506),"Enamel",
          geometry=cylinder((.54,.05,.506),((1,-.5),(1,.5),(.70,.5),(.70,-.5))))
    b.add("Home Bathroom Sink Pedestal",(.26,.72,.26),"Enamel",
          geometry=cylinder((.26,.72,.26),((1,-.5),(1,-.44),(.68,-.36),(.62,.26),(.84,.5))))
    # Contextual hand/mouth props keep the exact short semantic names used
    # beneath their existing socket-owned roots.
    for name,size,kind in (("Handle",(.012,.22,.012),"Cylinder"),
                           ("Bristles",(.014,.025,.010),"Box"),
                           ("Paper",(.0065,.070,.0065),"Cylinder"),
                           ("Ember",(.007,.004,.007),"Cylinder")):
        part=b.add(name,size,geometry=cylinder(size) if kind=="Cylinder" else box(size))
        part["primitive_kind"]=kind
    for i in range(1,4):
        b.add(f"Foam {i}",(.014,.011,.011),geometry=box((.014,.011,.011),.003))
    # Remaining literal authored parts: static small boxes and hardware are
    # enumerated from their owning builders, never admitted by a catch-all.
    exact=set();patterns=set();literal_sizes={}
    for builder in BUILDER_NAMES:
        text=(ROOT/f"Assets/Scripts/Runtime/World/{builder}.cs").read_text(encoding="utf-8-sig")
        for match in re.finditer(r'(\$?)"((?:Home |Player Home |Alarm Clock )[^"\r\n]*)"',text):
            interpolated,label=match.groups()
            if interpolated and "{" in label:
                expression="^"+".*".join(re.escape(s) for s in re.split(r"\{[^}]+\}",label))+"$"
                patterns.add(expression)
            else:exact.add(label)
        # Literal full-size call vectors are frozen as fixed authored meshes.
        for match in re.finditer(r'(CreateBox|CreateCylinder|CreateEnamelBox|CreatePipe|CreateExteriorSurfaceBox)\s*\(\s*"([^"]+)"',text):
            tail=text[match.end():];depth=1;stop=0
            for stop,char in enumerate(tail):
                if char=="(":depth+=1
                if char==")":
                    depth-=1
                    if depth==0:break
            args=tail[:stop]
            vectors=re.findall(r'new Vector3\(\s*([-+\d.]+)f?\s*,\s*([-+\d.]+)f?\s*,\s*([-+\d.]+)f?\s*\)',args)
            # Last vector can be pipe rotation: infer size from second argument
            # position only when two direct literals occur, never from a tint.
            if len(vectors)>=2 and match.group(2) not in fixed:
                vector=vectors[1];size=tuple(float(v) for v in vector)
                if min(size)>0:
                    if match.group(1) in ("CreateCylinder","CreatePipe"):
                        size=(size[0],size[1]*2,size[2]);kind="cylinder"
                    else:kind="box"
                    literal_sizes[match.group(2)]=(size,kind)
    bound={p["semantic_name"] for p in b.parts}
    for name,(size,kind) in sorted(literal_sizes.items()):
        if name in bound:continue
        b.add(name,size,geometry=cylinder(size) if kind=="cylinder" else box(size))
        bound.add(name)
    # Helper-name suffixes are explicit: authoring patterns are bounded to a
    # known assembly, preventing a misspelled/unknown furniture silently fitting.
    patterns.update((r"^Alarm Clock Digit [0-3] Segment [0-6]$",
                     r"^Home Balcony (Window|Door) (Frame|Glass|Handle).*$",
                     r"^Player Home (Lower Window|Street Entry).*$",
                     r"^Player Home Authored Front Window Glass .*$"))
    unresolved=sorted(exact-bound)
    # Shared profile is a chamfered 1 m cube, or a capped 12-sided 1 m cylinder.
    # Both explicit aliases/patterns resolve to the requested shape through
    # primitive_kind; runtime has to select the matching library entry.
    for kind in ("Box","Cylinder"):
        part=b.add("Profile."+kind,(1,1,1),role="library",fit="parametric",
                   aliases=unresolved,patterns=sorted(patterns),
                   geometry=box((1,1,1),.025) if kind=="Box" else cylinder((1,1,1)))
        part["primitive_kind"]=kind
    # These are household neglect, not old repair/rust. Even when a small
    # authored profile is shared, exact bindings own their calendar visibility.
    b.add("Home Bathroom Leak Stain",(.74,.94,.003),min_day=4,
          geometry=irregular_patch((.74,.94,.003),wall=True))
    staged={"Home Refrigerator Interior Drip Left":(3,"Box"),
            "Home Refrigerator Interior Drip Right":(4,"Box"),
            "Home Refrigerator Interior Spill":(3,"Cylinder"),
            "Home Refrigerator Lower Grime Line":(4,"Box")}
    for i in range(1,4):staged[f"Home Refrigerator Shelf {i} Stain"]=(3,"Box")
    for name,(day,kind) in staged.items():
        record=next((p for p in b.parts if p["semantic_name"]==name),None)
        if record is None:
            record=b.add(name,(1,1,1),fit="parametric",min_day=day,
                         geometry=box((1,1,1),.025) if kind=="Box" else cylinder((1,1,1)))
        record["min_day"]=day;record["primitive_kind"]=kind
        bpy.data.objects[record["name"]]["bp_min_day"]=day


def add_decor(b):
    def add(name,size,pos,sheet="Plain",day=1,last=7,geom=None,tint=None,group="shell",rot=(0,0,0),collider=False):
        return b.add(name,size,sheet,role="decor",position=pos,rotation=rot,
                     min_day=day,max_day=last,geometry=geom,tint=tint,group=group,collider=collider)
    # Ordinary third door; no light, special trim, writing or room contents.
    add("Home Locked Room West Wall",(.18,3.4,3.06),(-.01,1.70,2.35),"Wallpaper",collider=True)
    for suffix,x,width in (("Left",.0275,.255),("Right",1.3325,.255)):
        add("Home Locked Room Front "+suffix,(width,3.4,.18),(x,1.7,.91),"Wallpaper",collider=True)
    add("Home Locked Room Lintel",(1.05,1.20,.18),(.68,2.8,.91),"Wallpaper",collider=True)
    add("Home Locked Room Door",(1.05,2.20,.08),(.68,1.10,.855),"DarkWood",collider=True)
    # Long-standing age, kept separate from the seven-day accumulation.
    add("Home North Window Glass",(1.62,1.05,.025),(-2.65,2.12,3.865),tint=(.16,.22,.21,1))
    add("Home North Window Frame",(1.76,1.19,.065),(-2.65,2.12,3.83),"DarkWood",geom=frame((1.76,1.19,.065),.065))
    for i in range(7):
        add(f"Home Radiator Fin {i+1}",(.10,.63,.16),(-3.58+i*.15,.52,3.70),"PaintedMetal")
    for y in (.24,.79):
        add(f"Home Radiator Header {y}",(1.02,.07,.07),(-3.13,y,3.70),"PaintedMetal")
    add("Home Old Radio",(.56,.30,.36),(-4.535,2.40,3.31),"DarkWood",group="home.furniture.bookcase")
    add("Home Radio Dial",(.32,.12,.018),(-4.535,2.41,3.12),tint=(.29,.20,.10,1),group="home.furniture.bookcase")
    # No photographs, readable calendar, medicine, new clues or personal lore.
    for i,x in enumerate((-1.26,-.82)):
        add(f"Home Day1 Stacked Plate {i}",(.28,.025,.28),(x,.898,1.65),"Enamel",last=2,geom=dish((.28,.025,.28)),group="home.furniture.table")
    add("Home Day1 Folded Towel",(.48,.045,.28),(-3.70,1.053,3.08),"BedLinen",last=2,geom=cloth((.48,.045,.28)),group="home.furniture.kitchen.left")
    for i,x in enumerate((-1.97,-1.73)):
        add(f"Home Paired Slipper {i}",(.20,.10,.38),(x,.05,-1.08),"Upholstery")

    def bottle(name,pos,day,height=.34,side=False,group="shell",brown=False,lean=0):
        size=(.105,height,.105)
        geom=cylinder(size,((.86,-.5),(1,-.46),(1,.13),(.77,.27),(.32,.33),(.32,.47),(.40,.5)))
        tint=(.22,.105,.045,1) if brown else (.10,.22,.14,1)
        add(name,size,pos,day=day,geom=geom,tint=tint,rot=(0,0,84) if side else (0,day*19,lean),group=group)

    def rag(name,pos,day,size=(.43,.06,.31),group="shell",tint=(.27,.29,.28,1),rot=0):
        add(name,size,pos,"BedLinen",day,geom=cloth(size),tint=tint,group=group,rot=(0,rot,0))

    def stain(name,pos,day,size=(.46,.005,.36),group="shell",tint=(.12,.105,.075,1)):
        seed=sum(ord(c) for c in name)%41
        add(name,size,pos,day=day,geom=irregular_patch(size,seed),tint=tint,group=group)

    def plate(name,pos,day,group="shell",diam=.27):
        size=(diam,.035,diam)
        add(name,size,pos,"Enamel",day,geom=dish(size),tint=(.40,.39,.30,1),group=group)
        stain(name+" Remains",(pos[0],pos[1]+.015,pos[2]),day,(diam*.60,.006,diam*.58),group)

    # Each day adds distinct composition, with denser piles constrained to
    # furniture tops and peripheral pockets. The centre, wake-up approach,
    # fridge aisle, bathroom route and balcony passage never receive piles.
    for day in range(2,8):
        for i in range(day-1):
            slot=(day-2)*(day-1)//2+i
            x=-1.43+(slot%7)*.175+.033*math.sin(slot*3.21)
            z=1.83+(slot//7)*.24+.036*math.cos(slot*2.33)
            bottle(f"Day{day} Table Bottle {i}",(x,.88+.17+(i//4)*.05,z),day,
                   brown=(i+day)%2==0,group="home.furniture.table",lean=12 if day>=5 and i%3==0 else 0)
        for i in range(max(1,day-2)):
            x=-3.82+(i%3)*.26;z=2.66+((day+i)%2)*.31
            plate(f"Day{day} Kitchen Plate {i}",(x,1.05+.035*(day-2),z),day,
                  "home.furniture.kitchen.left")
        if day>=3:
            rag(f"Day{day} Sofa Laundry",(3.65,.78+(day-3)*.075,-2.48+(day%2)*.36),day,
                (.64,.13,.59),"home.furniture.sofa",(.24,.27,.25,1),day*23)
            rag(f"Day{day} Floor Laundry",(2.99,.05+(day-3)*.026,-2.60+(day%2)*.54),day,
                (.28,.08,.44),tint=(.22,.25,.25,1),rot=0)
        if day>=4:
            stain(f"Day{day} Kitchen Spill",(-3.50,1.084+(day-4)*.001,2.84),day,
                  (.47+(day-4)*.12,.006,.41),"home.furniture.kitchen.left")
            stain(f"Day{day} Table Spill",(-.65,.886+(day-4)*.001,2.07),day,
                  (.38+(day-4)*.11,.004,.35),"home.furniture.table")
            # Foot-end patches leave the sleeper's deforming contact strip clear.
            rag(f"Day{day} Bed Foot Linen",(-2.63,.62+(day-4)*.014,.13),day,
                (.40,.075,.46),"home.bed.surface-clutter",(.30-.02*day,.30-.019*day,.23-.014*day,1),day*13)
        if day>=5:
            for i in range((day-3)*3):
                slot={5:0,6:6,7:15}[day]+i
                x=-4.36+(slot%5)*.23+.025*math.sin(slot*2.7)
                # Compress the same calendar composition to the shallower
                # south-west pile; its front stays behind the bed approach.
                z=-3.648+(slot//5)*.189+.0238*math.cos(slot*3.9)
                bottle(f"Day{day} Corner Bottle {i}",(x,.50 if i%3==0 else .61,z),day,
                       side=i%3==0,brown=i%2==0,group="home.furniture.camera-junk")
            for i in range(day-3):
                # Trash bags on the old low storage pile, never in the aisle.
                size=(.41,.31,.43)
                geom=cylinder(size,((.58,-.5),(.91,-.4),(1,-.10),(.84,.26),(.36,.43),(.24,.5)))
                slot={5:0,6:2,7:5}[day]+i
                add(f"Day{day} Refuse Bag {i}",size,(-4.10+(slot%2)*.45,.68+(slot//2)*.22,-2.745),
                    day=day,geom=geom,tint=(.16,.18,.14,1),group="home.furniture.camera-junk")
            rag(f"Day{day} Bathroom Towel",(2.94+(day-5)*.025,.05+(day-5)*.065,3.44+(day-5)*.035),
                day,(.39,.08,.46),tint=(.28,.29,.23,1))
            stain(f"Day{day} Bathroom Floor Soil",(3.17,.029+(day-5)*.001,2.03),day,
                  (.46+(day-5)*.09,.004,.51),tint=(.14,.17,.13,1))
        if day>=6:
            for i in range((day-4)*4):
                slot=i+(8 if day==7 else 0)
                x=3.24+(slot%4)*.29;z=-3.30+(slot//4)*.27
                add(f"Day{day} Sofa Crumpled Paper {i}",(.19,.09,.16),(x,.93+(i%2)*.07,z),
                    day=day,geom=cloth((.19,.09,.16)),tint=(.39,.33,.22,1),group="home.furniture.sofa")
            for i in range((day-4)*3):
                slot=i+(6 if day==7 else 0)
                add(f"Day{day} Counter Can {i}",(.12,.15,.12),(-3.82+(slot%4)*.24,1.14+(slot//4)*.15,3.13),
                    day=day,geom=cylinder((.12,.15,.12)),tint=(.29,.25,.17,1),group="home.furniture.kitchen.left")
    # Dirt is a matte, shallow surface layer, not standing water or an
    # obstacle. Its larger, broken shapes make the later home read neglected
    # even when its useful passage remains physically open.
    for i,(day,pos,size) in enumerate((
            (4,(-.42,.003,-1.35),(.76,.004,.62)),
            (5,(1.12,.004,-.28),(1.18,.004,.58)),
            (5,(-1.35,.004,.88),(.69,.004,.64)),
            (6,(.18,.005,-2.75),(1.18,.004,.43)),
            (6,(2.38,.005,-1.90),(.54,.004,.90)),
            (7,(.31,.006,-1.28),(1.43,.004,1.12)),
            (7,(1.67,.006,-.86),(.82,.004,1.37)),
            (7,(-1.29,.006,-.57),(.94,.004,.68)),
    )):
        stain(f"Day{day} Floor Tracked Grime {i}",pos,day,size,tint=(.15,.125,.09,1))
        b.parts[-1]["surface_overlay"]=True
    for i in range(12):
        day=5+i//5
        x=.10+(i%2)*.28+math.sin(i*1.3)*.08;z=-2.80+i*.27
        stain(f"Day{day} Floor Tread {i}",(x,.008,.0+z),day,(.17,.002,.31),tint=(.12,.10,.07,1))
        b.parts[-1]["surface_overlay"]=True
    # The last days must change the large shapes visible in Home's actual
    # camera, not merely increase a count behind its foreground furniture.
    # These loose blankets share the bed's clutter root and disappear for
    # the complete lie-down/sleep interaction, leaving its deforming mesh.
    rag("Day5 Bed Abandoned Duvet",(-3.41,.655,-.38),5,(1.82,.18,1.32),
        "home.bed.surface-clutter",(.44,.40,.28,1),-5)
    b.parts[-1]["max_day"]=6
    rag("Day7 Bed Crumpled Duvet",(-3.52,.70,-.38),7,(2.05,.28,1.40),
        "home.bed.surface-clutter",(.36,.33,.23,1),0)
    rag("Day6 Bed Dirty Sheet",(-3.15,.76,-.54),6,(1.02,.17,.82),
        "home.bed.surface-clutter",(.52,.48,.35,1),17)
    rag("Day7 Bed Rolled Shirt",(-4.07,.77,-.08),7,(.58,.21,.56),
        "home.bed.surface-clutter",(.29,.32,.29,1),-23)

    for day,pos,size,tint,angle in (
            (5,(3.53,.93,-2.59),(.84,.36,1.12),(.41,.43,.37,1),5),
            (6,(3.61,1.11,-2.64),(.76,.32,.93),(.48,.44,.33,1),-14),
            (7,(3.60,1.31,-2.47),(.79,.30,.76),(.31,.35,.32,1),11),
            (7,(3.56,1.38,-2.91),(.66,.29,.72),(.50,.46,.34,1),-7)):
        rag(f"Day{day} Sofa Large Laundry {angle}",pos,day,size,
            "home.furniture.sofa",tint,angle)
    # Two sagging refuse sacks and their loose contents occupy the front
    # underside of the table. Their entire footprint starts north of Z=1.25,
    # beyond the refrigerator's 0.70 m-wide guided approach corridor.
    for day,pos,size in ((5,(-1.18,.245,1.63),(.48,.49,.52)),
                         (6,(-.61,.285,1.61),(.50,.57,.51)),
                         (7,(-.89,.315,2.10),(.62,.63,.54))):
        add(f"Day{day} Under Table Refuse Sack",size,pos,day=day,
            geom=cylinder(size,((.55,-.5),(.92,-.40),(1,-.10),(.91,.22),(.31,.42),(.19,.5))),
            tint=(.23,.25,.18,1),group="home.furniture.table")
    for i in range(14):
        day=5+i//5
        x=-1.39+(i%5)*.27;z=1.39+(i//5)*.27
        size=(.18,.065,.16)
        add(f"Day{day} Under Table Food Wrapper {i}",size,(x,.04,z),day=day,
            geom=cloth(size),tint=(.50,.44,.29,1),rot=(0,i*31,0),group="home.furniture.table")
    for i in range(7):
        day=6+i//4
        x=-1.29+(i%4)*.31;z=1.42+(i//4)*.39
        bottle(f"Day{day} Under Table Tipped Bottle {i}",(x,.065,z),day,
               height=.28,side=True,brown=i%2==0,group="home.furniture.table")
    for i in range(8):
        day=6+i//4
        x=-1.34+(i%4)*.29;z=1.54+(i//4)*.24
        add(f"Day{day} Under Table Crushed Can {i}",(.135,.18,.12),(x,.085,z),day=day,
            geom=cylinder((.135,.18,.12),((.63,-.5),(1,-.35),(.79,.0),(1,.32),(.72,.5))),
            tint=(.42,.38,.24,1),rot=(67,i*43,22),group="home.furniture.table")
    # A narrow ragged ridge collects against the sofa's foot, outside the
    # main path and below the balcony approach's southern edge.
    for i in range(5):
        day=5+i//2
        rag(f"Day{day} Sofa Foot Rag {i}",(2.995,.09+i*.028,-3.13+i*.23),day,
            (.29,.16,.33),"home.furniture.sofa",(.42-.025*i,.40-.02*i,.30-.02*i,1))
    # Flattened paper can cross the used floor without inventing an obstacle.
    # A few larger visible scraps break the pristine central-floor read.
    for i,(x,z) in enumerate(((.63,-1.49),(1.53,-.16),(.41,-2.47),(2.24,-2.59),
                             (-.36,-.57),(1.42,-1.92),(2.35,-.65),(.95,-3.22))):
        day=5+i//3
        size=(.24,.018,.19)
        rag(f"Day{day} Floor Flattened Paper {i}",(x,.015,z),day,size,
            "shell",(.45,.39,.26,1),i*47)
        b.parts[-1]["surface_overlay"]=True
    # An ashtray already belongs to an ordinary household at day one; visible
    # ash/ends accumulate, no animated smoke or invented sound source.
    add("Home Table Ashtray",(.26,.045,.26),(-.55,.908,1.49),"Enamel",
        geom=dish((.26,.045,.26),True),group="home.furniture.table")
    for day in range(2,8):
        for i in range(day*2):
            angle=(day*1.3+i*2.399)
            add(f"Day{day} Ashtray End {i}",(.013,.013,.064),
                (-.55+math.cos(angle)*.07,.94+(day-2)*.006,1.49+math.sin(angle)*.07),
                day=day,geom=cylinder((.013,.013,.064)),tint=(.42,.35,.24,1),
                rot=(0,math.degrees(angle),0),group="home.furniture.table")


def validate(b):
    errors=[];triangles=0
    # The same plan-derived rectangles as HomeApartmentDressing. Coarse
    # gameplay reserves remain independent from authored furniture surfaces.
    paths=[("entry",(-.80,-3.65,.80,-1.50)),
           ("main",(-.80,-3.65,2.82,.82)),
           ("bathroom-access",(1.72,.50,2.82,3.15)),
           ("balcony-access",(2.55,-1.34,4.65,.34)),
           ("bed-approach",(-3.80,-2.08,-.45,-1.38)),
           ("fridge-dock",(-2.4545,1.43,-1.6345,2.17))]
    waypoints=[(-.62,.76),(-1.31,.875),(-1.94,.875),(-2.0445,1.80)]
    for i,(a,c) in enumerate(zip(waypoints,waypoints[1:])):
        paths.append((f"fridge-route-{i}",(min(a[0],c[0])-.35,min(a[1],c[1])-.35,
                      max(a[0],c[0])+.35,max(a[1],c[1])+.35)))
    # Decor is placed in room coordinates, not parented to its furniture.
    # Measure every day-gated mesh against its relocated support footprint,
    # so a furniture move cannot leave last week's props over empty floor.
    supports={"home.bed.surface-clutter":(-4.86,-1.25,-2.31,.50),
              "home.furniture.bookcase":(-4.86,2.76,-4.21,3.86),
              "home.furniture.kitchen.left":(-4.19,2.44,-2.7395,3.34),
              "home.furniture.camera-junk":(-4.55,-3.76,-3.25,-2.36)}
    placements={}
    for p in b.parts:
        geom=b.geometry[p["name"]];volume=bp.signed_volume(geom)
        triangles+=p["triangles"]
        if volume<=1e-11:errors.append(f"{p['name']}: non-positive volume {volume}")
        if not 1<=p["min_day"]<=p["max_day"]<=7:errors.append("bad day range")
        if any(not math.isfinite(x) for v in geom[0] for x in v):errors.append("non-finite vertex")
        if p["grid_top_heights"]:
            top=p["grid_top_heights"];bottom=p["grid_bottom_heights"]
            cols,rows=p["grid_columns"],p["grid_rows"]
            if len(top)!=(cols+1)*(rows+1) or len(bottom)!=len(top):
                errors.append(p["name"]+": incomplete cloth profiles")
            if max(top)-min(top)<.075 or min(t-b for t,b in zip(top,bottom))<.0029:
                errors.append(p["name"]+": pillow lacks filled crown or seam thickness")
            # Weld only for validation: the exported duplicate corners keep
            # the cloth's per-facet lighting, while every shell edge is closed.
            keys=[tuple(round(c,7) for c in vertex) for vertex in geom[0]]
            edges={};directions={}
            for face in geom[1]:
                for left,right in zip(face,face[1:]+face[:1]):
                    edge=tuple(sorted((keys[left],keys[right])))
                    edges[edge]=edges.get(edge,0)+1
                    directions[edge]=directions.get(edge,0)+(1 if keys[left]<keys[right] else -1)
            if any(count!=2 for count in edges.values()) or any(directions.values()):
                errors.append(p["name"]+": open or non-manifold cloth shell")
        actual=[h-l for l,h in zip(p["bounds_min"],p["bounds_max"])]
        if max(abs(a-s) for a,s in zip(actual,p["size"]))>.0001:
            errors.append(f"{p['name']}: measured size {actual} != {p['size']}")
        mesh=bpy.data.objects[p["name"]].data
        if not mesh.uv_layers:errors.append(p["name"]+": missing UV")
        for poly in mesh.polygons:
            if poly.area<1e-12:errors.append(p["name"]+": degenerate face")
        if p["role"]=="decor":
            unity=([swap(v) for v in geom[0]],geom[1])
            turned=bp.u_rotated(unity,p["rotation"])[0]
            world=[tuple(a+c for a,c in zip(v,p["position"])) for v in turned]
            lo=[min(v[i] for v in world) for i in range(3)]
            hi=[max(v[i] for v in world) for i in range(3)]
            if p["group"] in supports:
                xmin,zmin,xmax,zmax=supports[p["group"]]
                if lo[0]<xmin-.035 or hi[0]>xmax+.035 or lo[2]<zmin-.035 or hi[2]>zmax+.035:
                    errors.append(p["name"]+": geometry extends beyond its relocated furniture support")
            if p.get("surface_overlay") and (p["collider"] or hi[1]>.045):
                errors.append(p["name"]+": overlay is not shallow/collider-free")
            if not p["collider"] and hi[1]>.045:
                for label,(xmin,zmin,xmax,zmax) in paths:
                    if lo[0]<xmax-.001 and hi[0]>xmin+.001 and lo[2]<zmax-.001 and hi[2]>zmin+.001:
                        errors.append(p["name"]+": geometry intrudes into "+label)
            key=json.dumps([geom,p["position"],p["rotation"]],sort_keys=True)
            for previous in placements.get(key,[]):
                if p["min_day"]<=previous["max_day"] and previous["min_day"]<=p["max_day"]:
                    errors.append(p["name"]+": coincident cumulative placement with "+previous["name"])
            placements.setdefault(key,[]).append(p)
    counts=[sum(p["role"]=="decor" and p["min_day"]<=d<=p["max_day"] for p in b.parts) for d in range(1,8)]
    if not all(a<b for a,b in zip(counts,counts[1:])):errors.append("days do not increase distinctly")
    if triangles>75000:errors.append("triangle cap exceeded")
    if errors:raise SystemExit("Home validation FAILED:\n"+"\n".join(errors))
    return {"part_count":len(b.parts),"triangle_count":triangles,"day_decor_counts":counts,
            "positive_volume":True,"measured_bounds":True,"metre_uv":True,
            "main_route_clearance":True,"household_route_clearance":True,
            "relocated_furniture_supports":True,
            "no_coincident_cumulative_decor":True}


def compose_source_preview(b,render=False):
    """An inspectable cutaway assembly, separate from the exported mesh library.

    Frames 1..7 show the calendar dressing. Runtime camera, actor, interaction
    rigs and exterior city are deliberately absent from this authoring preview.
    """
    poses={
        "Home Floor":(0,-.08,0),"Home Back Wall":(0,1.7,4),
        "Home Left Wall":(-5,1.7,0),
        "Home Bed Frame":(-3.585,.22,-.375),
        "Home Bed Mattress":(-3.585,.47,-.375),
        "Home Pillow":(-4.409,.6035,-.375),
        "Home Bed Crooked Blanket":(-3.135,.60,.17),
        "Home Sofa Base":(3.8,.35,-2.51),"Home Sofa Back":(4.37,.94,-2.51),
        "Home Sofa Sunken Cushion":(3.68,.67,-2.51),
        "Home Scarred Table":(-.825,.82,1.85),"Home Table Base Crooked":(-.97,.40,1.946),
        "Home Battered Cabinet":(-4.535,1.125,3.31),
        "Home Cabinet Shelf 1":(-4.585,.46,2.837),"Home Cabinet Shelf 2":(-4.585,1.08,2.837),
        "Home Camera Corner Junk Base":(-3.90,.22,-3.06),
        "Home Camera Corner Broken Wardrobe Door":(-3.80,.50,-2.88),
        "Home Camera Corner Suitcase":(-3.68,.69,-3.42),
        "Home Camera Corner Old Coat":(-4.14,.86,-2.68),
        "Home Alarm Clock Nightstand":(-4.48,.36,.80),
        "Home Alarm Clock Nightstand Top":(-4.48,.745,.80),
        "Alarm Clock Body":(-4.48,.853,.80),"Alarm Clock Face":(-4.48,.853,.7244),
        "Alarm Clock Snooze":(-4.48,.9448,.809),
        "Home Kitchen Counter Left":(-3.46475,.48,2.89),
        "Home Kitchen Top Left":(-3.46475,.98,2.89),
        "Home Kitchen Counter Right":(-1.20475,.48,2.89),
        "Home Kitchen Top Right":(-1.20475,.98,2.89),
        "Home Refrigerator Cabinet Left":(-2.6595,1.12,2.9),
        "Home Refrigerator Cabinet Right":(-1.7095,1.12,2.9),
        "Home Refrigerator Cabinet Top":(-2.1845,2.125,2.9),
        "Home Refrigerator Cabinet Bottom":(-2.1845,.115,2.9),
        "Home Refrigerator Cabinet Back":(-2.1845,1.12,3.2025),
        "Home Refrigerator Door Enamel":(-2.1845,1.12,2.47),
        "Home Bathroom West Wall":(1.55,1.70,2.15),
        "Home Bathroom Tile Floor":(3.10,.012,2.15),
        "Home Bathroom Back Tile":(3.10,.88,3.868),
        "Home Bathroom Toilet Footprint":(4.15,.24,1.40),
        "Home Bathroom Toilet Bowl":(4.05,.49,1.40),
        "Home Bathroom Toilet Seat":(4.05,.62,1.40),
        "Home Bathroom Toilet Cistern":(4.49,.76,1.40),
        "Home Bathroom Shower Tray":(3.925,.09,2.925),
        "Home Bathroom Shower Basin":(3.925,.19,2.925),
        "Home Bathroom Shower Rim Front":(3.925,.225,2.375),
        "Home Bathroom Shower Rim Left":(3.375,.225,2.925),
        "Home Bathroom Sink Pedestal":(2.075,.36,3.425),
        "Home Bathroom Sink Basin":(2.075,.78,3.425),
        "Home Bathroom Sink Hollow":(1.995,.895,3.425),
        "Home Bathroom Cracked Mirror":(2.075,1.72,3.866),
        "Home Balcony Deck":(6.15,-.09,-1.45),
        "Home Balcony Threshold":(5,.045,-.5),
        "Home Balcony Outer Rail Cap":(7.25,1.085,-1.45),
        "Home Balcony South Rail Cap":(6.15,1.085,-3.35),
        "Home Balcony North Rail Cap":(6.15,1.085,.45),
    }
    collection=bpy.data.collections.new("COMPOSED_PREVIEW_Frames_1_to_7")
    bpy.context.scene.collection.children.link(collection)
    for part in b.parts:
        original=bpy.data.objects[part["name"]]
        original.hide_render=True;original.hide_set(True)
        if part["role"]!="decor" and part["name"] not in poses:continue
        obj=original.copy();obj.data=original.data.copy();obj.name="VIEW_"+part["name"]
        obj.parent=None;collection.objects.link(obj);obj.hide_set(False)
        obj.location=swap(part["position"] if part["role"]=="decor" else poses[part["name"]])
        if any(part["rotation"]):
            unity=([swap(v.co) for v in obj.data.vertices],[])
            for vertex,rotated in zip(obj.data.vertices,bp.u_rotated(unity,part["rotation"])[0]):
                vertex.co=swap(rotated)
        material=bpy.data.materials.new("VIEW_"+part["name"])
        material.diffuse_color=part["tint"];material.use_nodes=True
        bsdf=material.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value=part["tint"]
        bsdf.inputs["Roughness"].default_value=.84
        obj.data.materials.clear();obj.data.materials.append(material)
        for day in range(1,8):
            obj.hide_render=not part["min_day"]<=day<=part["max_day"]
            obj.hide_viewport=obj.hide_render
            obj.keyframe_insert("hide_render",frame=day)
            obj.keyframe_insert("hide_viewport",frame=day)
    scene=bpy.context.scene;scene.frame_start=1;scene.frame_end=7
    scene.render.engine="CYCLES";scene.cycles.samples=20
    scene.render.resolution_x=1200;scene.render.resolution_y=900
    scene.render.resolution_percentage=100
    scene.world.color=(.18,.18,.18)
    camera_data=bpy.data.cameras.new("AuthoringCamera")
    camera=bpy.data.objects.new("AuthoringCamera",camera_data);collection.objects.link(camera)
    camera.location=swap((10,11,-15));target=Vector(swap((.6,.15,0)))
    camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.type="ORTHO";camera.data.ortho_scale=15;scene.camera=camera
    for name,pos,power,size in (("AuthoringKey",(0,8,-2),1300,8),
                               ("AuthoringFill",(-3,5,3),650,5)):
        light_data=bpy.data.lights.new(name,"AREA");light_data.energy=power;light_data.shape="DISK";light_data.size=size
        light=bpy.data.objects.new(name,light_data);collection.objects.link(light)
        light.location=swap(pos);light.rotation_euler=(Vector((0,0,0))-light.location).to_track_quat("-Z","Y").to_euler()
    if render:
        for day in (1,7):
            scene.frame_set(day);scene.render.filepath=str(SOURCE/f"HomeInterior-Day{day}-Authoring.png")
            bpy.ops.render.render(write_still=True)
    scene.frame_set(1)


def main():
    argv=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    parser=argparse.ArgumentParser();parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--preview",action="store_true")
    args=parser.parse_args(argv)
    b=Build();add_bindings(b);add_decor(b);report=validate(b)
    canonical={"parts":b.parts,"geometry":b.geometry,"uv_pitch":PITCH}
    signature=hashlib.sha256(json.dumps(canonical,sort_keys=True,separators=(",",":"),ensure_ascii=True).encode()).hexdigest()
    print("HOME_INTERIOR_VALIDATED "+json.dumps(report)+" sha256="+signature,flush=True)
    if args.validate_only:return
    SOURCE.mkdir(parents=True,exist_ok=True);MODEL.parent.mkdir(parents=True,exist_ok=True)
    payload={"schema_version":1,"design_id":"home_interior_v1","generator_version":VERSION,
             "signature":signature,"coordinates":"Unity local metres +X east +Y up +Z north",
             "source_conversion":"Y/Z swap and face rewinding; FBX -Z forward Y up",
             "report":report,"parts":b.parts,
             "anchors":{"FridgeDoorPivot":[-2.6845,1.12,2.47],
                        "ShowerCurtainPivot":[3.40,0,2.384],
                        "LockedRoomDoor":[.68,1.1,.855]},
             "import_contract":{"fixed_mesh_scale":1,"parametric_size":"full metre envelope; cylinder caller Y is half-height",
                                "grid":"flat mattress: independent top quads and five rest faces; profiled pillow: top/bottom height samples and imported top vertex-to-sample mapping",
                                "dynamic_hierarchy":"runtime-owned; binding changes meshes only"}}
    (SOURCE/"home-interior-3d-model.json").write_text(json.dumps(payload,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    MODEL.with_suffix(".json").write_text(json.dumps(payload,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    bpy.ops.object.select_all(action="DESELECT");b.root.select_set(True)
    for part in b.parts:bpy.data.objects[part["name"]].select_set(True)
    bpy.context.view_layer.objects.active=b.root
    bpy.ops.export_scene.fbx(filepath=str(MODEL),use_selection=True,object_types={"EMPTY","MESH"},
                            axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",
                            bake_space_transform=False,add_leaf_bones=False,bake_anim=False,
                            use_mesh_modifiers=True,mesh_smooth_type="FACE",use_custom_props=True,
                            path_mode="STRIP",embed_textures=False)
    compose_source_preview(b,args.preview)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"HomeInterior3D.blend"))
    print("HOME_INTERIOR_EXPORTED "+str(MODEL),flush=True)


if __name__=="__main__":main()
