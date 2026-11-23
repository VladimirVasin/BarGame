#!/usr/bin/env python3
"""The closed municipal rental station. Blender metres are lateral, seaward, up."""
import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import interior_kit as kit
from river_bridge_mesh import Part

OUT = ROOT/'Assets/Resources/City'
PLASTER, SPALL, WOOD, END, GREEN, BLUE, BARE, TAR, DECK, IRON, SILL, SUBMERGED, PRINT = range(13)


class RentalPart(Part):
    grain_axis = 0

    def append(self, shape, finish, top=None):
        lo, hi = kit.bounds(shape)
        self.grain_axis = max(range(3), key=lambda a: hi[a]-lo[a])
        super().append(shape, finish, top)

    def atlas_uv(self, cell, s, t, point, axis):
        if cell == PRINT:
            # On the forward facade camera-right points toward negative local X.
            return (.99-.98*s, (.018+.964*t)/4)
        if cell in (WOOD, END, GREEN, BLUE, BARE, TAR, DECK, SUBMERGED):
            a, b = ((1,2),(0,2),(0,1))[axis]
            if self.grain_axis == axis and cell in (WOOD, DECK):
                cell = END
            elif self.grain_axis == b:
                s,t = t,s
            if self.name=='Rail' and cell==DECK:
                s=.36+.28*s
            if cell != DECK:
                s = .78*s + .19*((self.solids*5)%13)/12
                t = .68*t + .29*((self.solids*7)%11)/10
        return ((cell%4+.012+.976*s)/4, 1-(cell//4+.988-.976*t)/4)


def inclined(part, centre, size, cell, degrees):
    verts, faces = kit.joint_box((0,0,0),size,.008)
    a = math.radians(degrees)
    x,y,z = centre
    part.append(([(x+u*math.cos(a)-w*math.sin(a),y+v,z+u*math.sin(a)+w*math.cos(a))
                  for u,v,w in verts],faces),cell)


def ring_solid(part, rings, cell):
    count = len(rings[0])
    verts = [p for ring in rings for p in ring]
    faces = [tuple(reversed(range(count))), tuple(range(len(verts)-count,len(verts)))]
    for k in range(len(rings)-1):
        for i in range(count):
            j = (i+1)%count
            faces.append((k*count+i,k*count+j,(k+1)*count+j,(k+1)*count+i))
    volume = sum(Vector(verts[f[0]]).dot(Vector(verts[f[j]]).cross(Vector(verts[f[j+1]])))
                 for f in faces for j in range(1,len(f)-1))
    assert abs(volume)>1e-10
    if volume<0: faces=[tuple(reversed(f)) for f in faces]
    part.append((verts, faces),cell)


def column(part, x,y,profile,cell,sides=12):
    ring_solid(part,[[(x+r*math.cos(2*math.pi*i/sides), y+r*math.sin(2*math.pi*i/sides), z)
                      for i in range(sides)] for z,r in profile],cell)


def hut():
    p=RentalPart('Hut')
    # Apertures and threshold share the production door and lamp datums.
    for c,s in [((0,-1.71,1.335),(2.8,.18,2.35)), ((-1.31,0,1.335),(.18,3.6,2.35)),
                ((1.31,0,1.335),(.18,3.6,2.35)), ((.915,1.71,1.335),(.97,.18,2.35)),
                ((0,1.71,2.31),(.86,.18,.40)), ((-1.33,1.71,1.335),(.14,.18,2.35)),
                ((-.505,1.71,1.335),(.15,.18,2.35)), ((-.92,1.71,.656),(.68,.18,.992)),
                ((-.92,1.71,2.31),(.68,.18,.40))]:
        p.box(c,s,SPALL if c[2]<1 else PLASTER,.014)
    for side in (-1,1):
        p.box((side*1.315,0,.24),(.19,3.61,.16),SPALL,.012)
    for i in range(12):
        p.box((-1.52+(i+.5)*3.04/12,0,.115),(3.04/12-.005,3.84,.09),WOOD,.007)
    for x in (-1.20,0,1.20): p.box((x,0,.038),(.16,3.65,.076),TAR,.009)
    p.box((0,2.02,.08),(1.26,.44,.16),SPALL,.018,top=SILL)
    for i in range(17):
        x=-1.62+(i+.5)*3.24/17
        p.box((x,.24,2.62),(3.24/17-.004,4.52,.11),WOOD,.008)
    for y in (-1.99,2.47):
        p.box((0,y,2.56),(3.26,.095,.18),BARE,.009)
    for x in (-1.41,1.41): p.box((x,.20,2.51),(.13,4.40,.17),TAR,.012)
    for x in (-.408,.408): p.box((x,1.77,1.135),(.045,.15,1.95),BARE,.008)
    p.box((0,1.77,2.09),(.86,.15,.045),BARE,.006)
    # Closed hatch has a reveal and its own recessed shutter behind the nailed boards.
    for i in range(5): p.box((-1.254+(i+.5)*.668/5,1.667,1.635),(.129,.055,.95),GREEN,.006)
    for x in (-1.262,-.578): p.box((x,1.78,1.63),(.055,.16,1.02),WOOD,.008)
    p.box((-.92,1.83,1.155),(.84,.31,.10),SILL,.014)
    p.box((-.92,1.80,2.122),(.81,.17,.077),WOOD,.007)
    for y,z,angle in [(1.856,1.55,25),(1.899,1.76,-21)]:
        inclined(p,(-.92,y,z),(.93,.044,.125),BARE,angle)
        for side in (-1,1):
            p.box((-.92+side*.372,y+.023,z+side*.372*math.tan(math.radians(angle))),
                  (.021,.009,.023),IRON,.003)
    column(p,.784,-.9,[(2.65,.15),(2.70,.15),(2.73,.089),(3.51,.084),(3.54,.114),(3.58,.112)],IRON)
    for x in (-1.18,1.18):
        p.box((x,2.46,2.70),(.07,.20,.39),IRON,.009)
    # The old single bulb is carried outboard of the print on a bent iron stay.
    p.box((.32,2.40,2.47),(.11,.18,.21),IRON,.009)
    p.box((.32,2.78,2.46),(.050,.79,.045),IRON,.008)
    column(p,.32,3.14,[(2.24,.032),(2.45,.029)],IRON)
    column(p,.32,3.14,[(2.18,.152),(2.19,.156),(2.235,.084),(2.26,.050)],IRON,16)
    return p


def bulb():
    p=RentalPart('Bulb')
    column(p,0,0,[(-.072,.018),(-.050,.045),(-.006,.050),(.033,.041),(.068,.026)],IRON,16)
    return p


def sign():
    p=RentalPart('Sign')
    p.box((0,0,0),(1, .94, 1),WOOD,.018)
    # Only the outward-facing skin carries letters. All backing and edges are plain.
    p.append(([(-.492,.50,-.47),(-.492,.50,.47),(.492,.50,.47),(.492,.50,-.47)],[(0,1,2,3)]),PRINT)
    for x in (-.474,.474):
        for z in (-.39,.39): p.box((x,.504,z),(.010,.012,.050),IRON,.003)
    return p


HULLS=[(4.10,1.35,.54),(3.526,1.512,.60),(4.592,1.188,.44),(4.10,1.35,.48)]


def boat(variant):
    length,beam,depth=HULLS[variant]
    p=RentalPart('Hull'+str(variant))
    for end in (-1,1):
        p.box((end*length*.28,0,.09),(.22,beam+.30,.18),TAR,.016)
    # Half-breadths surveyed from stern to bow. Each strake follows the moulded stations.
    stations=(-1,-.84,-.58,-.29,0,.29,.58,.84,1)
    widths=((.47,.74,.94,1,.99,.92,.73,.41,.065),
            (.58,.83,.98,1,.99,.96,.84,.58,.15),
            (.83,.94,1,1,1,1,.98,.86,.69),
            (.48,.74,.91,1,1,.93,.79,.49,.12))[variant]
    keel=(.07,.09,.69,.16)[variant]
    power=(.87,1.65,.83,.94)[variant]
    def profile(i, side, level, inset):
        beam_here=widths[i]*beam*.5
        breadth=keel+(1-keel)*max(0,1-level**power)**(.69 if variant==1 else 1)
        sheer=.083*max(0,(abs(stations[i])-.58)/.42)**1.6
        return (stations[i]*length*.5,side*(beam_here*breadth-inset*min(1,beam_here*breadth*.55/.034)),
                .18+sheer+(depth-sheer)*level)
    n=(6,7,4,6)[variant]
    for side in (-1,1):
        for row in range(n):
            lo,hi=row/n+.003,(row+1)/n-.003
            skirt=((lo,.013),(lo+.006,0),(hi-.006,0),(hi,.005),(hi,.034),(lo,.034))
            spans=[range(9)]
            if variant==3 and side==1 and row in (2,3,4):
                spans=[range(0,3 if row==3 else 4),range(6 if row==3 else 5,9)]
            for indices in spans:
                rings=[[profile(i,side,l,t) for l,t in skirt] for i in indices]
                paint=(GREEN,BLUE,BARE,BARE)[variant]
                ring_solid(p,rings,TAR if row==n-1 or (variant==3 and row==0) else paint)
        if variant==3:
            for i in (3,4,5):
                path=[profile(i,side,j/8,.040) for j in range(9)]
                rings=[[(x+dx,y+dy,z) for dx,dy in ((-.035,-.012),(.035,-.012),(.035,.012),(-.035,.012))]
                       for x,y,z in path]
                ring_solid(p,rings,TAR)
    for i in (0,8):
        rings=[]
        for j in range(9):
            x,w,z=profile(i,1,j/8,.004)
            rings.append([(x+dx,dy,z) for dx,dy in ((-.013,-w),(.013,-w),(.013,w),(-.013,w))])
        ring_solid(p,rings,BARE)
    # A boarded bottom on the punt, a narrow keel on the clinker-built craft.
    rings=[]
    for i,k in enumerate(stations):
        w=keel*beam*widths[i]*.5+.012
        rings.append([(k*length*.5,y,.18+depth+z) for y,z in
                      ((-w,-.012),(w,-.012),(w,.008),(w*.90,.024),(-w*.90,.024),(-w,.008))])
    ring_solid(p,rings,TAR if variant!=2 else BARE)
    return p


def oar():
    p=RentalPart('Oar')
    # The blade lies on the sand; its continuous loom rests against the first skiff.
    tip=Vector((1.47,1.12,.024))
    grip=Vector((-.66,.51,.395))
    axis=(grip-tip).normalized()
    lateral=Vector((-axis.y,axis.x,0)).normalized()
    up=lateral.cross(axis).normalized()
    rings=[]
    for t,width,thick in ((0,.062,.012),(.025,.099,.018),(.16,.087,.024),(.31,.036,.03),
                          (.36,.023,.023),(.86,.021,.021),(.93,.027,.027),(1,.025,.025)):
        centre=tip.lerp(grip,t)
        rings.append([tuple(centre+lateral*(width*math.cos(i*math.pi/4))+up*(thick*math.sin(i*math.pi/4)))
                      for i in range(8)])
    ring_solid(p,rings,WOOD)
    return p


def deck():
    p=RentalPart('Deck')
    for i in range(4):
        y=(i-1.5)*.25
        p.box((0,y,0),(2.2,.245,.12),WOOD,.008,top=DECK)
        for x in (-.96,.96):
            column(p,x,y,[ (.059,.014),(.061,.012)],IRON,8)
    return p


def pier_parts():
    rail=RentalPart('Rail')
    rail.box((0,0,0),(.08,1,.10),WOOD,.014,top=DECK)
    post=RentalPart('Pile')
    column(post,0,0,[(-.5,.124),(-.493,.130),(.492,.128),(.5,.122)],TAR)
    wet=RentalPart('WetPile')
    column(wet,0,0,[(-.5,.128),(.5,.130)],SUBMERGED)
    slip=RentalPart('Slipway')
    for i in range(3):
        slip.box(((i-1)*1.333,0,0),(1.328,1.70,.18),SPALL,.017,top=SILL)
    return [rail,post,wet,slip]


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root=bpy.data.objects.new('BoatRental',None)
    bpy.context.collection.objects.link(root)
    for name,pos in [('AxisForward',(0,1,0)),('AxisUp',(0,0,1))]:
        obj=bpy.data.objects.new(name,None)
        bpy.context.collection.objects.link(obj)
        obj.parent=root
        obj.location=pos
    records=[]
    for i,p in enumerate([hut(),sign(),bulb(),*(boat(i) for i in range(4)),oar(),deck(),*pier_parts()]):
        records.append(p.emit(root))
        bpy.data.objects[p.name].location=((i%4)*6,-(i//4)*6,0)
    return records


if __name__=='__main__':
    arg=argparse.ArgumentParser()
    arg.add_argument('--validate-only',action='store_true')
    opt=arg.parse_args(sys.argv[sys.argv.index('--')+1:])
    records=build()
    path=OUT/'BoatRental.json'
    manifest=json.dumps(records,indent=2)+'\n'
    assert sum(p['triangles'] for p in records)<17000
    if opt.validate_only:
        assert manifest==path.read_text()
    else:
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/City/Blender/BoatRental.blend'))
        bpy.ops.export_scene.fbx(filepath=str(OUT/'BoatRental.fbx'),object_types={'MESH','EMPTY'},
            axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',mesh_smooth_type='FACE',
            add_leaf_bones=False,bake_anim=False,use_triangles=True)
        path.write_text(manifest)
    assert records==build()
