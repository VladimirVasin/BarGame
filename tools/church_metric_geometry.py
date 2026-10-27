"""Metric church interiors derived from the placed Gothic exterior.

Blender XYZ = Unity XZY. Exterior stays in its historic authoring scale;
this module authors the interior at one metre per unit, with human-size
furniture. The two exports meet at the same west threshold.
"""
from __future__ import annotations
import math
import interior_kit as kit

EXTERIOR_SCALE = .55
ROOM_WIDTH, ROOM_LENGTH = 12.1, 24.2
DOOR_WIDTH, DOOR_HEIGHT, DOOR_DEPTH = 1.8, 2.6, -12.1275
WINDOW_DEPTHS = (-5.5, -2.2, 1.1, 4.4, 7.7)
WINDOW_X, WINDOW_HEIGHT = 6.06, 2.6675
WINDOW_WIDTH, WINDOW_CLEAR_HEIGHT = .6875, 1.7875
WINDOW_SILL = WINDOW_HEIGHT - WINDOW_CLEAR_HEIGHT / 2
WINDOW_HEAD = WINDOW_HEIGHT + WINDOW_CLEAR_HEIGHT / 2
ROSE_CENTER = (0.0, -12.078, 4.95)
ROSE_RADIUS = 1.5125
NARTHEX_BAY_HALF_WIDTH = 1.8
NARTHEX_BAY_CEILING = 6.68
# One passive donor owns both upper tower lancets. All coordinates here are
# Blender XYZ in real placed metres, independent of the exterior's .55 root.
TOWER_LANCET = {
    'coordinate_space': 'Blender XYZ metres before exterior wrapper',
    'centres_x_m': [-1.10, 1.10],
    'wall_centre_y_m': -11.895,
    'wall_thickness_m': .30,
    'glass_plane_y_m': -12.035,
    'clear_width_m': .62,
    'sill_height_m': 9.10,
    'shoulder_height_m': 9.96,
    'tip_height_m': 10.28,
    'frame_plane_y_m': -12.085,
    'frame_half_width_m': .40,
    'frame_sill_height_m': 9.055,
    'frame_shoulder_height_m': 10.0,
    'frame_tip_height_m': 10.43,
    'frame_radius_m': .045,
    'tower_top_height_m': 10.82125,
}
ROSE_EXTERIOR_RIM = {
    'mean_radius_m': 1.5375, 'tube_radius_m': .035,
    'spoke_outer_radius_m': 1.514,
    'plane_y_m': -12.111,
}
PEW_XS = (-2.05, 2.05)
PEW_ROWS = (-6.1, -4.7, -2.3, -.9, .5, 1.9)
STATION_DEPTHS = (-7.6, -6.55, -4.25, -1.1, 2.45, 5.65, 8.75)


def contract(name, centers, footprint, vertical):
    return dict(name=name, count=len(centers), centers_xz=[list(c) for c in centers],
                footprint_xz_m=list(footprint), vertical_envelope_m=list(vertical))

LAYOUT = [
    contract('nave_piers', [(x,z) for z in (-3.5,4.0) for x in (-3.5,3.5)], (.84,.84), (0,4.9)),
    contract('pew_halves', [(x,z) for z in PEW_ROWS for x in PEW_XS], (2.2,.72), (0,1.05)),
    contract('communion_rail', [(0,8.0)], (11.5,.28), (0,.85)),
    contract('altar_table', [(0,9.2)], (2.3,1.3), (0,1.0)),
    contract('high_altar', [(0,10.45)], (3.3,1.15), (0,3.8)),
    contract('crucifix', [(0,10.53)], (1.75,.18), (2.4,4.7)),
    contract('confessionals', [(-4.95,6.55),(4.95,6.55)], (1.4,1.5), (0,2.45)),
    contract('votive_stands', [(-3.2,6.3),(3.2,6.3)], (.7,.7), (0,1.35)),
    contract('baptismal_font', [(-3.5,-8.8)], (1.1,1.1), (0,1.1)),
    contract('choir_loft', [(0,-10.75)], (8.7,2.4), (3.0,3.22)),
    contract('choir_loft_supports', [(x,-10.3) for x in (-4.1,-3.25,3.25,4.1)], (.22,.22), (0,3.0)),
    contract('pipe_organ', [(0,-10.3)], (4.8,.9), (3.22,5.92)),
]


