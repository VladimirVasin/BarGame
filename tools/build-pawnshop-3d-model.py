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

VERSION = "1.0.0"
PALETTE = {
    "Plaster": ((.48, .54, .49, 1), "SupermarketWallPaint", .1, 0),
    "Cream": ((.76, .75, .63, 1), "SupermarketWallPaint", .1, 0),
    "Concrete": ((.32, .35, .33, 1), "CityFringeConcrete", .1, 0),
    "Metal": ((.18, .23, .21, 1), "SupermarketShelfMetal", .23, .4),
    "PaleMetal": ((.56, .59, .52, 1), "SupermarketShelfMetal", .3, .5),
    "Floor": ((.60, .64, .58, 1), "SupermarketLinoleum", .16, 0),
    "Wood": ((.33, .25, .19, 1), "SupermarketCounter", .28, 0),
    "Cardboard": ((.65, .56, .41, 1), "SupermarketCardboard", .06, 0),
    "Glass": ((.32, .43, .39, .2), "", .64, 0),
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
        pitch = 2.4 if material in ("Floor", "Plaster", "Cream") else 1.2
        for face in mesh.polygons:
            n = face.normal
            axis = max(range(3), key=lambda i: abs(n[i]))
            for loop in face.loop_indices:
                v = mesh.vertices[mesh.loops[loop].vertex_index].co
                uv.data[loop].uv = ((v[1] if axis == 0 else v[0]) / pitch,
                                    (v[1] if axis == 2 else v[2]) / pitch)
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
        return hashlib.sha256(json.dumps({"parts": self.parts, "anchors": self.anchors},
                                         sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def frame(width, height, thick, center):
    return translated(kit_unity(kit.door_frame(width, height, thick, jamb=.065, architrave=.018)), center)


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
        model.add("PART_DoorPull"+suffix,
                  union([box((.56,1.05,z+side*.065),(.033,.23,.03)),
                         box((.56,.94,z+side*.035),(.07,.03,.06)),
                         box((.56,1.16,z+side*.035),(.07,.03,.06))]),
                  "PaleMetal",pivot,pivot_position)
    model.add("PART_EntranceFrame", frame(width, height, .2, (0, 0, z)), "Metal")


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
    m.add("PART_Canopy",union([box((0,2.59,-4.91),(2.1,.10,1.02)),box((-.8,2.40,-4.7),(.055,.35,.055)),
                               box((.8,2.40,-4.7),(.055,.35,.055))]),"Metal")
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
    m.add(f"PART_Case{index}Base",union([box((x,.10,z),(1.8,.2,.74)),box((x,.41,z),(1.72,.45,.67)),
                                       box((x,.70,z),(1.86,.055,.78))]),"Wood")
    frameparts=[box((x,.97,z-.37),(1.86,.045,.035)),box((x,.97,z+.37),(1.86,.045,.035))]
    for dx in (-.9,.9):
        for dz in (-.36,.36):frameparts.append(box((x+dx,.84,z+dz),(.035,.29,.035)))
    m.add(f"PART_Case{index}Frame",union(frameparts),"PaleMetal")
    m.add(f"PART_Case{index}Glass",union([box((x,.99,z),(1.8,.015,.72)),
                                         box((x,.84,z-.365),(1.8,.27,.012))]),"Glass",shadow=False)
    # Ordinary text-free objects, passive until inventory/economy has a contract.
    if index==0:
        m.add("PART_DisplayRadio",union([box((x-.35,.82,z),(.48,.20,.22)),box((x-.35,.84,z-.125),(.31,.12,.018)),
                                         box((x-.12,.84,z-.14),(.04,.04,.025))]),"Metal")
        m.add("PART_DisplayTools",union([box((x+.35,.77,z),(.42,.06,.11)),box((x+.52,.80,z),(.12,.12,.23))]),"PaleMetal")
    else:
        m.add("PART_DisplayAppliance",union([box((x,.80,z),(.52,.15,.29)),box((x,.89,z+.03),(.43,.035,.22)),
                                             box((x-.2,.86,z-.17),(.05,.05,.025)),box((x+.2,.86,z-.17),(.05,.05,.025))]),"PaleMetal")


def interior():
    m=Model("Interior")
    m.add("PART_Floor",box((0,-.07,0),(7.7,.14,8.7),.01),"Floor")
    m.add("PART_Ceiling",box((0,3.06,0),(7.7,.12,8.7),.01),"Cream")
    m.add("PART_Walls",union([box((-3.775,1.5,0),(.15,3,8.4)),box((3.775,1.5,0),(.15,3,8.4)),
                              box((0,1.5,4.275),(7.7,3,.15)),
                              translated(kit_unity(kit.wall_run(7.7,3,.15,[kit.Opening(-2.25,1.8,2.55,.85),
                                                                                 kit.Opening(0,1.4,2.35),
                                                                                 kit.Opening(2.25,1.8,2.55,.85)])),(0,0,-4.275))]),"Cream")
    m.add("PART_LowerWallPaint",union([box((-3.688,.52,0),(.022,1.04,8.4)),box((3.688,.52,0),(.022,1.04,8.4)),
                                        box((0,.52,4.188),(7.4,1.04,.022)),
                                        box((-2.235,.52,-4.188),(2.93,1.04,.022)),box((2.235,.52,-4.188),(2.93,1.04,.022))]),"Plaster")
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
    m.add("PART_CounterTop",kit.joint_box((-.2,1.02,.425),(5.12,.06,.84),.019),"Wood")
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
    bars.extend([box((-.13,2.84,.69),(5.35,.045,.065)),box((-.13,1.70,.69),(5.35,.025,.035)),
                 box((-.13,1.08,.69),(5.35,.035,.065)),box((0,1.43,.69),(.84,.035,.065))])
    m.add("PART_SecurityGrille",union(bars),"Metal")
    m.add("PART_TransferTray",union([box((0,1.06,.35),(.73,.018,.47)),box((-.36,1.10,.35),(.018,.08,.47)),
                                    box((.36,1.10,.35),(.018,.08,.47))]),"PaleMetal")
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
    m.add("PART_WorkMat",box((0,1.059,.92),(.90,.012,.28)),"Dark")
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
    assert triangles<55000 and len(model.parts)<90
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
                folder="Textures" if texture.startswith("City") else "Supermarket/Textures"
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
