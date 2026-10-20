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
import home_interior_remodel as remodel

VERSION = "2.0.0"
SOURCE = ROOT / "ArtSource/Home/Interior"
MODEL = ROOT / "Assets/Home/Interior/Models/HomeInterior3D.fbx"
SHOWER_CURTAIN_FRONT_Z = 2.384
SHOWER_CURTAIN_WALL_Z = 3.857
SHOWER_CURTAIN_SIDE_COUNT = 5
SHOWER_CURTAIN_SIDE_WIDTH = .32
SHOWER_CURTAIN_END_OVERLAP = .012
SHOWER_BASIN_CENTER = (3.925, .19, 2.925)
SHOWER_DRAIN_POSITION = (4.32, .215, 3.32)
SHOWER_DRAIN_HOLE = (.395, .395)
SHOWER_WATER_POSITION = (3.925, .2135, 2.925)
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
PITCH.update({name: values[0] for name, values in remodel.SHEETS.items()})
COLORS.update({name: values[1] for name, values in remodel.SHEETS.items()})
BUILDER_NAMES = ("HomeInteriorWorldBuilder", "HomeBathroomBuilder",
                 "HomeBalconyWorldBuilder", "HomeAlarmClockBuilder",
                 "HomeRefrigeratorWorldBuilder")
# These retain their runtime names but use the sink's actual cross-wheel mesh.
# Do not leave obsolete round-handle aliases in the generic interior library.
EXTERNAL_BINDINGS = {
    "Home Bathroom Shower Mixer Handle Hot": "HomeBrushingAction/Models/FaucetHandle",
    "Home Bathroom Shower Mixer Handle Cold": "HomeBrushingAction/Models/FaucetHandle",
}
ENTRY_DOOR_PARTS = (
    "Home Exit Door", "Home Entry Door Recessed Panels",
    "Home Entry Door Panel Mouldings", "Home Entry Door Frame",
    "Home Entry Door Architraves", "Home Entry Door Threshold",
    "Home Entry Door Hinges", "Home Entry Door Handle Plates",
    "Home Entry Door Levers", "Home Entry Door Peephole",
    "Home Entry Door Hardware Insets", "Home Entry Door Edge Wear",
)


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


def closed_rings(rings):
    """Join a closed cross-section of Unity-space rings, retaining its hole."""
    count=len(rings[0]);vertices=[point for ring in rings for point in ring];faces=[]
    for row in range(len(rings)):
        next_row=(row+1)%len(rings)
        for i in range(count):
            next_i=(i+1)%count
            faces.append((row*count+i,next_row*count+i,next_row*count+next_i,row*count+next_i))
    return bp.to_source((vertices,faces))


def rectangular_drain_surface(size,hole_radius,bevel=0):
    """A closed slab around a real drain aperture; no polygon covers its hole."""
    x,y,z=size;cx,cz=SHOWER_DRAIN_HOLE
    angles={math.tau*i/48 for i in range(48)}
    # Include every rectangular corner at each bevel radius. A radial ring
    # without these angles would cut the slab's corner off diagonally.
    for inset in (0,bevel):
        for px in (-x*.5+inset,x*.5-inset):
            for pz in (-z*.5+inset,z*.5-inset):
                angles.add(math.atan2(pz-cz,px-cx)%math.tau)
    angles=sorted({round(angle,12) for angle in angles})
    def outer(height,inset):
        result=[]
        for angle in angles:
            dx,dz=math.cos(angle),math.sin(angle)
            distance=min((((x*.5-inset if dx>0 else -x*.5+inset)-cx)/dx) if abs(dx)>1e-9 else math.inf,
                         (((z*.5-inset if dz>0 else -z*.5+inset)-cz)/dz) if abs(dz)>1e-9 else math.inf)
            result.append((cx+dx*distance,height,cz+dz*distance))
        return result
    def inner(height):
        return [(cx+math.cos(a)*hole_radius,height,cz+math.sin(a)*hole_radius) for a in angles]
    rings=[outer(-y*.5,0)]
    if bevel:rings.append(outer(y*.5-bevel,0))
    rings.extend([outer(y*.5,bevel),inner(y*.5),inner(-y*.5)])
    return closed_rings(rings)