# Passive trim above the player/collision height. These bearings are not
# widened pier colliders: each cap carries the vault spring and its rib.
OVERHEAD_BEARINGS = [dict(
    id=f'overhead-bearing-{index+1}', centre_xyz=[(-1 if x<0 else 1)*3.72,z,4.88],
    size_xyz=[.68,.20,.18],
    cap_contact_xyz=[(-1 if x<0 else 1)*3.75,z,4.895],
    rib_contact_xyz=[(-1 if x<0 else 1)*3.99,z+.01,4.90],
    vault_contact_xyz=[(-1 if x<0 else 1)*3.99,z+.01,4.913])
    for index,(x,z) in enumerate(LAYOUT[0]['centers_xz'])]

ANCHORS = {
    'Spawn': ((0,-10.1,0),(0,0,0),'player_spawn'),
    'Exit': ((0,-11.2,0),(0,0,180),'exit_interaction'),
    'NarthexLight': ((0,-9.1,3.2),(0,0,0),'light_narthex'),
    'NaveLight': ((0,-1.5,4.5),(0,0,0),'light_nave'),
    'SanctuaryLight': ((0,9.2,3.5),(0,0,180),'light_sanctuary'),
    'Door': ((0,DOOR_DEPTH,0),(0,0,180),'entrance_threshold'),
    'Rose': (ROSE_CENTER,(0,0,0),'rose_aperture'),
    'Rose.Radius': ((ROSE_RADIUS,ROSE_CENTER[1],ROSE_CENTER[2]),(0,0,0),'rose_aperture_radius'),
    'Votive.North': ((-3.2,6.3,1.28),(0,0,0),'votive_flames'),
    'Votive.South': ((3.2,6.3,1.28),(0,0,0),'votive_flames'),
    'Warm.HighAltar.North': ((-1.2,10.0,2.0),(0,0,0),'warm_practical'),
    'Warm.HighAltar.South': ((1.2,10.0,2.0),(0,0,0),'warm_practical'),
    'Warm.Altar': ((0,8.95,1.34),(0,0,0),'warm_practical'),
    'Warm.Font': ((-3.5,-8.8,1.32),(0,0,0),'warm_practical'),
}
SCONCES = {
    'NorthNave': (-5.58,-4.0,2.5), 'SouthNave': (5.58,-4.0,2.5),
    'NorthCrossing': (-5.58,2.8,2.5), 'SouthCrossing': (5.58,2.8,2.5),
    'NorthNarthex': (-4.42,-9.1,2.45), 'SouthNarthex': (4.42,-9.1,2.45),
}
CORONAS = {'Narthex': (0,-10.65,2.5), 'NaveWest': (0,-6.8,3.8),
           'NaveEast': (0,-1.8,3.8), 'Crossing': (0,4.5,3.8)}
for name, pos in SCONCES.items():
    ANCHORS['Sconce.'+name] = (pos,(0,0,0),'sconce_flame')
for name, pos in CORONAS.items():
    ANCHORS['Corona.'+name] = (pos,(0,0,0),'corona_light')
    ANCHORS['CoronaChain.'+name] = ((pos[0],pos[1],3.0 if name=='Narthex' else 7.3),(0,0,0),'corona_chain_contact')
for side, sign in [('North',-1),('South',1)]:
    for index, depth in enumerate(WINDOW_DEPTHS):
        ANCHORS[f'Window.{side}.{index}'] = ((sign*WINDOW_X,depth,WINDOW_HEIGHT),(0,0,0),'window_aperture')
ANCHORS = {'ANCHOR_Interior.'+name:value for name,value in ANCHORS.items()}

