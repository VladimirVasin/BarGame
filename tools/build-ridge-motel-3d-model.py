#!/usr/bin/env python3
"""Fixed-metre Ridge motel, looking into the court from the north (+Z)."""
import argparse
import hashlib
import json
from math import ceil, cos, radians, sin
from pathlib import Path
import runpy
import sys

sys.dont_write_bytecode = True
import bpy

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
import interior_kit as kit
import bar_parts as parts

# Both buildings use the same physical finish atlas, projection and bevel tooling.
garage = runpy.run_path(str(TOOLS / 'build-city-ridge-garages-3d-model.py'))
Assembly, mesh_object = garage['Assembly'], garage['mesh_object']
NAME = 'RidgeMotel'
STOREY = 3.06
WIDTH = 28
BAY = WIDTH / 6
LANDING = 1.92
RUN = 2.88
TREADS = 9
RAIN_EDGE = 16
# Points on a 2 by 3 grid, traced in the order a sign painter would draw them.
LETTERS = {
    'М': ['00 03 11 23 20'], 'О': ['01 02 13 22 21 10 01'],
    'Т': ['03 23','13 10'], 'Е': ['23 03 00 20','02 12'],
    'Л': ['00 13 23 20'], 'Ь': ['03 00 10 21 12 02'],
    '0':['00 03 23 20 00'], '1':['02 13 10','00 20'],
    '2':['03 23 22 01 00 20'], '3':['03 23 20 00','02 22'],
    '4':['03 01 21','23 20'], '5':['23 03 02 22 20 00'],
    '6':['23 03 00 20 21 01']
}


def lettering(model, text, center, unit, finish):
    cx, cy, cz = center
    for index, ch in enumerate(text):
        start = cx + ((len(text)-1)*1.65 + 1 - index*3.3)*unit
        for stroke in LETTERS[ch]:
            points = [(start-int(p[0])*unit, cy+(int(p[1])-1.5)*unit, cz)
                      for p in stroke.split()]
            model.path('Lettering', finish, points, unit*.115, 5)


def wedge(x, width, z0, z1, y0, y1, depth):
    shape = kit.box((x,(y0+y1)*.5-depth*.5,(z0+z1)*.5),
                    (width,depth,abs(z1-z0)))
    vertices, faces = shape
    return [(a,b+(c-(z0+z1)*.5)*(y1-y0)/(z1-z0),c) for a,b,c in vertices],faces


def guard(model, start, end, group='Ironwork'):
    a,b=start,end
    def point(t,h):
        return (a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t+h,a[2]+(b[2]-a[2])*t)
    length=((a[0]-b[0])**2+(a[2]-b[2])**2)**.5
    posts=max(1,ceil(length/1.35))
    for i in range(posts+1):
        model.rod(group,'iron',point(i/posts,.02),point(i/posts,1.04),.027,6)
    for height,radius,finish in ((.38,.017,'sage'),(.72,.017,'sage'),(1.06,.034,'zinc')):
        model.rod(group,finish,point(0,height),point(1,height),radius,8)
    if abs(a[2]-b[2])>.001:
        solid=wedge(a[0],.075,a[2],b[2],a[1]+1.07,b[1]+1.07,1.07)
    else:
        solid=kit.box(point(.5,.535),(length,1.07,.075))
    model.add('COL_Guards','iron',solid)


