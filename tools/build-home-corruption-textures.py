#!/usr/bin/env python3
"""Unique, whole-surface Home/Stairwell wear masks; original micro albedos repeat.

Two bounded shared atlas pages, linear RGBA: soil, substrate, accent, roughness.
Every object owns a seeded chart; continuous shell pieces share room-plane charts.
No large damage is baked into a repeating material tile. --validate-only compares
all authored bytes, coverage, unique charts, packing and source-family coverage.
"""
from __future__ import annotations
import argparse
import hashlib
import io
import json
from pathlib import Path
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
HOME = ROOT / 'ArtSource/Home/Interior/home-interior-3d-model.json'
STAIR = ROOT / 'ArtSource/Stairwell/Interior/stairwell-interior-3d-model.json'
OUTPUT = ROOT / 'Assets/Resources/SurfaceDamage'
MANIFEST = ROOT / 'ArtSource/Home/home-corruption-textures.json'
RETIRED = ROOT / 'Assets/Resources/Home/Textures/Corrupted'
SIZE, PAGES, GUTTER = 2048, 2, 4
VERSION = 'unique_surface_damage_v2'
HOME_FAMILIES = ('Wallpaper', 'CeilingPlaster', 'PlankFloor', 'DarkWood', 'WornLaminate',
    'Upholstery', 'BedLinen', 'BathroomTile', 'Enamel', 'PaintedMetal', 'Concrete', 'Rug',
    'RoomWallpaper', 'RoomPlanks', 'RoomWood', 'RoomLaminate', 'RoomUpholstery')
PROFILES = {
    'Wallpaper':'wallpaper', 'RoomWallpaper':'wallpaper', 'CeilingPlaster':'ceiling',
    'PlankFloor':'floor', 'RoomPlanks':'floor', 'DarkWood':'wood', 'RoomWood':'wood',
    'WornLaminate':'laminate', 'RoomLaminate':'laminate', 'Upholstery':'upholstery',
    'RoomUpholstery':'upholstery', 'BedLinen':'linen', 'BathroomTile':'tile',
    'Enamel':'enamel', 'PaintedMetal':'metal', 'Concrete':'concrete', 'Rug':'rug',
    'WallPaint':'wallpaint', 'StairConcrete':'stairs', 'CorrodedMetal':'metal',
    'DoorPaint':'painteddoor', 'Damage':'wallpaint', 'DirtyWood':'wood', 'Debris':'linen',
}
# sRGB substrate/accent targets; soil is a linear multiplier of the source.
PALETTES = {
 'wallpaper':((.23,.27,.18),(.77,.69,.52),(.19,.18,.12),.70),
 'wallpaint':((.25,.28,.19),(.66,.61,.46),(.21,.19,.12),.66),
 'ceiling':((.27,.23,.15),(.73,.66,.51),(.34,.25,.12),.65),
 'floor':((.24,.21,.15),(.61,.48,.29),(.71,.61,.42),.67),
 'wood':((.31,.25,.17),(.65,.46,.23),(.24,.18,.09),.69),
 'laminate':((.29,.27,.20),(.62,.53,.35),(.25,.22,.13),.67),
 'upholstery':((.33,.31,.22),(.65,.59,.43),(.73,.66,.48),.63),
 'linen':((.38,.33,.23),(.69,.61,.42),(.73,.68,.51),.65),
 'rug':((.24,.22,.16),(.57,.48,.32),(.68,.58,.37),.61),
 'tile':((.24,.28,.20),(.73,.69,.51),(.28,.29,.19),.65),
 'enamel':((.37,.37,.27),(.30,.31,.27),(.65,.58,.39),.73),
 'metal':((.40,.24,.11),(.42,.43,.35),(.65,.42,.19),.71),
 'concrete':((.31,.30,.21),(.64,.59,.45),(.22,.23,.17),.63),
 'stairs':((.28,.26,.18),(.63,.58,.43),(.27,.25,.17),.63),
 'painteddoor':((.30,.28,.19),(.55,.42,.27),(.24,.21,.13),.66),
}
FACES = ('+X', '-X', '+Y', '-Y', '+Z', '-Z')


def stable(value):
    return int.from_bytes(hashlib.sha256(value.encode()).digest()[:8], 'little')


def smooth(a, b, x):
    t = np.clip((x-a)/(b-a), 0., 1.)
    return t*t*(3.-2.*t)


def field(rng, width, height, cells):
    # Nonperiodic multiscale field. Its seed belongs to a surface, never a tile.
    result = np.zeros((height,width),np.float32)
    for scale, weight in ((cells,1.), (cells*3,.38), (cells*9,.17)):
        aspect = width/max(height,1)
        gx=max(3,int(scale*aspect)); gy=max(3,scale)
        grid=rng.random((gy,gx), dtype=np.float32)
        layer=Image.fromarray(grid).resize((width,height),Image.Resampling.BICUBIC)
        result += np.asarray(layer)*weight
    lo,hi=float(result.min()),float(result.max())
    return (result-lo)/max(hi-lo,1e-6)