SHELL = {
    'exterior_source_scale': EXTERIOR_SCALE,
    'interior_to_exterior_metric': {'scale': [1,1,1], 'yaw_degrees': 180, 'translation': [0,0,0]},
    'room_size_xz_m': [ROOM_WIDTH,ROOM_LENGTH],
    'door_threshold_xz_m': [0,DOOR_DEPTH],
    'door_opening_m': {'width':DOOR_WIDTH,'height':DOOR_HEIGHT},
    'narthex_clear_bounds_xz_m': [-4.60,-11.9,4.60,-8.55],
    'aisle_clear_bounds_xz_m': [-5.75,-8.55,5.75,9.55],
    'nave_vault_section_xh_m': [[-4,4.9],[-2,6.2],[0,7.4],[2,6.2],[4,4.9]],
    'aisle_roof_section_xh_m': [[4,4.35],[5.95,3.65]],
    'window_depths_m': list(WINDOW_DEPTHS),
    'window_plane_x_m': WINDOW_X,
    'window_center_height_m': WINDOW_HEIGHT,
    'window_clear_size_m': [WINDOW_WIDTH,WINDOW_CLEAR_HEIGHT],
    'sanctuary_rail_z_m': 8.0,
    'rose_center_xyz_m': list(ROSE_CENTER),
    'rose_coordinate_space': 'Blender XYZ metres (Unity XZY)',
    'rose_clear_diameter_m': ROSE_RADIUS*2,
    'narthex_central_ceiling_height_m': NARTHEX_BAY_CEILING,
    'narthex_central_ceiling_width_m': NARTHEX_BAY_HALF_WIDTH*2,
}



def rose_facade_geometry(side_head=6.2):
    """Shared thick wall with a real circular rose and a separate door hole.

    The square corners around the inscribed 32-gon are masonry solids,
    not a pane over a box. Both exports use exactly this metric aperture.
    """
    radius=ROSE_RADIUS; centre_h=ROSE_CENTER[2]
    sill,head=centre_h-radius,centre_h+radius
    pieces=[]
    for side in (-1,1):
        pieces.append(kit.chamfered_box((side*(4.9+radius)/2,0,side_head/2),
            (4.9-radius,.30,side_head),.007))
        pieces.append(kit.chamfered_box((side*(NARTHEX_BAY_HALF_WIDTH+radius)/2,0,
            (side_head+NARTHEX_BAY_CEILING)/2),
            (NARTHEX_BAY_HALF_WIDTH-radius,.30,NARTHEX_BAY_CEILING-side_head),.005))
    pieces.append(kit.wall_run(radius*2,sill,.30,[kit.Opening(0,DOOR_WIDTH,DOOR_HEIGHT)],.007))
    pieces.append(kit.chamfered_box((0,0,(head+NARTHEX_BAY_CEILING)/2),
        (radius*2,.30,NARTHEX_BAY_CEILING-head),.005))
    for i in range(32):
        a,b=math.tau*i/32,math.tau*(i+1)/32
        inner_a=(radius*math.cos(a),centre_h+radius*math.sin(a))
        inner_b=(radius*math.cos(b),centre_h+radius*math.sin(b))
        def outer(t):
            r=radius/max(abs(math.cos(t)),abs(math.sin(t)))
            return (r*math.cos(t),centre_h+r*math.sin(t))
        polygon=[inner_a,outer(a),outer(b),inner_b]
        unique=[]
        for point in polygon:
            if not unique or math.dist(point,unique[-1])>1e-7: unique.append(point)
        if len(unique)>1 and math.dist(unique[0],unique[-1])<1e-7: unique.pop()
        if len(unique)<3: continue
        area=sum(x[0]*y[1]-y[0]*x[1] for x,y in zip(unique,unique[1:]+unique[:1]))
        if area<0: unique.reverse()
        pieces.append(kit.prism(unique,.30))
    return kit.translated(kit.merge_all(pieces),(0,-11.95,0))



def tower_front_geometry():
    """Actual upper tower wall, with the same pointed hole used by its pane."""
    data=TOWER_LANCET; base=6.53125
    pieces=[kit.wall_run(4.51,4.29,.30,[
        kit.Opening(x,data['clear_width_m'],data['tip_height_m']-base,
                    data['sill_height_m']-base)
        for x in data['centres_x_m']],.007,base=base)]
    half=data['clear_width_m']/2
    for centre in data['centres_x_m']:
        # Fill the two upper rectangle corners, leaving a genuine pointed
        # lancet rather than an unrelated rectangular opening behind it.
        for side in (-1,1):
            polygon=[(centre+side*half,data['shoulder_height_m']),
                     (centre,data['tip_height_m']),
                     (centre+side*half,data['tip_height_m'])]
            area=sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(polygon,polygon[1:]+polygon[:1]))
            if area<0: polygon.reverse()
            pieces.append(kit.prism(polygon,.30))
    return kit.translated(kit.merge_all(pieces),(0,data['wall_centre_y_m'],0))


