#!/usr/bin/env python3
"""Thirty fixed-metre narrative compositions. +Z faces the road; origins are ground.

N04/N16 are wall-mounted sheets only (paper centre 1.45m above the origin).
No text, animation, light, reflection or gameplay geometry is generated at runtime.
"""
from __future__ import annotations
import argparse, hashlib, importlib.util, json, math, sys
from pathlib import Path
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
spec = importlib.util.spec_from_file_location('narrative_prop_helpers', ROOT / 'tools/build-village-life-props-3d-model.py')
p = importlib.util.module_from_spec(spec); spec.loader.exec_module(p)
kit, bp, box, beam, cyl = p.kit, p.bp, p.box, p.beam, p.cylinder
VERSION = '1.0.0'
DESIGN = 'village_narrative_30_v1'
COLORS = {'Wood': (.30,.235,.175,1), 'FreshWood': (.46,.355,.245,1),
          'Iron': (.25,.24,.22,1), 'Rust': (.33,.205,.14,1),
          'Cloth': (.39,.365,.30,1), 'Paper': (.70,.67,.55,1),
          'Stone': (.39,.40,.38,1), 'Rubber': (.105,.11,.105,1),
          'Glass': (.28,.315,.30,1), 'Snow': (.76,.79,.78,1)}
SURFACES = {'Wood':'Timber','FreshWood':'Timber','Iron':'RustedIron','Rust':'RustedIron',
            'Cloth':'Cloth','Paper':'Paper','Stone':'BareStone','Rubber':'Rubber',
            'Glass':'DullGlass','Snow':'WindSnow'}

def move(g, v): return kit.translated(g, v)
def rotate(g, e, at=(0,0,0)): return move(bp.u_rotated(g, e), at)

def tube(a,b,r=.018,sides=8):
    a,b=Vector(a),Vector(b);d=b-a;q=Vector((0,1,0)).rotation_difference(d.normalized())
    g=cyl((0,0,0),r*2,d.length,sides)
    return ([tuple(q@Vector(v)+(a+b)*.5) for v in g[0]],g[1])

def path(points,r=.02): return kit.merge_all(tube(a,b,r) for a,b in zip(points,points[1:]))

def ring(center, radius, minor=.016, axis='z', start=0, end=math.tau, steps=24):
    points=[]
    for i in range(steps+1):
        a=start+(end-start)*i/steps
        v=(math.cos(a)*radius,math.sin(a)*radius,0)
        if axis=='x':v=(0,v[0],v[1])
        if axis=='y':v=(v[0],0,v[1])
        points.append(tuple(center[j]+v[j] for j in range(3)))
    return path(points,minor)

def disc(center,diameter,depth=.035,axis='z',sides=24):
    return rotate(cyl((0,0,0),diameter,depth,sides), (90,0,0) if axis=='z' else ((0,0,90) if axis=='x' else (0,0,0)),center)

def panel(outline, thickness=.01):
    # Outline is a closed sheet boundary; extrusion retains a true visible edge.
    n=len(outline)
    normal=(Vector(outline[1])-Vector(outline[0])).cross(Vector(outline[2])-Vector(outline[0])).normalized()
    v=[tuple(Vector(point)+normal*side*thickness*.5) for side in (-1,1) for point in outline]
    f=[]
    for i in range(1,n-1):f.extend(((0,i+1,i),(n,n+i,n+i+1)))
    for i in range(n):
        j=(i+1)%n;f.extend(((i,j,j+n),(i,j+n,i+n)))
    if bp.signed_volume((v,f))<0:f=[tuple(reversed(face)) for face in f]
    return v,f

def curved_strip(center,radius,width=.035,height=.065,span=math.pi,steps=18):
    pts=[(center[0]+math.cos(i*span/steps)*radius,center[1],center[2]+math.sin(i*span/steps)*radius) for i in range(steps+1)]
    return kit.merge_all(beam(a,b,width,height) for a,b in zip(pts,pts[1:]))

def bench(width=1.1,height=.8,depth=.55):
    a=[box((0,height,0),(width,.07,depth))]
    for x in (-width*.41,width*.41):
        for z in (-depth*.36,depth*.36):a.append(beam((x*1.1,0,z*1.2),(x,height,z),.065))
    a.append(box((0,height*.28,0),(width*.86,.06,.07)))
    return a