def staircase(m):
    # Each storey returns to the same through landing beside the gallery.
    for floor in range(2):
        base=floor*STOREY
        for flight,(x,z0,z1) in enumerate(((-15.55,LANDING,LANDING+RUN),
                                           (-13.90,LANDING+RUN,LANDING))):
            y0=base+flight*STOREY/2
            y1=y0+STOREY/2
            m.add('COL_Metal','iron',wedge(x,1.65,z0,z1,y0,y1,.11))
            for step in range(TREADS):
                z=z0+(step+.5)*(z1-z0)/TREADS
                top=y0+(step+1)*(y1-y0)/TREADS
                m.box('Stair','zinc',(x,top-.038,z),(1.65,.076,.325),.014)
            for edge in (-1,1):
                gx=x+edge*.784
                m.add('Stair','iron',wedge(gx,.085,z0,z1,y0-.04,y1-.04,.16))
                guard(m,(gx,y0,z0),(gx,y1,z1))
        for top,z in ((base+STOREY/2,LANDING+RUN+.80),(base+STOREY,LANDING/2)):
            size=(3.38,.15,1.60 if z>LANDING else LANDING)
            pos=(-14.70,top-.075,z)
            m.box('Stair','zinc',pos,size)
            m.add('COL_Metal','iron',kit.box(pos,size))
            outer=z+(size[2]/2-.04)*(1 if z>LANDING else -1)
            guard(m,(-16.34,top,outer),(-13.08,top,outer))
            guard(m,(-16.34,top,z-size[2]/2),(-16.34,top,z+size[2]/2))
            if z>LANDING:
                guard(m,(-13.08,top,z-size[2]/2),(-13.08,top,z+size[2]/2))
    for x in (-16.25,-13.15):
        for z,h in ((.12,2*STOREY),(LANDING+RUN+1.45,1.5*STOREY)):
            m.box('Stair','iron',(x,h/2,z),(.12,h,.12))


def frontage(m):
    for level in range(3):
        y=level*STOREY
        for i in range(6):
            left=-13+i*BAY
            mid=left+BAY/2
            m.box('Rooms','dado' if level==0 else 'limewash',(mid,y+1.51,-2.90),(BAY-.018,3.07,5.38),.035)
            door=left+.87
            m.box('Doors','case',(door,y+1.09,-.13),(1.16,2.18,.16))
            m.box('Doors','sage' if i+2*level in (2,5) else 'card',(door,y+1.045,-.025),(.96,2.09,.10))
            m.box('Doors','zinc',(door+.32,y+1.03,.045),(.14,.045,.045),.009)
            m.box('Doors','iron',(door,y+1.68,.035),(.34,.145,.026),.009)
            lettering(m,str(100*(level+1)+i+1),(door,y+1.68,.052),.027,'limewash')
            window=left+3.03
            m.box('Windows','iron',(window,y+1.57,-.095),(1.91,1.30,.21))
            lit=(level,i) in ((0,0),(1,3),(2,5))
            m.box('WarmPanes' if lit else 'Windows','glass' if lit else 'iron',
                  (window,y+1.57,.018),(1.78,1.17,.04))
            cloth=.34+((level+i*2)%4)*.17
            m.box('Windows','canvas',(window-.89+cloth/2,y+1.57,.047),(cloth,1.17,.019),.004)
            m.box('Windows','case',(window+.13,y+1.57,.068),(.045,1.21,.047),.005)
            m.box('Windows','slab',(window,y+.90,.025),(2,.09,.32))
        if level:
            m.box('Gallery','slab',(1,y-.095,.83),(28.06,.19,2.18),.024)
            m.add('COL_Concrete','slab',kit.box((1,y-.095,.83),(28.06,.19,2.18)))
            guard(m,(-12.97,y,1.87),(14.94,y,1.87))
            guard(m,(14.94,y,1.87),(14.94,y,-.05))
    for i in range(7):
        x=-13+i*BAY
        m.box('Gallery','case',(x,STOREY*1.5,1.77),(.13,STOREY*3,.13),.014)
        m.add('COL_Posts','iron',kit.box((x,STOREY*1.5,1.77),(.13,STOREY*3,.13)))
    m.box('Roof','felt',(1,STOREY*3+.07,-1.78),(28.45,.24,7.86),.036)
    m.box('Roof','zinc',(1,STOREY*3+.12,2.13),(28.48,.19,.105))
    for x in (-12.92,5.64,14.92):
        m.path('Rainwater','zinc',[(x,9.23,2.10),(x,9.05,1.98),(x,.32,1.98),(x,.13,2.16)],.045,8)
    m.add('COL_Shell','slab',kit.box((1,4.49,-2.79),(28,9.50,5.62)))
    m.box('Foundation','concrete',(1,-.22,-2.78),(28.1,.48,5.65),.018)