def tower_lancet_geometry(centre, cylinder, between):
    """One donor instance: glass and complete attached stone frame."""
    data=TOWER_LANCET;half=data['clear_width_m']/2
    profile=[(centre-half,data['sill_height_m']),
             (centre+half,data['sill_height_m']),
             (centre+half,data['shoulder_height_m']),
             (centre,data['tip_height_m']),
             (centre-half,data['shoulder_height_m'])]
    glass=kit.translated(kit.prism(profile,.018),(0,data['glass_plane_y_m'],0))
    plane=data['frame_plane_y_m'];half=data['frame_half_width_m']
    bottom,shoulder,tip=(data[key] for key in
        ('frame_sill_height_m','frame_shoulder_height_m','frame_tip_height_m'))
    frame=[]
    for side in (-1,1):
        frame.append(between((centre+side*half,plane,bottom),
                             (centre+side*half,plane,shoulder),data['frame_radius_m'],10))
        frame.append(between((centre+side*half,plane,shoulder),
                             (centre,plane,tip),data['frame_radius_m'],10))
    frame.append(between((centre,plane,data['sill_height_m']),
                         (centre,plane,data['shoulder_height_m']),.025,8))
    frame.append(kit.chamfered_box((centre,-12.07,bottom),(.90,.12,.10),.007))
    return glass,kit.merge_all(frame)

def rose_glass_geometry(cylinder):
    # Positive-handed quarter turn makes the cylinder axis normal to the
    # west facade; no reflection or reversed face winding is introduced.
    vertices,faces=cylinder((0,0,0),ROSE_RADIUS,.025,32)
    return ([(x,ROSE_CENTER[1]-z,ROSE_CENTER[2]+y) for x,y,z in vertices],faces)