def strokes(rng, width, height, count, vertical=False, border=False, thickness=1):
    result=Image.new('L',(width,height),0); draw=ImageDraw.Draw(result)
    for _ in range(count):
        u=float(rng.uniform(.04,.96)); v=float(rng.uniform(.04,.96))
        if border:
            side=int(rng.integers(4))
            if side<2:u=.018 if side==0 else .982
            else:v=.018 if side==2 else .982
        pts=[]
        du,dv=(rng.uniform(-.012,.012),rng.uniform(.018,.065)) if vertical else (rng.uniform(.015,.06),rng.uniform(-.012,.012))
        for _ in range(int(rng.integers(4,12))):
            pts.append((int(u*width),int((1-v)*height)))
            u+=du+rng.uniform(-.015,.015);v+=dv+rng.uniform(-.015,.015)
        draw.line(pts, fill=int(rng.integers(105,230)), width=max(1,thickness))
    return np.asarray(result,dtype=np.float32)/255


def paint(profile, key, width, height, face=0):
    rng=np.random.default_rng(stable(key+':'+str(face)))
    yy,xx=np.mgrid[0:height,0:width]
    u=(xx+.5)/width; v=1-(yy+.5)/height
    fine=field(rng,width,height,11); coarse=field(rng,width,height,3)
    medium=field(rng,width,height,6)
    edge=np.minimum.reduce((u,1-u,v,1-v))
    perimeter=1-smooth(.015,.13,edge)
    bottom=np.exp(-v/(.14+rng.uniform(0,.11)))
    soil=np.zeros_like(u); expose=soil.copy(); accent=soil.copy()
    if profile in ('wallpaper','wallpaint'):
        rising=bottom*(.34+.8*coarse)
        soil=np.maximum(rising, smooth(.53,.86,coarse)*.50)
        # Irregular falling wet trails, each beginning at a distinct top joint.
        for _ in range(int(rng.integers(1,4))):
            center=rng.uniform(.03,.97)
            path=center+.018*np.sin(v*rng.uniform(6,17)+rng.uniform(0,6))
            trail=np.exp(-((u-path)/(rng.uniform(.018,.08)*(1-v+.25)))**2)
            soil=np.maximum(soil,trail*smooth(rng.uniform(.3,.65),1,v)*(.4+.5*medium))
        seams=np.zeros_like(u)
        for seam in rng.uniform(.05,.95,int(rng.integers(2,5))):
            seams=np.maximum(seams,np.exp(-((u-seam)/(.012+.026*fine))**2))
        decay=(rising*.55+seams*.35+coarse*.28+fine*.15)
        expose=smooth(.54,.66,decay) if profile=='wallpaper' else smooth(.55,.72,decay)*.8
        accent=strokes(rng,width,height,3,vertical=True)*(.3+.6*medium)
        soil=np.maximum(soil,smooth(.60,.80,fine)*rising)
    elif profile=='ceiling':
        # Few separate water sources; concentric ragged tide lines belong to
        # those sources, rather than a repeating set of smooth organic blobs.
        for _ in range(int(rng.integers(2,4))):
            cx,cy=rng.uniform(.05,.95,2); rx=rng.uniform(.12,.30);ry=rng.uniform(.10,.28)
            radius=np.sqrt(((u-cx)/rx)**2+((v-cy)/ry)**2)+(medium-.5)*.52+(fine-.5)*.14
            soil=np.maximum(soil,(1-smooth(.50,1.5,radius))*.93)
            accent=np.maximum(accent,(smooth(.87,.96,radius)-smooth(1.02,1.15,radius))*.88)
            expose=np.maximum(expose,(1-smooth(.65,.95,radius))*smooth(.57,.76,fine)*.62)
        accent=np.maximum(accent,strokes(rng,width,height,2)*.5)
    elif profile in ('floor','stairs'):
        soil=perimeter*(.35+.5*medium)
        path=np.zeros_like(u)
        for _ in range(2):
            center=rng.uniform(.20,.80)+.10*np.sin(v*rng.uniform(2,5)+rng.uniform(0,6))
            path=np.maximum(path,np.exp(-((u-center)/rng.uniform(.12,.22))**2))
        soil=np.maximum(soil,path*(.22+.40*coarse))
        expose=strokes(rng,width,height,max(4,width//17),vertical=True,thickness=1)*path*.65
        accent=smooth(.60,.80,fine)*perimeter*.65
        if profile=='stairs':expose=np.maximum(expose,np.exp(-((v-.08)/.028)**2)*(.35+.6*fine))
    elif profile in ('wood','painteddoor'):
        soil=bottom*(.30+.50*medium)+perimeter*.23
        expose=perimeter*smooth(.45,.72,medium)*.88
        handle=np.exp(-(((u-rng.uniform(.72,.90))/.14)**2+((v-rng.uniform(.35,.65))/.16)**2))
        expose=np.maximum(expose,handle*(.35+.60*fine))
        accent=strokes(rng,width,height,max(3,width//40),vertical=True)*smooth(.35,.7,coarse)*.6
    elif profile=='laminate':
        soil=perimeter*(.22+.35*coarse)+bottom*.20
        water=smooth(.50,.68,medium)*perimeter
        expose=smooth(.27,.63,water+bottom*fine*.31)*.92
        accent=strokes(rng,width,height,4,border=True)*(.2+.5*medium)
    elif profile=='upholstery':
        soil=perimeter*(.38+.4*medium)
        contact=np.exp(-(((u-rng.uniform(.30,.7))/.27)**2+((v-rng.uniform(.32,.7))/.24)**2))
        soil=np.maximum(soil,contact*(.30+.30*coarse))
        expose=contact*smooth(.38,.75,medium)*.66
        fibres=(rng.random((height,width))>.94)*smooth(.50,.75,fine)
        accent=np.maximum(fibres*contact,strokes(rng,width,height,5,border=True)*.6)
    elif profile=='linen':
        # Narrow capillary paths follow folds; worn fibres stay on cloth edges.
        soil=np.zeros_like(u)
        for _ in range(3):
            line=rng.uniform(.05,.95)+.04*np.sin(u*rng.uniform(4,11)+rng.uniform(0,6))
            soil=np.maximum(soil,np.exp(-((v-line)/rng.uniform(.025,.10))**2)*(.26+.5*medium))
        expose=perimeter*smooth(.34,.70,medium)*.55
        accent=strokes(rng,width,height,max(5,width//22),border=True)*(.35+.55*fine)
    elif profile=='rug':
        soil=perimeter*(.5+.35*medium)
        center=np.exp(-((u-.5)/.28)**4)*np.exp(-((v-.5)/.38)**4)
        expose=center*(.25+.50*fine)
        accent=strokes(rng,width,height,max(6,width//16),border=True)*perimeter
    elif profile=='tile':
        soil=perimeter*(.44+.45*coarse)+bottom*(.20+.3*medium)
        # Mineral scale follows a waterline rather than wallpaper peel islands.
        tide=rng.uniform(.12,.32)+.025*np.sin(u*rng.uniform(3,9))
        expose=np.exp(-((v-tide)/(.018+.015*fine))**2)*(.35+.6*fine)
        accent=strokes(rng,width,height,2,vertical=True)*perimeter*.65
    elif profile=='enamel':
        soil=bottom*.29+perimeter*(.20+.3*medium)
        expose=perimeter*smooth(.62,.79,fine)*.84
        tide=rng.uniform(.20,.50)+.015*np.sin(u*8)
        accent=np.exp(-((v-tide)/.02)**2)*(.18+.42*medium)
    elif profile=='metal':
        seam=perimeter*(.35+.6*medium)+bottom*.24
        soil=np.clip(seam,0,1)
        expose=strokes(rng,width,height,max(3,width//28),border=True)*(.4+.5*fine)
        accent=smooth(.62,.83,fine)*smooth(.25,.65,seam)*.88
    elif profile=='concrete':
        soil=bottom*(.35+.65*coarse)+perimeter*.16
        expose=perimeter*smooth(.52,.76,fine)*.7
        accent=strokes(rng,width,height,3,vertical=True)*(.2+.6*medium)
    else:raise ValueError('Missing material painter '+profile)
    masks=np.stack((np.clip(soil,0,1),np.clip(expose,0,1),np.clip(accent,0,1),
        np.clip(.28+soil*.44+expose*.26+fine*.14,0,1)),axis=2)
    return np.rint(masks*255).astype(np.uint8)


def family_rows():
    result=[]
    for domain,names in (('home',HOME_FAMILIES),('stairwell',('WallPaint','Concrete','StairConcrete',
            'CorrodedMetal','DoorPaint','Damage','DirtyWood','Debris'))):
        for family in names:
            painter=PROFILES[family]; soil,expose,accent,smoothness=PALETTES[painter]
            row=dict(domain=domain,family=family,painter=painter,
                soil_multiplier=list(soil),expose_color=list(expose),accent_color=list(accent),
                smoothness_scale=smoothness)
            if domain=='stairwell' and family in ('WallPaint','Damage'):
                row['micro_resource_override']='SurfaceDamage/Micro/Stairwell'+family+'Micro'
            result.append(row)
    return result


def object_projection(size, family):
    # Broad textiles, boards, dishes and tread tops get a single contiguous
    # physical face. Other furniture uses six independently painted box faces.
    profile=PROFILES[family]
    if profile in ('ceiling','floor','rug','stairs') or size[1]<min(size[0],size[2])*.25:
        return 'XZ'
    if size[2]<min(size[0],size[1])*.25:return 'XY'
    if size[0]<min(size[1],size[2])*.25:return 'ZY'
    if profile in ('wallpaper','wallpaint'):
        return 'ZY' if size[0]<size[2] else 'XY'
    return 'Box3x2'


def local_bounds(part):
    return list(part['bounds_min']),list(part['bounds_max'])


def chart_size(size, projection, shell=False):
    if projection=='Box3x2':
        maximum,middle,_=sorted(size,reverse=True)
        return (256,256) if maximum>=1.8 and middle>=.7 else (128,128) if maximum>=1.0 and middle>=.2 else (64,64)
    axes={'XY':(0,1),'ZY':(2,1),'XZ':(0,2)}[projection]
    a,b=[max(size[i],.02) for i in axes]
    scale=512 if shell and max(a,b)>=5 else 256 if shell or max(a,b)>=2.0 else 128 if max(a,b)>=.6 else 64
    if a>=b:return scale,max(32,min(scale,2**int(np.ceil(np.log2(scale*b/a)))))
    return max(32,min(scale,2**int(np.ceil(np.log2(scale*a/b))))),scale


def design():
    home=json.loads(HOME.read_text())['parts']; stair=json.loads(STAIR.read_text())['parts']
    charts={};entries={}
    def add(domain,key,family,part,group=None,projection=None,room_bounds=None,aliases=()):
        lo,hi=local_bounds(part);size=[b-a for a,b in zip(lo,hi)]
        projection=projection or object_projection(size,family)
        chart_id=group or domain+':'+key
        if room_bounds:
            lo,hi=map(list,room_bounds);size=[b-a for a,b in zip(lo,hi)]
        if chart_id not in charts:
            charts[chart_id]=dict(id=chart_id,domain=domain,family=family,painter=PROFILES[family],
                projection=projection,size=chart_size(size,projection,room_bounds is not None))
        row=dict(key=domain+':'+key,chart_id=chart_id,domain=domain,family=family,
            projection=projection,space='room' if room_bounds else 'local',bounds_min=lo,bounds_max=hi)
        entries[row['key']]=row
        for alias in aliases:
            aliasrow=dict(row);aliasrow['key']=domain+':'+alias;entries.setdefault(aliasrow['key'],aliasrow)
    shell={
        'north':('XY',([-7,0,3.88],[5,3.4,4.12])),
        'west':('ZY',([-7.12,0,-4],[-6.88,3.4,4])),
        'east':('ZY',([4.88,0,-4],[5.12,3.4,4])),
        'south':('XY',([-7,0,-4.12],[5,3.4,-3.88])),
        'bathwest':('ZY',([1.46,0,.65],[1.64,3.4,3.866])),
        'bathfront':('XY',([1.55,0,.56],[4.65,3.4,.74])),
        'lockedfront':('XY',([-2.8,0,.82],[1.46,3.4,1.0])),
    }
    for p in home:
        if p['sheet']=='Plain' or p['role'] not in ('binding','grid','decor'):continue
        name=p['name'];family=p['sheet'];group=None
        if name=='Home Back Wall':group='north'
        elif name=='Home Left Wall':group='west'
        elif name.startswith('Home Right Wall'):group='east'
        elif name.startswith(('Home Entry Wall','Home Entry Lintel','Home Entry Door Transom')):group='south'
        elif name=='Home Bathroom West Wall':group='bathwest'
        elif name.startswith(('Home Bathroom Front Wall','Home Bathroom Door Lintel')):group='bathfront'
        elif name.startswith(('Home Locked Room Front','Home Locked Room Lintel')):group='lockedfront'
        aliases=list(p.get('aliases',[]))
        if p.get('semantic_name') and p['semantic_name']!=name:aliases.append(p['semantic_name'])
        if name in ('Home Floor','Home Ceiling'):
            bounds=([-7,-.16,-4],[5,0,4]) if name=='Home Floor' else ([-7.24,3.36,-4.24],[5.24,3.52,4.24])
            add('home',name,family,p,projection='XZ',room_bounds=bounds,aliases=aliases)
        elif group:
            projection,bounds=shell[group]
            add('home',name,family,p,'home:shell:'+group,projection,bounds,aliases)
        else:add('home',name,family,p,aliases=aliases)
    # All17 recipes are represented even if a layout currently overrides a
    # nominal binding. Runtime may fall back by the explicit family chart.
    for family in HOME_FAMILIES:
        p=dict(bounds_min=[-.5,-.5,-.5],bounds_max=[.5,.5,.5])
        add('home','family:'+family,family,p)
    # Parameterized source profiles are shared geometry, never shared damage.
    # Enumerate their finite authored semantic aliases, with the current finite
    # builder loops expanded explicitly. Untextured glass/light aliases remain
    # dormant entries because their existing owner never requests surface wear.
    semantic=set(alias for p in home if p['name'].startswith('Profile.') for alias in p.get('aliases',[]))
    semantic.update('Home Refrigerator Shelf '+str(i)+suffix for i in range(1,4)
        for suffix in ('',' Front Rail'))
    semantic.update('Home Refrigerator Door Bin '+str(i)+suffix for i in range(4)
        for suffix in (' Bottom',' Front Rail',' Left Stop',' Right Stop'))
    semantic.update('Home Refrigerator Vent Slat '+str(i) for i in range(1,6))
    semantic.update('Home Refrigerator Foot '+str(a)+' '+str(b) for a in (-1,1) for b in (-1,1))
    for name in sorted(semantic):
        if 'home:'+name in entries or name.startswith('Player Home'):continue
        if 'Curtain' in name:family='BedLinen'
        elif 'Refrigerator' in name:family='Enamel'
        elif 'Sofa' in name:family='Upholstery'
        elif 'Soap' in name:family='Enamel'
        elif 'Nightstand' in name:family='RoomWood'
        else:family='PaintedMetal'
        unit=dict(bounds_min=[-.5,-.5,-.5],bounds_max=[.5,.5,.5])
        add('home',name,family,unit)
        entries['home:'+name]['use_renderer_bounds']=True
        # Small fixtures carry sparse localized damage at64px; this is macro
        # only. Their original full-resolution micro albedo is unchanged.
        charts['home:'+name]['size']=(64,64)
    # Final-view balcony barrier reuses the actual door/frame meshes. Its
    # opposing stiles/jambs each carry their own damage rather than a mirrored
    # copy of one repeated fixture chart.
    for suffix in ('frame-south','frame-north','header','hinge-stile','latch-stile',
            'bottom-rail','middle-rail','top-rail','lower-panel','handle'):
        name='barrier:balcony:'+suffix
        family='PaintedMetal' if suffix=='handle' else 'RoomWood'
        p=dict(bounds_min=[-.5,-.5,-.5],bounds_max=[.5,.5,.5])
        add('home',name,family,p,projection='Box3x2')
        entries['home:'+name]['use_renderer_bounds']=True
        charts['home:'+name]['size']=(64,64)
    stair_aliases={alias:p for p in stair for alias in p['aliases']}
    def stair_family(alias):
        if any(s in alias for s in ('Damp Wall','Peeling Paint')):return 'Damage'
        if 'Door Leaf' in alias:return 'DoorPaint'
        if any(s in alias for s in ('Wall','Dirty Wall Band','Column')):return 'WallPaint'
        if any(s in alias for s in ('Handrail ', 'Rail ')) and any(s in alias for s in ('Handrail',' Top')):return 'DirtyWood'
        if any(s in alias for s in ('Stair ', 'Landing','Flight')) and 'Rail' not in alias:return 'StairConcrete'
        if 'Wardrobe' in alias or 'Plank' in alias:return 'DirtyWood'
        if any(s in alias for s in ('Mattress','Paper','Sack','Bottle')):return 'Debris'
        if any(s in alias for s in ('Floor','Ceiling','Threshold')):return 'Concrete'
        return 'CorrodedMetal'
    room_planes={
      'back':('XY',([-4.3,0,4.66],[4.3,7.3,4.94])),
      'front':('XY',([-4.3,0,-4.94],[4.3,7.3,-4.66])),
      'left':('ZY',([-4.44,0,-4.8],[-4.16,7.3,4.8])),
      'right':('ZY',([4.16,0,-4.8],[4.44,7.3,4.8])),
    }
    for alias,p in stair_aliases.items():
        if alias in ('Black Void','Door Leaf','Handle Lever','Handle Spindle','Left Door Frame','Right Door Frame','Door Lintel','Door Threshold','Threshold Floor'):continue
        family=stair_family(alias);group=None
        for direction in room_planes:
            if alias in ('Stairwell '+direction.title()+' Wall',direction.title()+' Dirty Wall Band',
                'Stairwell Corrupted '+direction.title()+' Wall'):group=direction
        if group:
            projection,bounds=room_planes[group]
            add('stairwell',alias,family,p,'stairwell:shell:'+group,projection,bounds)
        else:add('stairwell',alias,family,p)
        if 'Ceiling' in alias:charts['stairwell:'+alias]['painter']='ceiling'
    # Merged repeats have unique masks per placed module, never a cyclic pool.
    for index in range(12):
        add('stairwell','Distant Flight '+str(index+1),'StairConcrete',stair_aliases['Corrupted Endless Open Flight'])
    # The new upper route reuses authored mesh bindings but owns different
    # visible objects. Their masks must not copy the lower grille/landing.
    upper_receivers=[('Corrupted Upper Temporary Grille',(1.78,1.84,.10))]
    upper_receivers.extend(('Corrupted Upper Temporary Grille Bar '+str(i),(.045,1.84,.045))
        for i in range(1,8))
    for side in ('Left','Right'):
        upper_receivers.append(('Corrupted Upper Landing '+side+' Rail',(1.2,1.16,.1)))
        upper_receivers.append(('Corrupted Upper Landing '+side+' Rail Top',(1.28,.1,.18)))
    for name,size in upper_receivers:
        p=dict(bounds_min=[-v*.5 for v in size],bounds_max=[v*.5 for v in size])
        add('stairwell',name,'CorrodedMetal',p,projection='Box3x2')
        entries['stairwell:'+name]['use_renderer_bounds']=True
        charts['stairwell:'+name]['size']=(128,128) if name=='Corrupted Upper Temporary Grille' else (64,64)
    # Runtime-created mirror fill panels share their source's canonical field.
    for alias,source in (('Home Bathroom Mirror West Fill','Home Bathroom West Wall'),
            ('Home Bathroom Mirror East Wall','Home Right Wall North Pier'),
            ('Home Bathroom Mirror Beyond Door','Home Bathroom Front Wall Left'),
            ('Home Bathroom Mirror Floor','Home Floor'),('Home Bathroom Mirror Ceiling','Home Ceiling')):
        row=dict(entries['home:'+source]);row['key']='home:'+alias
        # Only synthetic patch slabs need this explicit inverse reflection;
        # ordinary mirror clones inherit their source mapping unchanged.
        row['room_point_scale']=[1,1,-1];row['room_point_offset']=[0,0,7.732]
        entries[row['key']]=row
    return charts,list(entries.values())


def pack(charts):
    # Skyline placement, deterministic largest first. Tile sizes include their
    # gutters and divide2048; no rescaling can silently reduce wall resolution.
    skylines=[[0]*SIZE for _ in range(PAGES)]
    for chart in sorted(charts.values(),key=lambda c:(-max(c['size']),-min(c['size']),c['id'])):
        width,height=chart['size'];best=None
        for page,sky in enumerate(skylines):
            for x in range(0,SIZE-width+1,32):
                y=max(sky[x:x+width])
                candidate=(y+height,page,y,x)
                if y+height<=SIZE and (best is None or candidate<best):best=candidate
        if best is None:raise RuntimeError('Shared damage atlas budget exceeded; chart '+chart['id'])
        _,page,y,x=best
        skylines[page][x:x+width]=[y+height]*width
        chart.update(page=page,pixels=[x,y,width,height],rect=[(x+GUTTER+.5)/SIZE,
            1-(y+height-GUTTER-.5)/SIZE,(width-2*GUTTER-1)/SIZE,(height-2*GUTTER-1)/SIZE])
    return skylines


def render_chart(chart):
    width,height=chart['size'];width-=2*GUTTER;height-=2*GUTTER
    if chart['projection']!='Box3x2':
        return paint(chart['painter'],chart['id'],width,height)
    result=np.empty((height,width,4),np.uint8)
    # PNG rows are top-down; Unity chart coordinates and face table are bottom-up.
    for face in range(6):
        col,row=face%3,face//3
        x0,x1=round(col*width/3),round((col+1)*width/3)
        y0,y1=round((1-row)*height/2),round((2-row)*height/2)
        result[y0:y1,x0:x1]=paint(chart['painter'],chart['id'],x1-x0,y1-y0,face)
    return result


def validate_mapping(charts, entries):
    if len({r['key'] for r in entries})!=len(entries):raise RuntimeError('Duplicate surface key')
    occupied=[np.zeros((SIZE,SIZE),np.bool_) for _ in range(PAGES)]
    for c in charts.values():
        x,y,w,h=c['pixels'];page=c['page']
        if not (0<=page<PAGES and 0<=x<x+w<=SIZE and 0<=y<y+h<=SIZE):
            raise RuntimeError('Atlas chart outside fixed pages '+c['id'])
        rect=occupied[page][y:y+h,x:x+w]
        if rect.any():raise RuntimeError('Overlapping damage charts '+c['id'])
        rect[:]=True
    for row in entries:
        if row['chart_id'] not in charts:raise RuntimeError('Missing damage chart '+row['key'])
        axes={'XY':(0,1),'ZY':(2,1),'XZ':(0,2),'Box3x2':(0,1,2)}[row['projection']]
        if any(row['bounds_max'][a]-row['bounds_min'][a]<=1e-6 for a in axes):
            raise RuntimeError('Degenerate surface mapping '+row['key'])
    # These separate mesh runs must never restart/repeat the damage field.
    by_key={r['key']:r for r in entries}
    for keys in (('home:Home Entry Wall Left','home:Home Entry Wall Right','home:Home Entry Lintel'),
            ('home:Home Right Wall South Pier','home:Home Right Wall North Pier'),
            ('stairwell:Stairwell Left Wall','stairwell:Left Dirty Wall Band')):
        if len({by_key[k]['chart_id'] for k in keys})!=1:raise RuntimeError('Continuous wall damage restarted')
    return sum(int(page.sum()) for page in occupied)


def png(image):
    buf=io.BytesIO();image.save(buf,format='PNG',compress_level=9,optimize=False);return buf.getvalue()


def micro_overrides():
    outputs={};rows=[]
    for family,source in (('WallPaint','StairwellWallPaintAlbedoV2'),('Damage','StairwellDamageAlbedo')):
        path=ROOT/('Assets/Resources/Stairwell/Textures/'+source+'.png')
        base=np.asarray(Image.open(path).convert('RGB').resize((512,512),Image.Resampling.BOX),np.float64)
        fy,fx=np.meshgrid(np.fft.fftfreq(512),np.fft.fftfreq(512),indexing='ij')
        kernel=np.exp(-2*np.pi*np.pi*9.0**2*(fx*fx+fy*fy))
        broad=np.fft.ifft2(np.fft.fft2(base,axes=(0,1))*kernel[:,:,None],axes=(0,1)).real
        mean=base.mean(axis=(0,1),keepdims=True)
        # Preserve the measured source grain spectrum but discard flake-edge
        # phase: simple high-pass alone still retains recognizable outlines.
        rng=np.random.default_rng(stable('micro:'+family))
        phase=np.fft.fft2(rng.standard_normal((512,512)))
        phase/=np.maximum(np.abs(phase),1e-8)
        grain=np.fft.ifft2(np.fft.fft2(base-broad,axes=(0,1))*phase[:,:,None],axes=(0,1)).real
        sigma=np.maximum(grain.std(axis=(0,1),keepdims=True),1e-6)
        grain*=np.minimum(sigma,4.0)/sigma
        target=np.rint(np.clip(mean+np.clip(grain,-14,14),0,255)).astype(np.uint8)
        if np.max(np.abs(target.mean(axis=(0,1))-base.mean(axis=(0,1))))>1:
            raise RuntimeError('Neutral micro lost original mean '+family)
        data=png(Image.fromarray(target,'RGB'));stem='Stairwell'+family+'Micro'
        outputs[stem]=data
        rows.append(dict(domain='stairwell',family=family,resource='SurfaceDamage/Micro/'+stem,
            size=[512,512],sRGB=True,wrap='Repeat',sha256=hashlib.sha256(data).hexdigest(),
            source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
            maximum_source_mean_difference=round(float(np.max(np.abs(target.mean(axis=(0,1))-base.mean(axis=(0,1))))),5)))
    return outputs,rows


def build():
    charts,entries=design();skylines=pack(charts)
    packed_pixels=validate_mapping(charts,entries)
    pages=[np.zeros((SIZE,SIZE,4),np.uint8) for _ in range(PAGES)]
    seen={};stats=[]
    for chart in sorted(charts.values(),key=lambda c:c['id']):
        raster=render_chart(chart)
        digest=hashlib.sha256(raster.tobytes()).hexdigest()
        if digest in seen:raise RuntimeError('Repeated whole-surface motif: '+chart['id']+' / '+seen[digest])
        seen[digest]=chart['id']
        changed=float(np.mean(np.max(raster[:,:,:3],axis=2)>45))
        if changed<.06:raise RuntimeError('Surface lacks causal visible wear: '+chart['id'])
        x,y,w,h=chart['pixels']
        pages[chart['page']][y:y+h,x:x+w]=np.pad(raster,((GUTTER,GUTTER),(GUTTER,GUTTER),(0,0)),mode='edge')
        stats.append(dict(id=chart['id'],sha256=digest,coverage=round(changed,5)))
    for row in entries:
        c=charts[row['chart_id']]
        row.update(page=c['page'],rect=c['rect'])
    source_hash=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
    page_rows=[];page_bytes=[]
    for index,values in enumerate(pages):
        data=png(Image.fromarray(values,'RGBA'));page_bytes.append(data)
        page_rows.append(dict(resource='SurfaceDamage/SurfaceDamageAtlas'+str(index),
            size=[SIZE,SIZE],sha256=hashlib.sha256(data).hexdigest()))
    micro=[]
    for family in HOME_FAMILIES:
        path=ROOT/('Assets/Resources/Home/Textures/Home'+family+'Albedo.png')
        micro.append(dict(domain='home',family=family,resource='Home/Textures/'+path.stem,sha256=source_hash(path)))
    for family,stem in (('WallPaint','StairwellWallPaintAlbedoV2'),('Concrete','StairwellConcreteAlbedo'),
            ('StairConcrete','StairwellStairConcreteAlbedo'),('CorrodedMetal','StairwellCorrodedMetalAlbedoV2'),
            ('DoorPaint','StairwellDoorPaintAlbedoV2'),('Damage','StairwellDamageAlbedo'),
            ('DirtyWood','StairwellDirtyWoodAlbedo'),('Debris','StairwellDebrisAlbedo')):
        path=ROOT/('Assets/Resources/Stairwell/Textures/'+stem+'.png')
        micro.append(dict(domain='stairwell',family=family,resource='Stairwell/Textures/'+stem,sha256=source_hash(path)))
    micro_bytes,micro_rows=micro_overrides()
    payload=dict(schema_version=2,contract=VERSION,final_day=7,generator=Path(__file__).name,
        pages=page_rows,linear_mask_channels=['soil','substrate','accent','roughness'],
        palette_space=dict(soil_multiplier='linear',expose_color='sRGB',accent_color='sRGB'),
        rect_contract='bottom-left normalized content texel centres; clamp within chart; no frac',
        mapping_contract='room: transform original position into canonical room before projection; local: normalize original mesh bounds; mirrors copy original mapping',
        box_faces=[dict(face=face,column=i%3,row=i//3,uv=uv) for i,(face,uv) in enumerate(zip(FACES,
            ('ZY','ZY','XZ','XZ','XY','XY')))],
        source_sha256=dict(home=source_hash(HOME),stairwell=source_hash(STAIR)),original_micro_sources=micro,
        micro_overrides=micro_rows,
        families=family_rows(),entries=sorted(entries,key=lambda r:r['key']),
        report=dict(chart_count=len(charts),entry_count=len(entries),maximum_pages=PAGES,gutter_pixels=GUTTER,
            packed_height=[max(s) for s in skylines],unique_chart_hashes=len(seen),
            packed_pixels=packed_pixels,maximum_pixels=PAGES*SIZE*SIZE,
            home_family_count=len(HOME_FAMILIES),charts=stats))
    return payload,page_bytes,micro_bytes


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--validate-only',action='store_true');args=parser.parse_args()
    payload,pages,micro=build()
    # Second independent build checks random state, packing and encoded bytes.
    again,again_pages,again_micro=build()
    if payload!=again or pages!=again_pages or micro!=again_micro:raise RuntimeError('Nondeterministic whole-surface damage')
    text=json.dumps(payload,indent=2)+'\n'
    files={OUTPUT/('SurfaceDamageAtlas'+str(i)+'.png'):data for i,data in enumerate(pages)}
    files.update({OUTPUT/'Micro'/(stem+'.png'):data for stem,data in micro.items()})
    files[OUTPUT/'SurfaceDamageManifest.json']=text.encode();files[MANIFEST]=text.encode()
    for path,data in files.items():
        if args.validate_only:
            if path.read_bytes()!=data:raise RuntimeError('Stale whole-surface damage asset '+str(path))
        else:
            path.parent.mkdir(parents=True,exist_ok=True)
            if not path.is_file() or path.read_bytes()!=data:path.write_bytes(data)
    # Retire only the17 generated first implementation sheets and their metas;
    # source micro sheets and any unrelated files are never touched.
    for family in HOME_FAMILIES:
        old=RETIRED/('Home'+family+'Albedo.png')
        for path in (old,Path(str(old)+'.meta')):
            if not path.exists():continue
            if args.validate_only:raise RuntimeError('Obsolete repeating macro sheet remains '+str(path))
            path.unlink()
    if RETIRED.is_dir() and not any(RETIRED.iterdir()):
        if not args.validate_only:
            RETIRED.rmdir();Path(str(RETIRED)+'.meta').unlink(missing_ok=True)
    expected={'home:'+f for f in HOME_FAMILIES}
    actual={r['domain']+':'+r['family'] for r in payload['families']}
    if not expected<=actual:raise RuntimeError('Missing Home family')
    print('UNIQUE_SURFACE_DAMAGE_OK: '+str(payload['report']['chart_count'])+' unique charts; '+
        str(payload['report']['entry_count'])+' stable bindings; two2048 pages; exact deterministic bytes; all17Home families')


if __name__=='__main__':main()
