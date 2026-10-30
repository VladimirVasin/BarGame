#!/usr/bin/env python3
"""One enclosed impossible stair interior, in measured Unity metres.

The authored graph below generates BOTH runtime C# constants and the imported model contract.
No runtime file reads, random layout, primitive scenery or independently copied route coordinates.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
sys.dont_write_bytecode = True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import interior_kit as kit
import bar_parts as bp
MODEL_DIR=ROOT/'Assets/Resources/Stairwell/Labyrinth'
SOURCE_DIR=ROOT/'ArtSource/Stairwell/Labyrinth'
CODE_DIR=ROOT/'Assets/Scripts/Runtime/World'
WIDTH=3.2
MAIN=[(-1.45,4.8,2.25),(-1.45,4.8,3.2),(-1.45,8.4,11.84),(-1.45,8.4,14),
      (-13,8.4,14),(-13,8.4,28),(3,8.4,28),(3,8.4,37),(11.64,12,37),
      (14,12,37),(14,12,23),(29,12,23),(29,12,32),(29,12,40),(42,12,40),
      (42,15.6,48.64),(42,15.6,51),(28,15.6,51),(28,15.6,63),
      (45,15.6,63),(45,15.6,73),(45,15.6,74)]
BRANCHES=[
 ('plaster-blind',1,False,[(-13,8.4,14),(-24,8.4,14),(-24,8.4,23)]),
 ('plaster-return',1,True,[(-13,8.4,28),(-25,8.4,28),(-25,8.4,37),(3,8.4,37)]),
 ('overlook-blind',2,False,[(14,12,23),(14,12,14),(-6,12,14)]),
 ('service-return',2,True,[(29,12,23),(39,12,23),(39,12,32),(29,12,32)]),
 ('duct-blind',2,False,[(29,12,40),(22,12,40),(22,12,48)]),
 ('crates-blind',3,False,[(42,15.6,51),(53,15.6,51),(53,15.6,60)]),
 ('west-return',3,True,[(28,15.6,51),(18,15.6,51),(18,15.6,63),(28,15.6,63)]),
 ('red-blind',3,False,[(28,15.6,63),(28,15.6,74),(20,15.6,74)]),
 ('east-return',3,True,[(45,15.6,63),(56,15.6,63),(56,15.6,73),(45,15.6,73)])]
NEAR_LIGHTS=[
 ((-1.45,8.2,5.2),1,False),((-1.45,11.7,14),1,True),((-13,11.7,21),1,False),
 ((-7,11.7,28),1,False),((3,11.7,32),1,True),((-24,11.7,20),1,False),
 ((-25,11.7,34),1,False),((-11,11.7,37),1,False),
 ((7,13.5,37),2,False),((14,15.3,32),2,True),((22,15.3,23),2,False),
 ((29,15.3,32),2,False),((35,15.3,40),2,True),((14,15.3,17),2,False),
 ((39,15.3,28),2,False),((22,15.3,46),2,False),
 ((42,17.2,44),3,False),((35,18.9,51),3,True),((28,18.9,57),3,False),
 ((36,18.9,63),3,False),((45,18.9,71),3,True),((53,18.9,56),3,False),
 ((18,18.9,59),3,False),((56,18.9,69),3,False)]
# One closed, stepped interior surrounds the entire corrupted stairwell and route.
# Bounds: xmin,zmin,xmax,zmax,floor,ceiling. Overlaps are merged, never walled off.
INTERIOR_VOLUMES=[(-34,-6,10,43,-1,27),(6,6,49,52,3,30),(12,44,65,83,7,33)]
SCENERY_LIGHT_START=len(NEAR_LIGHTS)
UPPER_LIGHTS=[((-33.65,12.5,23),1,(0,0,90)),((-8,24,30),1,(0,0,0)),
              ((48.65,17.5,28),2,(0,0,-90)),((25,27,24),2,(0,0,0)),
              ((64.65,23,64),3,(0,0,-90)),((35,30,64),3,(0,0,0))]
LIGHT_ROTATIONS=[(0,0,0)]*SCENERY_LIGHT_START+[rotation for _,_,rotation in UPPER_LIGHTS]
def rotated_point(point,rotation):return bp.u_rotated(([point],[]),rotation)[0][0]
LIGHTS=NEAR_LIGHTS+[(position,stage,False) for position,stage,_ in UPPER_LIGHTS]
LIGHT_DIRECTIONS=[rotated_point((0,-1,0),rotation) for rotation in LIGHT_ROTATIONS]
DOOR=(45,15.6,74)


def stage_for(a,b):
    return 3 if b[1]>12.01 or (a[1]>=12 and b[1]>a[1]) else 2 if b[1]>8.41 or (a[1]>=8.4 and b[1]>a[1]) else 1


def supports():
    rows=[]
    ramps=[(a,b) for a,b in zip(MAIN,MAIN[1:]) if abs(a[1]-b[1])>.01]
    def add(name,stage,a,b):
        ramp=abs(a[1]-b[1])>.01
        axis=0 if abs(b[0]-a[0])>.001 else 2
        start_pad=end_pad=WIDTH*.5 if not ramp else 0
        # A landing beside a ramp's high end must not cover the preceding treads.
        for ra,rb in ramps:
            ramp_axis=0 if abs(rb[0]-ra[0])>.001 else 2
            if axis==ramp_axis:
                if a in (ra,rb):start_pad=0
                if b in (ra,rb):end_pad=0
        if a==MAIN[0]:start_pad=0
        direction=1 if b[axis]>a[axis] else -1
        ends=(a[axis]-direction*start_pad,b[axis]+direction*end_pad)
        if axis==0:bounds=(min(ends),a[2]-WIDTH*.5,max(ends),a[2]+WIDTH*.5)
        else:bounds=(a[0]-WIDTH*.5,min(ends),a[0]+WIDTH*.5,max(ends))
        rows.append(dict(id=name,stage=stage,start=a,end=b,width=WIDTH,bounds=bounds,ramp=ramp))
    for i,(a,b) in enumerate(zip(MAIN,MAIN[1:])):add('main-%02d'%i,stage_for(a,b),a,b)
    for name,stage,loop,points in BRANCHES:
        for i,(a,b) in enumerate(zip(points,points[1:])):add(name+'-%02d'%i,stage,a,b)
    return rows


def floor_at(row,p):
    a,b=row['start'],row['end'];dx=b[0]-a[0];dz=b[2]-a[2]
    t=max(0,min(1,((p[0]-a[0])*dx+(p[2]-a[2])*dz)/(dx*dx+dz*dz)))
    return a[1]+(b[1]-a[1])*t


def contract():
    return dict(main=MAIN,branches=BRANCHES,supports=supports(),lights=LIGHTS,light_directions=LIGHT_DIRECTIONS,
                scenery_light_start=SCENERY_LIGHT_START,interior_volumes=INTERIOR_VOLUMES,door=DOOR,width=WIDTH)


def contract_signature():
    return hashlib.sha256(json.dumps(contract(),sort_keys=True,separators=(',',':')).encode()).hexdigest()


def cs_float(n):return ('%.6f'%n).rstrip('0').rstrip('.')+'f'
def cs_vec(p,root=False):return 'new Vector3('+','.join(cs_float(n+(.04 if root and i==1 else 0)) for i,n in enumerate(p))+')'


def write_runtime(directory):
    lines=['// Generated by tools/build-stairwell-labyrinth-3d-model.py. Edit the authored graph there.',
           'using UnityEngine;','namespace BarPromenade','{','    internal static class StairwellLabyrinthData','    {',
           '        public const string Signature="'+contract_signature()+'";',
           '        public const float MainRouteLength='+cs_float(sum(math.dist(a,b) for a,b in zip(MAIN,MAIN[1:])))+';',
           '        public static StairwellLabyrinthSupportPlan[] CreateSupports() => new[] {']
    for r in supports():
        lines.append('            new StairwellLabyrinthSupportPlan("'+r['id']+'",'+str(r['stage'])+','+cs_vec(r['start'])+','+cs_vec(r['end'])+','+cs_float(WIDTH)+',Rect.MinMaxRect('+','.join(cs_float(n) for n in r['bounds'])+')),')
    lines+=['        };','        public static Vector3[] CreateRoute() => new[] {']
    for i,p in enumerate(MAIN):
        if i==0:p=(p[0],p[1],2.55)
        lines.append('            '+cs_vec(p,True)+',')
    lines+=['        };','        public static StairwellLabyrinthBranchPlan[] CreateBranches() => new[] {']
    for name,stage,loop,points in BRANCHES:
        lines.append('            new StairwellLabyrinthBranchPlan("'+name+'",'+str(stage)+','+str(loop).lower()+',new[] {'+','.join(cs_vec(p,True) for p in points)+'}),')
    lines+=['        };','        public static StairwellLabyrinthLightPlan[] CreateLights() => new[] {']
    for i,(p,stage,shadow) in enumerate(LIGHTS):
        direction=cs_vec(LIGHT_DIRECTIONS[i])
        lines.append('            new StairwellLabyrinthLightPlan('+cs_vec(p)+','+direction+','+str(stage)+','+str(shadow).lower()+','+str(i>=SCENERY_LIGHT_START).lower()+'),')
    lines+=['        };','    }','}','']
    directory.mkdir(parents=True,exist_ok=True)
    (directory/'StairwellLabyrinthData.cs').write_text('\n'.join(lines))


def design():
    groups={};all_supports=supports()
    def add(stage,role,g,chunk=None):
        if bp.signed_volume(g)<=0:raise ValueError('Inward solid '+role)
        lo,hi=kit.bounds(g)
        if chunk is None:chunk='C%d_%d'%(math.floor((lo[0]+hi[0])/32),math.floor((lo[2]+hi[2])/32))
        groups.setdefault((stage,chunk,role),[]).append(g)
    def box(stage,role,c,size,bevel=.008,chunk=None):add(stage,role,bp.u_box(c,size,bevel),chunk)
    def cylinder(stage,role,c,size,chunk=None):add(stage,role,bp.u_cylinder(c,size,8),chunk)
    def transform(g,origin,rotation=(0,0,0)):return kit.translated(bp.u_rotated(g,rotation),origin)
    def rail(stage,a,b,collision=True,chunk=None,wood=False):
        dx=b[0]-a[0];dz=b[2]-a[2];dy=b[1]-a[1];length=math.sqrt(dx*dx+dz*dz)
        yaw=math.degrees(math.atan2(dx,dz));slope=-math.degrees(math.atan2(dy,length));full=math.dist(a,b)
        mid=tuple((a[i]+b[i])/2 for i in range(3))
        for h,thick in ((.55,.05),(1.12,.075)):
            g=bp.u_rotated(bp.u_box((0,0,0),(thick,thick,full),.003),(slope,0,0))
            g=bp.u_rotated(g,(0,yaw,0));g=kit.translated(g,(mid[0],mid[1]+h,mid[2]))
            add(stage,'Wood' if wood and h>1 else 'Steel',g,chunk)
        count=max(2,math.ceil(length/1.2)+1)
        for n in range(count):
            t=n/(count-1);p=tuple(a[i]+(b[i]-a[i])*t for i in range(3))
            box(stage,'Steel',(p[0],p[1]+.55,p[2]),(.055,1.1,.055),.003,chunk)
        if collision:
            g=bp.u_rotated(bp.u_box((0,0,0),(.10,1.22,full+.02),0),(slope,0,0))
            add(stage,'Collision',kit.translated(bp.u_rotated(g,(0,yaw,0)),(mid[0],mid[1]+.61,mid[2])),chunk)
    def stairs(stage,a,b,width,steps,collision=True,chunk=None):
        dx,dz=b[0]-a[0],b[2]-a[2];run=math.hypot(dx,dz);rise=b[1]-a[1];yaw=math.degrees(math.atan2(dx,dz))
        sr=rise/steps;sd=run/steps
        role='Concrete' if stage==1 else 'WornSteel'
        for n in range(steps):
            g=bp.u_box((0,(n+1)*sr-.07,(n+.5)*sd),(width,.14,sd+.01),.006)
            add(stage,role,transform(g,a,(0,yaw,0)),chunk)
            if stage>1 and n%3==0:
                g=bp.u_box((0,(n+1)*sr+.006,(n+.16)*sd),(width-.20,.012,.035),.002)
                add(stage,'Steel',transform(g,a,(0,yaw,0)),chunk)
        slope=-math.degrees(math.atan2(rise,run));full=math.hypot(rise,run)
        for side in (-1,1):
            offset=(math.cos(math.radians(yaw))*side*width/2,0,-math.sin(math.radians(yaw))*side*width/2)
            aa=tuple(a[i]+offset[i] for i in range(3));bb=tuple(b[i]+offset[i] for i in range(3))
            # A perpendicular landing enters through one side of the ramp foot.
            # Omit that side's rail ONLY while a real adjacent landing still supports
            # its outside; the opposite exposed edge retains its complete guard.
            guard_start=aa
            if collision:
                outward=(offset[0]/(width/2),0,offset[2]/(width/2))
                for n in range(math.ceil(run/.05)+1):
                    d=min(run,n*.05);t=d/run
                    q=(aa[0]+dx*t+outward[0]*.08,a[1]+rise*t,aa[2]+dz*t+outward[2]*.08)
                    adjacent=any(not s['ramp'] and s['bounds'][0]-.001<=q[0]<=s['bounds'][2]+.001 and
                                 s['bounds'][1]-.001<=q[2]<=s['bounds'][3]+.001 and
                                 -.05<=q[1]-s['start'][1]<=.8 for s in all_supports)
                    if not adjacent:
                        guard_t=max(0,d-.10)/run
                        guard_start=tuple(aa[i]+(bb[i]-aa[i])*guard_t for i in range(3))
                        break
            rail(stage,guard_start,bb,collision,chunk,stage==1)
            g=bp.u_rotated(bp.u_box((side*(width/2-.12),0,0),(.18,.26,full+.05),.006),(slope,0,0))
            add(stage,'Steel',transform(kit.translated(g,(0,rise/2-.19,run/2)),a,(0,yaw,0)),chunk)
        if collision:
            # Top of the ramp follows step noses. Vertical thickness is .12 m.
            g=bp.u_rotated(bp.u_box((0,0,0),(width,.12,full),0),(slope,0,0))
            add(stage,'Collision',transform(kit.translated(g,(0,rise/2-.055,run/2)),a,(0,yaw,0)),chunk)
    # Floors and three actual stair rises.
    for index,r in enumerate(all_supports):
        stage=r['stage'];a,b=r['start'],r['end'];x0,z0,x1,z1=r['bounds']
        if r['ramp']:stairs(stage,a,b,WIDTH,36);continue
        y=a[1];box(stage,'Concrete' if stage==1 else 'WornSteel',((x0+x1)/2,y-.12,(z0+z1)/2),(x1-x0,.24,z1-z0),.008)
        box(stage,'Collision',((x0+x1)/2,y-.12,(z0+z1)/2),(x1-x0,.24,z1-z0),0)
        # Metric seams, restrained anti-slip strips and local nonrepeating wear.
        along=0 if x1-x0>z1-z0 else 2;run=max(x1-x0,z1-z0)
        for n in range(1,math.ceil(run/2.4)):
            p=[(x0+x1)/2,y+.008,(z0+z1)/2];p[along]=(x0 if along==0 else z0)+n*2.4
            box(stage,'ConcreteDark' if stage==1 else 'Steel',p,(.022,.012,WIDTH-.04) if along==0 else (WIDTH-.04,.012,.022),.001)
        if index%2==0:
            for n in range(3):
                p=[(x0+x1)/2,y+.018,(z0+z1)/2];p[along]+=(n-1)*1.05
                p[2 if along==0 else 0]+=.7 if n%2 else -.75
                g=bp.u_rotated(bp.u_box((0,0,0),(.35+.14*n,.008,.68+.1*index%1),.002),(0,index*19+n*31,0))
                add(stage,'Wear',kit.translated(g,p))
    # Exact exposed perimeter of each flat storey. Shared edges/junction mouths stay open.
    for y in sorted(set(r['start'][1] for r in all_supports if not r['ramp'])):
        flats=[r for r in all_supports if not r['ramp'] and abs(r['start'][1]-y)<.01]
        xs=sorted(set(v for r in flats for v in (r['bounds'][0],r['bounds'][2])))
        zs=sorted(set(v for r in flats for v in (r['bounds'][1],r['bounds'][3])))
        def owner(x,z):
            return next((r for r in flats if r['bounds'][0]-.001<x<r['bounds'][2]+.001 and r['bounds'][1]-.001<z<r['bounds'][3]+.001),None)
        edges=[]
        for x0,x1 in zip(xs,xs[1:]):
            for z0,z1 in zip(zs,zs[1:]):
                r=owner((x0+x1)/2,(z0+z1)/2)
                if r is None:continue
                candidates=[((x0,z0),(x1,z0),(0,-.03)),((x0,z1),(x1,z1),(0,.03)),((x0,z0),(x0,z1),(-.03,0)),((x1,z0),(x1,z1),(.03,0))]
                for aa,bb,normal in candidates:
                    mx=(aa[0]+bb[0])/2;mz=(aa[1]+bb[1])/2
                    if owner(mx+normal[0],mz+normal[1]) is not None:continue
                    p=(mx+normal[0],y,mz+normal[1])
                    # A same-height ramp connection, and the old stairwell mouth, have no guard.
                    if y==4.8 and abs(mz-2.25)<.02:continue
                    if any(s['ramp'] and s['bounds'][0]-.001<=p[0]<=s['bounds'][2]+.001 and s['bounds'][1]-.001<=p[2]<=s['bounds'][3]+.001 and abs(floor_at(s,p)-y)<.8 for s in all_supports):continue
                    edges.append((r['stage'],aa,bb))
        # Merge neighbouring collinear pieces, so intersections do not multiply posts/draw data.
        merged={}
        for stage,a,b in edges:
            horizontal=abs(a[1]-b[1])<.001;fixed=a[1] if horizontal else a[0]
            merged.setdefault((stage,horizontal,fixed),[]).append((a[0] if horizontal else a[1],b[0] if horizontal else b[1]))
        for (stage,horizontal,fixed),spans in merged.items():
            output=[]
            for lo,hi in sorted(spans):
                if output and lo<=output[-1][1]+.001:output[-1][1]=max(output[-1][1],hi)
                else:output.append([lo,hi])
            for lo,hi in output:
                a=(lo,y,fixed) if horizontal else (fixed,y,lo);b=(hi,y,fixed) if horizontal else (fixed,y,hi)
                rail(stage,a,b,True,wood=stage==1)
    # Source-shaped pipe/fixture poles and substantial bridge supports.
    for i,r in enumerate(all_supports):
        if r['ramp'] or i%2:continue
        a,b=r['start'],r['end'];stage=r['stage'];x0,z0,x1,z1=r['bounds']
        x=(x0+x1)/2;z=(z0+z1)/2;y=a[1]
        horizontal=(x1-x0)>(z1-z0)
        px=x if horizontal else x0-.32;pz=z0-.32 if horizontal else z
        box(stage,'Concrete' if stage==1 else 'Steel',(px,y-2.3,pz),(.44,4.4,.44),.018)
        cylinder(stage,'Rust',(px,y+1.35,pz),(.18,1.5,.18))
        for h in (.4,2.4):box(stage,'Steel',(px,y+h,pz),(.27,.07,.27),.005)
        # Objects sit beyond the clear lane in small authored shelf pockets.
        if i%4==0:
            side=-1 if i%8 else 1
            px=x+(0 if horizontal else side*1.25);pz=z+(side*1.25 if horizontal else 0)
            if stage==1:
                box(stage,'Wood',(px,y+.36,pz),(.48,.72,.38),.02)
                box(stage,'DoorPaint',(px,y+.77,pz),(.52,.10,.43),.012)
                for n in range(3):box(stage,'Steel',(px-.12+n*.12,y+.85,pz),(.05,.08,.05),.002)
            else:
                box(stage,'Timber',(px,y+.11,pz),(.58,.18,.65),.005)
                box(stage,'Cardboard',(px,y+.40,pz),(.49,.40,.49),.012)
                box(stage,'Rubber',(px+.12,y+.63,pz),(.24,.05,.37),.005)
    # Dead-end walls are visible reasons to turn; no interactive/lore doors.
    for bi,(name,stage,loop,points) in enumerate(BRANCHES):
        if loop:continue
        a,b=points[-2:];yaw=math.degrees(math.atan2(b[0]-a[0],b[2]-a[2]));origin=(b[0],b[1],b[2])
        for x in (-1.27,1.27):add(stage,'Plaster' if stage==1 else 'Concrete',transform(bp.u_box((x,1.6,1.42),(.54,3.2,.38),.02),origin,(0,yaw,0)))
        add(stage,'Concrete',transform(bp.u_box((0,3.1,1.42),(3.2,.4,.38),.02),origin,(0,yaw,0)))
        add(stage,'DoorPaint' if stage==1 else 'Rust',transform(bp.u_box((0,1.25,1.46),(1.9,2.5,.16),.012),origin,(0,yaw,0)))
        add(stage,'Collision',transform(bp.u_box((0,1.55,1.45),(3.2,3.1,.35),0),origin,(0,yaw,0)))
        for h in (.65,1.85):add(stage,'Steel',transform(bp.u_box((0,h,1.31),(2.15,.11,.10),.003),origin,(0,yaw,0)))
    # Familiar fragments frame the entry but do not hide the growing view.
    for center,size in [((-4.05,7.4,7),(1.0,4.8,7.5)),((1.2,8.8,10.2),(.4,4.5,3.5)),((-15.05,10.2,20),(.45,3.6,8))]:
        box(1,'Plaster',center,size,.025)
        box(1,'ConcreteDark',(center[0],center[1]-size[1]/2+.4,center[2]),(size[0]+.03,.8,size[2]+.02),.005)
    # Closed union of the three staggered atria. Every interior view ends on a
    # real wall, floor or ceiling; the formerly broken old stairwell is inside too.
    shell_x=sorted(set(v for room in INTERIOR_VOLUMES for v in (room[0],room[2])))
    shell_z=sorted(set(v for room in INTERIOR_VOLUMES for v in (room[1],room[3])))
    def column(x,z):
        members=[(i,room) for i,room in enumerate(INTERIOR_VOLUMES) if room[0]-.001<=x<=room[2]+.001 and room[1]-.001<=z<=room[3]+.001]
        if not members:return None
        return min(room[4] for _,room in members),max(room[5] for _,room in members),max(i for i,_ in members)+1
    def shell_box(stage,role,c,size):
        box(stage,role,c,size,.012)
        box(stage,'ShellCollision',c,size,0)
    for x0,x1 in zip(shell_x,shell_x[1:]):
        for z0,z1 in zip(shell_z,shell_z[1:]):
            x,z=(x0+x1)/2,(z0+z1)/2;own=column(x,z)
            if own is None:continue
            floor,ceiling,stage=own
            shell_box(stage,'Concrete',(x,floor-.22,z),(x1-x0,.44,z1-z0))
            shell_box(stage,'Plaster' if stage==1 else 'Concrete',(x,ceiling+.22,z),(x1-x0,.44,z1-z0))
            # Visible ceiling ribs break up the room scale and connect hung pieces.
            for rx in range(math.ceil(x0/5)*5,math.ceil(x1),5):
                if x0+.3<rx<x1-.3:box(stage,'Concrete' if stage==1 else 'Steel',(rx,ceiling-.22,z),(.24,.44,z1-z0),.01)
            for axis,fixed,lo,hi,out in [(0,x0,z0,z1,-.05),(0,x1,z0,z1,.05),(2,z0,x0,x1,-.05),(2,z1,x0,x1,.05)]:
                adjacent=column(fixed+out,z) if axis==0 else column(x,fixed+out)
                intervals=[(floor,ceiling)] if adjacent is None else [(floor,min(ceiling,adjacent[0])),(max(floor,adjacent[1]),ceiling)]
                for low,high in intervals:
                    if high-low<.01:continue
                    center=(fixed,(low+high)/2,(lo+hi)/2) if axis==0 else ((lo+hi)/2,(low+high)/2,fixed)
                    size=(.5,high-low,hi-lo) if axis==0 else (hi-lo,high-low,.5)
                    shell_box(stage,'Plaster' if stage==1 else 'Concrete',center,size)
                    # Exposed panels, repair bands and conduits belong to this wall.
                    if high-low>3:
                        for height in range(math.ceil((low+2)/4)*4,math.floor(high),4):
                            band_center=(fixed,height,(lo+hi)/2) if axis==0 else ((lo+hi)/2,height,fixed)
                            band_size=(.54,.07,hi-lo) if axis==0 else (hi-lo,.07,.54)
                            box(stage,'ConcreteDark' if stage==1 else 'Steel',band_center,band_size,.003)
    # Independently varied small stair runs, landing fragments, doors and bridges.
    # There is no facade/room module: each run sits in the common interior and
    # meets two small landings; separate cross-walks and roof beams stitch the field.
    piece_index=0;landing_anchors=[]
    def local_rail(parts,a,b,role='Steel'):
        dx,dz=b[0]-a[0],b[2]-a[2];rise=b[1]-a[1];run=math.hypot(dx,dz);length=math.hypot(run,rise)
        yaw=math.degrees(math.atan2(dx,dz));slope=-math.degrees(math.atan2(rise,run))
        for h in (.53,1.06):
            g=bp.u_rotated(bp.u_box((0,0,0),(.045,.055,length),.003),(slope,0,0));g=bp.u_rotated(g,(0,yaw,0))
            parts.append((role,kit.translated(g,((a[0]+b[0])/2,(a[1]+b[1])/2+h,(a[2]+b[2])/2))))
        count=max(2,math.ceil(run/.95)+1)
        for n in range(count):
            t=n/(count-1);parts.append((role,bp.u_box((a[0]+dx*t,a[1]+rise*t+.52,a[2]+dz*t),(.045,1.04,.045),.003)))
    def place_small(stage,origin,rotation,parts,source_floor,anchored=False):
        # Keep the camera/head tube clear on every storey, including overpasses.
        transformed=[(role,transform(g,origin,rotation)) for role,g in parts]
        all_geometry=kit.merge_all(g for _,g in transformed);lo,hi=kit.bounds(all_geometry)
        room=next((r for r in reversed(INTERIOR_VOLUMES) if r[0]<=origin[0]<=r[2] and r[1]<=origin[2]<=r[3]),INTERIOR_VOLUMES[0])
        ox=max(0,room[0]+1.2-lo[0])-max(0,hi[0]-(room[2]-1.2))
        oz=max(0,room[1]+1.2-lo[2])-max(0,hi[2]-(room[3]-1.2))
        offset=[ox,0,oz];lo=tuple(lo[i]+offset[i] for i in range(3));hi=tuple(hi[i]+offset[i] for i in range(3))
        for surface in all_supports:
            r=surface['bounds']
            if hi[0]<r[0]-.8 or lo[0]>r[2]+.8 or hi[2]<r[1]-.8 or lo[2]>r[3]+.8:continue
            top=max(surface['start'][1],surface['end'][1])
            if hi[1]>top-.25 and lo[1]<top+3.3:
                lift=top+3.3-lo[1];lo=(lo[0],lo[1]+lift,lo[2]);hi=(hi[0],hi[1]+lift,hi[2]);offset[1]+=lift
        if anchored and any(abs(value)>.001 for value in offset):
            return None
        if hi[1]>room[5]-.6:
            if anchored:return None
            raise ValueError('Small interior piece exceeds ceiling '+str((origin,lo,hi,room)))
        for role,g in transformed:
            placed_geometry=kit.translated(g,offset)
            add(stage,role,placed_geometry)
            # The free third-person camera must collide with these nearby pieces too.
            add(stage,'DecorCollision',placed_geometry)
        # A visible structural tie reaches the actual common ceiling, not empty air.
        anchor=((lo[0]+hi[0])/2,hi[1],(lo[2]+hi[2])/2)
        ceiling=column(anchor[0],anchor[2])[1]
        if ceiling-anchor[1]>.1:
            box(stage,'Concrete' if stage==1 and piece_index%3==0 else 'Steel',(anchor[0],(anchor[1]+ceiling)/2,anchor[2]),(.18,ceiling-anchor[1],.18),.008)
        return tuple(origin[i]+offset[i] for i in range(3))
    for si,surface in enumerate(all_supports):
        a,b=surface['start'],surface['end'];stage=surface['stage'];length=math.dist(a,b)
        copies=2 if length>12 else 1
        for copy in range(copies):
            t=(copy+1)/(copies+1);center=tuple(a[i]+(b[i]-a[i])*t for i in range(3))
            idx=piece_index;piece_index+=1
            roll=(0,90,180,0,0,180,90)[idx%7]
            yaw=(idx*90+(15 if idx%5==0 else 0))%360
            side=-1 if idx%2 else 1
            along_x=abs(b[0]-a[0])>abs(b[2]-a[2])
            x=center[0]+(0 if along_x else side*(3.4+idx%3*.5))
            z=center[2]+(side*(3.4+idx%3*.5) if along_x else 0)
            y=center[1]+(5.2 if roll==0 else 9.8 if roll==180 else 7.2)+(idx%3)*.35
            steps=8+(idx%5)*2;depth=.25+(idx%3)*.035;rise=.16+(idx%3)*.025
            run=steps*depth;climb=steps*rise;width=1.35+(idx%4)*.22;parts=[]
            material='Concrete' if stage==1 or idx%4==0 else 'WornSteel'
            for step in range(steps):parts.append((material,bp.u_box((0,(step+1)*rise-.065,(step+.5)*depth),(width,.13,depth+.01),.005)))
            for sy,sz in ((0,-.65),(climb,run+.65)):
                parts.append((material,bp.u_box((0,sy-.12,sz),(width+.65,.24,1.3),.008)))
                for sign in (-1,1):local_rail(parts,(sign*(width+.65)/2,sy,sz-.64),(sign*(width+.65)/2,sy,sz+.64),'Wood' if stage==1 else 'Steel')
            for sign in (-1,1):
                local_rail(parts,(sign*width/2,0,0),(sign*width/2,climb,run),'Wood' if stage==1 else 'Steel')
                slope=-math.degrees(math.atan2(climb,run))
                g=bp.u_rotated(bp.u_box((sign*(width/2-.10),0,0),(.16,.22,math.hypot(run,climb)),.004),(slope,0,0))
                parts.append(('Steel',kit.translated(g,(0,climb/2-.19,run/2))))
            # Only some landings meet a plain rectangular utility doorway.
            # Others open onto independently placed transverse bridges or room fragments.
            if idx%3==0:
                for sign in (-1,1):parts.append(('Plaster' if stage==1 else 'Concrete',bp.u_box((sign*.86,climb+1.35,run+1.22),(.26,2.7,.32),.015)))
                parts.append(('Concrete',bp.u_box((0,climb+2.72,run+1.22),(1.98,.28,.32),.015)))
                if idx%2==0:parts.append(('DoorPaint',bp.u_box((0,climb+1.24,run+1.26),(1.42,2.48,.12),.008)))
            elif idx%3==1:
                parts.append(('Plaster' if stage==1 else 'Concrete',bp.u_box((-(width+.65)/2,climb+1.1,run+.65),(.18,2.2,1.6),.012)))
            placed=place_small(stage,(x,y,z),(0,yaw,roll),parts,center[1])
            if idx%3==2:
                # A separate small balcony extends crosswise from this run's landing.
                portal=rotated_point((0,climb,run+.65),(0,yaw,roll));anchor=tuple(placed[i]+portal[i] for i in range(3))
                landing_anchors.append((stage,anchor,center[1],idx))
    bridge_count=0
    for stage,anchor,floor,idx in landing_anchors:
        # Off-axis landings/short bridges have different widths, spans and one-sided walls.
        span=3.2+(idx%4)*1.1;parts=[('Concrete' if stage==1 else 'WornSteel',bp.u_box((span/2,-.13,0),(span,.26,1.5),.008))]
        local_rail(parts,(0,0,-.75),(span,0,-.75));local_rail(parts,(0,0,.75),(span,0,.75))
        parts.append(('Concrete' if stage==1 else 'Steel',bp.u_box((span,-.6,0),(.24,1.2,1.5),.008)))
        # Shared start anchor gives the bridge a real visible connection to the preceding landing.
        if place_small(stage,anchor,(0,(idx%4)*90,0),parts,floor,anchored=True) is not None:bridge_count+=1
    # Wall utility fragments vary independently of the stair webs. Blank rectangular
    # recesses/frames, pipe runs and horizontal beams retain the project's own grammar.
    for stage,room in enumerate(INTERIOR_VOLUMES,1):
        x0,z0,x1,z1,floor,ceiling=room
        for n in range(8):
            on_x=n%2==0;y=7.5+(stage-1)*4+(n%3)*3.3
            x=(x0+.45 if n%4==0 else x1-.45) if on_x else x0+5+(n/8)*(x1-x0-10)
            z=z0+5+(n/8)*(z1-z0-10) if on_x else (z0+.45 if n%4==1 else z1-.45)
            rotation=(0,90 if x<x0+1 else -90,0) if on_x else (0,0 if z<z0+1 else 180,0)
            # Only true outer walls get mounted details; shared atrium openings remain open.
            across=column(x+(-.8 if x<x0+1 else .8),z) if on_x else column(x,z+(-.8 if z<z0+1 else .8))
            if across is not None:continue
            parts=[]
            for side in (-1,1):parts.append(('Concrete',bp.u_box((side*.85,1.25,0),(.20,2.5,.26),.012)))
            parts.append(('Concrete',bp.u_box((0,2.5,0),(1.9,.22,.26),.012)))
            parts.append(('DoorPaint',bp.u_box((0,1.18,-.08),(1.48,2.36,.10),.006)))
            for offset in (1.4,1.7):parts.append(('Rust',bp.u_cylinder((offset,1.7,.10),(.09,2.0,.09),8)))
            for role,g in parts:add(stage,role,transform(g,(x,y,z),rotation))
    # Physical final door surround; shared runtime door and hand contact remain separate.
    for x in (DOOR[0]-1.25,DOOR[0]+1.25):
        box(3,'Concrete',(x,DOOR[1]+1.7,DOOR[2]),(.65,3.4,.42),.02)
        box(3,'Collision',(x,DOOR[1]+1.7,DOOR[2]),(.65,3.4,.42),0)
    box(3,'Concrete',(DOOR[0],DOOR[1]+3.25,DOOR[2]),(3.2,.45,.42),.015)
    box(3,'Collision',(DOOR[0],DOOR[1]+3.25,DOOR[2]),(3.2,.45,.42),0)
    for i,(c,stage,shadow) in enumerate(LIGHTS):
        # Near lamps and six wall/ceiling sources have matching interior fixture anchors.
        rotation=LIGHT_ROTATIONS[i]
        def lb(role,offset,size,bevel=.003):
            add(stage,role,transform(bp.u_box(offset,size,bevel),c,rotation))
        lb('Steel',(0,.08,0),(1.04,.16,.30),.012)
        lb('Lens',(0,0,0),(.82,.035,.18))
        for z in (-.15,.15):lb('Steel',(0,-.04,z),(.98,.035,.025),.002)
        for x in (-.46,0,.46):lb('Steel',(x,-.04,0),(.025,.035,.30),.002)
        lb('Steel',(.67,-.3,0),(.055,.8,.055))
        lb('Steel',(.34,.08,0),(.7,.045,.045))
        if i>=SCENERY_LIGHT_START:
            # Attach ceiling cages to the common roof; wall cages already meet their wall.
            if LIGHT_ROTATIONS[i]==(0,0,0):lb('Steel',(.67,1.5,0),(.055,3.0,.055))
    design.last_counts=dict(short_stair_runs=piece_index,small_landings=piece_index*2,cross_bridges=bridge_count)
    return groups


def validate(groups):
    triangles=sum(kit.triangle_count(g) for parts in groups.values() for g in parts)
    if len(groups)>360:raise ValueError('Labyrinth spatial mesh budget '+str(len(groups)))
    if triangles>260000:raise ValueError('Labyrinth triangle budget '+str(triangles))
    for parts in groups.values():
        for g in parts:
            if bp.signed_volume(bp.to_source(g))<=0:raise ValueError('Invalid swapped winding')
    rows=supports();ramps=[r for r in rows if r['ramp']]
    if len(ramps)!=3 or [r['start'][1] for r in ramps]!=[4.8,8.4,12]:raise ValueError('Three primary rises required')
    if [sum(b[1]==s for b in BRANCHES) for s in (1,2,3)]!=[2,3,4]:raise ValueError('Branch progression')
    for _,_,loop,points in BRANCHES:
        if points[0] not in MAIN or (loop and points[-1] not in MAIN):raise ValueError('Branch disconnected from route')
    return dict(meshes=len(groups),triangles=triangles,flights=3,steps_per_flight=36,clear_width=WIDTH,total_rise=10.8,
                main_route_metres=round(sum(math.dist(a,b) for a,b in zip(MAIN,MAIN[1:])),3),supports=len(rows),branches=9,loops=4,
                lights=len(LIGHTS),interior_volumes=len(INTERIOR_VOLUMES),**design.last_counts,deterministic=True)


def validate_collision_clearance(groups):
    """Use exported collision triangles, not the support rectangles, for capsule-side clearance."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    collision=kit.merge_all(g for (_,_,role),parts in groups.items() if role in ('Collision','ShellCollision','DecorCollision') for g in parts)
    tree=BVHTree.FromPolygons(collision[0],collision[1],all_triangles=False)
    paths=[('main',MAIN[:-1]+[(DOOR[0]-.715724,DOOR[1],DOOR[2]-.719976)])]+[(name,points) for name,_,_,points in BRANCHES]
    for name,path in paths:
        for a,b in zip(path,path[1:]):
            count=max(1,math.ceil(math.dist(a,b)/.15))
            for n in range(count+1):
                p=tuple(a[i]+(b[i]-a[i])*n/count for i in range(3))
                for height in (.40,1.25,1.70):
                    start=Vector((p[0],p[1]+height,p[2]))
                    for ray in range(16):
                        angle=ray*math.tau/16;direction=Vector((math.cos(angle),0,math.sin(angle)))
                        hit,_,_,distance=tree.ray_cast(start,direction,.34)
                        if hit is not None:
                            raise ValueError('Route capsule hits authored collision: '+str((name,p,height,tuple(hit),distance)))
    print('STAIRWELL_LABYRINTH_COLLISION_CLEARANCE_OK',flush=True)