def geometry(api):
    """Return material-grouped passive geometry plus audited fixture solids."""
    groups = {}
    audit = {}
    box = kit.chamfered_box
    move = kit.translated
    merge = kit.merge_all
    cyl, between = api['cylinder'], api['between']
    slope, cross = api['sloped_slab'], api['cross_geometry']
    def add(name, slot, role, shape):
        if name not in groups: groups[name] = [slot,role,[]]
        groups[name][2].append(shape)
    def record(name, shapes): audit[name] = shapes
    def wall(x, y0, y1, height, holes=()):
        g = kit.wall_run(y1-y0,height,.30,
            [kit.Opening(d-(y0+y1)/2,WINDOW_WIDTH,WINDOW_HEAD,WINDOW_SILL) for d in holes], .007)
        return move(kit.rotated_z(g,90),(x,(y0+y1)/2,0))
    def polygon_slab(points, low, high):
        points=list(points)
        if sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(points,points[1:]+points[:1])) < 0:
            points.reverse()
        n=len(points)
        vertices=[(x,y,z) for z in (low,high) for x,y in points]
        faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]
        faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        return vertices,faces
    apse_outline=[(-3.9,9.55),(-3.6,10.8),(-2.45,11.65),(0,11.8),
                  (2.45,11.65),(3.6,10.8),(3.9,9.55)]
    # The ground outline follows the broad aisles and narrower western porch;
    # the east sanctuary follows the polygonal apse, never the tower AABB.
    floors = [box((0,.5,-.09),(11.5,18.1,.18),.012),
              box((0,-10.325,-.09),(9.2,3.55,.18),.012),
              polygon_slab(apse_outline,-.18,0)]
    add('INT_Floor','Floor','metric_stone_floor_and_sanctuary',merge(floors))
    walls = [wall(s*5.9,-8.55,9.55,3.65,WINDOW_DEPTHS) for s in (-1,1)]
    walls += [wall(s*4.75,-12.1,-8.55,6.2) for s in (-1,1)]
    # Door and front masonry use the same clear opening as the City export.
    walls.append(rose_facade_geometry())
    # Narrow west volume opens into both side aisles without a closing wall.
    for sign in (-1,1):
        walls.append(box((sign*5.325,-8.55,1.825),(1.15,.30,3.65),.007))
        walls.append(slope(sign*4 if sign>0 else -5.95,4.35 if sign>0 else 3.65,
            sign*5.95 if sign>0 else -4,3.65 if sign>0 else 4.35,-8.55,9.55,.12))
        walls.append(box((sign*4.0,.5,4.625),(.16,18.1,.55),.006))
    # A finite central pocket clears the upper rose. Its sides and east
    # closure join both the lower side ceiling and the existing vault cap.
    for side in (-1,1):
        walls.append(box((side*3.35,-10.325,6.26),(3.1,3.55,.12),.007))
        walls.append(box((side*NARTHEX_BAY_HALF_WIDTH,-10.325,6.44),(.12,3.55,.48),.005))
    walls.append(box((0,-10.325,NARTHEX_BAY_CEILING+.06),(3.6,3.55,.12),.007))
    walls.append(box((0,-8.55,6.44),(3.6,.18,.48),.005))
    # Back is inaccessible beyond the communion rail. Its roof still seals
    # the volume rather than allowing a directional to enter over the altar.
    walls.append(polygon_slab(apse_outline,4.72,4.84))
    apse=[(-5.9,9.55),(-3.9,9.55),(-3.6,10.8),(-2.45,11.65),(0,11.8),(2.45,11.65),(3.6,10.8),(3.9,9.55),(5.9,9.55)]
    for a,b in zip(apse,apse[1:]):
        dx,dy=b[0]-a[0],b[1]-a[1]
        height=3.65 if abs(a[0])>4 or abs(b[0])>4 else 4.8
        g=kit.rotated_z(box((0,0,height/2),(math.hypot(dx,dy),.22,height),.007),math.degrees(math.atan2(dy,dx)))
        walls.append(move(g,((a[0]+b[0])/2,(a[1]+b[1])/2,0)))
    # Clip each end closure to the actual vault profile. A rectangular
    # gable would put its top corners OUTSIDE the exterior sloping roof.
    for y,base in [(-8.55,6.2),(9.55,5.0)]:
        section=SHELL['nave_vault_section_xh_m']
        clipped=[]
        for a,b in zip(section,section[1:]):
            if a[1] >= base: clipped.append(tuple(a))
            if (a[1]-base)*(b[1]-base) < 0:
                t=(base-a[1])/(b[1]-a[1])
                clipped.append((a[0]+(b[0]-a[0])*t,base))
        if section[-1][1] >= base: clipped.append(tuple(section[-1]))
        if clipped:
            profile=[(clipped[0][0],base),*clipped,(clipped[-1][0],base)]
            # Eliminate coincident section endpoints before extrusion.
            unique=[]
            for point in profile:
                if not unique or point!=unique[-1]: unique.append(point)
            if unique[0]==unique[-1]: unique.pop()
            walls.append(move(kit.prism(list(reversed(unique)),.18),(0,y,0)))
    add('INT_PlasteredShell','Plaster','stepped_narthex_aisle_and_polygonal_apse_shell',merge(walls))
    door=move(kit.panelled_leaf(DOOR_WIDTH,DOOR_HEIGHT,.10,panels=3,stile=.09),(-DOOR_WIDTH/2,DOOR_DEPTH,0))
    add('INT_WestDoor','Wood','panelled_west_exit_door',door)
    section=SHELL['nave_vault_section_xh_m']
    vault=[slope(a[0],a[1],b[0],b[1],-8.55,9.55,.15) for a,b in zip(section,section[1:])]
    add('INT_RibbedVault','Mural','solid_pointed_metric_vault',merge(vault))
    ribs=[]
    for depth in (-8.45,-5.5,-3.5,-2.2,1.1,4.0,4.4,7.7,9.45):
        for a,b in zip(section,section[1:]):
            ribs.append(between((a[0],depth,a[1]-.045),(b[0],depth,b[1]-.045),.055,8))
    ribs.append(between((0,-8.5,7.35),(0,9.5,7.35),.055,8))
    add('INT_VaultRibs','Stone','gothic_vault_ribs_and_relieved_courses',merge(ribs))
    piers=[]
    for x,z in LAYOUT[0]['centers_xz']:
        shape=kit.merge(cyl((x,z,.14),.42,.28,16),cyl((x,z,2.45),.27,4.34,16),cyl((x,z,4.75),.42,.30,16))
        piers.append(shape)
    record('nave_piers',piers)
    add('INT_NavePiers','Plaster','four_metric_nave_piers',merge(piers))
    capitals=[box((x,z,4.69),(.78,.78,.08),.015) for x,z in LAYOUT[0]['centers_xz']]
    capitals += [box(item['centre_xyz'],item['size_xyz'],.010) for item in OVERHEAD_BEARINGS]
    add('INT_PierCapitals','Stone','pier_capitals_and_overhead_vault_bearings',merge(capitals))
    pews=[]
    for x,z in [(x,z) for z in PEW_ROWS for x in PEW_XS]:
        pieces=[box((x,z,.46),(2.2,.68,.12),.013),box((x,z-.27,.80),(2.2,.14,.50),.012)]
        for dx in (-.94,.94):
            pieces += [box((x+dx,z,.23),(.16,.54,.46),.012),box((x+dx,z-.27,.53),(.18,.18,.96),.012)]
        pews.append(merge(pieces))
    record('pew_halves',pews)
    add('INT_Pews','Wood','twelve_human_size_pew_halves',merge(pews))
    confessionals=[]
    for x,z in LAYOUT[6]['centers_xz']:
        shape=kit.merge(box((x,z,1.19),(1.4,1.5,2.38),.016),
            move(kit.panelled_leaf(1.10,2.10,.08,3,stile=.075), (x-.55,z-.70,.06)),
            box((x,z,2.415),(1.4,1.5,.07),.014))
        confessionals.append(shape)
    record('confessionals',confessionals)
    add('INT_Confessionals','Wood','paired_closed_panelled_confessionals',merge(confessionals))
    altar=kit.merge(box((0,9.2,.90),(2.3,1.3,.20),.02),box((0,9.2,.40),(1.8,.94,.80),.015))
    font=kit.merge(cyl((-3.5,-8.8,.12),.55,.24,20),cyl((-3.5,-8.8,.53),.18,.62,16),
        cyl((-3.5,-8.8,.95),.48,.30,24))
    record('altar_table',[altar]);record('baptismal_font',[font])
    add('INT_StoneFurnishings','Stone','baptismal_font_and_altar_table',kit.merge(altar,font))
    add('INT_AltarCloth','Textile','altar_frontal',box((0,8.54,.77),(1.8,.022,.38),.004))
    stands=[];wax=[]
    for x,z in LAYOUT[7]['centers_xz']:
        stand=kit.merge(cyl((x,z,.10),.29,.20,16),cyl((x,z,.55),.055,.90,12),cyl((x,z,1.02),.34,.08,20))
        candles=[]
        for i in range(16):
            ring=i%3;angle=math.tau*i/16+ring*.31;r=.28*(.3+ring*.32)
            candles.append(cyl((x+math.cos(angle)*r,z+math.sin(angle)*r,1.135),.022,.15,8))
        stands.append(stand);wax.extend(candles)
    record('votive_stands',[kit.merge(g,*wax[i*16:(i+1)*16]) for i,g in enumerate(stands)])
    add('INT_VotiveStands','Gold','paired_votive_candle_stands',merge(stands))
    rail=[box((0,8,.12),(11.5,.28,.24),.008),box((0,8,.80),(11.5,.16,.10),.008)]
    rail += [cyl((x,8,.49),.032,.55,8) for x in [i*.48 for i in range(-11,12)]]
    rail=merge(rail);record('communion_rail',[rail]);add('INT_CommunionRail','Gold','closed_communion_rail_and_center_gate',rail)
    # Raised stiles and a stepped cornice make the existing reredos a
    # joined piece of woodwork. Every layer stays in its declared envelope;
    # the upper rail remains below the cross arms, leaving their silhouette.
    high_parts=[box((0,10.45,1.0),(3.3,1.15,2.0),.015),
                box((0,10.79,2.65),(2.8,.24,2.30),.012)]
    for side in (-1,1):
        high_parts += [box((side*1.40,10.63,2.64),(.14,.13,2.26),.009),
                       box((side*1.40,10.55,2.64),(.075,.05,2.10),.006)]
    high_parts += [box((0,10.62,3.72),(3.00,.16,.16),.012),
                   box((0,10.55,3.63),(2.88,.07,.08),.006),
                   box((0,10.63,1.57),(2.92,.13,.14),.009),
                   # Rear mounting pad joins the cross to the reredos.
                   box((0,10.64,3.52),(.12,.12,.18),.005)]
    high=merge(high_parts)
    tab=kit.merge(box((0,10.35,2.1),(.72,.42,.84),.012),api['cone']((0,10.35,2.63),.46,.04,.24,8))
    record('high_altar',[kit.merge(high,tab)])
    add('INT_HighAltar','Wood','high_altar_reredos',high);add('INT_Tabernacle','Gold','gilded_tabernacle',tab)
    crucifix=kit.merge(box((0,10.53,3.55),(.14,.18,2.3),.008),box((0,10.53,3.94),(1.75,.18,.14),.008))
    record('crucifix',[crucifix]);add('INT_Crucifix','Wood','sanctuary_latin_crucifix',crucifix)
    stations=[box((side*5.72,z,2.75),(.045,.62,.84),.006) for side in (-1,1) for z in STATION_DEPTHS]
    add('INT_StationsOfCross','SacredArt','fourteen_stations_of_cross',merge(stations))
    loft=box((0,-10.75,3.11),(8.7,2.4,.22),.012)
    supports=[box((x,-10.3,1.5),(.22,.22,3.0),.012) for x in (-4.1,-3.25,3.25,4.1)]
    record('choir_loft',[loft]);record('choir_loft_supports',supports)
    organ=box((0,-10.3,3.82),(4.8,.9,1.20),.014)
    choir=[loft,*supports,organ,box((0,-9.58,3.55),(8.7,.12,.66),.009)]
    add('INT_ChoirAndOrganCase','Wood','choir_loft_and_pipe_organ_case',merge(choir))
    pipes=[]
    for i in range(19):
        height=1.0+(1-abs(i-9)/9)*1.7
        pipes.append(cyl(((i-9)*.23,-10.3,3.22+height/2),.065,height,10))
    record('pipe_organ',[kit.merge(organ,*pipes)])
    add('INT_OrganPipes','Iron','pipe_organ_rank',merge(pipes))
    frames=[]
    for side,label in [(-1,'North'),(1,'South')]:
        panes=[]
        for depth in WINDOW_DEPTHS:
            panes.append(box((side*6.04,depth,WINDOW_HEIGHT),(.025,WINDOW_WIDTH-.02,WINDOW_CLEAR_HEIGHT-.02),.003))
            x=side*5.72;lo=WINDOW_SILL;hi=WINDOW_HEAD-.25;half=WINDOW_WIDTH/2+.07
            for a,b in [((x,depth-half,lo),(x,depth-half,hi)),((x,depth+half,lo),(x,depth+half,hi)),
                        ((x,depth-half,hi),(x,depth,WINDOW_HEAD+.10)),((x,depth+half,hi),(x,depth,WINDOW_HEAD+.10)),
                        ((x,depth,lo),(x,depth,WINDOW_HEAD))]:frames.append(between(a,b,.035,8))
        if side == -1:
            panes.append(rose_glass_geometry(cyl))
        add('INT_StainedGlass'+label,'GlassCold',label.lower()+'_aisle_stained_glass',merge(panes))
    rose_y=-11.76
    frames += [api['vertical_torus']((0,rose_y,4.95),1.5675,.07,32,8),
               api['vertical_torus']((0,rose_y,4.95),.7425,.045,24,8)]
    for index in range(16):
        angle=math.tau*index/16
        frames.append(between((math.cos(angle)*.22,rose_y,4.95+math.sin(angle)*.22),
            (math.cos(angle)*1.496,rose_y,4.95+math.sin(angle)*1.496),.033,8))
    add('INT_WindowTracery','Stone','interior_lancet_tracery',merge(frames))
    # Passive fixtures are authored once; only real lights and flame effects
    # are instantiated by Unity, attached to the exact semantic contacts.
    metal=[]
    for name,(x,z,h) in SCONCES.items():
        sign=1 if x>0 else -1
        metal += [box((x+sign*.13,z,h-.23),(.045,.18,.42),.006),
                  box((x+sign*.04,z,h-.27),(.28,.055,.055),.006),cyl((x,z,h-.2375),.07,.035,12)]
        wax.append(cyl((x,z,h-.14),.025,.16,10))
    for name,(x,z,h) in CORONAS.items():
        radius=.46 if name=='Narthex' else .65
        chain_top=3.0 if name=='Narthex' else 7.3
        metal.append(cyl((x,z,(h+chain_top)/2),.012,chain_top-h,8))
        for i in range(8):
            a=math.tau*i/8;b=math.tau*(i+1)/8
            p=(radius*math.cos(a),z+radius*math.sin(a),h-.02)
            q=(radius*math.cos(b),z+radius*math.sin(b),h-.02)
            metal.append(between(p,q,.025,8))
            metal.append(between((0,z,h+.2),p,.018,8))
            wax.append(cyl((p[0],p[1],h+.07),.025,.18,8))
    # Candles visible at every remaining warm light anchor.
    for key in ['Warm.HighAltar.North','Warm.HighAltar.South','Warm.Altar','Warm.Font']:
        x,z,h=ANCHORS['ANCHOR_Interior.'+key][0]
        metal.append(cyl((x,z,h-.26),.065,.04,10));wax.append(cyl((x,z,h-.15),.025,.18,8))
    add('INT_LightingHardware','Iron','authored_sconces_and_hung_coronas',merge(metal))
    add('INT_VotiveCandles','Plaster','wax_at_all_practical_contacts',merge(wax))
    return {name:(values[0],values[1],merge(values[2])) for name,values in groups.items()},audit