def office(m):
    # Recessed shopfront. Real glass, a counter and two walls behind the glazing.
    for pos,size in [((12,-.15,6.22),(6,.52,7.80)),((14.91,1.47,6.22),(.18,2.95,7.8)),
                     ((9.09,.48,6.22),(.18,.96,7.8)),((9.09,1.96,3.34),(.18,2.02,2.04)),
                     ((9.09,1.96,9.11),(.18,2.02,2.02)),((12,3.03,6.22),(6.45,.24,8.6)),
                     ((12,1.47,2.41),(6,2.95,.18)),((12,.49,10.03),(6,.98,.18)),((13.84,1.75,10.03),(2.32,1.7,.18))]:
        m.box('Reception','dado' if size[1]>.5 else 'slab',pos,size,.026)
    m.box('Glass','glass',(9.09,1.88,6.23),(.05,1.90,3.76))
    for z in (4.36,6.23,8.10):
        m.box('Reception','case',(9.055,1.92,z),(.12,1.98,.07))
    m.box('Reception','slab',(9.02,.95,6.23),(.32,.10,3.87))
    m.box('Reception','timber',(9.86,1.06,6.26),(.63,.11,2.96))
    m.box('Reception','sage',(9.88,.58,6.26),(.51,.91,2.80))
    m.box('Reception','timber',(11.3,1.16,8.75),(3.1,.12,.60))
    m.box('Reception','sage',(11.3,.61,8.75),(2.95,1.05,.48))
    m.box('Reception','case',(13.82,1.13,10.14),(1.18,2.26,.12))
    m.box('Reception','sage',(13.82,1.08,10.22),(.98,2.16,.09))
    m.box('Reception','zinc',(13.49,1.10,10.29),(.15,.045,.05))
    m.box('Glass','glass',(10.94,1.90,10.03),(3.57,1.81,.055))
    for x in (9.18,10.94,12.70):
        m.box('Reception','case',(x,1.90,10.08),(.075,1.88,.115))
    m.box('Reception','sage',(12,3.32,10.43),(5.88,1.03,.16),.035)
    lettering(m,'МОТЕЛЬ',(12,3.32,10.53),.202,'limewash')
    m.add('COL_Shell','concrete',kit.box((12,1.50,6.22),(6,3.15,7.8)))


# Patio furniture has metre pivots so chairs can slide independently in play.
TABLES = ((10.85, 12.05, 12), (6.65, 13.0, -19))
PIVOTS = {}


def patio(m):
    def place(group, assembly, position, yaw=0):
        PIVOTS[group] = position
        for finish, shape in assembly.meshes['Part']:
            m.add(group, finish, garage['turned'](shape, yaw, position))

    for table, (x, z, yaw) in enumerate(TABLES):
        top = Assembly()
        for plank in range(4):
            top.box('Part', 'worktop' if table == 0 else 'repair',
                    (0, .748, (plank-1.5)*.197), (.99, .049, .189), .012)
        for dx in (-.39, .39):
            for dz in (-.29, .29):
                top.rod('Part', 'iron', (dx, .72, dz), (dx*1.05, .025, dz*1.05), .021)
            top.rod('Part', 'sage', (dx, .24, -.30), (dx, .24, .30), .016)
        place('PatioTable'+str(table), top, (x, 0, z), yaw)
        m.add('COL_Furniture', 'iron', garage['turned'](kit.box((0,.39,0),(.98,.78,.78)),yaw,(x,0,z)))
        for side in range(2):
            sign = 1 if side else -1
            chair = Assembly()
            for dx in (-.19,.19):
                chair.rod('Part','iron',(dx,.025,-.22),(dx,.44,.15),.018)
                chair.rod('Part','zinc',(dx,.025,.21),(dx,.80,-.20),.017)
            chair.box('Part','sage' if side else 'timber',(0,.44,0),(.43,.045,.40))
            chair.box('Part','canvas' if side else 'repair',(0,.74,-.195),(.45,.16,.045))
            centre=(x+sign*.80*cos(radians(yaw)),0,z-sign*.80*sin(radians(yaw)))
            place('PatioChair'+str(table*2+side),chair,centre,yaw-sign*90)
        # Worn pocket opener with a visible cutout at the wide end.
        if not table:
            opener = Assembly()
            opener.box('Part','zinc',(0,.007,-.022),(.026,.009,.088),.003)
            opener.path('Part','zinc',[(-.010,.007,.015),(-.018,.007,.040),
                                       (.018,.007,.040),(.010,.007,.015)],.005)
            place('Opener',opener,(x+.06,.775,z+.08),yaw)

    bag=Assembly()
    bag.box('Part','canvas',(0,.16,0),(.37,.32,.24),.037)
    for dz in (-.075,.075):
        bag.path('Part','leather',[(-.10,.30,dz),(-.065,.43,dz),(.065,.43,dz),(.10,.30,dz)],.011)
    place('BottleBag',bag,(10.85,0,12.05))
    # Day linen cart, folded towels and a removable bundle at the top.
    cart=Assembly()
    for dx in (-.32,.32):
        for dz in (-.21,.21):
            cart.rod('Part','rubber',(dx-.026,.088,dz),(dx+.026,.088,dz),.083,10)
            cart.rod('Part','zinc',(dx,.14,dz),(dx,.89,dz),.018)
    for y in (.22,.70):
        cart.box('Part','case',(0,y,0),(.72,.038,.49))
        for stack in (-.17,.17):
            for n in range(2):
                cart.box('Part','canvas' if n==0 else 'limewash',(stack,y+.054+n*.068,0),(.31,.067,.38),.023)
    cart.rod('Part','iron',(-.32,.89,.21),(.32,.89,.21),.018)
    place('LinenCart',cart,(-10.8,0,2.56))
    towels=Assembly()
    for j in range(3):
        towels.box('Part','canvas' if j==1 else 'limewash',(0,.028+j*.056,0),(.40,.055,.29),.018)
    place('LinenBundle',towels,(-10.8,.862,2.56))


