#!/usr/bin/env python3
"""Deterministic passive pawnshop models; Unity owns actors, collisions and lights.

Unity metres: +Z runs from the centred entrance toward the secure rear room.
Uses the shared interior kit, re-winds the reflected Y/Z conversion and checks
the complete geometry twice before writing the measured model manifests.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as bp
import interior_kit as kit

VERSION = "1.1.0"
PALETTE = {
    "Plaster": ((.48, .54, .49, 1), "SupermarketWallPaint", .1, 0),
    "Cream": ((.76, .75, .63, 1), "SupermarketWallPaint", .1, 0),
    "Concrete": ((.32, .35, .33, 1), "CityFringeConcrete", .1, 0),
    "Metal": ((.18, .23, .21, 1), "SupermarketShelfMetal", .23, .4),
    "PaleMetal": ((.56, .59, .52, 1), "PawnshopZinc", .3, .5),
    "Floor": ((.62, .66, .59, 1), "PawnshopFloor", .16, 0),
    "Wood": ((.33, .25, .19, 1), "SupermarketCounter", .28, 0),
    "Cardboard": ((.65, .56, .41, 1), "SupermarketCardboard", .06, 0),
    "Glass": ((.46, .53, .49, .34), "PawnshopGlass", .38, 0),
    "Tray": ((.69, .72, .69, 1), "PawnshopTray", .32, .38),
    "CounterLaminate": ((.90, .88, .80, 1), "PawnshopCounter", .28, 0),
    "Dark": ((.07, .09, .085, 1), "", .13, 0),
    "Letters": ((.83, .81, .65, 1), "", .16, 0),
    "Light": ((.88, .91, .79, 1), "", .1, 0),
    "WarmLight": ((.96, .77, .47, 1), "", .12, 0),
    "MatteSign": ((.18, .23, .21, 1), "SupermarketShelfMetal", .03, 0),
    "MatteLetters": ((.83, .81, .65, 1), "", .03, 0),
}


def box(center, size, bevel=.012):
    return bp.u_box(center, size, min(bevel, min(size) * .2))


def union(items):
    return kit.merge_all(items)


def translated(geometry, position):
    return kit.translated(geometry, position)


def kit_unity(geometry):
    return bp.to_source(geometry)


class Model:
    def __init__(self, kind):
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        self.kind = kind
        self.parts = []
        self.anchors = {}
        self.root = bpy.data.objects.new("ROOT_Pawnshop" + kind + "3D", None)
        bpy.context.collection.objects.link(self.root)

    def anchor(self, name, position):
        obj = bpy.data.objects.new("ANCHOR_" + name, None)
        bpy.context.collection.objects.link(obj)
        obj.parent = self.root
        obj.location = (position[0], position[2], position[1])
        obj.empty_display_type = "ARROWS"
        self.anchors[name] = {"name": obj.name, "position": list(position)}
        return obj

    def add(self, name, geometry, material, parent=None, pivot=None, shadow=True):
        # Every piece is an outward-wound solid. A union may contain separated
        # solids; its positive volume is checked before and after reflection.
        assert bp.signed_volume(geometry) > 1e-10, name + " has inverted/empty volume"
        original = geometry
        if pivot is not None:
            geometry = translated(geometry, tuple(-v for v in pivot))
        source = bp.to_source(geometry)
        assert bp.signed_volume(source) > 1e-10, name + " reflection reversed winding"
        mesh = bpy.data.meshes.new(name + "_Mesh")
        mesh.from_pydata(*[source[0], [], source[1]])
        mesh.update(calc_edges=True)
        uv = mesh.uv_layers.new(name="UVMap")
        # Metric triplanar-style box projection authored into UV0. Existing
        # texture/response sheets retain their own consistent physical scale.
        pitch = .53 if material == "PaleMetal" else 2.4 if material in ("Plaster", "Cream") else 1.2
        for face in mesh.polygons:
            n = face.normal
            axis = max(range(3), key=lambda i: abs(n[i]))
            # Blender's Z axis is height. Glass panels each have their own
            # protected edges, including the top and the ends of a case.
            axes = ((1,2),(0,2),(0,1))[axis]
            corners = [mesh.vertices[mesh.loops[i].vertex_index].co for i in face.loop_indices]
            if material == "Glass":
                ranges = [(min(v[k] for v in corners),max(v[k] for v in corners)) for k in axes]
            for loop,v in zip(face.loop_indices,corners):
                x,z,y = v
                if material == "Glass":
                    coord = tuple((v[k]-lo)/max(hi-lo,.00001) for k,(lo,hi) in zip(axes,ranges))
                elif material == "Floor":
                    coord = ((x+3.85)/7.7,(z+4.35)/8.7)
                elif material == "Tray":
                    coord = ((x+.376)/.752,(z-.065)/.845)
                elif material == "CounterLaminate":
                    coord = ((x+2.76)/5.12,z/1.30)
                else:
                    coord = tuple(v[k]/pitch for k in axes)
                uv.data[loop].uv = coord
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = parent or self.root
        self.parts.append({"name": name, "material": material,
                           "shadows": shadow, "vertices": original[0], "faces": original[1]})
        return obj

    def bounds(self):
        vertices = [v for p in self.parts for v in p["vertices"]]
        return {"min": [round(min(v[i] for v in vertices), 6) for i in range(3)],
                "max": [round(max(v[i] for v in vertices), 6) for i in range(3)]}

    def signature(self):
        return hashlib.sha256(json.dumps({"version": VERSION, "parts": self.parts, "anchors": self.anchors, "palette": PALETTE},
                                         sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def frame(width, height, thick, center):
    return translated(kit_unity(kit.door_frame(width, height, thick, jamb=.065, architrave=.018)), center)


def rail(start, end, diameter, sides=12):
    from mathutils import Vector
    delta = Vector(end) - Vector(start)
    rotation = Vector((0,1,0)).rotation_difference(delta.normalized())
    vertices, faces = bp.u_cylinder((0,0,0),(diameter,delta.length/2,diameter),sides)
    centre = (Vector(start) + Vector(end)) / 2
    return [tuple(centre + rotation @ Vector(v)) for v in vertices], faces


def folded_section(profile, z0, z1):
    area = sum(a*d-c*b for (a,b),(c,d) in zip(profile,profile[1:]+profile[:1]))
    section = kit.prism(profile if area > 0 else profile[::-1], z1-z0)
    return translated(kit_unity(section),(0,0,(z0+z1)/2))


def door(model, z, width=1.4, height=2.35):
    pivot_position = (-width * .5, 0, z)
    pivot = model.anchor("DoorPivot", pivot_position)
    model.anchor("DoorHandle", (width * .5 - .14, 1.05, z - .065))
    leaf = translated(kit_unity(kit.panelled_leaf(width - .025, height - .025, .065,
                                                panels=2, stile=.12)), pivot_position)
    model.add("PART_DoorLeaf", leaf, "Metal", pivot, pivot_position)
    model.add("PART_DoorKickPlate", box((0, .24, z - .046), (1.1, .26, .018)),
              "PaleMetal", pivot, pivot_position)
    for suffix,side in (("Outside",-1),("Inside",1)):
        def point(y, reach):
            return (.56,y,z+side*reach)
        path = [point(.932,.033),point(.932,.072),point(.948,.089),
                point(1.152,.089),point(1.168,.072),point(1.168,.033)]
        handle = [rail(a,b,.024) for a,b in zip(path,path[1:])]
        for y in (.932,1.168):
            handle.append(rail(point(y,.027),point(y,.045),.056))
        model.add("PART_DoorPull"+suffix,union(handle),"PaleMetal",pivot,pivot_position)
    model.add("PART_EntranceFrame", frame(width, height, .2, (0, 0, z)), "Metal")
    # The foot crosses a shallow folded saddle. Ribs stay below step clearance.
    model.add("PART_Threshold", union([box((0,.006,z),(1.39,.012,.26),.003)] +
        [box((0,.014,z+offset),(1.33,.005,.009),.002) for offset in (-.09,-.055,.055,.09)]),"PaleMetal")


def glyph(letter, cx, cy, z, w=.45, h=.63):
    # A small explicit Cyrillic stroke alphabet keeps the sign local, stable
    # and independent of installed fonts. No other fiction text is added.
    s=.065
    def b(x,y,sx,sy,a=0):
        g=box((cx+x,cy+y,z),(sx,sy,.018),.003)
        return bp.u_rotated_about(g,(0,0,a),(cx+x,cy+y,z)) if a else g
    l,r,t,d=-w/2+s/2,w/2-s/2,h/2-s/2,-h/2+s/2
    if letter=="Л": items=[b(l+.06,0,s,h,-13),b(r,0,s,h),b(0,t,w*.75,s)]
    elif letter=="О": items=[b(l,0,s,h),b(r,0,s,h),b(0,t,w,s),b(0,d,w,s)]
    elif letter=="М": items=[b(l,0,s,h),b(r,0,s,h),b(-.08,.10,s,h*.65,24),b(.08,.10,s,h*.65,-24)]
    elif letter=="Б": items=[b(l,0,s,h),b(0,t,w,s),b(0,0,w,s),b(0,d,w,s),b(r,-h*.25,s,h*.5)]
    elif letter=="А": items=[b(l+.04,0,s,h,-9),b(r-.04,0,s,h,9),b(0,t,w*.7,s),b(0,-.05,w*.85,s)]
    elif letter=="Р": items=[b(l,0,s,h),b(0,t,w,s),b(0,0,w,s),b(r,h*.25,s,h*.5)]
    elif letter=="Д": items=[b(l+.06,.02,s,h*.85,-8),b(r-.05,.02,s,h*.85),b(0,t,w*.75,s),b(0,d,w,s),b(l,d-.06,s,.15),b(r,d-.06,s,.15)]
    else: raise ValueError(letter)
    return union(items)


def facade_practical(model, name, position, yaw):
    """A small wall-mounted downlight, facing local +Z before placement.

    The anchor sits at the real underside diffuser. Its cap and side cheeks
    keep the working fixture from becoming an unshielded decorative beacon.
    Wall and street illumination are owned by the existing runtime light pool.
    """
    def placed(geometry):
        return translated(bp.u_rotated(geometry, (0, yaw, 0)), position)
    hood=union([box((0,.07,.03),(.48,.10,.32),.018),
                box((-.235,.005,.025),(.025,.09,.30),.004),
                box((.235,.005,.025),(.025,.09,.30),.004)])
    mount=union([box((0,.14,-.10),(.20,.34,.04),.009),
                 box((0,.10,-.03),(.08,.08,.16),.009)])
    model.add("PART_FacadeHood"+name,placed(hood),"Metal")
    model.add("PART_FacadeMount"+name,placed(mount),"Metal")
    model.add("PART_FacadePractical"+name,placed(box((0,0,0),(.37,.025,.21),.004)),
              "Light",shadow=False)
    model.anchor("FacadeLight"+name,position)


def sign_projector(model,name,position,target):
    """Ordinary shielded sign lamp on a bolted arm, shining toward the board.

    A lens-centred measured anchor lets runtime aim from the true fixture.
    Opaque backs and edge hoods hide the diffuser from the street approach.
    """
    from mathutils import Vector
    direction=(Vector(target)-Vector(position)).normalized()
    # Author in Unity's Y-up frame; Blender's tracking helper assumes Z-up
    # and would roll these broad rectangular heads diagonally over the roof.
    right=Vector((0,1,0)).cross(direction).normalized()
    up=direction.cross(right).normalized()
    def placed(geometry):
        vertices,faces=geometry
        return ([tuple(right*v[0]+up*v[1]+direction*v[2]+Vector(position)) for v in vertices],faces)
    hood=union([box((0,0,-.09),(.48,.27,.13),.018),
                box((0,.155,.035),(.51,.045,.21),.007),
                box((0,-.155,.035),(.51,.045,.21),.007),
                box((-.235,0,.035),(.045,.28,.21),.007),
                box((.235,0,.035),(.045,.28,.21),.007)])
    model.add("PART_SignHood"+name,placed(hood),"Metal")
    model.add("PART_SignPractical"+name,placed(box((0,0,0),(.40,.22,.020),.004)),
              "Light",shadow=False)
    wall=Vector((position[0],3.85,-4.55))
    end=Vector(position)-direction*.14
    centre=(wall+end)*.5
    segment=end-wall
    pipe_rotation=segment.normalized().to_track_quat("Y","Z").to_matrix()
    vertices,faces=bp.u_cylinder((0,0,0),(.05,segment.length*.5,.05))
    arm=([tuple(pipe_rotation@Vector(v)+centre) for v in vertices],faces)
    model.add("PART_SignMount"+name,
              union([box(tuple(wall),(.25,.34,.05),.012),arm]),"Metal")
    model.anchor("SignLight"+name,position)


def exterior():
    m=Model("Exterior")
    m.add("PART_Foundation",box((0,-.175,0),(8,.35,9),.025),"Concrete")
    # Actual openings with deep reveals: centre entry and two square shop windows.
    openings=[kit.Opening(-2.25,1.8,2.55,.85),kit.Opening(0,1.4,2.35),kit.Opening(2.25,1.8,2.55,.85)]
    front=translated(kit_unity(kit.wall_run(8,3.65,.22,openings)),(0,0,-4.39))
    m.add("PART_FrontWall",front,"Plaster")
    m.add("PART_SideWalls",union([box((-3.89,1.825,0),(.22,3.65,8.56)),
                                  box((3.89,1.825,0),(.22,3.65,8.56)),
                                  box((0,1.825,4.39),(8,3.65,.22))]),"Plaster")
    m.add("PART_Plinth",union([box((-3.90,.20,0),(.24,.4,8.6)),box((3.90,.20,0),(.24,.4,8.6)),
                               box((-2.36,.20,-4.4),(3.28,.4,.24)),box((2.36,.20,-4.4),(3.28,.4,.24)),
                               box((0,.20,4.4),(8,.4,.24))]),"Concrete")
    m.add("PART_Roof",box((0,3.72,0),(8.1,.16,9.1),.02),"Metal")
    m.add("PART_Parapet",union([box((0,3.9,-4.48),(8.1,.22,.14)),box((0,3.9,4.48),(8.1,.22,.14)),
                                box((-3.98,3.9,0),(.14,.22,8.9)),box((3.98,3.9,0),(.14,.22,8.9))]),"Concrete")
    m.add("PART_RoofCap",union([box((0,4.015,-4.48),(8.18,.06,.23)),box((0,4.015,4.48),(8.18,.06,.23)),
                                box((-3.98,4.015,0),(.23,.06,8.9)),box((3.98,4.015,0),(.23,.06,8.9))]),"Metal")
    door(m,-4.5)
    for i,x in enumerate((-2.25,2.25)):
        # Glass is inset behind a real frame and low interior shelf silhouette.
        bars=[box((x,.87,-4.51),(1.88,.07,.10)),box((x,2.55,-4.51),(1.88,.07,.10)),
              box((x-.90,1.71,-4.51),(.07,1.75,.10)),box((x+.90,1.71,-4.51),(.07,1.75,.10)),
              box((x,1.71,-4.53),(.045,1.66,.06))]
        m.add(f"PART_WindowFrame{i}",union(bars),"Metal")
        m.add(f"PART_WindowGlass{i}",box((x,1.70,-4.47),(1.74,1.6,.018)),"Glass",shadow=False)
        m.add(f"PART_WindowBacking{i}",box((x,1.7,-4.20),(1.73,1.62,.035)),"Dark")
        m.add(f"PART_WindowGrille{i}",union([bp.u_cylinder((x-.7+j*.175,1.7,-4.38),(.012,.8,.012)) for j in range(9)]),"Metal")
        m.add(f"PART_WindowShelf{i}",union([box((x,1.1,-4.32),(1.65,.055,.23)),
                                           box((x-.35,1.3,-4.31),(.24,.33,.18)),
                                           box((x+.32,1.25,-4.31),(.40,.22,.18))]),"Wood")
    m.add("PART_SignField",box((0,3.12,-4.55),(5.2,.9,.12),.025),"MatteSign")
    m.add("PART_SignLetters",union([glyph(ch,(i-3)*.62,3.15,-4.63) for i,ch in enumerate("ЛОМБАРД")]),"MatteLetters")
    # Both gussets bolt into the masonry piers outside the moving door leaf.
    canopy = [box((0,2.595,-4.94),(2.30,.052,1.02)),
              box((0,2.56,-5.438),(2.31,.079,.032))]
    for x in (-.99,.99):
        canopy += [box((x,2.395,-4.514),(.109,.39,.04)),
                   box((x,2.55,-4.91),(.067,.059,.82)),
                   rail((x,2.23,-4.54),(x,2.53,-5.29),.044)]
        for y in (2.27,2.48):
            canopy.append(rail((x,y,-4.53),(x,y,-4.552),.033,6))
    m.add("PART_Canopy",union(canopy),"Metal")
    sills=[]
    for x in (-2.25,2.25):
        sills += [bp.u_rotated_about(box((x,.835,-4.55),(1.96,.034,.26)),(-7,0,0),(x,.835,-4.55)),
                  box((x,.81,-4.677),(1.96,.040,.019),.003)]
        for side in (-1,1):
            sills.append(box((x+side*.96,.847,-4.54),(.023,.054,.24),.004))
    m.add("PART_FoldedWindowSills",union(sills),"PaleMetal")
    m.add("PART_EntrancePractical",box((0,2.52,-4.95),(.62,.035,.22)),"WarmLight",shadow=False)
    m.add("PART_Drainpipe",union([bp.u_cylinder((3.78,1.78,4.55),(.085,1.78,.085)),
                                  box((3.78,.17,4.6),(.13,.22,.18))]),"Metal")
    m.add("PART_ServiceDoor",translated(kit_unity(kit.panelled_leaf(.95,2.2,.08,panels=3)),(1.3,0,4.54)),"Metal")
    m.add("PART_ServiceDoorFrame",frame(.95,2.2,.20,(1.775,0,4.54)),"PaleMetal")
    m.add("PART_Vent",union([box((-1.6,2.1,4.53),(1.2,.6,.08))]+[box((-1.6,1.9+j*.08,4.59),(1.08,.035,.03)) for j in range(6)]),"Metal")
    m.add("PART_Repairs",union([box((-3.54,.92,-4.52),(.63,.39,.025)),box((3.995,.65,.7),(.025,.4,.65))]),"Cream")
    facade_practical(m,"Left",(-4.12,3.25,0),-90)
    facade_practical(m,"Right",(4.12,3.25,0),90)
    facade_practical(m,"Rear",(0,3.25,4.62),0)
    sign_projector(m,"Left",(-1.25,3.85,-6.00),(-.85,3.15,-4.64))
    sign_projector(m,"Right",(1.25,3.85,-6.00),(.85,3.15,-4.64))
    m.anchor("Entrance",(0,0,-4.5));m.anchor("EntryLight",(0,2.45,-4.95))
    m.anchor("Spawn",(0,0,-5.35))
    return m


def display_case(m,index,x,z):
    def piece(dx,y,dz,w,h,d,r=.006):
        return box((x+dx,y,z+dz),(w,h,d),r)
    base = [piece(0,.077,0,1.73,.154,.665),piece(0,.411,0,1.79,.515,.70),
            piece(0,.696,0,1.86,.063,.78)]
    for dx in (-.455,.455):
        base.append(piece(dx,.405,-.357,.86,.42,.021))
    m.add(f"PART_Case{index}Base",union(base),"Wood")
    supports=[]
    for dx in (-.907,.907):
        for dz in (-.36,.36):
            supports += [piece(dx,.869,dz,.032,.306,.032),piece(dx,.743,dz,.062,.036,.064)]
    for y in (.740,1.014):
        for dz in (-.36,.36):
            supports.append(piece(0,y,dz,1.838,.022,.032))
        for dx in (-.906,.906):
            supports.append(piece(dx,y,0,.030,.022,.70))
    m.add(f"PART_Case{index}Frame",union(supports),"PaleMetal")
    panels = [piece(0,1.015,0,1.795,.008,.692,.002)]
    panels += [piece(0,.878,dz,1.79,.243,.008,.002) for dz in (-.36,.36)]
    panels += [piece(dx,.878,0,.008,.243,.683,.002) for dx in (-.907,.907)]
    m.add(f"PART_Case{index}Glass",union(panels),"Glass",shadow=False)
    if index==0:
        # A portable radio stands on two feet and looks into the aisle.
        rx=-.27
        radio=[piece(rx,.842,0,.498,.204,.181,.017)]
        for off in (-.165,.165):
            radio.append(piece(rx+off,.734,0,.072,.014,.12,.003))
        radio += [piece(rx+off,.956,0,.019,.042,.021) for off in (-.14,.14)]
        radio.append(piece(rx,.979,0,.296,.021,.023))
        m.add("PART_DisplayRadio",union(radio),"Wood")
        m.add("PART_RadioFace",union([piece(rx-.064,.855,-.097,.288,.132,.014),
                                     piece(rx+.158,.891,-.098,.115,.045,.018,.004)]),"Dark")
        face=[]
        for row in range(10):
            face.append(piece(rx-.064,.801+row*.012,-.108,.270,.006,.010,.002))
        for i in range(8):
            face.append(piece(rx+.111+i*.013,.892,-.109,.003,.011 if i%2 else .019,.002,.0005))
        for dx in (.124,.195):
            face.append(rail((x+rx+dx,.816,z-.088),(x+rx+dx,.816,z-.119),.043,16))
        m.add("PART_RadioDialAndGrille",union(face),"PaleMetal")
        # Two drop-forged plier jaws share a pin. The screwdriver has a turned grip.
        tool = [bp.u_rotated_about(piece(.34,.7395,0,.27,.024,.038), (0,a,0), (x+.42,.7395,z)) for a in (-15,15)]
        for sign in (-1,1):
            tool.append(bp.u_rotated_about(piece(.47,.740,sign*.009,.095,.025,.026),(0,sign*17,0),(x+.42,.7395,z)))
        tool.append(rail((x+.42,.749,z),(x+.42,.765,z),.032))
        tool.append(rail((x+.12,.744,z+.114),(x+.41,.744,z+.114),.012))
        m.add("PART_DisplayTools",union(tool),"Metal")
        m.add("PART_ScrewdriverGrip",rail((x+.40,.748,z+.114),(x+.52,.748,z+.114),.041),"Dark")
    else:
        # A single cast hotplate sits in a raised enamel rim. Its working face
        # has concentric grooves turned into one solid radial section.
        body=[piece(0,.807,0,.50,.125,.278,.023),piece(0,.879,0,.472,.032,.248,.013)]
        for dx in (-.18,.18):
            for dz in (-.082,.082):
                body.append(piece(dx,.737,dz,.058,.019,.040))
        m.add("PART_DisplayAppliance",union(body),"PaleMetal")
        profile=[(.109,.893),(.111,.899),(.107,.912)]
        for radius in (.095,.075,.055,.035):
            profile.extend([(radius+.003,.913),(radius,.909),(radius-.003,.913)])
        profile.append((.014,.913))
        burner=translated(kit_unity(kit.lathe(profile,32)),(x-.076,0,z))
        hardware=[burner,rail((x+.15,.806,z-.129),(x+.15,.806,z-.170),.055,16)]
        for row in range(6):
            hardware.append(piece(.143,.896,-.036+row*.015,.083,.004,.004,.001))
        hardware.append(piece(.150,.815,-.170,.004,.022,.005,.001))
        m.add("PART_ApplianceHardware",union(hardware),"Dark")


def interior():
    m=Model("Interior")
    # Four laid rolls share a supporting bed. Two staggered end cuts cross the room.
    rolls=[box((0,-.074,0),(7.70,.132,8.70))]
    for left,right,cut in ((-3.85,-1.83,-.62),(-1.83,.12,None),(.12,2.05,1.14),(2.05,3.85,None)):
        stops=[-4.35,4.35] if cut is None else [-4.35,cut,4.35]
        for near,far in zip(stops,stops[1:]):
            rolls.append(box(((left+right)/2,-.004,(far+near)/2),
                             (right-left-.002,.008,far-near-.002),.001))
    m.add("PART_Floor",union(rolls),"Floor")
    m.add("PART_Ceiling",box((0,3.06,0),(7.7,.12,8.7),.01),"Cream")
    m.add("PART_Walls",union([box((-3.775,1.5,0),(.15,3,8.4)),box((3.775,1.5,0),(.15,3,8.4)),
                              box((0,1.5,4.275),(7.7,3,.15)),
                              translated(kit_unity(kit.wall_run(7.7,3,.15,[kit.Opening(-2.25,1.8,2.55,.85),
                                                                                 kit.Opening(0,1.4,2.35),
                                                                                 kit.Opening(2.25,1.8,2.55,.85)])),(0,0,-4.275))]),"Cream")
    m.add("PART_LowerWallPaint",union([box((-3.688,.52,0),(.022,1.04,8.4)),box((3.688,.52,0),(.022,1.04,8.4)),
                                        box((0,.52,4.188),(7.4,1.04,.022)),
                                        box((-2.235,.423,-4.188),(2.93,.846,.022)),box((2.235,.423,-4.188),(2.93,.846,.022))]),"Plaster")
    trim=[kit.joint_box(center,size,.010) for center,size in (
        ((-3.67,.07,0),(.045,.14,8.4)), ((3.67,.07,0),(.045,.14,8.4)),
        ((0,.07,4.17),(7.34,.14,.045)), ((-2.23,.07,-4.17),(2.9,.14,.045)),
        ((2.23,.07,-4.17),(2.9,.14,.045)))]
    m.add("PART_Skirting",union(trim),"Metal")
    door(m,-4.2)
    for i,x in enumerate((-2.25,2.25)):
        windowbars=[box((x,.87,-4.18),(1.88,.07,.10)),box((x,2.55,-4.18),(1.88,.07,.10)),
                    box((x-.90,1.71,-4.18),(.07,1.75,.10)),box((x+.90,1.71,-4.18),(.07,1.75,.10))]
        windowbars.extend([bp.u_cylinder((x-.70+j*.175,1.7,-4.23),(.012,.8,.012)) for j in range(9)])
        m.add(f"PART_InsideWindowFrame{i}",union(windowbars),"Metal")
        m.add(f"PART_InsideWindowGlass{i}",box((x,1.7,-4.28),(1.74,1.6,.018)),"Glass",shadow=False)
        m.add(f"PART_InsideWindowBacking{i}",box((x,1.7,-4.32),(1.74,1.6,.018)),"Plaster")
    # Counter and opaque bottom partition leave no path into the separate room.
    m.add("PART_CounterBody",union([box((-.2,.10,.46),(4.94,.2,.58)),
                                    box((-.2,.55,.46),(5,.80,.68)),
                                    box((-1.65,.55,.105),(1.4,.63,.03)),box((.2,.55,.105),(1.4,.63,.03)),box((1.75,.55,.105),(1.2,.63,.03))]),"Plaster")
    # Three fitted laminate sections above a continuous substrate and bullnose edging.
    top = [box((-.2,.986,.425),(5.12,.072,.84),.009)]
    for x,w in ((-1.83,1.86),(-.1,1.596),(1.53,1.656)):
        top.append(box((x,1.036,.425),(w,.028,.826),.005))
    top.append(box((-.2,1.021,.015),(5.12,.055,.032),.009))
    top.append(box((-.2,1.022,1.073),(5.12,.056,.46),.006))
    m.add("PART_CounterTop",union(top),"CounterLaminate")
    # Staff door is a real opaque leaf at the right, with a visible lock.
    m.add("PART_PartitionSides",union([box((-3.25,1.5,.70),(.9,3,.20)),box((3.53,1.5,.70),(.34,3,.20)),
                                       box((2.98,2.69,.70),(.77,.62,.20)),box((-.2,2.91,.70),(5.2,.18,.20))]),"Plaster")
    m.add("PART_StaffDoor",translated(kit_unity(kit.panelled_leaf(.82,2.35,.08,panels=3)),(2.51,0,.68)),"Metal")
    m.add("PART_StaffDoorFrame",frame(.82,2.35,.20,(2.92,0,.70)),"PaleMetal")
    m.add("PART_StaffDoorLock",union([box((3.18,1.07,.615),(.075,.13,.04)),box((3.13,1.10,.59),(.14,.025,.035))]),"PaleMetal")
    # Solid geometric bars, with a small safe transfer opening in the centre.
    bars=[]
    for i in range(37):
        x=-2.65+i*.14
        low=1.43 if abs(x)<.4 else 1.06
        bars.append(bp.u_cylinder((x,(low+2.85)/2,.69),(.015,(2.85-low)/2,.015),8))
    # A channel frame returns round the opening. The bottom rail stops at the tray.
    for x in (-2.76,2.47):
        bars.append(box((x,1.95,.69),(.064,1.80,.072),.009))
    bars += [box((-.145,y,.69),(5.29,.045,.072),.007) for y in (1.70,2.84)]
    for left,right in ((-2.78,-.41),(.41,2.50)):
        bars.append(box(((left+right)/2,1.075,.69),(right-left,.048,.066),.008))
    bars.append(box((0,1.43,.69),(.86,.045,.072),.008))
    for x in (-.419,.419):
        bars.append(box((x,1.25,.69),(.031,.39,.072),.006))
    m.add("PART_SecurityGrille",union(bars),"Metal")
    clamps=[]
    for x in (-2.70,-1.52,-.48,.48,1.62,2.42):
        clamps.append(box((x,1.059,.69),(.115,.018,.145),.003))
        for z in (.64,.74):
            clamps.append(rail((x,1.065,z),(x,1.077,z),.025,6))
    m.add("PART_GrilleFeet",union(clamps),"PaleMetal")
    # One folded steel trough runs through the grille. Both edges turn out and back.
    side=[(.320,1.055),(.335,1.063),(.350,1.124),(.355,1.133),
          (.366,1.136),(.375,1.130),(.376,1.121),(.371,1.120),
          (.370,1.126),(.363,1.130),(.357,1.125),(.342,1.060),(.323,1.050)]
    profile=[(-x,y) for x,y in reversed(side)] + side
    m.add("PART_TransferTray",folded_section(profile,.065,.91),"Tray")
    display_case(m,0,-2.55,-1.9);display_case(m,1,2.55,-1.9)
    # Back room: restrained working objects; no provenance, labels or clutter.
    m.add("PART_BackShelving",union([box((-2.55,.98,3.75),(1.65,.06,.62)),box((-2.55,.25,3.75),(1.65,.06,.62)),
                                     box((-2.55,1.7,3.75),(1.65,.06,.62)),box((-3.32,.96,3.75),(.06,1.9,.62)),
                                     box((-1.78,.96,3.75),(.06,1.9,.62))]),"Metal")
    m.add("PART_StoredBoxes",union([box((-2.7,.5,3.75),(.65,.43,.48)),box((-2.25,1.22,3.75),(.53,.4,.48)),
                                    box((-2.9,1.9,3.75),(.48,.34,.48))]),"Cardboard")
    m.add("PART_BackDesk",union([box((.55,.82,3.55),(2.05,.065,.72)),box((-.35,.40,3.55),(.08,.80,.60)),
                                box((1.40,.40,3.55),(.08,.80,.60))]),"Wood")
    m.add("PART_InsideVent",union([box((-1.6,2.1,4.15),(.6,.35,.08))]+
                                  [box((-1.6,1.97+j*.06,4.10),(.53,.023,.025)) for j in range(5)]),"Metal")
    m.add("PART_CashRegister",union([box((1.25,1.14,.85),(.43,.19,.33)),box((1.25,1.28,.96),(.34,.12,.08)),
                                    box((1.25,1.25,.72),(.32,.025,.13))]),"Metal")
    m.add("PART_WorkMat",box((0,1.056,1.07),(.90,.012,.28)),"Dark")
    for name,x,z in (("PublicLeft",-1.9,-1.4),("PublicRight",1.9,-1.4),("Clerk",0,1.65)):
        m.add("PART_LightHousing"+name,box((x,2.91,z),(1.15,.12,.29)),"Metal")
        m.add("PART_LightDiffuser"+name,box((x,2.84,z),(1.05,.025,.23)),"Light",shadow=False)
        m.anchor("Light"+name,(x,2.76,z))
    m.add("PART_CeilingConduit",union([box((0,2.97,-1.4),(4.8,.035,.035)),
                                      box((0,2.97,.4),(.035,.035,3.6))]),"PaleMetal")
    m.anchor("Entrance",(0,0,-4.2));m.anchor("Clerk",(0,0,2));m.anchor("Trade",(0,0,-.45));m.anchor("Spawn",(0,0,-3.15))
    return m


def export(model, model_dir, source_dir):
    name="Pawnshop"+model.kind+"3D"
    vertices=sum(len(p["vertices"]) for p in model.parts)
    triangles=sum(sum(len(face)-2 for face in p["faces"]) for p in model.parts)
    assert len(model.parts)<90
    if model.kind == "Interior":
        assert triangles <= kit.DETAILED_INTERIOR_TRIANGLES
    else:
        assert triangles < 55000
    bounds=model.bounds()
    payload={"schema_version":1,"design_id":"pawnshop_"+model.kind.lower()+"_v1",
             "generator_version":VERSION,"build_signature":model.signature(),
             "bounds_m":bounds,"triangle_count":triangles,"vertex_count":vertices,
             "parts":[{k:p[k] for k in ("name","material","shadows")} for p in model.parts],
             "anchors":list(model.anchors.values()),
             "materials":[{"name":key,"color":list(v[0]),"texture":v[1],"smoothness":v[2],"metallic":v[3]} for key,v in PALETTE.items()]}
    model_dir.mkdir(parents=True,exist_ok=True);source_dir.mkdir(parents=True,exist_ok=True)
    (model_dir/(name+".json")).write_text(json.dumps(payload,ensure_ascii=False,indent=2)+"\n")
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(source_dir/(name+".blend")))
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active=model.root
    bpy.ops.export_scene.fbx(filepath=str(model_dir/(name+".fbx")),use_selection=True,
                            object_types={"MESH","EMPTY"},axis_forward="-Z",axis_up="Y",
                            apply_scale_options="FBX_SCALE_ALL",bake_space_transform=False,
                            add_leaf_bones=False,bake_anim=False,use_mesh_modifiers=True,
                            mesh_smooth_type="FACE",use_custom_props=False,path_mode="STRIP")
    print(f"{name}: signature={model.signature()}, triangles={triangles}, bounds={bounds}")


def preview(model, directory):
    """Optional disposable authoring review, never part of the exported model."""
    from mathutils import Vector
    scene=bpy.context.scene
    scene.render.engine="CYCLES"
    scene.cycles.samples=12
    scene.render.resolution_x=1100;scene.render.resolution_y=760;scene.render.resolution_percentage=100
    scene.world.color=(.12,.14,.13)
    for part in model.parts:
        key=part["material"];color,texture,smooth,metal=PALETTE[key]
        mat=bpy.data.materials.get("Preview"+key)
        if mat is None:
            mat=bpy.data.materials.new("Preview"+key);mat.use_nodes=True
            bsdf=mat.node_tree.nodes.get("Principled BSDF")
            bsdf.inputs["Base Color"].default_value=color
            bsdf.inputs["Roughness"].default_value=1-smooth
            bsdf.inputs["Metallic"].default_value=metal
            if key=="Glass":
                bsdf.inputs["Transmission Weight"].default_value=.7
                bsdf.inputs["Roughness"].default_value=.2
            if key in ("Light","WarmLight"):
                bsdf.inputs["Emission Color"].default_value=color
                bsdf.inputs["Emission Strength"].default_value=2
            if texture:
                folder="Pawnshop/Textures" if texture.startswith("Pawnshop") else "Textures" if texture.startswith("City") else "Supermarket/Textures"
                path=ROOT/"Assets/Resources"/folder/(texture+"Albedo.png")
                image=mat.node_tree.nodes.new("ShaderNodeTexImage");image.image=bpy.data.images.load(str(path))
                mix=mat.node_tree.nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY"
                mix.inputs[0].default_value=1;mix.inputs[2].default_value=color
                mat.node_tree.links.new(image.outputs["Color"],mix.inputs[1]);mat.node_tree.links.new(mix.outputs[0],bsdf.inputs["Base Color"])
        bpy.data.objects[part["name"]].data.materials.clear()
        bpy.data.objects[part["name"]].data.materials.append(mat)
    def source(v):return Vector((v[0],v[2],v[1]))
    def area(name,position,target,energy,size):
        data=bpy.data.lights.new(name,"AREA");data.energy=energy;data.shape="DISK";data.size=size
        obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj)
        obj.location=source(position);obj.rotation_euler=(source(target)-obj.location).to_track_quat("-Z","Y").to_euler()
    if model.kind=="Exterior":
        location=(-10,7,-13);target=(0,1.8,0)
        area("Review sky",(-3,9,-7),(0,0,0),1800,8)
        area("Review fill",(7,5,-2),(0,1.5,0),850,7)
    else:
        location=(0,1.65,-3.85);target=(0,1.45,.8)
        for x in (-1.9,1.9):area("Review public"+str(x),(x,2.75,-1.4),(x,0,-1.4),150,1.2)
        area("Review clerk",(0,2.75,1.65),(0,1,1.65),190,1.2)
    camera_data=bpy.data.cameras.new("Review camera");camera_data.lens=23 if model.kind=="Interior" else 45
    camera=bpy.data.objects.new("Review camera",camera_data);bpy.context.collection.objects.link(camera)
    camera.location=source(location);camera.rotation_euler=(source(target)-camera.location).to_track_quat("-Z","Y").to_euler()
    scene.camera=camera;directory.mkdir(parents=True,exist_ok=True)
    scene.render.filepath=str(directory/("Pawnshop"+model.kind+".png"))
    bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Pawnshop/Models")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/Pawnshop/Blender")
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--preview-dir",type=Path)
    parser.add_argument("--kind",choices=("all","exterior","interior"),default="all")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    for builder in (exterior,interior):
        if args.kind!="all" and builder.__name__!=args.kind:continue
        first=builder().signature();model=builder()
        assert model.signature()==first,"Non-deterministic pawnshop geometry"
        if args.validate_only:
            target=args.model_dir/("Pawnshop"+model.kind+"3D.json")
            previous=json.loads(target.read_text())
            assert previous["build_signature"]==first,"Pawnshop model manifest is stale"
        else:export(model,args.model_dir,args.source_dir)
        if args.preview_dir:preview(model,args.preview_dir)
    print("Pawnshop deterministic geometry, winding, bounds and budget contract passed.")


if __name__=="__main__":main()