def make_props():
    props=[]
    def add(number,label,groups,focus=None,anchors=None):
        kind=f'N{number:02d}';parts=[]
        for role,solids in groups.items():
            if not solids:continue
            for solid in solids:assert bp.signed_volume(solid)>1e-11,(kind,role,bp.signed_volume(solid))
            parts.append(dict(mesh=f'GEO_{kind}_{role}',role=role,surface=SURFACES[role],tint=COLORS[role],geometry=kit.merge_all(solids)))
        lo,hi=kit.bounds(kit.merge_all(part['geometry'] for part in parts))
        # Ground origins are literal support contacts, not rounded bounding
        # estimates. Sheets and the suspended bicycle retain wall heights.
        if number not in (4,11,16):
            offset=lo[1]
            for part in parts:part['geometry']=move(part['geometry'],(0,-offset,0))
            if focus:focus=(focus[0],focus[1]-offset,focus[2])
            lo,hi=kit.bounds(kit.merge_all(part['geometry'] for part in parts))
        points={'Focus':focus or (0,(lo[1]+hi[1])*.5,max(.06,hi[2])),
                'Approach':(0,0,hi[2]+1.20)}
        points.update(anchors or {})
        props.append(dict(id=number,kind=kind,label=label,origin='ground',parts=parts,anchors=points))

    # 01: narrow transport cradle, disconnected chair members and taut straps.
    wood=[]
    for x in (-.24,.24):
        wood += [box((x,.065,0),(.08,.13,.58)),beam((x,.09,-.18),(x,1.22,-.18),.06)]
    for y in (.15,.61,1.16):wood.append(box((0,y,-.18),(.58,.06,.05)))
    wood += [box((0,.40,.04),(.46,.055,.43)),curved_strip((0,1.03,-.16),.20,.035,.05)]
    for x in (-.14,-.05,.05,.14):wood.append(beam((x,.18,.10),(x+.025,1.05,.02),.042))
    cloth=[path([(-.26,.31,.21),(-.26,.68,.21),(.26,.68,.21),(.26,.31,.21)],.012),box((0,.65,.23),(.06,.075,.016))]
    add(1,'Chair transport cradle',{'Wood':wood,'Cloth':cloth})
    # 02: platform scales with dial, needle, worn increments and balance beam.
    iron=[box((0,.10,.10),(.78,.16,.62)),tube((0,.15,-.22),(0,1.19,-.22),.055),disc((0,1.25,-.19),.43,.08)]
    face=[disc((0,1.25,-.142),.365,.008)]
    for i in range(11):
        a=math.pi*.15+i*math.pi*.17
        iron.append(tube((math.cos(a)*.15,1.25+math.sin(a)*.15,-.131),(math.cos(a)*.172,1.25+math.sin(a)*.172,-.131),.005))
    iron += [tube((0,1.25,-.125),(-.075,1.375,-.125),.007),box((0,.21,.10),(.66,.04,.54))]
    add(2,'Baggage scales',{'Iron':iron,'Paper':face})
    # 03: crossed folding stand; broken webbing stays distinctly readable.
    wood=[]
    for x in (-.40,.40):
        wood += [beam((x,0,-.27),(x,.74,.22),.05),beam((x,0,.28),(x,.74,-.22),.05)]
    for z in (-.22,.22):wood.append(box((0,.73,z),(.90,.05,.05)))
    cloth=[box((x,.756,0),(.07,.014,.45)) for x in (-.31,-.13,.13)]
    cloth += [beam((.31,.756,-.21),(.31,.54,-.02),.06,.011)]
    add(3,'Folding luggage stand',{'Wood':wood,'Cloth':cloth,'Iron':[disc((x,.37,0),.055,.025,'x') for x in (-.43,.43)]})

    def note(number,second=False):
        outline=[(-.235,1.11,.024),(.13,1.095,.029),(.16,1.125,.033),(.23,1.10,.037),(.245,1.75,.022),(-.23,1.785,.026)]
        if second:outline=[(-.225,1.10,.025),(.23,1.14,.027),(.222,1.42,.033),(.24,1.76,.024),(-.24,1.79,.022),(-.207,1.47,.023)]
        wood=[box((0,1.752,.045),(.55,.043,.026))]
        if second:wood.append(box((-.229,1.43,.042),(.029,.66,.023)))
        iron=[disc((x,1.75,.066),.022,.009) for x in (-.195,.19)]
        paper=[panel(outline,.006)]
        add(number,'Nailed household note' if not second else 'Nailed road repair note',{'Paper':paper,'Wood':wood,'Iron':iron},(0,1.45,.047),{'Paper':(0,1.45,.045)})
    note(4)
    # 05: full-size bending former on a work stand, visible curved stock and screws.
    wood=bench(1.18,.74,.66)+[curved_strip((0,.84,-.22),.42,.075,.11)]
    fresh=[curved_strip((0,.86,-.22),.48,.022,.075)]
    iron=[]
    for a in (.18,.95,1.6,2.25,2.97):
        x,z=math.cos(a)*.5,-.22+math.sin(a)*.5
        iron += [tube((x,.76,z),(x,1.0,z),.018),tube((x-.06,.99,z),(x+.06,.99,z),.012)]
    add(5,'Chair back bending mould',{'Wood':wood,'FreshWood':fresh,'Iron':iron})
    # 06: pedal lathe, flywheel, live spindle, treadle and leather belt.
    wood=bench(1.65,.84,.46);iron=[tube((-.69,1.02,0),(.71,1.02,0),.021)]
    for x in (-.68,.68):wood.append(box((x,.97,0),(.10,.28,.29)))
    iron += [ring((-.48,.37,.04),.31,.03),tube((-.48,.37,-.11),(-.48,.37,.19),.025)]
    for a in range(6):
        t=a*math.tau/6;iron.append(tube((-.48,.37,.04),(-.48+math.cos(t)*.29,.37+math.sin(t)*.29,.04),.016))
    wood.append(box((.05,.11,.14),(.40,.045,.30)))
    iron.append(beam((.05,.13,.11),(-.48,.36,.17),.024))
    fresh=[rotate(cyl((0,0,0),.105,.89,12),(0,0,90),(0,1.02,0))]
    cloth=[path([(-.79,.37,.07),(-.72,1.02,.07),(-.57,1.02,.07),(-.17,.37,.07),(-.79,.37,.07)],.009)]
    add(6,'Pedal spindle lathe',{'Wood':wood,'FreshWood':fresh,'Iron':iron,'Cloth':cloth})
    # 07: three unmistakably different chair-back templates on a raised slatted board.
    wood=[box((x,.83,-.06),(.055,1.66,.06)) for x in (-.46,.46)]
    wood += [box((x,1.05,-.09),(.22,1.12,.035)) for x in (-.36,-.12,.12,.36)]
    templates=[]
    for i in range(3):
        cy=.73+i*.34
        templates.append(path([(-.36,cy,0),(-.32,cy+.13,0),(-.18,cy+.23,0),(.0,cy+.26,0),(.20,cy+.23,0),(.35,cy+.12,0)],.032))
    add(7,'Three generations of chair templates',{'Wood':wood,'FreshWood':templates,'Iron':[disc((x,y,.015),.025,.009) for x in (-.30,.3) for y in (.8,1.14,1.48)]})
    # 08: tilted weaving frame; loose ends show an unfinished seat.
    wood=bench(.76,.72,.54)
    for z in (-.27,.27):wood.append(box((0,.78,z),(.68,.075,.06)))
    for x in (-.32,.32):wood.append(box((x,.78,0),(.06,.075,.57)))
    cloth=[]
    for i in range(7):
        x=-.255+i*.085;cloth.append(box((x,.825,0),(.038,.009,.52)))
    for i in range(5):cloth.append(box((0,.836,-.205+i*.075),(.62,.010,.035)))
    cloth.append(beam((.23,.833,.19),(.26,.43,.40),.035,.009))
    add(8,'Seat weaving frame',{'Wood':wood,'Cloth':cloth,'FreshWood':[box((x,.83,.26),(.027,.02,.15)) for x in (-.28,.28)]})
    # 09: full-size bentwood walking support with broad feet and continuous hand rails.
    wood=[]
    for x in (-.33,.33):
        wood.append(path([(x*1.25,0,-.24),(x,.76,-.19),(x,.88,-.09),(x,.89,.14),(x,.82,.24),(x*1.25,0,.29)],.032))
        wood.append(beam((x,.37,-.2),(x,.37,.23),.035))
    wood.append(path([(-.33,.86,-.14),(-.25,.89,-.20),(.25,.89,-.20),(.33,.86,-.14)],.032))
    add(9,'Bentwood walking frame',{'Wood':wood,'Cloth':[tube((x,.889,-.04),(x,.889,.13),.037) for x in (-.33,.33)]})
    # 10: folded guest bed with sagging fabric between two tubular halves.
    iron=[]
    for x in (-.37,.37):
        iron.append(path([(x,0,.15),(x,.16,.02),(x,1.48,-.12),(x,1.67,-.12)],.025))
        iron.append(path([(x,.07,-.17),(x,.30,-.19),(x,1.60,-.15)],.023))
    for y in (.19,.94,1.65):iron.append(tube((-.37,y,-.10),(.37,y,-.10),.025))
    cloth=[panel([(-.34,.32,-.06),(.34,.32,-.06),(.34,1.61,-.10),(.13,1.56,-.02),(-.17,1.58,.01),(-.34,1.61,-.10)],.018)]
    iron += [tube((x,.39,-.07),(x,.36,.04),.009) for x in (-.30,-.18,-.06,.06,.18,.30)]
    add(10,'Folded guest bed',{'Iron':iron,'Cloth':cloth})
    # 11: small bicycle suspended on hooks, no saddle; flat tyre rests against rim.
    iron=[];rubber=[]
    for x in (-.45,.45):
        rubber.append(ring((x,.67,.075),.235,.028))
        iron.append(ring((x,.67,.075),.218,.012))
        for i in range(10):
            a=i*math.tau/10;iron.append(tube((x,.67,.075),(x+math.cos(a)*.215,.67+math.sin(a)*.215,.075),.003))
    pts=[(-.45,.67,.075),(-.16,1.05,.075),(.08,.69,.075),(.31,1.05,.075),(-.16,1.05,.075),(.08,.69,.075),(-.45,.67,.075)]
    iron += [path(pts,.022),tube((.31,1.05,.075),(.45,.67,.075),.021),tube((-.16,1.05,.075),(-.17,1.15,.075),.016),path([(.31,1.05,.075),(.26,1.23,.075),(.22,1.24,.075)],.02)]
    iron += [tube((.22,1.24,-.12),(.22,1.24,.26),.016),ring((.08,.69,.075),.10,.010),tube((.08,.69,.075),(.19,.62,.12),.009)]
    for x in (-.16,.31):iron.append(path([(x,1.20,-.17),(x,1.10,.11),(x,1.05,.14)],.022))
    add(11,'Child bicycle on hooks',{'Iron':iron,'Rubber':rubber,'Rust':[tube((-.45,.67,.08),(.08,.69,.08),.007)]},(0,.87,.12))
    # 12: paired ladder halves, hooks and short rungs.
    wood=[];iron=[]
    for side in (-1,1):
        for x in (-.29,.29):wood.append(beam((x,0,side*.35),(x,1.51,0),.054))
        for i in range(5):wood.append(box((0,.19+i*.29,side*(.31-i*.066)),(.61,.055,.065)))
    for x in (-.29,.29):iron.append(path([(x,1.44,0),(x,1.63,-.015),(x,1.66,-.12),(x,1.56,-.16)],.017))
    add(12,'Folding bunk ladder',{'Wood':wood,'Iron':iron})
    # 13: weathered grindstone on treadle stand beneath a crooked metal hood.
    wood=bench(.92,.58,.57);iron=[]
    stone=[disc((0,.92,0),.63,.12,'x')]
    iron += [tube((-.33,.92,0),(.40,.92,0),.027),path([(.39,.92,0),(.40,.77,.16),(.52,.77,.16)],.024)]
    wood.append(box((.12,.12,.13),(.31,.045,.35)))
    iron.append(tube((.12,.12,.13),(.40,.77,.16),.015))
    for x in (-.52,.52):wood.append(beam((x,0,-.31),(x,1.54,-.28),.055))
    iron.append(panel([(-.61,1.58,-.39),(.60,1.54,-.40),(.63,1.46,.34),(-.59,1.50,.31)],.02))
    add(13,'Pedal grindstone',{'Wood':wood,'Iron':iron,'Stone':stone})
    # 14: open waffle iron: two cast plates, inset grid and long insulated handles.
    iron=[];wood=bench(.79,.71,.60)
    iron += [disc((0,.79,0),.53,.045,'y'),disc((0,1.055,-.25),.53,.042)]
    for x in (-.18,-.09,0,.09,.18):
        length=math.sqrt(.235**2-x*x)*2
        iron += [box((x,.818,0),(.015,.018,length)),box((0,.818,x),(length,.018,.015)),box((x,1.055,-.222),(.015,length,.018)),box((0,1.055+x,-.222),(length,.015,.018))]
    iron += [tube((0,.79,.21),(0,.79,.59),.019),tube((0,1.27,-.25),(0,1.60,-.25),.019)]
    wood += [tube((0,.79,.43),(0,.79,.62),.032),tube((0,1.43,-.25),(0,1.64,-.25),.030)]
    add(14,'Open waffle press',{'Wood':wood,'Iron':iron})
    # 15: a portable lectern with missing hinge and a makeshift brace.
    wood=[]
    for x in (-.31,.31):wood += [beam((x,0,.31),(x,1.03,-.05),.055),beam((x,0,-.30),(x,.84,.21),.055)]
    wood += [rotate(box((0,0,0),(.84,.065,.62)),(-15,0,0),(0,1.04,0)),box((0,.993,.293),(.84,.055,.04)),beam((-.29,.15,.26),(.28,.88,.18),.035)]
    add(15,'Folding public lectern',{'Wood':wood,'Iron':[box((-.29,.83,.16),(.065,.06,.02))]})
    note(16,True)
    # 17: three-arm resort turnstile, long angled rotor, bolted weighted foot.
    iron=[box((0,.045,0),(.57,.09,.46)),box((0,.59,0),(.14,1.09,.17)),disc((0,1.05,.12),.25,.13)]
    for i in range(3):
        a=i*math.tau/3+.2;iron.append(tube((math.cos(a)*.07,1.05+math.sin(a)*.07,.20),(math.cos(a)*.61,1.05+math.sin(a)*.61,.45),.028))
    for x in (-.22,.22):
        for z in (-.16,.16):iron.append(cyl((x,.102,z),.045,.028,6))
    add(17,'Removed ski turnstile',{'Iron':iron,'Rust':[box((0,.58,.092),(.15,.21,.012))]})
    # 18: unplugged tubular boot dryer, two rows of open curved pipes.
    iron=[tube((-.64,.22,0),(.64,.22,0),.065)]
    for x in (-.6,.6):iron += [tube((x,.05,-.20),(x,.05,.22),.033),tube((x,.05,0),(x,1.46,0),.032)]
    for y in (.58,1.12):
        iron.append(tube((-.62,y,0),(.62,y,0),.033))
        for i in range(7):
            x=-.54+i*.18;iron.append(path([(x,y,0),(x,y+.07,.14),(x,y+.24,.21),(x,y+.29,.16)],.024))
    add(18,'Disconnected boot dryer',{'Iron':iron,'Rust':[box((-.57,.15,0),(.14,.17,.13))]})
    # 19: long ski press, raised curve and two hand-screw bridges.
    wood=bench(1.90,.66,.43);iron=[]
    wood.append(path([(-.92,.75,0),(-.77,.72,0),(-.32,.71,0),(.38,.73,0),(.79,.80,0),(.94,.91,0)],.045))
    for x in (-.55,.57):
        iron += [path([(x,.69,-.23),(x,1.10,-.23),(x,1.10,.23),(x,.69,.23)],.022),tube((x,.77,0),(x,1.19,0),.020),tube((x-.12,1.18,0),(x+.12,1.18,0),.014)]
        wood.append(box((x,.79,0),(.22,.035,.23)))
    add(19,'Manual ski straightening press',{'Wood':wood,'Iron':iron})
    # 20: folded rescue stretcher, canvas seams and clearly distinct repair patches.
    wood=[beam((x,0,.13),(x,1.91,-.08),.052) for x in (-.34,.34)]
    for y in (.2,1.66):wood.append(box((0,y,.11-y*.1),(.69,.043,.044)))
    cloth=[panel([(-.31,.31,.08),(.31,.31,.08),(.31,1.65,-.06),(-.31,1.65,-.06)],.019)]
    paper=[box((x,y,.115-y*.1),(.17,.16,.009)) for x,y in ((-.16,.56),(.14,1.08),(-.13,1.43))]
    iron=[disc((x,1.02,.03),.066,.025) for x in (-.34,.34)]
    add(20,'Patched folding rescue stretcher',{'Wood':wood,'Cloth':cloth,'Paper':paper,'Iron':iron})
    # 21: bent T-bar torn from the tow; thick support legs rest on ground.
    iron=[path([(-.63,.13,.20),(-.30,.16,.14),(0,.15,.12),(.53,.18,.14),(.64,.24,.19)],.047),
          path([(0,.15,.12),(.08,.64,.06),(-.05,1.21,-.04),(.12,1.49,-.13)],.034)]
    for strand in range(3):
        pts=[(.12+.018*math.cos(i*.8+strand*math.tau/3),1.49+i*.018,-.13-.025*i+.018*math.sin(i*.8+strand*math.tau/3)) for i in range(20)]
        iron.append(path(pts,.008))
    add(21,'Torn ski tow yoke',{'Iron':iron,'Rubber':[tube((-.62,.13,.20),(-.30,.16,.14),.055),tube((.30,.165,.13),(.55,.19,.15),.055)]})
    # 22: crumpled sheet-metal extraction hood, real folded sides and crushed neck.
    rust=[]
    rim=[(-.77,.08,-.33),(.62,.10,-.42),(.83,.10,.30),(-.66,.12,.43)]
    top=[(-.24,.65,-.13),(.21,.53,-.12),(.23,.45,.14),(-.18,.59,.12)]
    for i in range(4):
        j=(i+1)%4;rust.append(panel([rim[i],rim[j],top[j],top[i]],.018))
    rust.append(path([(-.02,.58,0),(.14,.87,-.04),(.11,1.06,-.10)],.12))
    iron=[tube(a,b,.025) for a,b in zip(rim,rim[1:]+rim[:1])]
    add(22,'Crushed extraction hood',{'Rust':rust,'Iron':iron})
    # 23: bent snow gauge with physical division bars, broken base fastening.
    wood=[beam((-.18,0,0),(-.11,.94,.01),.065),beam((-.11,.94,.01),(.38,1.96,.015),.065)]
    iron=[box((-.12,.14,0),(.32,.06,.35))]
    for i in range(18):
        y=.17+i*.098;x=-.18+.07*y/.94 if y<.94 else -.11+(y-.94)*.49/1.02
        iron.append(box((x,y,.048),(.135 if i%5==0 else .095,.012,.009)))
    iron += [tube((-.18,.18,-.13),(-.4,.035,-.34),.018)]
    add(23,'Bent snow gauge',{'Wood':wood,'Iron':iron})
    # 24: homemade snow scooter, carved chair-back seat and twin curled runners.
    wood=[];iron=[]
    for x in (-.21,.21):
        iron.append(path([(x,.045,-.56),(x,.045,.46),(x,.10,.62),(x,.20,.70)],.026))
        wood += [beam((x,.09,-.32),(x,.36,-.24),.045),beam((x,.09,.27),(x,.36,.2),.045)]
    wood += [box((0,.37,-.06),(.47,.045,.55)),curved_strip((0,.40,-.28),.20,.026,.05),path([(0,.13,.51),(0,.81,.46),(-.27,.83,.46)],.027),tube((-.27,.83,.46),(.27,.83,.46),.03)]
    add(24,'Chair-wood snow scooter',{'Wood':wood,'Iron':iron})
    # 25: chair checking jig has empty interior and four individual foot stops.
    wood=[box((0,.06,0),(.96,.12,.85))]
    for x in (-.44,.44):wood.append(box((x,.71,-.32),(.08,1.30,.08)))
    wood.append(box((0,1.33,-.32),(1.06,.09,.07)))
    iron=[]
    for x in (-.26,.26):
        for z in (-.24,.24):iron += [box((x,.15,z),(.15,.05,.035)),box((x-.065,.15,z+.04),(.025,.05,.10))]
    iron += [box((x,1.34,-.265),(.012,.04,.012)) for x in (-.35,-.21,-.07,.07,.21,.35)]
    add(25,'Chair level checking frame',{'Wood':wood,'Iron':iron})
    # 28: ratcheting banding spool, wound steel strip and broken hanging end.
    wood=bench(.90,.64,.51);iron=[];rust=[]
    iron += [tube((-.34,.99,0),(.34,.99,0),.035)]
    for x in (-.31,.31):
        iron += [beam((x,.66,0),(x,.99,0),.04),disc((x,.99,0),.62,.024,'x')]
    for r in (.11,.145,.18,.215,.25):rust.append(ring((0,.99,0),r,.019,'x'))
    iron += [box((.17,.73,.20),(.22,.12,.12)),path([(.16,.76,.20),(.18,.98,.32),(.33,1.03,.32)],.019)]
    rust.append(beam((0,.89,.27),(.08,.30,.34),.04,.01))
    add(28,'Steel packing strap tensioner',{'Wood':wood,'Iron':iron,'Rust':rust})
    # 29: low road winch on sled rails with wound cable and a large hand crank.
    wood=[];iron=[]
    for x in (-.42,.42):wood += [beam((x,.055,-.52),(x,.055,.58),.10,.14),beam((x,.11,0),(x,.66,0),.10)]
    wood += [box((0,.14,z),(1.02,.12,.12)) for z in (-.35,.35)]
    iron.append(tube((-.51,.67,0),(.56,.67,0),.045))
    for x in (-.32,.32):iron.append(disc((x,.67,0),.63,.045,'x'))
    for i in range(15):iron.append(ring((-.29+i*.041,.67,0),.247,.018,'x',steps=20))
    iron += [path([(.56,.67,0),(.58,1.03,.22),(.80,1.03,.22)],.032),path([(-.2,.46,.10),(-.1,.17,.57),(.26,.08,.68),(.42,.08,.45)],.018)]
    add(29,'Abandoned road hand winch',{'Wood':wood,'Iron':iron,'Rust':[disc((-.37,.67,0),.36,.07,'x',12)]})
    # 30: open tilted mixer drum. Bowl profile keeps the opening hollow.
    iron=[]
    for x in (-.36,.36):
        iron += [tube((x,0,-.33),(x,.77,0),.035),tube((x,0,.38),(x,.77,0),.035)]
    iron += [tube((-.49,.79,0),(.52,.79,0),.04),path([(.52,.79,0),(.54,.95,.24),(.73,.95,.24)],.026)]
    rings=[(.16,-.29),(.37,-.15),(.43,.20),(.31,.38),(.28,.38),(.40,.20),(.34,-.13),(.145,-.26)]
    vertices=[];faces=[];n=20
    for radius,y in rings:
        for i in range(n):
            a=i*math.tau/n;vertices.append((radius*math.cos(a),y,radius*math.sin(a)))
    for j in range(len(rings)-1):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    for i in range(n):faces.append((i,(len(rings)-1)*n+i,(len(rings)-1)*n+(i+1)%n,(i+1)%n))
    bowl=(vertices,faces)
    if bp.signed_volume(bowl)<0:bowl=(vertices,[tuple(reversed(f)) for f in faces])
    bowl=rotate(bowl,(57,0,0),(0,.94,0))
    concrete=[rotate(bp.u_tapered_cylinder((0,-.14,0),(.60,.07,.60),.85,15),(57,0,0),(0,.94,0))]
    add(30,'Hand concrete mixer',{'Iron':iron,'Rust':[bowl,ring((0,.97,.19),.34,.017)],'Stone':concrete})
    # 31: screw jack, threaded central shaft and dented cover; unused beam alongside.
    iron=[box((-.18,.06,0),(.44,.12,.37)),cyl((-.18,.33,0),.19,.48,12),tube((-.18,.36,0),(-.18,.82,0),.035),box((-.18,.83,0),(.25,.06,.23))]
    for i in range(12):iron.append(ring((-.18,.45+i*.026,0),.047,.008,'y',steps=12))
    iron.append(tube((-.40,.40,0),(.19,.40,0),.014))
    rust=[panel([(.1,.03,-.2),(.42,.03,-.18),(.36,.32,-.16),(.19,.35,-.17)],.014),panel([(.1,.03,-.2),(.11,.03,.19),(.2,.32,.15),(.19,.35,-.17)],.014)]
    wood=[beam((.58,.085,-.54),(.58,.085,.65),.15,.16)]
    add(31,'Screw jack and unraised beam',{'Iron':iron,'Rust':rust,'Wood':wood})
    # 32: removed, bent road mirror. Opaque dull metal glazing, finite cracks.
    iron=[path([(-.23,0,0),(-.20,.64,-.015),(.06,1.30,0),(.05,1.79,0)],.041),ring((.05,1.57,.028),.365,.035)]
    # Shallow convex face, closed; no runtime reflection material.
    glass=disc((.05,1.57,.057),.66,.04,'z',24)
    glass=([(x,y,z+.028*max(0,1-((x-.05)**2+(y-1.57)**2)/.33**2)) for x,y,z in glass[0]],glass[1])
    cracks=[path([(-.12,1.87,.092),(-.02,1.65,.103),(.15,1.54,.100),(.32,1.42,.084)],.003),path([(-.02,1.65,.103),(-.18,1.54,.099),(-.24,1.39,.079)],.003)]
    add(32,'Removed bend mirror',{'Iron':iron,'Glass':[glass],'Rubber':cracks},(.05,1.57,.095))
    return props