def parked_wagon(m):
    # Across the middle painted bay. The tailgate opens toward the yard aisle.
    x,z=6.08,7.06
    def panel(key, finish, pos, size, bevel=.025):
        a,b,c=pos
        m.box(key,finish,(x+a,b,z+c),size,bevel)
    def bar(key, finish, a, b, r):
        m.rod(key,finish,(x+a[0],a[1],z+a[2]),(x+b[0],b[1],z+b[2]),r)
    body='ParkedWagon'
    panel(body,'iron',(0,.49,0),(4.43,.27,1.69),.09)
    panel(body,'sage',(0,.80,0),(4.37,.47,1.70),.095)
    panel(body,'sage',(1.48,1.025,0),(1.33,.125,1.65),.06)
    panel(body,'sage',(-.66,1.60,0),(2.68,.10,1.55),.06)
    panel(body,'iron',(-.38,1.29,0),(2.10,.50,1.48),.06)
    panel(body,'rubber',(-1.77,1.045,0),(.73,.045,1.42))
    for side in (-1,1):
        side_z=side*.766
        for a,b in ((-1.94,-1.15),(-1.08,-.15),(-.08,.63)):
            panel(body,'case',((a+b)/2,1.305,side_z),(b-a,.41,.024),.035)
        for dx in (-1.12,-.115):
            panel(body,'sage',(dx,1.295,side_z),(.062,.51,.052),.014)
        bar(body,'sage',(.64,1.56,side_z),(.99,1.04,side*.81),.042)
        panel(body,'iron',(-.31,.69,side*.857),(3.55,.045,.036))
        for dx in (.37,-.58):
            panel(body,'iron',(dx,.94,side*.857),(.17,.04,.052),.009)
        for dx in (-1.40,1.36):
            bar(body,'rubber',(dx,.333,side*.70),(dx,.333,side*.88),.325)
            bar(body,'case',(dx,.333,side*.874),(dx,.333,side*.898),.19)
        panel(body,'glass',(2.179,.85,side*.55),(.052,.18,.42))
        panel(body,'oxide',(-2.166,.81,side*.69),(.067,.25,.17))
    panel(body,'iron',(2.20,.58,0),(.13,.118,1.68))
    panel(body,'iron',(-2.18,.55,0),(.16,.105,1.67))
    panel(body,'iron',(2.19,.86,0),(.033,.16,.57))
    hatch='WagonHatch'
    PIVOTS[hatch]=(x-2.00,1.595,z)
    panel(hatch,'sage',(-2.12,1.28,0),(.095,.62,1.52),.04)
    panel(hatch,'case',(-2.177,1.34,0),(.025,.39,1.39))
    panel(hatch,'iron',(-2.175,1.01,0),(.043,.04,.29),.009)
    m.add('COL_Furniture','iron',kit.box((x,.67,z),(4.4,1.34,1.69)))

    for name, rest, finish in [('TravelCase',(x-1.97,1.07,z-.22),'leather'),('TravelCoat',(x-1.94,1.07,z+.26),'canvas')]:
        PIVOTS[name]=rest
        if name=='TravelCase':
            m.box(name,finish,(rest[0],rest[1]+.166,rest[2]),(.18,.33,.41),.034)
            for dz in (-.078,.078):
                m.rod(name,'iron',(rest[0],rest[1]+.31,rest[2]+dz),(rest[0],rest[1]+.39,rest[2]+dz),.012)
            m.rod(name,'leather',(rest[0],rest[1]+.39,rest[2]-.078),(rest[0],rest[1]+.39,rest[2]+.078),.018)
        else:
            m.box(name,finish,(rest[0],rest[1]+.05,rest[2]),(.32,.10,.31),.035)
            m.box(name,'dado',(rest[0]-.04,rest[1]+.11,rest[2]-.03),(.22,.032,.13),.012)