def shower_drain_grille():
    """A bevelled metal rim and five seated bars, with visible open slots."""
    profile=((.072,-.006),(.075,.002),(.073,.006),(.057,.006),(.055,.004),(.055,-.006))
    rings=[[(math.cos(math.tau*i/32)*radius,height,math.sin(math.tau*i/32)*radius)
            for i in range(32)] for radius,height in profile]
    pieces=[closed_rings(rings)]
    for x in (-.040,-.020,0,.020,.040):
        half_length=math.sqrt(.055**2-(abs(x)+.0045)**2)+.004
        pieces.append(kit.translated(box((.009,.010,half_length*2),.0015),swap((x,.001,0))))
    return kit.merge_all(pieces)


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
            grid_top_heights=(),grid_bottom_heights=(),uv_mode="metres"):
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
                if uv_mode=="full_image_xy":
                    uv.data[loop].uv=((p.y/size[2]+.5,p.z/size[1]+.5) if axis==0 else
                                      (p.x/size[0]+.5,p.z/size[1]+.5) if axis==1 else
                                      (p.x/size[0]+.5,p.y/size[2]+.5))
                else:
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
        if uv_mode!="metres":item["uv_mode"]=uv_mode
        for key in ("role","group","sheet","fit","min_day","max_day"):
            obj["bp_"+key]=item[key]
        self.parts.append(item);self.geometry[name]=geometry
        return item


def shower_side_position(index):
    """The fixed folded run reaches the tile, beyond the tray's back edge."""
    span = SHOWER_CURTAIN_WALL_Z - SHOWER_CURTAIN_FRONT_Z
    step = (span + 2 * SHOWER_CURTAIN_END_OVERLAP - SHOWER_CURTAIN_SIDE_WIDTH) / (SHOWER_CURTAIN_SIDE_COUNT - 1)
    return (3.36 + (-.012 if index % 2 == 0 else .012), 1.30,
            SHOWER_CURTAIN_FRONT_Z - SHOWER_CURTAIN_END_OVERLAP + SHOWER_CURTAIN_SIDE_WIDTH * .5 + index * step)