def signature(props):return hashlib.sha256(json.dumps((VERSION,DESIGN,props),sort_keys=True,separators=(',',':')).encode()).hexdigest()

def validate(props):
    assert [q['id'] for q in props]==[i for i in range(1,33) if i not in (26,27)]
    total=0
    for prop in props:
        for part in prop['parts']:
            vertices,faces=part['geometry'];edges={}
            assert all(math.isfinite(c) for v in vertices for c in v)
            for f in faces:
                for a,b in zip(f,f[1:]+f[:1]):edges[a,b]=edges.get((a,b),0)+1
            assert all(n==edges.get((b,a),0) for (a,b),n in edges.items()),part['mesh']
            assert bp.signed_volume(bp.to_source(part['geometry']))>1e-10,part['mesh']
            total+=sum(len(f)-2 for f in faces)
        lo,hi=kit.bounds(kit.merge_all(part['geometry'] for part in prop['parts']))
        assert lo[1]>=-1e-6 and hi[1]<2.2,(prop['kind'],lo,hi)
        if prop['id'] not in (4,11,16):assert abs(lo[1])<1e-6,prop['kind']
        assert max(hi[i]-lo[i] for i in range(3))<2.4,prop['kind']
        assert prop['anchors']['Approach'][2]>hi[2]+1
    assert signature(props)==signature(make_props()),'Non-deterministic geometry'
    assert total<110000,total
    return total