def exterior_roof_height(x, y):
    """Top of the union of the placed exterior building roof/body solids."""
    x,y=round(x,5),round(y,5)
    tops=[]
    if abs(x)<=4.95 and -11.99<=y<=-7.81: tops.append(6.53125)
    if abs(x)<=5.225 and -12.1<=y<=-7.7:
        tops.append(8.855-(8.855-6.545)*abs(x)/5.225)
    if abs(x)<=4.235 and -9.295<=y<=10.835: tops.append(5.37625)
    if abs(x)<=4.565 and -9.515<=y<=10.835:
        tops.append(7.81-(7.81-5.335)*abs(x)/4.565)
    if abs(x)<=6.105 and -8.8<=y<=9.9: tops.append(3.94625)
    if 3.82<=abs(x)<=6.14 and -8.8825<=y<=9.9825:
        tops.append(4.1525+(4.9775-abs(x))*math.tan(math.radians(18))+.09253)
    if (x/4.0975)**2+((y-10.45)/1.65)**2<=1.00001: tops.append(4.82625)
    r=math.hypot(x,(y-10.45)/.40)
    profile=[(4.18,4.7025),(4.125,4.8675),(3.74,5.1975),(3.135,5.555),
             (2.31,5.885),(1.32,6.16),(.11,6.325)]
    if r<=profile[0][0]:
        for (r0,h0),(r1,h1) in zip(profile,profile[1:]):
            if r>=r1:
                tops.append(h0+(h1-h0)*(r0-r)/(r0-r1));break
        else: tops.append(profile[-1][1])
    # Door leaves/front dressing can stand just proud of the main wall.
    if abs(x)<=.96 and -12.21<=y<=-11.9: tops.append(2.7)
    return max(tops,default=-1.0)


def candle_contacts():
    """Ordered wick planes used to verify the exported passive wax solids."""
    contacts=[]
    for x,z in LAYOUT[7]['centers_xz']:
        for i in range(16):
            ring=i%3;angle=math.tau*i/16+ring*.31;r=.28*(.3+ring*.32)
            contacts.append((x+math.cos(angle)*r,z+math.sin(angle)*r,1.21))
    contacts += [(x,z,h-.06) for x,z,h in SCONCES.values()]
    for name,(x,z,h) in CORONAS.items():
        radius=.46 if name=='Narthex' else .65
        contacts += [(x+radius*math.cos(math.tau*i/8),z+radius*math.sin(math.tau*i/8),h+.16)
                     for i in range(8)]
    contacts += [(ANCHORS['ANCHOR_Interior.'+key][0][0],
                  ANCHORS['ANCHOR_Interior.'+key][0][1],
                  ANCHORS['ANCHOR_Interior.'+key][0][2]-.06)
                 for key in ('Warm.HighAltar.North','Warm.HighAltar.South','Warm.Altar','Warm.Font')]
    return contacts