def add_entry_door(b):
    """A fixed-metre joinery assembly, shared by Home and the warehouse.

    The floor origin is the wall centre, with +Z facing the occupied room.
    Stiles and rails surround actual thin inset panels: there is no slab or
    contrasting rectangular patch masquerading as door construction.
    """
    def placed(name, geometry, sheet="DarkWood", tint=None):
        lo, hi = kit.bounds(geometry)
        centre = tuple((a+c)*.5 for a,c in zip(lo,hi))
        local = kit.translated(geometry, tuple(-v for v in centre))
        return b.add(name, tuple(c-a for a,c in zip(lo,hi)), sheet,
                     position=centre, geometry=bp.to_source(local), tint=tint)

    def cuboid(centre, size, bevel=.004):
        return bp.u_box(centre, size, bevel)

    def rod(centre, diameter, length, axis="y", sides=16):
        geometry = bp.u_cylinder((0,0,0), (diameter,length*.5,diameter), sides)
        if axis=="x": geometry=bp.u_rotated(geometry,(0,0,90))
        if axis=="z": geometry=bp.u_rotated(geometry,(90,0,0))
        return kit.translated(geometry,centre)

    # The familiar four-panel proportions keep the grip on a solid lock stile.
    # Every panel has a physical recess on both faces and a bevelled moulding.
    stiles=[cuboid((side*.49,1.115,0),(.14,2.16,.052)) for side in (-1,1)]
    # The muntin extends beneath both adjacent rails. Butt-ending a bevelled
    # member at the upper panel edge left a small through-notch in the leaf.
    stiles.append(cuboid((0,1.135,0),(.075,1.88,.052)))
    for y, height in ((.125,.18),(.825,.14),(2.125,.14)):
        stiles.append(cuboid((0,y,0),(.84,height,.052)))
    placed("Home Exit Door",kit.merge_all(stiles),tint=(.245,.145,.078,1))
    panels=[]; mouldings=[]
    for x in (-.22875,.22875):
        for y, height in ((.485,.54),(1.475,1.16)):
            width=.3825
            panels.append(cuboid((x,y,0),(width,height,.020),.005))
            for side in (-1,1):
                z=side*.018
                for sx in (-1,1):
                    mouldings.append(cuboid((x+sx*(width*.5-.010),y,z),(.020,height,.016),.003))
                for sy in (-1,1):
                    mouldings.append(cuboid((x,y+sy*(height*.5-.010),z),(width-.040,.020,.016),.003))
    placed("Home Entry Door Recessed Panels",kit.merge_all(panels),tint=(.214,.121,.064,1))
    placed("Home Entry Door Panel Mouldings",kit.merge_all(mouldings),tint=(.27,.164,.09,1))

    # Lining occupies the existing wall thickness, with a small dark reveal
    # around the leaf. The broad old opening is closed by Home's wall infills.
    frame_geometry=kit.door_frame(1.132,2.206,.22,jamb=.094,chamfer=.006)
    b.add("Home Entry Door Frame",(1.32,2.30,.22),"DarkWood",
          position=(0,1.15,0),geometry=kit.translated(frame_geometry,(0,0,-1.15)),
          tint=(.18,.104,.057,1))
    architraves=[]
    for side in (-1,1):
        for sx in (-1,1):
            architraves.append(cuboid((sx*.698,1.15,side*.133),(.124,2.30,.034),.006))
        architraves.append(cuboid((0,2.255,side*.133),(1.272,.09,.034),.006))
    placed("Home Entry Door Architraves",kit.merge_all(architraves),tint=(.23,.136,.073,1))
    placed("Home Entry Door Threshold",cuboid((0,.018,0),(1.132,.036,.24),.004),
           tint=(.16,.105,.066,1))

    hinges=[]
    for y in (.34,1.115,1.89):
        for side in (-1,1):
            # A restrained two-leaf hinge: seated plates, three real knuckles.
            hinges.append(cuboid((-.567,y,side*.031),(.065,.096,.008),.002))
            for offset in (-.033,0,.033):
                hinges.append(rod((-.569,y+offset,side*.039),.018,.030))
    metal=(.29,.265,.20,1)
    placed("Home Entry Door Hinges",kit.merge_all(hinges),"PaintedMetal",metal)
    plates=[];levers=[];insets=[];peepholes=[]
    for side in (-1,1):
        z=side*.032
        plates.append(cuboid((.475,1.015,z),(.055,.214,.010),.010))
        for y in (.925,1.105):
            plates.append(rod((.475,y,side*.039),.009,.004,"z",12))
            insets.append(cuboid((.475,y,side*.0415),(.006,.0017,.001),.0003))
        levers.append(rod((.475,1.065,side*.065),.026,.062,"z"))
        # The lever runs towards the centre, with a rounded terminal and an
        # upward bent end. It reads as working hardware from either side.
        levers.append(rod((.414,1.065,side*.103),.020,.136,"x"))
        levers.append(rod((.347,1.074,side*.103),.020,.027))
        insets.append(rod((.475,.985,side*.038),.013,.003,"z",12))
        insets.append(cuboid((.475,.976,side*.039),(.007,.017,.002),.001))
        peepholes.append(rod((0,1.58,side*.032),.035,.014,"z",20))
        insets.append(rod((0,1.58,side*.0395),.018,.002,"z",16))
    placed("Home Entry Door Handle Plates",kit.merge_all(plates),"PaintedMetal",metal)
    placed("Home Entry Door Levers",kit.merge_all(levers),"PaintedMetal",(.35,.32,.25,1))
    placed("Home Entry Door Peephole",kit.merge_all(peepholes),"PaintedMetal",metal)
    placed("Home Entry Door Hardware Insets",kit.merge_all(insets),"Plain",(.035,.030,.022,1))

    # Small worn edges concentrate where the grip and floor would touch them.
    # They do not turn the household object into a marked or symbolic prop.
    wear=[]
    for side in (-1,1):
        for x,y,width,height in ((.526,1.03,.006,.048),(.508,.945,.004,.028),
                                 (-.41,.059,.052,.003),(.31,.063,.085,.0025),
                                 (.537,.19,.003,.061),(-.546,.37,.003,.037)):
            wear.append(cuboid((x,y,side*.0265),(width,height,.0015),.0004))
    placed("Home Entry Door Edge Wear",kit.merge_all(wear),"DarkWood",(.31,.205,.118,1))


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
        "Home Entry Wall Left Door Infill":((.64,2.64,.24),"Wallpaper"),
        "Home Entry Wall Right Door Infill":((.64,2.64,.24),"Wallpaper"),
        "Home Entry Door Transom Infill":((1.32,.34,.24),"Wallpaper"),
        "Home Entry Rug":((1.55,.03,1.35),"Rug"),
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
    remodel.configure_fixed(fixed)
    for name,(size,sheet) in fixed.items():
        geom=None
        if "Blanket" in name or "Coat" in name or "Cushion" in name:
            geom=cloth(size)
        if name=="Home Battered Cabinet":geom=remodel.cabinet(size)
        if name=="Home Table Base Crooked":
            geom=cylinder(size,((1,-.5),(1,-.46),(.62,-.42),(.52,.38),(.70,.45),(1,.5)))
        if name=="Home Bathroom Shower Basin":geom=rectangular_drain_surface(size,.060,.012)
        if name=="Home Kitchen Top Left":geom=remodel.sink_countertop(size)
        b.add(name,size,sheet,geometry=geom)
    remodel.add_bindings(b)
    b.add("Home Sink",(.85,.18,.55),"Enamel",geometry=remodel.sink_bowl())
    add_entry_door(b)
    b.add("Home Bathroom Shower Drain",(.15,.012,.15),"PaintedMetal",
          position=SHOWER_DRAIN_POSITION,tint=(.38,.39,.34,1),geometry=shower_drain_grille())
    b.add("Home Bathroom Shower Drain Well",(.12,.030,.12),"Plain",
          position=(SHOWER_DRAIN_POSITION[0],.197,SHOWER_DRAIN_POSITION[2]),tint=(.065,.075,.065,1),
          geometry=kit.lathe(((.060,-.015),(.060,.015),(.054,.015),(.050,-.010),(.001,-.010)),32))
    b.add("Home Bathroom Shower Collected Water",(1,.002,1),"Plain",
          position=SHOWER_WATER_POSITION,tint=(.30,.38,.34,1),
          geometry=rectangular_drain_surface((1,.002,1),.075))
    for i in range(SHOWER_CURTAIN_SIDE_COUNT):
        b.add(f"Home Bathroom Shower Curtain Side {i + 1}",
              (.03,1.82,SHOWER_CURTAIN_SIDE_WIDTH), "BedLinen",
              position=shower_side_position(i))
    for name,size,cols,rows,depth in (
            ("Home Bed Mattress",(2.39,.18,1.57*remodel.SOFA_DEPTH_SCALE),17,11,.10),):
        b.add(name,size,"BedLinen",role="grid",semantic=name,
              geometry=grid(size,cols,rows),grid_columns=cols,grid_rows=rows,max_depth=depth)
    size=(.62,.14,1.05*remodel.SOFA_DEPTH_SCALE); cols,rows=8,14
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
    # This existing literal moved to a constant-position call; retain its
    # fixed shape instead of letting the two-vector scanner drop the binding.
    b.add("Home Bathroom Shower Head",(.15,.12,.15),geometry=cylinder((.15,.12,.15)))
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
            elif label not in EXTERNAL_BINDINGS:exact.add(label)
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
            if len(vectors)>=2 and match.group(2) not in fixed and match.group(2) not in EXTERNAL_BINDINGS:
                vector=vectors[1];size=tuple(float(v) for v in vector)
                if min(size)>0:
                    if match.group(1) in ("CreateCylinder","CreatePipe"):
                        size=(size[0],size[1]*2,size[2]);kind="cylinder"
                    else:kind="box"
                    literal_sizes[match.group(2)]=(size,kind)
    bound={p["semantic_name"] for p in b.parts}
    for name,(size,kind) in sorted(literal_sizes.items()):
        if name.startswith("Home Camera Corner "):continue
        if name in bound:continue
        b.add(name,size,geometry=cylinder(size) if kind=="cylinder" else box(size))
        bound.add(name)
    # Helper-name suffixes are explicit: authoring patterns are bounded to a
    # known assembly, preventing a misspelled/unknown furniture silently fitting.
    patterns.update((r"^Alarm Clock Digit [0-3] Segment [0-6]$",
                     r"^Home Balcony (Window|Door) (Frame|Glass|Handle).*$",
                     r"^Player Home (Lower Window|Street Entry).*$",
                     r"^Player Home Authored Front Window Glass .*$"))
    unresolved=sorted(name for name in exact-bound if not name.startswith("Home Camera Corner "))
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
    remodel.add_decor(b, cloth, dish, cylinder, irregular_patch)