def lights():
    # World metres on the shared slabs. Unequal intervals and lateral aims
    # follow the room bays; every door gets spill from its nearest fixture.
    specs = {}
    for level, stations in enumerate(((-10.55,-.9,9.65),(-9.75,1.55,11.20),(-10.4,-.2,10.48))):
        for bay,x in enumerate(stations):
            top=(level+1)*STOREY - (.10 if level==2 else .19)
            specs['Gallery'+str(level)+str(bay)] = ((x,top-.145,1.08),
                (x+(.28,-.12,.18)[bay],level*STOREY+.70,.66))
    specs['Entry']=((13.84,2.75,10.48),(13.2,.70,11.05))
    specs['CourtCorner']=((8.78,2.87,10.64),(6.70,1.14,13.1))
    specs['Window']=((10.94,2.65,10.16),(10.82,.79,12.23))
    specs['Desk']=((10.43,2.72,7.44),(10.03,.96,6.26))
    specs['Stair']=((-14.7,2.77,1.52),(-15.42,.60,2.91))
    specs['Landing']=((-14.7,5.83,1.52),(-15.42,3.68,2.91))
    return specs


def create():
    m=Assembly()
    frontage(m)
    office(m)
    patio(m)
    parked_wagon(m)
    staircase(m)
    # The return at the top has no next upward flight on the outside lane.
    guard(m,(-16.34,2*STOREY,1.87),(-14.77,2*STOREY,1.87))
    # A folded steel chair with a faded canvas seat at the quiet end of the deck.
    for x in (14.32,14.78):
        m.rod('Gallery','zinc',(x,STOREY+.02,.18),(x,STOREY+.84,.56),.025)
        m.rod('Gallery','zinc',(x,STOREY+.02,.60),(x,STOREY+.46,.18),.025)
    m.box('Gallery','canvas',(14.55,STOREY+.45,.38),(.51,.055,.40))
    m.box('Gallery','sage',(14.55,STOREY+.76,.54),(.51,.19,.055))
    m.add('COL_Posts','iron',kit.box((14.55,STOREY+.42,.39),(.51,.84,.43)))
    m.rod('Fixtures','iron',(9.06,2.95,10.13),(8.78,2.98,10.64),.035)
    for name,((x,y,z),aim) in lights().items():
        m.box('Fixtures','case',(x,y+.145,z),(.24,.17,.18))
        m.box('Fixtures','iron',(x,y+.055,z),(.56,.085,.27),.02)
        m.box('Lens'+name,'glass',(x,y-.014,z),(.46,.10,.21),.038)
        for offset in (-.18,.18):
            m.box('Fixtures','iron',(x+offset,y-.071,z),(.012,.012,.20),.004)
    for z in (3.0,5.7,8.4,11.1):
        m.box('Parking','slab',(6.12,.009,z),(5.05,.017,.083),.005)
    m.box('Doors','rubber',(-12.1,.022,.39),(.88,.044,.48))
    # The courtyard uses the shared City asphalt shader in world metres.
    m.add('Court','felt',kit.box((-.75,-.22,5.20),(31.7,.44,21.6)))
    m.add('COL_Court','slab',kit.box((-.75,-.22,5.20),(31.7,.44,21.6)))
    return m