def manifest(props,total):
    rows=[]
    for prop in props:
        geom=kit.merge_all(part['geometry'] for part in prop['parts']);lo,hi=kit.bounds(geom)
        rows.append(dict(id=prop['id'],kind=prop['kind'],label=prop['label'],origin='ground',bounds_min=lo,bounds_max=hi,
            anchors=[dict(name=n,position=v) for n,v in prop['anchors'].items()],
            parts=[dict(mesh=q['mesh'],role=q['role'],surface=q['surface'],tint=q['tint'],
                        bounds_min=kit.bounds(q['geometry'])[0],bounds_max=kit.bounds(q['geometry'])[1],
                        triangles=sum(len(f)-2 for f in q['geometry'][1])) for q in prop['parts']]))
    return dict(generator='tools/build-village-narrative-3d-model.py',generator_version=VERSION,design_id=DESIGN,
        build_signature=signature(props),scale_mode='fixed_metres',uv_mode='projected_metres',forward='Unity +Z',
        colliders=False,lights=False,cameras=False,animation_count=0,prop_count=len(props),mesh_count=sum(len(q['parts']) for q in props),triangle_count=total,props=rows)

def preview(directory,groups):
    scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH'
    scene.display.shading.color_type='MATERIAL';scene.display.shading.show_cavity=True
    scene.display.shading.cavity_type='BOTH';scene.display.shading.show_shadows=True
    scene.display.shading.background_type='WORLD';scene.world.color=(.105,.115,.125)
    camera=bpy.data.objects.new('Review',bpy.data.cameras.new('Review'));scene.collection.objects.link(camera)
    scene.camera=camera;camera.data.type='ORTHO'
    scene.render.resolution_x=1800;scene.render.resolution_y=1250;scene.render.resolution_percentage=100
    for page in range(3):
        selected=groups[page*10:(page+1)*10]
        for root in groups:
            root.hide_render=root not in selected
            for child in root.children_recursive:child.hide_render=root not in selected
        for i,root in enumerate(selected):root.location=((i%5-2)*2.6,(-1 if i//5==0 else 1)*1.9,0)
        camera.location=(6,11,8.2);target=Vector((0,0,.6));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=15.2;scene.render.filepath=str(directory/f'VillageNarrative3D-{page+1}.png')
        bpy.ops.render.render(write_still=True)
        for root in selected:root.location=(0,0,0)
    for root in groups:
        root.hide_render=False
        for child in root.children_recursive:child.hide_render=False

def ensure_meta(path):
    target=path.with_name(path.name+'.meta')
    if target.exists():return
    rel=path.relative_to(ROOT).as_posix();text='fileFormatVersion: 2\nguid: '+hashlib.sha256(rel.encode()).hexdigest()[:32]+'\n'
    if path.is_dir():text+='folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    target.write_text(text,encoding='utf8')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model-dir',type=Path,default=ROOT/'Assets/Resources/VillageNarrative')
    parser.add_argument('--source-dir',type=Path,default=ROOT/'ArtSource/VillageNarrative')
    parser.add_argument('--validate-only',action='store_true');parser.add_argument('--no-preview',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    props=make_props();total=validate(props);data=manifest(props,total)
    if args.validate_only:
        assert json.loads((args.model_dir/'VillageNarrative3D.json').read_text(encoding='utf8'))==json.loads(json.dumps(data))
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);groups=[]
        for prop in props:
            root=bpy.data.objects.new(prop['kind'],None);bpy.context.scene.collection.objects.link(root);groups.append(root)
            for part in prop['parts']:p.make_mesh(part,root)
            for name,(x,y,z) in prop['anchors'].items():
                anchor=bpy.data.objects.new(f"ANCHOR_{prop['kind']}_{name}",None);bpy.context.scene.collection.objects.link(anchor)
                anchor.parent=root;anchor.location=(x,z,y)
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.export_scene.fbx(filepath=str(args.model_dir/'VillageNarrative3D.fbx'),use_selection=True,
            object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',
            bake_space_transform=True,add_leaf_bones=False,bake_anim=False,mesh_smooth_type='FACE')
        (args.model_dir/'VillageNarrative3D.json').write_text(json.dumps(data,indent=2)+'\n',encoding='utf8')
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/'VillageNarrative3D.blend'))
        if not args.no_preview:preview(args.source_dir,groups)
        if args.model_dir.is_relative_to(ROOT):
            ensure_meta(args.model_dir)
            for path in args.model_dir.iterdir():
                if path.suffix!='.meta':ensure_meta(path)
    print(f"VILLAGE NARRATIVE VALIDATION OK: {len(props)} objects, {data['mesh_count']} meshes, {total} triangles",flush=True)
    print(data['build_signature'],flush=True)

if __name__=='__main__':main()