def validate(b):
    errors=[];triangles=0
    # Inspect the actual exported front surfaces. A full flat slab behind
    # applique trim would pass bounds/count checks while losing the recess.
    def door_depths(x,y):
        depths=[]
        for name in ENTRY_DOOR_PARTS[:3]:
            p=next(part for part in b.parts if part["name"]==name)
            vertices,faces=b.geometry[name]
            vertices=[tuple(a+c for a,c in zip(swap(v),p["position"])) for v in vertices]
            for face in faces:
                for i in range(1,len(face)-1):
                    a,c,d=(vertices[j] for j in (face[0],face[i],face[i+1]))
                    dx,dy=c[0]-a[0],c[1]-a[1];ex,ey=d[0]-a[0],d[1]-a[1]
                    determinant=dx*ey-dy*ex
                    if abs(determinant)<1e-12:continue
                    u=((x-a[0])*ey-(y-a[1])*ex)/determinant
                    v=(dx*(y-a[1])-dy*(x-a[0]))/determinant
                    if u>=-1e-8 and v>=-1e-8 and u+v<=1+1e-8:
                        depths.append(a[2]+u*(c[2]-a[2])+v*(d[2]-a[2]))
        return depths
    for x in (-.22875,.22875):
        for y,height in ((.485,.54),(1.475,1.16)):
            panel=door_depths(x,y);stile=door_depths(.49,y)
            if not panel or not stile or max(stile)-max(panel)<.015:
                errors.append("entry door has no real recessed panel")
            # Probe inside each visible aperture corner, where a bevelled
            # panel alone might leave daylight. The seated mouldings close
            # these joints on both sides without filling the visible recess.
            for sx in (-1,1):
                for sy in (-1,1):
                    corner=door_depths(x+sx*(.3825*.5-.0005),y+sy*(height*.5-.0005))
                    if not corner or min(corner)>=0 or max(corner)<=0:
                        errors.append("entry door has an open panel corner")
    for x,y in ((0,2.04),(0,.22)):
        seam=door_depths(x,y)
        if not seam or min(seam)>-.025 or max(seam)<.025:
            errors.append("entry door centre stile leaves an open rail joint")
    if door_depths(.563,1.5):errors.append("entry door leaf blocks its jamb clearance")
    for name in ENTRY_DOOR_PARTS:
        p=next(part for part in b.parts if part["name"]==name)
        if p["fit"]!="fixed" or p["role"]!="binding":errors.append("entry door is not a fixed shared binding")
        if p["bounds_min"][2]+p["position"][2]<-.15001 or p["bounds_max"][2]+p["position"][2]>.15001:
            errors.append("entry door exceeds its wall-depth contract")
    def vertical_hits(name,x,z):
        vertices,faces=b.geometry[name];vertices=[swap(v) for v in vertices];heights=[]
        for face in faces:
            for i in range(1,len(face)-1):
                a,c,d=(vertices[j] for j in (face[0],face[i],face[i+1]))
                dx,dz=c[0]-a[0],c[2]-a[2];ex,ez=d[0]-a[0],d[2]-a[2]
                determinant=dx*ez-dz*ex
                if abs(determinant)<1e-12:continue
                u=((x-a[0])*ez-(z-a[2])*ex)/determinant
                v=(dx*(z-a[2])-dz*(x-a[0]))/determinant
                if u>=-1e-8 and v>=-1e-8 and u+v<=1+1e-8:
                    heights.append(a[1]+u*(c[1]-a[1])+v*(d[1]-a[1]))
        return heights
    # Ray-test the exported solids themselves: the drain is not a painted
    # disc, and neither the enamel floor nor the water covers its opening.
    if vertical_hits("Home Kitchen Top Left",-.08463,0):
        errors.append("kitchen counter caps its sink opening")
    sink_floor=vertical_hits("Home Sink",0,0)
    if not sink_floor or max(sink_floor)>-.064:
        errors.append("kitchen sink has no recessed closed basin")
    if not vertical_hits("Home Sink",.405,0):
        errors.append("kitchen sink rim does not close the countertop cutout")
    chair_seat=vertical_hits("Dining Chair",0,.015)
    if not chair_seat or abs(max(chair_seat)+.03)>.001:
        errors.append("dining chair seat disagrees with its world support anchor")
    for surface in ("Home Bathroom Shower Basin","Home Bathroom Shower Collected Water"):
        if vertical_hits(surface,*SHOWER_DRAIN_HOLE):errors.append(surface+": drain aperture is capped")
    for x in (.010,.030,-.010,-.030):
        if vertical_hits("Home Bathroom Shower Drain",x,0):errors.append("shower grate slot is capped")
    if not vertical_hits("Home Bathroom Shower Drain",0,0):errors.append("shower drain is missing its central bar")
    well=vertical_hits("Home Bathroom Shower Drain Well",.010,0)
    if not well or max(well)>-.009:errors.append("shower drain well lacks visible recess")
    if min(.505-SHOWER_DRAIN_HOLE[0],.505-SHOWER_DRAIN_HOLE[1])-.075<.034:
        errors.append("shower drain overlaps the basin's far corner bevel")
    for p in b.parts:
        names=[p["name"],p["semantic_name"],*p["aliases"]]
        if any(name in EXTERNAL_BINDINGS for name in names):
            errors.append("shower valve duplicates its shared sink resource")
        if any(name.startswith("Home Bathroom Shower Hose ") for name in names) or any(
                re.fullmatch(pattern,"Home Bathroom Shower Hose 1") for pattern in p["patterns"]):
            errors.append("removed shower hose still has an interior binding")
    painting=bpy.data.objects["Home Weaver Painting Image"].data
    front=[poly for poly in painting.polygons if poly.normal.y<-.99]
    if len(front)!=1:errors.append("painting image lacks a south-facing complete front")
    else:
        for loop in front[0].loop_indices:
            vertex=painting.vertices[painting.loops[loop].vertex_index].co
            actual=painting.uv_layers.active.data[loop].uv
            expected=(vertex.x/remodel.PAINTING_IMAGE_SIZE[0]+.5,
                      vertex.z/remodel.PAINTING_IMAGE_SIZE[1]+.5)
            if max(abs(a-e) for a,e in zip(actual,expected))>1e-6:
                errors.append("painting front is cropped or mirrored")
    frame_vertices,frame_faces=b.geometry["Home Weaver Painting Frame"]
    for face in frame_faces:
        for i in range(1,len(face)-1):
            a,c,d=(swap(frame_vertices[index]) for index in (face[0],face[i],face[i+1]))
            dx,dy=c[0]-a[0],c[1]-a[1];ex,ey=d[0]-a[0],d[1]-a[1]
            determinant=dx*ey-dy*ex
            if abs(determinant)<1e-12:continue
            u=(-a[0]*ey+a[1]*ex)/determinant
            v=(-dx*a[1]+dy*a[0])/determinant
            if u>=-1e-8 and v>=-1e-8 and u+v<=1+1e-8:
                errors.append("painting frame fills its image aperture")
    # Measure the actual authored folds in their planned placement, including
    # overlaps, instead of accepting a count that can still leave a wall gap.
    side_folds=sorted((p for p in b.parts if p["name"].startswith("Home Bathroom Shower Curtain Side ")),
                      key=lambda p:p["position"][2])
    intervals=[(p["position"][2]+p["bounds_min"][2],p["position"][2]+p["bounds_max"][2]) for p in side_folds]
    if len(intervals)!=SHOWER_CURTAIN_SIDE_COUNT or intervals[0][0]>SHOWER_CURTAIN_FRONT_Z or intervals[-1][1]<SHOWER_CURTAIN_WALL_Z:
        errors.append("shower side curtain does not cover entrance-to-tile span")
    if any(left[1]-right[0]<.024 for left,right in zip(intervals,intervals[1:])):
        errors.append("shower side curtain folds do not overlap beyond their bevels")
    # The same plan-derived rectangles as HomeApartmentDressing. Coarse
    # gameplay reserves remain independent from authored furniture surfaces.
    paths=[("entry",(-.80,-3.65,.80,-1.50)),
           ("main",(-.80,-3.65,2.82,.82)),
           ("bathroom-access",(1.72,.50,2.82,3.15)),
           ("balcony-access",(2.55,-1.34,4.65,.34)),
           ("sofa-approach",(-5.58,-.39,-3.425,.31)),
           ("fridge-dock",(-4.9345,1.43,-4.1145,2.17))]
    waypoints=[(-.62,-2.15),(-3.775,-2.15),(-3.775,1.745),(-4.5245,1.80)]
    for i,(a,c) in enumerate(zip(waypoints,waypoints[1:])):
        paths.append((f"fridge-route-{i}",(min(a[0],c[0])-.35,min(a[1],c[1])-.35,
                      max(a[0],c[0])+.35,max(a[1],c[1])+.35)))
    supports={"home.bed.surface-clutter":remodel.SLEEP,
              "home.furniture.bookcase":remodel.BOOKCASE,
              "home.furniture.kitchen.left":(-6.67,2.35,-5.2195,3.34)}
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
            if not p["collider"] and .045<hi[1] and lo[1]<.20:
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
            "shower_side_curtain_closed_to_wall":True,
            "shower_hose_bindings_removed":True,
            "shower_valves_use_shared_sink_resource":True,
            "shower_drain_open_grate_and_recess":True,
            "shower_basin_and_water_drain_apertures":True,
            "entry_door_real_panels_and_leaf_clearance":True,
            "entry_door_joinery_closed":True,
            "kitchen_sink_hollow_and_counter_open":True,
            "dining_chair_measured_seat_contact":True,
            "painting_full_image_uv_and_open_frame":True,
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
        "Home Bathroom Shower Drain":SHOWER_DRAIN_POSITION,
        "Home Bathroom Shower Drain Well":(SHOWER_DRAIN_POSITION[0],.197,SHOWER_DRAIN_POSITION[2]),
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
    poses.update(remodel.preview_poses())
    for name in ("Home Sofa Base", "Home Sofa Back", "Home Sofa Sunken Cushion", "Home Table Base Crooked",
                 "Home Camera Corner Junk Base","Home Camera Corner Broken Wardrobe Door",
                 "Home Camera Corner Suitcase","Home Camera Corner Old Coat"):
        poses.pop(name, None)
    poses.update({f"Home Bathroom Shower Curtain Side {i + 1}":shower_side_position(i)
                  for i in range(SHOWER_CURTAIN_SIDE_COUNT)})
    poses.update({part["name"]:(part["position"][0],part["position"][1],part["position"][2]-4)
                  for part in b.parts if part["name"] in ENTRY_DOOR_PARTS})
    collection=bpy.data.collections.new("COMPOSED_PREVIEW_Frames_1_to_7")
    bpy.context.scene.collection.children.link(collection)
    for part in b.parts:
        original=bpy.data.objects[part["name"]]
        original.hide_render=True;original.hide_set(True)
        if part["role"]!="decor" and part["name"] not in poses:continue
        obj=original.copy();obj.data=original.data.copy();obj.name="VIEW_"+part["name"]
        obj.parent=None;collection.objects.link(obj);obj.hide_set(False)
        obj.location=swap(part["position"] if part["role"]=="decor" else poses[part["name"]])
        rotation=remodel.preview_rotations().get(part["name"],part["rotation"])
        if any(rotation):
            unity=([swap(v.co) for v in obj.data.vertices],[])
            for vertex,rotated in zip(obj.data.vertices,bp.u_rotated(unity,rotation)[0]):
                vertex.co=swap(rotated)
        material=bpy.data.materials.new("VIEW_"+part["name"])
        material.diffuse_color=part["tint"];material.use_nodes=True
        bsdf=material.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value=part["tint"]
        bsdf.inputs["Roughness"].default_value=.84
        if part["name"]=="Home Weaver Painting Image":
            texture=material.node_tree.nodes.new("ShaderNodeTexImage")
            texture.image=bpy.data.images.load(str(ROOT/remodel.PAINTING_TEXTURE),check_existing=True)
            texture.image.pack()
            material.node_tree.links.new(texture.outputs["Color"],bsdf.inputs["Base Color"])
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
    camera.location=swap((10,11,-15));target=Vector(swap((-1.0,.7,.25)))
    camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.type="ORTHO";camera.data.ortho_scale=16.6;scene.camera=camera
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
    global SOURCE, MODEL
    argv=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    parser=argparse.ArgumentParser();parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--preview",action="store_true")
    parser.add_argument("--source-dir",type=Path,default=SOURCE)
    parser.add_argument("--model-dir",type=Path,default=MODEL.parent)
    args=parser.parse_args(argv)
    SOURCE=args.source_dir.resolve();MODEL=args.model_dir.resolve()/"HomeInterior3D.fbx"
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
                        "ShowerDrain":list(SHOWER_DRAIN_POSITION),
                        "ShowerCollectedWater":list(SHOWER_WATER_POSITION),
                        "EntryDoorFloor":[0,0,-4],
                        "LockedRoomDoor":[.68,1.1,.855],
                        "DiningChairSeat":list(remodel.SEAT_TOP)},
             "remodel_contract":{"shell_bounds_xz":list(remodel.SHELL),"locked_bounds_xz":list(remodel.LOCKED),
                                  "sleep_bounds_xz":list(remodel.SLEEP),"table_bounds_xz":list(remodel.TABLE),
                                  "chair_position":list(remodel.CHAIR_POSITION),"sofa_support_heights_unchanged":True,
                                  "sofa_depth":remodel.SOFA_DEPTH,"sleep_cross_axis_scale":remodel.SOFA_DEPTH_SCALE,
                                  "sleep_assembly_yaw":-90,"sleep_head":"south","sleep_back":"west",
                                  "alarm_clock_nightstand_xz":[-6.285,-1.84],
                                  "bookcase_bounds_xz":list(remodel.BOOKCASE),"bookcase_yaw":-90,
                                  "camera_corner_junk_removed":True,
                                  "painting_image_uv":"full image, U eastward, V upward; south-facing front",
                                  "painting_frame_center":list(remodel.PAINTING_FRAME_CENTER),
                                  "painting_image_center":list(remodel.PAINTING_IMAGE_CENTER),
                                  "bathroom_and_balcony_retained":True},
             "import_contract":{"fixed_mesh_scale":1,"parametric_size":"full metre envelope; cylinder caller Y is half-height",
                                "external_bindings":EXTERNAL_BINDINGS,
                                "entry_door":{"parts":list(ENTRY_DOOR_PARTS),
                                              "origin":"floor at wall centre; +Z occupied-room face",
                                              "outer_frame":[1.32,2.30,.22],
                                              "architrave_bounds":[[-.76,0,-.15],[.76,2.30,.15]],
                                              "leaf":[1.12,2.16,.052],
                                              "leaf_position":[0,1.115,0],
                                              "panel_count":4,"minimum_recess":.016},
                                "shower_water":{"basin_top_y":.2125,"water_top_y":.2145,
                                                "drain_xz":[SHOWER_DRAIN_POSITION[0],SHOWER_DRAIN_POSITION[2]],
                                                "basin_hole_radius":.060,"water_hole_radius":.075,
                                                "water_uv":"Unity local XZ metres, centred on the mesh origin",
                                                "water_visibility":"runtime water effect; hidden at construction"},
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