def verify_export(path, model):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
    objects=bpy.context.scene.objects
    for name,solids in model.meshes.items():
        obj=objects[name]
        low,high=kit.bounds(kit.merge_all(shape for _,shape in solids))
        points=[obj.matrix_world @ v.co for v in obj.data.vertices]
        for axis,source_axis in enumerate((0,2,1)):
            assert abs(min(p[source_axis] for p in points)-low[axis])<.002, name
            assert abs(max(p[source_axis] for p in points)-high[axis])<.002, name
        if not name.startswith('COL_'):
            assert obj.data.uv_layers.active, name
    for name,(_,aim) in lights().items():
        x,z,y=objects['Aim'+name].matrix_world.translation
        assert max(abs(a-b) for a,b in zip((x,y,z),aim))<.002, name


def generate(check):
    out=ROOT/'Assets/Resources/City'/NAME
    source=ROOT/'ArtSource/City'/NAME
    m=create()
    bounds=kit.bounds(kit.merge_all(shape for group,solids in m.meshes.items()
                                   if not group.startswith('COL_') for _,shape in solids))
    manifest={'model':NAME,'units':'Unity metres, +Z to street','storeyRise':STOREY,
              'streetEdge':RAIN_EDGE,'lights':lights(),'bounds':bounds,'triangles':sum(kit.triangle_count(s)
              for group,v in m.meshes.items() if not group.startswith('COL_') for _,s in v),
              'sha256':hashlib.sha256(json.dumps(m.meshes,separators=(',',':')).encode()).hexdigest()}
    record=json.dumps(manifest,indent=2)+'\n'
    if check:
        assert (out/(NAME+'.json')).read_text()==record
        assert (source/(NAME+'.blend')).is_file()
        verify_export(out/(NAME+'.fbx'),m)
        return
    out.mkdir(parents=True,exist_ok=True)
    source.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root=bpy.data.objects.new(NAME,None)
    bpy.context.collection.objects.link(root)
    surface=bpy.data.materials.new('Ridge finishes')
    surface.use_nodes=True
    image=bpy.data.images.load(str(ROOT/'Assets/Resources/City/RidgeGarages/RidgeGaragesSurface.png'))
    image.pack()
    texture=surface.node_tree.nodes.new('ShaderNodeTexImage')
    texture.image=image
    surface.node_tree.links.new(texture.outputs['Color'],surface.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    for name,solids in m.meshes.items():
        if name in PIVOTS:
            x,y,z=PIVOTS[name]
            solids=[(key,kit.translated(shape,(-x,-y,-z))) for key,shape in solids]
        obj=mesh_object(root,name,solids,None if name.startswith('COL_') else surface)
        if name in PIVOTS:
            obj.location=(x,z,y)
    for name,(_,aim) in lights().items():
        target=bpy.data.objects.new('Aim'+name,None)
        bpy.context.collection.objects.link(target)
        target.parent=root
        target.location=(aim[0],aim[2],aim[1])
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=str(out/(NAME+'.fbx')),use_selection=True,axis_forward='-Z',axis_up='Y',
        apply_scale_options='FBX_SCALE_ALL',bake_space_transform=False,bake_anim=False,add_leaf_bones=False,path_mode='STRIP')
    for obj in root.children:
        if obj.name.startswith('COL_'):
            obj.hide_set(True)
            obj.hide_render=True
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(source/(NAME+'.blend')),compress=True)
    (out/(NAME+'.json')).write_text(record)
    verify_export(out/(NAME+'.fbx'),m)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--validate-only',action='store_true')
    generate(parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []).validate_only)