def validate_enclosure(groups):
    """Only the continuous shell may satisfy this proof; nearby props cannot hide an exterior hole."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    shell=kit.merge_all(g for (_,_,role),parts in groups.items() if role=='ShellCollision' for g in parts)
    tree=BVHTree.FromPolygons(shell[0],shell[1],all_triangles=False)
    directions=[(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)]
    directions += [(math.cos((i+.5)*math.tau/8),0,math.sin((i+.5)*math.tau/8)) for i in range(8)]
    points=list(MAIN)+[p for _,_,_,path in BRANCHES for p in path]+[(0,3.2,-2),(-1.45,4.8,1.5)]
    for p in points:
        origin=Vector((p[0],p[1]+1.5,p[2]))
        for direction in directions:
            hit,_,_,distance=tree.ray_cast(origin,Vector(direction),150)
            if hit is None:raise ValueError('Interior opens into exterior void: '+str((p,direction)))
    print('STAIRWELL_LABYRINTH_ENCLOSURE_OK',flush=True)


def main():
    import bpy
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser();parser.add_argument('--validate-only',action='store_true')
    parser.add_argument('--model-dir',type=Path,default=MODEL_DIR);parser.add_argument('--source-dir',type=Path,default=SOURCE_DIR)
    parser.add_argument('--code-dir',type=Path,default=CODE_DIR)
    args=parser.parse_args(argv);groups=design();report=validate(groups);validate_collision_clearance(groups);validate_enclosure(groups)
    canonical=lambda g:json.dumps([(k,v) for k,v in g.items()],separators=(',',':'))
    signature=hashlib.sha256(canonical(groups).encode()).hexdigest()
    if canonical(groups)!=canonical(design()):raise ValueError('Nondeterministic labyrinth')
    print('STAIRWELL_LABYRINTH_DETERMINISM_OK '+json.dumps(report),flush=True)
    if args.validate_only:return
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    root=bpy.data.objects.new('Labyrinth3D',None);bpy.context.collection.objects.link(root);records=[]
    for (stage,chunk,role),parts in groups.items():
        geometry=kit.merge_all(parts);lo,hi=kit.bounds(geometry);v,f=bp.to_source(geometry)
        name=f'Stage{stage}__{chunk}__{role}'
        mesh=bpy.data.meshes.new(name);mesh.from_pydata(v,[],f);mesh.update();uv=mesh.uv_layers.new(name='UVMap')
        for poly in mesh.polygons:
            normal=(poly.normal.x,poly.normal.z,poly.normal.y);major=max(range(3),key=lambda i:abs(normal[i]));a,b=((2,1),(0,2),(0,1))[major]
            for loop in poly.loop_indices:
                p=geometry[0][mesh.loops[loop].vertex_index];uv.data[loop].uv=(p[a],p[b])
        obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.parent=root
        records.append(dict(name=name,stage=stage,chunk=chunk,role=role,bounds_min=lo,bounds_max=hi,triangles=kit.triangle_count(geometry)))
    payload=dict(schema_version=2,generator_version='3.0.0',signature=signature,contract_signature=contract_signature(),report=report,parts=records,contract=contract())
    args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
    manifest=json.dumps(payload,indent=2)+'\n';(args.model_dir/'Labyrinth3D.json').write_text(manifest);(args.source_dir/'labyrinth-3d-model.json').write_text(manifest)
    write_runtime(args.code_dir)
    bpy.ops.object.select_all(action='SELECT');bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir/'Labyrinth3D.fbx'),use_selection=True,object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',bake_space_transform=False,add_leaf_bones=False,bake_anim=False,mesh_smooth_type='FACE',path_mode='STRIP')
    print('STAIRWELL_LABYRINTH_EXPORTED',flush=True)

if __name__=='__main__':main()
