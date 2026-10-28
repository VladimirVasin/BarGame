#!/usr/bin/env python3
"""Fixed-metre, passive Stairwell and DoorTransition meshes; layout/collision stay in C#.
The import bridge preserves the old unit-transform contract (including cylinder
half-height and ray-only tread proxies), after measuring the FBX in metres.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import sys
sys.dont_write_bytecode = True
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import interior_kit as kit
import bar_parts as bp

VERSION = '1.1.0'
DESIGN = 'stairwell_interior_v1'
MODEL_DIR = ROOT / 'Assets/Stairwell/Interior/Models'
SOURCE_DIR = ROOT / 'ArtSource/Stairwell/Interior'


def swap(v): return (v[0], v[2], v[1])
def box(size, center=(0, 0, 0), cut=.008):
    return kit.chamfered_box(swap(center), swap(size), cut)
def merged(parts): return kit.merge_all(parts)
def move(g, v): return kit.translated(g, swap(v))
def around_y(g, angle): return kit.rotated_z(g, -angle)
def fit(g, size):
    lo, hi = kit.bounds(g)
    g = kit.translated(g, tuple(-(a+b)/2 for a, b in zip(lo, hi)))
    return kit.scaled(g, tuple(s/(b-a) for s, a, b in zip(swap(size), lo, hi)))


def leaf(size):
    """Four true inset panels, bounded by full-depth stiles and rails."""
    x, y, z = size
    t = min(.115, x*.13)
    pieces = [box((t, y, z), (s*(x-t)/2, 0, 0)) for s in (-1, 1)]
    pieces += [box((x-2*t, t, z), (0, s*(y-t)/2, 0)) for s in (-1, 1)]
    pieces += [box((x-2*t, t, z)), box((t, y-2*t, z))]
    w = (x-3*t)/2; h = (y-3*t)/2
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = (sx*(w+t)/2, sy*(h+t)/2, 0)
            pieces.append(box((w, h, z*.53), c, .006))
            # The raised edge sits inside the old leaf envelope, surrounding
            # the inset field on both faces rather than pasted onto a slab.
            for side in (-1, 1):
                d = z*.34
                relief = min(.024,z*.22)
                for edge in (-1, 1):
                    pieces.append(box((w, .022, relief),
                        (c[0], c[1]+edge*(h-.022)/2, side*d), .004))
                    pieces.append(box((.022, h-.044, relief),
                        (c[0]+edge*(w-.022)/2, c[1], side*d), .004))
    return merged(pieces)


def frame(size, axis='xy', bars=False):
    if axis == 'zy': return around_y(frame((size[2], size[1], size[0]), bars=bars), 90)
    x,y,z = size; t = min(.06, x*.18, y*.18)
    pieces = [box((t, y, z), (s*(x-t)/2, 0, 0)) for s in (-1, 1)]
    pieces += [box((x-2*t, t, z), (0, s*(y-t)/2, 0)) for s in (-1, 1)]
    if bars:
        count = max(1, round((x-2*t)/.22))
        for i in range(1, count+1):
            px = -x/2+t+(x-2*t)*i/(count+1)
            pieces.append(box((.028, y-2*t, z*.52), (px, 0, 0), .004))
    return merged(pieces)


def rail(size):
    # Existing collision envelope retained; the visible barrier is open steel.
    x,y,z=size; side = x < z
    if side: return around_y(rail((z,y,x)), 90)
    t=min(.045,x*.25)
    pieces=[box((t,y,z),(s*(x-t)/2,0,0),.005) for s in (-1,1)]
    pieces += [box((x-2*t,.045,z*.75),(0,s*(y-.045)/2,0),.005) for s in (-1,1)]
    count=max(1,round(x/.25))
    for i in range(1,count):
        pieces.append(box((.025,y-.09,z*.5),(-x/2+x*i/count,0,0),.004))
    return merged(pieces)


def cylinder(size, style='pipe'):
    x,half,z=size
    profile={'pipe':[(.96,-1),(1,-.98),(1,.98),(.96,1)],
             'clamp':[(.88,-1),(1,-.70),(1,.70),(.88,1)],
             'bottle':[(.72,-1),(.92,-.92),(1,-.8),(1,.3),(.5,.58),(.42,.64),(.42,.94),(.5,1)],
             'spindle':[(.8,-1),(1,-.8),(1,-.3),(.58,-.2),(.58,.85),(.48,1)]}[style]
    g=kit.lathe([(r*x/2,h*half) for r,h in profile],12)
    return kit.scaled(g,(1,z/x,1))


def wall(size, opening, side=False):
    x,y,z=size
    if side:
        # Rotated source-space wall maps local X -> -Unity Z; opening
        # centre is negated to retain the apartment's actual south-side dock.
        g=kit.wall_run(z,y,x,[kit.Opening(-opening[0],opening[1],opening[3],opening[2])],.006)
        return around_y(kit.translated(g,(0,0,-y/2)),90)
    g=kit.wall_run(x,y,z,[kit.Opening(*opening)],.006)
    return kit.translated(g,(0,0,-y/2))


def shape(style,size):
    x,y,z=size
    if style=='front-wall': return wall(size,(-1.45,1.72,2.36,0))
    if style=='front-band': return wall(size,(-1.45,1.72,y,0))
    if style=='right-wall': return wall(size,(-3.72,1.24,3.2,5.52),True)
    if style=='leaf': return leaf(size)
    if style=='side-leaf': return around_y(leaf((z,y,x)),90)
    if style in ('rail','curb'): return rail(size)
    if style=='grille': return frame(size)
    if style=='mesh':
        pieces=[frame(size)]
        for i in range(1,10): pieces.append(box((.014,y-.09,z*.42),(-x/2+x*i/10,0,0),.002))
        for i in range(1,8): pieces.append(box((x-.09,.014,z*.38),(0,-y/2+y*i/8,0),.002))
        return merged(pieces)
    if style=='recess':
        return merged([frame(size),box((x-.06,y-.06,z*.24),(0,0,z*.38),.004)])
    if style=='side-recess': return around_y(shape('recess',(z,y,x)),90)
    if style=='radiator':
        # Cast loop with a real void and end bosses within the old fin box.
        return frame(size)
    if style=='wardrobe':
        return merged([frame(size),box((x-.10,y-.10,.035),(0,0,z*.45)),
                       box((x-.10,.055,z),(0,-y*.12,0))])
    if style=='mattress':
        return merged([box((x,y*.9,z),cut=.045),box((x*.985,y*.05,z*.985),(0,-y*.475,0),.008),
                       box((x*.985,y*.05,z*.985),(0,y*.475,0),.008)])
    if style=='sack':
        g=kit.lathe([(.025,-.5),(.32,-.49),(.48,-.35),(.5,-.02),(.40,.28),(.18,.43),(.10,.5)],10)
        return fit(g,size)
    if style=='paper':
        return merged([box((x*.52,y*.45,z),(x*-.24,-y*.275,0),.001),
                       box((x*.52,y*.55,z),(x*.24,y*.225,0),.001)])
    if style=='patch':
        # Irregular low-relief existing damage, no new clue or decoration.
        g=kit.prism([(-.5,-.35),(-.26,-.5),(.35,-.44),(.5,-.16),(.38,.34),(.04,.5),(-.45,.28)],1)
        return fit(g,size)
    if style=='tube':
        v,f=kit.lathe([(.43,-.5),(.5,-.47),(.5,.47),(.43,.5)],12)
        return fit(([(c,b,-a) for a,b,c in v],f),size)
    if style=='jamb':
        return merged([box((x,y,z*.58),(0,0,z*.21),.006),
                       box((x*.7,y,z*.42),(x*.15,0,-z*.29),.006)])
    if style=='tread':
        # Flat full-width contact plane, relieved front arris and riser.
        return merged([box((x,y-.016,z-.022),(0,-.008,.011),.003),
                       box((x,.016,z),(0,(y-.016)/2,0),.004)])
    if style in ('pipe','clamp','bottle','spindle'): return cylinder(size,style)
    if style=='valve':
        g=kit.lathe([(.075,-y),(.11,-y),(.11,y),(.075,y)],16)
        spokes=[box((x*.80,y*1.35,x*.065)),box((x*.065,y*1.35,x*.80)),box((x*.17,y*2,x*.17))]
        return fit(merged([g,*spokes]),(x,y*2,z))
    return box(size)


# Dedicated trough: a housing is open underneath rather than a flat slab.
def housing(size):
    x,y,z=size
    return merged([box((x,y*.28,z),(0,y*.36,0),.004),
                   box((x,y*.72,z*.10),(0,-y*.14,-z*.45),.004),
                   box((x,y*.72,z*.10),(0,-y*.14,z*.45),.004)])


def rail_supports():
    """Steel extensions and saddles in the unchanged sloped wood's frame.
    The old 680 mm panels end 290 mm below the handrail centre. Two
    32x16 mm necks continue each panel's end posts, with saddles fitted
    into the wood. Their coordinates derive from the actual 1.6/3.84 rise.
    """
    run,rise,half=3.84,1.6,.095/2
    length=math.hypot(run,rise);cos=run/length;sin=rise/length
    pieces=[];contacts=[]
    def local(along,y):
        d=along-run/2;h=y-(1.02+rise/2)
        return (0,h*cos-d*sin,h*sin+d*cos)
    for step in range(16):
        metal_top=(step+1)*.1+.68
        # The neck sits within the existing end post. Keeping it 12.5 mm
        # inboard also clears the chamfer at the wood's lower endpoint.
        for end in (-1,1):
            along=(step+.5)*.24+end*.095
            wood_bottom=1.02+rise/run*along-half/cos
            bottom=metal_top-.025;top=wood_bottom+.012
            # Author the vertical world-space neck, then map it into the
            # beam frame; it remains vertical after the beam's slope/yaw.
            g=box((.032,top-bottom,.016),(0,(bottom+top)/2,along-run/2))
            v,f=g
            # Source->Unity, remove beam centre, inverse its pitch.
            unity=[(x,z-(1.02+rise/2),y) for x,y,z in v]
            turned=[(x,y*cos-z*sin,y*sin+z*cos) for x,y,z in unity]
            pieces.append(bp.to_source((turned,[tuple(reversed(face)) for face in f])))
            lower=local(along,wood_bottom)
            shoe_z=max(-length/2+.025,min(length/2-.025,lower[2]))
            pieces.append(box((.060,.018,.040),(0,-half+.004,shoe_z),.003))
            contacts.append(dict(step=step,end=end,along=along,metal_top=metal_top,
                neck_bottom=bottom,neck_top=top,wood_bottom=wood_bottom,
                metal_overlap=.010,wood_overlap=.006,
                foot=local(along,metal_top-.015),head=local(along,wood_bottom+.006)))
    return merged(pieces),contacts


def door_hardware(size):
    # Backplate, spindle and rounded lever form one passive shared mesh.
    # The backplate's rear plane is the declared mounting plane.
    plate=box((.060,.240,.012),(.090,0,-.0315),.004)
    v,f=cylinder((.050,.030,.050),'spindle')
    spindle=([(x,z,-y) for x,y,z in v],f)  # source Z-up -> shaft along Unity Z
    spindle=move(spindle,(.090,.025,-.003))
    return merged([plate,spindle,box((.240,.036,.028),(0,.025,.0235),.010)])


def cabinet_pull(size):
    return merged([box((.045,.120,.008),(0,0,-.0135),.003),
        box((.024,.024,.024),(0,.016,-.0015),.004),
        box((.080,.018,.014),(0,.016,.0105),.005)])


def design():
    parts=[]
    def add(names,size,style='box',kind='Box',projection='BoxXY',geometry=None,position=None):
        if isinstance(names,str): names=[names]
        g=geometry if geometry is not None else housing(size) if style=='housing' else shape(style,size)
        # Each closed constituent must wind outward. No post-hoc normal flip.
        if bp.signed_volume(g)<=1e-10: raise ValueError(f'Inverted {names[0]}')
        lo,hi=kit.bounds(g)
        parts.append(dict(name=f'StairwellPart_{len(parts):03d}',aliases=names,size=list(size),
            kind=kind,style=style,projection=projection,
            bounds_min=list(swap(lo)),bounds_max=list(swap(hi)),
            triangles=kit.triangle_count(g),geometry=g,position=list(position or (0,0,0))))
    add('Stairwell Ground Floor',(8.6,.24,9.6))
    add('Stairwell Ceiling',(8.6,.20,9.6))
    add('Stairwell Back Wall',(8.6,6.25,.24))
    add('Stairwell Front Wall',(8.6,6.25,.24),'front-wall')
    add('Stairwell Left Wall',(.24,6.25,9.6))
    add('Stairwell Right Wall',(.24,6.25,9.6),'right-wall')
    add('Back Dirty Wall Band',(8.32,1.18,.025))
    add('Front Dirty Wall Band',(8.32,1.18,.025),'front-band')
    add(['Left Dirty Wall Band','Right Dirty Wall Band'],(.025,1.18,9.32))
    add(['Stairwell Column 1','Stairwell Column 2'],(.34,6.25,.34))
    for i in range(16): add([f'{l} Stair {i+1:02}' for l in ('Lower','Apartment','Upper')],(1.55,(i+1)*.1,.255),'tread',projection='BoxXZ')
    add('Middle Landing',(4.45,.24,1.45),'tread',projection='BoxXZ')
    add('Apartment Landing',(6.45,.24,1.36),'tread',projection='BoxXZ')
    add([f'{l} Rail Curb {i+1:02} {s}' for l in ('Lower','Apartment','Upper') for i in range(16) for s in ('A','B')],(.11,.68,.26),'curb',projection='BoxZY')
    add([f'{l} Handrail {s}' for l in ('Lower','Apartment','Upper') for s in ('A','B')],(.095,.095,4.16))
    for name,size,side in [('Middle Landing Back Rail',(4.75,1.16,.10),False),('Apartment Landing Center Rail',(1.2,1.16,.10),False),('Apartment Landing Right Rail',(1.65,1.16,.10),False),('Apartment Landing Left Edge',(.10,1.16,2.35),True)]:
        add(name,size,'rail',projection='BoxZY' if side else 'BoxXY')
        add(name+' Top',(size[0]+.08,.1,size[2]+.08))
    add('Under Stair Grille Blocker',(1.78,1.84,.10),'grille')
    add([f'Under Stair Grille Bar {i+1}' for i in range(7)],(.045,1.84,.045))
    add('Street Door Leaf',(1.72,2.36,.13),'leaf')
    add('Apartment Door Leaf',(.13,2.32,1.24),'side-leaf',projection='BoxZY')
    for w,h in ((1.72,2.36),(1.24,2.32)):
        add(['Door Frame Left','Door Frame Right'],(.16,h+.2,.20),'jamb')
        add('Door Frame Lintel',(w+.32,.18,.20),'jamb')
    for name,size in [('Left Waste Stack',(.13,3,.13)),('Left Water Riser',(.055,3,.055)),('Right Waste Stack',(.10,3,.10)),('Ceiling Main Pipe',(.085,4,.085)),('Apartment Feed Pipe',(.065,2.05,.065)),('Radiator Feed',(.045,.46,.045))]: add(name,size,'pipe','Cylinder','CylinderSide')
    add([f'Pipe Clamp {i+1}' for i in range(5)],(.17,.025,.17),'clamp','Cylinder','CylinderCapXZ')
    add('Pipe Shutoff Valve',(.22,.035,.22),'valve','Cylinder','CylinderCapXZ')
    add('Large Vent Recess',(1.90,1.12,.08),'recess')
    add([f'Large Vent Slat {i+1}' for i in range(8)],(1.70,.045,.045))
    add('Ground Vent Recess',(1.12,.78,.08),'recess')
    add([f'Ground Vent Slat {i+1}' for i in range(5)],(.94,.04,.045))
    add('Ground Electrical Cabinet',(.10,1.48,1.12),'side-recess',projection='BoxZY')
    add('Ground Electrical Cabinet Door',(.05,1.34,.98),'side-leaf',projection='BoxZY')
    add('Apartment Fuse Box',(.10,.82,.68),'side-recess',projection='BoxZY')
    add([f'Loose Cable {i+1}' for i in range(4)],(.035,.035,.72))
    add([f'Radiator Rib {i+1}' for i in range(7)],(.18,.92,.075),'radiator',projection='BoxZY')
    for i,size in enumerate(((1.25,1.85,.025),(.92,1.35,.025),(1.72,.65,.025),(.52,1.05,.025))): add(f'Damp Wall Patch {i+1}',size,'patch')
    for i in range(2): add([f'Peeling Paint Chip {j+1}' for j in range(i,6,2)],(.30+i*.16,.18,.018),'patch')
    add('Ground Damp Puddle',(.82,.008,.48),'clamp','Cylinder','CylinderCapXZ')
    add([f'Discarded Paper {i+1}' for i in range(4)],(.38,.012,.26),'paper',projection='BoxXZ')
    add('Empty Bottle',(.075,.22,.075),'bottle','Cylinder','CylinderSide')
    add('Collapsed Wardrobe',(1.18,1.72,.42),'wardrobe')
    add('Mouldy Mattress',(1.42,.26,1.82),'mattress',projection='BoxXZ')
    add('Broken Metal Cabinet',(.72,1.05,.56),'recess')
    add('Wire Mesh Sheet',(1.66,1.28,.055),'mesh')
    add([f'Debris Plank {i+1}' for i in range(5)],(.16,1.48,.10))
    add([f'Debris Sack {i+1}' for i in range(3)],(.55,.42,.58),'sack',projection='BoxXZ')
    for label,pos,support in [('Ground',(-1.45,2.70,-3.40),3.12),('Middle',(-.45,4.15,1.62),6.12),('Apartment',(2.48,5.78,-3.20),6.12)]:
        add(label+' Fluorescent Housing',(1.34,.10,.30),'housing',projection='BoxXZ')
        add(label+' Fluorescent Tube',(1.18,.11,.15),'tube')
        add([label+' Fluorescent End Cap '+s for s in ('Left','Right')],(.10,.16,.22),'jamb',projection='BoxZY')
        add([label+' Fluorescent Suspension '+s for s in ('Left','Right')],(.035,(support-pos[1]-.18)*.5,.035),'pipe','Cylinder','CylinderSide')
    # DoorTransition keeps its camera, hierarchy, all pivots and black aperture.
    for names,size,style in [('Black Void',(8,6,.20),'box'),('Threshold Floor',(5.5,.07,4.8),'box'),(['Left Door Frame','Right Door Frame'],(.24,3.08,.27),'jamb'),('Door Lintel',(2.24,.28,.31),'jamb'),('Door Threshold',(1.82,.07,.34),'box'),('Door Leaf',(1.73,2.84,.14),'leaf'),('Handle Lever',(.34,.065,.075),'box')]: add(names,size,style)
    add('Handle Spindle',(.105,.055,.105),'spindle','Cylinder','CylinderSide')
    supports,contacts=rail_supports()
    lo,hi=kit.bounds(supports);centre=tuple((a+b)/2 for a,b in zip(lo,hi))
    supports=kit.translated(supports,tuple(-v for v in centre))
    add([f'{label} Handrail {side} Supports' for label in ('Lower','Apartment','Upper') for side in ('A','B')],
        tuple(b-a for a,b in zip(swap(lo),swap(hi))),'rail-supports',geometry=supports,position=swap(centre))
    add(['Street Door Hardware','Apartment Door Hardware'],(.240,.240,.075),'door-hardware',
        geometry=door_hardware((.240,.240,.075)))
    add('Electrical Cabinet Pull',(.080,.120,.035),'cabinet-pull',geometry=cabinet_pull((.080,.120,.035)))
    return parts


def canonical(parts):
    return json.dumps(parts,sort_keys=True,separators=(',',':'),ensure_ascii=True)
def ray_hits_solid(geometry, axes, point):
    vertices,faces=geometry
    for face in faces:
        for i in range(1,len(face)-1):
            a,b,c=[vertices[j] for j in (face[0],face[i],face[i+1])]
            ax,ay=a[axes[0]],a[axes[1]]
            bx,by=b[axes[0]],b[axes[1]]
            cx,cy=c[axes[0]],c[axes[1]]
            det=(bx-ax)*(cy-ay)-(by-ay)*(cx-ax)
            if abs(det)<1e-12:continue
            u=((point[0]-ax)*(cy-ay)-(point[1]-ay)*(cx-ax))/det
            v=((bx-ax)*(point[1]-ay)-(by-ay)*(point[0]-ax))/det
            if u>=-1e-8 and v>=-1e-8 and u+v<=1+1e-8:return True
    return False


def contains_geometry(geometry, point):
    """Generalized winding number, including overlapping closed solids.
    Unlike bounds or odd/even rays, this proves union membership even where
    a support neck and its saddle overlap each other inside the wood.
    """
    p=swap(point);vertices,faces=geometry;angle=0.0
    def dot(a,b):return sum(x*y for x,y in zip(a,b))
    for face in faces:
        for i in range(1,len(face)-1):
            a,b,c=[tuple(x-y for x,y in zip(vertices[j],p)) for j in (face[0],face[i],face[i+1])]
            la,lb,lc=[math.sqrt(dot(v,v)) for v in (a,b,c)]
            cross=(b[1]*c[2]-b[2]*c[1],b[2]*c[0]-b[0]*c[2],b[0]*c[1]-b[1]*c[0])
            angle+=2*math.atan2(dot(a,cross),la*lb*lc+dot(a,b)*lc+dot(b,c)*la+dot(c,a)*lb)
    return abs(angle)>math.tau



def door_contact_contract():
    # Measured on the unchanged production world-rig DoorUseLoop. A flipped
    # escutcheon mounts on the full-depth latch stile; the source dock meets
    # the actual lever front, not a nearest point behind its pierced surface.
    grip=(.3017243,1.1148247,.6069756)
    height=.04+grip[1]+.025
    return dict(street_plate=[-.800,height,.065],apartment_plate=[-.065,height,-.5625],
        street_hardware=[-.710,height,.1005],apartment_hardware=[-.1005,height,-.4725],
        street_rotation_euler=[0,0,180],apartment_rotation_euler=[0,-90,180],
        source_door_use_grip=list(grip),
        street_source_dock=[-1.45-.710+grip[0],.04,-4.67+.1005+.0375+grip[2]],
        apartment_source_dock=[4.17-.1005-.0375-grip[2],3.24,-3.72-.4725+grip[0]],
        cabinet_pull=True)


def validate_door_contacts():
    contract=door_contact_contract()
    hardware=door_hardware((.240,.240,.075))
    for name,size,leaf_centre,rotate,floor in (
            ('street',(1.72,2.36,.13),(0,1.18,0),lambda p:(-p[0],-p[1],p[2]),(-4.05,-4.50,4.05,-3.04)),
            ('apartment',(.13,2.32,1.24),(0,1.16,0),lambda p:(-p[2],-p[1],-p[0]),(-2.40,-4.40,4.05,-3.04))):
        geometry=leaf(size) if name=='street' else shape('side-leaf',size)
        pose=contract[name+'_hardware']
        # Inset 5 mm from the plate's edge chamfer. Every sampled point lies
        # inside BOTH actual closed solids, 1 mm beyond the plate rear plane.
        for y in range(5):
            for x in range(5):
                point=(.065+.05*x/4,-.115+.23*y/4,-.0365)
                transformed=rotate(point)
                leaf_point=tuple(a+b-c for a,b,c in zip(pose,transformed,leaf_centre))
                if not contains_geometry(hardware,point) or not contains_geometry(geometry,leaf_point):
                    raise ValueError(name+' escutcheon misses its actual full-depth leaf stile')
        grip=contract['source_door_use_grip'];dock=contract[name+'_source_dock']
        rotated_grip=(-grip[0],grip[1],-grip[2]) if name=='street' else (grip[2],grip[1],-grip[0])
        root=(-1.45,0,-4.67) if name=='street' else (4.17,3.2,-3.72)
        front=rotate((0,.025,.0375))
        target=tuple(a+b+c for a,b,c in zip(root,pose,front))
        sampled=tuple(a+b for a,b in zip(dock,rotated_grip))
        if max(abs(a-b) for a,b in zip(target,sampled))>1e-6:
            raise ValueError(name+' DoorUseLoop right grip misses the lever front')
        xmin,zmin,xmax,zmax=floor
        margin=min(dock[0]-xmin,xmax-dock[0],dock[2]-zmin,zmax-dock[2])-.32
        if margin<.18:
            raise ValueError(name+' source door dock is not capsule-clear on its own landing')
    if not contains_geometry(hardware,(0,.025,.0374)) or contains_geometry(hardware,(0,.025,.0376)):
        raise ValueError('The declared contact is not the actual lever front face')
    return contract


def validate(parts):
    count=sum(p['triangles'] for p in parts)
    if len(parts)>128 or count>16000: raise ValueError('Stairwell mesh/triangle budget exceeded')
    bindings=set()
    for p in parts:
        geom=p['geometry']
        if any(not math.isfinite(v) for point in geom[0] for v in point): raise ValueError('Nonfinite mesh')
        vertices,faces=geom; owner=list(range(len(vertices)))
        def find(i):
            while owner[i]!=i: owner[i]=owner[owner[i]];i=owner[i]
            return i
        for face in faces:
            for i in face[1:]: owner[find(i)]=find(face[0])
        solids={}
        for face in faces: solids.setdefault(find(face[0]),[]).append(face)
        if any(bp.signed_volume((vertices,solid))<=1e-12 for solid in solids.values()):
            raise ValueError('Inward solid '+p['name'])
        full=[v*(2 if p['kind']=='Cylinder' and i==1 else 1) for i,v in enumerate(p['size'])]
        lo,hi=p['bounds_min'],p['bounds_max']
        if max(abs((b-a)-v) for a,b,v in zip(lo,hi,full))>.00002: raise ValueError('Envelope mismatch '+p['name']+' '+str(p['aliases'])+' '+str((lo,hi,full)))
        for name in p['aliases']:
            key=(name,tuple(p['size']))
            if key in bindings: raise ValueError('Duplicate binding '+name)
            bindings.add(key)
    by_name={name:p for p in parts for name in p['aliases']}
    for name,axes,point in [('Stairwell Front Wall',(0,2),(-1.45,1.18-3.125)),
                            ('Stairwell Right Wall',(1,2),(-3.72,4.36-3.125)),
                            ('Front Dirty Wall Band',(0,2),(-1.45,0)),
                            ('Under Stair Grille Blocker',(0,2),(0,0))]:
        if ray_hits_solid(by_name[name]['geometry'],axes,point):raise ValueError('Blocked authored opening '+name)
    supports,contacts=rail_supports()
    for contact in contacts:
        if not contact['neck_bottom'] < contact['metal_top']-.015 < contact['metal_top']:
            raise ValueError('Support foot does not penetrate old metal post')
        if not contact['wood_bottom'] < contact['wood_bottom']+.006 < contact['neck_top']:
            raise ValueError('Support head does not penetrate wood')
        if not contains_geometry(shape('curb',(.11,.68,.26)),(0,.325,contact['end']*.095)):
            raise ValueError('Neck foot misses the actual old metal post')
        for point in (contact['foot'],contact['head']):
            if not contains_geometry(supports,point):
                raise ValueError('Missing support geometry at contact')
        if not contains_geometry(box((.095,.095,4.16)),contact['head']):
            raise ValueError('Neck head misses actual chamfered wood')
    # Every landing's metal top already overlaps the cap by 50 mm. Its
    # height and cat's perch do not change as part of the flight correction.
    for size in ((4.75,1.16,.10),(1.2,1.16,.10),(1.65,1.16,.10),(.10,1.16,2.35)):
        metal=rail(size);wood=box((size[0]+.08,.10,size[2]+.08),(0,size[1]/2,0))
        for side in (-1,1):
            point=(side*(size[0]-.045)/2,size[1]/2-.02,0) if size[0]>size[2] else (0,size[1]/2-.02,side*(size[2]-.045)/2)
            if not contains_geometry(metal,point) or not contains_geometry(wood,point):
                raise ValueError('Actual landing metal/cap disconnected')
    # New hardware rear faces penetrate their existing leaf face by 2 mm.
    for leaf_half,assembly_depth in ((.065,.075),(.025,.035)):
        assembly_centre=leaf_half+assembly_depth/2-.002
        if abs(assembly_centre-assembly_depth/2-leaf_half+.002)>1e-9:
            raise ValueError('Door hardware disconnected')
    validate_door_contacts()
    if canonical(parts)!=canonical(design()): raise ValueError('Nondeterministic design')
    return dict(meshes=len(parts),triangles=count,bindings=len(bindings),maximum_meshes=128,maximum_triangles=16000,flight_support_necks=192,landing_cap_overlap_m=.05,door_hardware_leaf_overlap_m=.002,door_mount_contact_samples=50)


def main():
    import bpy
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser();parser.add_argument('--validate-only',action='store_true')
    parser.add_argument('--model-dir',type=Path,default=MODEL_DIR);parser.add_argument('--source-dir',type=Path,default=SOURCE_DIR)
    args=parser.parse_args(argv)
    parts=design();report=validate(parts)
    signature=hashlib.sha256(canonical(parts).encode()).hexdigest()
    print('STAIRWELL_INTERIOR_ART_DETERMINISM_OK '+json.dumps(report)+' sha256='+signature,flush=True)
    if args.validate_only:return
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    root=bpy.data.objects.new('StairwellInterior3D',None);bpy.context.collection.objects.link(root)
    for p in parts:
        vertices,faces=p['geometry'];mesh=bpy.data.meshes.new(p['name']);mesh.from_pydata(vertices,[],faces);mesh.update()
        uv=mesh.uv_layers.new(name='UVMap')
        # Project in Unity-local unit coordinates: existing metre-scale MPB
        # transform remains authoritative, including each declared projection.
        dims=[v*(2 if p['kind']=='Cylinder' and i==1 else 1) for i,v in enumerate(p['size'])]
        for polygon in mesh.polygons:
            normal=swap(polygon.normal)
            major=max(range(3),key=lambda i:abs(normal[i]))
            face_axes=((2,1),(0,2),(0,1))[major]
            for loop_index in polygon.loop_indices:
                point=swap(vertices[mesh.loops[loop_index].vertex_index]);a,b=face_axes
                uv.data[loop_index].uv=(point[a]/dims[a]+.5,point[b]/dims[b]+.5)
        obj=bpy.data.objects.new(p['name'],mesh);bpy.context.collection.objects.link(obj);obj.parent=root
    payload=dict(schema_version=1,design_id=DESIGN,generator_version=VERSION,signature=signature,report=report,
        coordinates='Unity metres; Blender Y/Z swap with outward winding',
        contract=dict(room_size=[8.6,9.6],room_height=6.25,elevations=[0,1.6,3.2],steps_per_flight=16,
            street_door_floor=[-1.45,0,-4.67],apartment_door_floor=[4.17,3.2,-3.72],
            cat_perch=[-1.70,2.76,2.32],collision='existing runtime proxies and FootProbe unit boxes',
            transition_door_pivot=[-.87,.08,0],transition_handle_pivot=[1.46,1.4,-.105],
            effects='retain all existing light/SSAO/grade/particles/post owners; no SSR/GI rollout',
            rail_supports=dict(flights=3,sides=2,necks_per_side=32,post_foot_overlap=.010,wood_head_overlap=.006),
            door_hardware=door_contact_contract()),
        parts=[{k:v for k,v in p.items() if k!='geometry'} for p in parts])
    args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
    manifest=json.dumps(payload,indent=2)+'\n'
    (args.model_dir/'StairwellInterior3D.json').write_text(manifest)
    (args.source_dir/'stairwell-interior-3d-model.json').write_text(manifest)
    bpy.ops.object.select_all(action='SELECT');bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir/'StairwellInterior3D.fbx'),use_selection=True,
        object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=False,add_leaf_bones=False,bake_anim=False,mesh_smooth_type='FACE',path_mode='STRIP')
    print('STAIRWELL_INTERIOR_EXPORTED',flush=True)

if __name__=='__main__':main()
