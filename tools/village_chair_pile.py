"""Fixed-metre dense discarded-chair mound, reusing the Mountain Road donor."""
from __future__ import annotations
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
sys.dont_write_bytecode = True
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bar_parts as bp
import interior_kit as kit

KIND = "DiscardedChairPile"
WOOD_TINTS = ((.35,.335,.295,1),(.285,.285,.255,1),
              (.395,.365,.310,1),(.305,.300,.275,1))
PLACEMENTS = (
    (-2.13, -1.04, (12, 34, 88)),
    (-1.13, -1.16, (-86, -12, 8)),
    (0, -1.21, (177, 18, 2)),
    (1.08, -1.1, (72, -22, 11)),
    (2.12, -0.89, (-8, -28, -87)),
    (-2.05, 0.9, (91, 37, 7)),
    (-1.01, 1.11, (8, 151, 93)),
    (0.07, 1.15, (180, -17, -6)),
    (1.16, 1.05, (-77, 143, -9)),
    (2.12, 0.85, (14, 32, 87)),
    (-2.2, -0.03, (173, 80, 10)),
    (2.21, -0.04, (85, 102, 6)),
    (-0.64, -0.29, (86, 28, -7)),
    (0.59, -0.26, (87, -44, 8)),
    (-0.55, 0.48, (4, 118, -89)),
    (0.69, 0.53, (-84, -11, 7)),
    (-1.22, -0.39, (-73, 62, 17)),
    (-0.12, -0.64, (21, -23, 74)),
    (1.04, -0.41, (-102, 62, -17)),
    (-1.15, 0.44, (12, 36, -78)),
    (0.01, 0.38, (76, -51, -18)),
    (1.35, 0.98, (-87, 138, 8)),
    (-1.49, -0.12, (85, -32, 18)),
    (-0.5, -1.25, (14, 114, 75)),
    (-1.85, 0.82, (90, 28, 0)),
    (-0.35, 0.97, (-87, -24, -12)),
)
FRAGMENTS = (
    ('SeatApron', -1.4, -0.84, (9, 21, -8)),
    ('SlattedBack', -0.46, -0.91, (86, -33, 8)),
    ('TwoLegFrame', 0.48, -0.87, (178, 29, 6)),
    ('SeatApron', 1.45, -0.83, (-8, -23, 11)),
    ('SlattedBack', -1.45, 0.04, (78, 28, 12)),
    ('SeatApron', -0.47, 0.03, (7, -41, -6)),
    ('SlattedBack', 0.51, -0.02, (103, 32, -9)),
    ('TwoLegFrame', 1.44, 0.06, (165, -31, -11)),
    ('SeatApron', -1.42, 0.85, (-12, -21, 6)),
    ('SlattedBack', -0.47, 0.91, (96, 31, -9)),
    ('SeatApron', 0.48, 0.88, (11, -27, -12)),
    ('SlattedBack', 1.43, 0.89, (83, 17, 10)),
    ('SeatApron', -0.92, -0.47, (17, 24, -12)),
    ('SlattedBack', 0.01, -0.41, (68, -34, 17)),
    ('SeatApron', 0.9, -0.4, (-16, 31, 8)),
    ('TwoLegFrame', -0.92, 0.46, (173, -26, 13)),
    ('SlattedBack', 0.04, 0.44, (110, 24, -13)),
    ('SeatApron', 0.93, 0.47, (14, -26, 11)),
)

def _donor_geometry():
    name = "_village_chair_donor"
    module = sys.modules.get(name)
    if module is None:
        path = Path(__file__).with_name("build-mountain-road-misc-3d-model.py")
        spec = importlib.util.spec_from_file_location(name, path)
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        spec.loader.exec_module(module)
    source = module.build_abandoned_chair().parts[0].geometry
    # Same descriptor proportions as the old roadside chair, source floor -0.5.
    scaled = ([(x * .82, y * .82, (z + .5) * 1.1)
               for x, y, z in source[0]], source[1])
    result = bp.to_source(scaled)  # Axis swap is its own inverse, including winding.
    assert kit.triangle_count(result) == 396, "Donor chair contract changed"
    assert bp.signed_volume(result) > 0, "Inward donor chair"
    return result

def _donor_fragments():
    """Extract intact connected components from the exact source chair mesh."""
    components = _components(_donor_geometry())
    # The donor's component order is seat planks, apron, four legs, back posts,
    # cross rails and splats. Fail explicitly if its structural contract changes.
    assert len(components) == 15, "Donor chair components changed"
    result = {}
    for name, selection in (("SeatApron", range(5)), ("SlattedBack", range(9, 15)),
                            ("TwoLegFrame", (0, 1, 2, 3, 4, 5, 7))):
        geometry = kit.merge_all(components[i] for i in selection)
        low, high = kit.bounds(geometry)
        center = tuple((a + b) * -.5 for a, b in zip(low, high))
        result[name] = kit.translated(geometry, center)
        assert bp.signed_volume(result[name]) > 0, "Inward donor fragment"
    return result

def _components(geometry):
    vertices, faces = geometry
    adjacency = {index: set() for index in range(len(vertices))}
    for face in faces:
        for index in face:
            adjacency[index].update(face)
    remaining = set(adjacency)
    components = []
    while remaining:
        todo = [min(remaining)]
        indices = set()
        while todo:
            index = todo.pop()
            if index in indices:
                continue
            indices.add(index)
            todo.extend(adjacency[index] - indices)
        remaining -= indices
        ordered = sorted(indices)
        remap = {old: new for new, old in enumerate(ordered)}
        components.append(([vertices[i] for i in ordered],
                           [tuple(remap[i] for i in face) for face in faces
                            if face[0] in indices]))
    return components

def _edge_samples(geometry):
    """Sample thin rails too, so a leg cannot slip through a supported seat."""
    vertices, faces = geometry
    result = list(vertices)
    edges = set()
    for face in faces:
        for a, b in zip(face, face[1:] + face[:1]):
            edge = (min(a, b), max(a, b))
            if edge in edges:
                continue
            edges.add(edge)
            start, end = Vector(vertices[a]), Vector(vertices[b])
            steps = max(1, math.ceil((end - start).length / .032))
            result.extend(tuple(start.lerp(end, step / steps))
                          for step in range(1, steps))
    return result

def _snow_on_seat(geometry, seat):
    """Thin snow follows the actual supporting slat, without filling its gaps."""
    low, high = kit.bounds(geometry)
    tree = BVHTree.FromPolygons(*geometry, all_triangles=False)
    point, normal, face_index, _ = tree.ray_cast(Vector((seat[0], high[1] + 1, seat[2])),
                                                Vector((0, -1, 0)), high[1] - low[1] + 2)
    if point is None or normal.y < .55:
        return None
    face = [Vector(geometry[0][i]) for i in geometry[1][face_index]]
    center = sum(face, Vector()) / len(face)
    # Inset both rings into the real board face: no circular snow hovering off
    # a narrow leg, no broad white shell concealing the recognizable chair.
    bottom = [tuple(center + (v - center) * .84 + Vector((0, .001, 0))) for v in face]
    top = [tuple(center + (v - center) * (.62 + .035 * (i % 2)) + Vector((0, .024, 0)))
           for i, v in enumerate(face)]
    count = len(face)
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces.extend((i, (i + 1) % count, (i + 1) % count + count, i + count)
                 for i in range(count))
    result = bottom + top, faces
    assert bp.signed_volume(result) > 0, "Inward snow cap"
    return result


def _rest_height(geometry, previous):
    low, high = kit.bounds(geometry)
    height = -low[1]
    if not previous:
        return height
    prior = kit.merge_all(previous)
    prior_tree = BVHTree.FromPolygons(*prior, all_triangles=False)
    new_tree = BVHTree.FromPolygons(*geometry, all_triangles=False)
    prior_high = kit.bounds(prior)[1][1]
    for x,y,z in _edge_samples(geometry):
        point,_,_,_ = prior_tree.ray_cast(Vector((x,prior_high+1,z)),Vector((0,-1,0)),prior_high+2)
        if point is not None:
            height=max(height,point.y-y)
    for x,y,z in _edge_samples(prior):
        if not (low[0]<=x<=high[0] and low[2]<=z<=high[2]):
            continue
        point,_,_,_ = new_tree.ray_cast(Vector((x,low[1]-1,z)),Vector((0,1,0)),high[1]-low[1]+2)
        if point is not None:
            height=max(height,y-point.y)
    return height


def create_chairs():
    donor=kit.translated(_donor_geometry(),(0,-.50,0))
    result=[]
    for x,z,rotation in PLACEMENTS:
        chair=kit.translated(bp.u_rotated(donor,rotation),(x,0,z))
        chair=kit.translated(chair,(0,_rest_height(chair,result),0))
        assert kit.bounds(chair)[0][1]>=-1e-6 and bp.signed_volume(chair)>0
        result.append(chair)
    return result


def _fit_fragment(geometry,previous,x,z,rotation):
    obstacle=BVHTree.FromPolygons(*kit.merge_all(previous),all_triangles=False)
    rotated=bp.u_rotated(geometry,rotation)
    low,high=kit.bounds(rotated)
    for dx,dz in ((0,0),(.13,0),(-.13,0),(0,.13),(0,-.13),(.13,.13),(-.13,-.13),(.13,-.13),(-.13,.13)):
        base=kit.translated(rotated,(x+dx,-low[1],z+dz))
        def collision(height):
            placed=kit.translated(base,(0,height,0))
            return bool(BVHTree.FromPolygons(*placed,all_triangles=False).overlap(obstacle)),placed
        for step in range(46):
            height=step*.03
            if height+high[1]-low[1]>1.70:
                break
            intersects,placed=collision(height)
            if intersects:
                continue
            if step:
                bottom,top=height-.03,height
                for _ in range(10):
                    middle=(bottom+top)*.5
                    if collision(middle)[0]: bottom=middle
                    else: top=middle
                placed=collision(top+.0001)[1]
            return placed
    raise AssertionError(("No supported chair-fragment cavity",x,z,rotation))


def create_fragments(chairs):
    donors=_donor_fragments()
    fragments=[]
    for name,x,z,rotation in FRAGMENTS:
        geometry=_fit_fragment(donors[name],chairs+fragments,x,z,rotation)
        assert bp.signed_volume(geometry)>0
        fragments.append(geometry)
    assert sum(kit.triangle_count(g) for g in fragments)<=3000
    return fragments


def create_supports():
    """Broken furniture bridges under the four exposed upper chairs.

    Each frame retains its seat/apron and four original splayed legs. Their
    unequal angles bridge existing lower timber; nothing repeats as a column.
    """
    lower = kit.merge_all(_components(_donor_geometry())[:9])
    lo, hi = kit.bounds(lower)
    lower = kit.translated(lower, tuple(-(a+b)*.5 for a,b in zip(lo,hi)))
    backs = _donor_fragments()["SlattedBack"]
    result = []
    for position, rotation in (
        ((1.00,1.43,-.33),(12,-24,18)),
        ((-.02,1.50,.42),(-15,34,-12)),
        ((-1.6189904,1.6634451,.6051089),(9.3807141,-18.9541284,-39.6561978)),
        ((-.4246282,1.7978928,.8939705),(-6.840472,25.5867544,17.1750827)),
    ):
        result.append(kit.translated(bp.u_rotated(lower,rotation),position))
    for position, rotation in (
        ((1.00,1.03,-.78),(-35,-15,12)),
        ((-.50,1.87,.73),(18,37,-42)),
    ):
        result.append(kit.translated(bp.u_rotated(backs,rotation),position))
    assert sum(kit.triangle_count(g) for g in result)==1264
    assert all(bp.signed_volume(g)>0 for g in result)
    return result


def chair_pile(add):
    chairs=create_chairs()
    fragments=create_fragments(chairs)
    supports=create_supports()
    lower_tree=BVHTree.FromPolygons(*kit.merge_all(chairs+fragments),all_triangles=False)
    for index,frame in enumerate(supports[:4]):
        # The first seven vertices of each donor leg form its foot ring.
        feet=[sum((Vector(v) for v in leg[0][:7]),Vector())/7
              for leg in _components(frame)[5:9]]
        distances=[lower_tree.find_nearest(foot)[3] for foot in feet]
        assert sum(distance<=.04 for distance in distances)>=2, ("Unsupported frame feet",index+1,distances)
    low,high=kit.bounds(kit.merge_all(chairs))
    assert len(chairs)==26
    assert 5.0<=high[0]-low[0]<=6.0 and 3.0<=high[2]-low[2]<=4.1
    assert abs(low[1])<1e-6 and 2.7<=high[1]<=2.8
    snow=[]
    for index,chair in enumerate(chairs):
        add(KIND,f"WeatheredChair{index+1:02d}",chair,"Timber",True,WOOD_TINTS[index%4])
        if index<12 and index%2==0 or index>=22:
            x,z,rotation=PLACEMENTS[index]
            seat=bp.u_rotated(([(0,-.03,0)],[]),rotation)[0][0]
            patch=_snow_on_seat(chair,(x+seat[0],0,z+seat[2]))
            if patch is not None: snow.append(patch)
    if snow:
        add(KIND,"SnowInSeats",kit.merge_all(snow),"WindSnow",False)
    for index,((name,_,_,_),geometry) in enumerate(zip(FRAGMENTS,fragments)):
        lo,hi=kit.bounds(geometry)
        assert all(a>=b-1e-6 for a,b in zip(lo,low)) and all(a<=b+1e-6 for a,b in zip(hi,high))
        add(KIND,f"Discarded{name}{index+1:02d}",geometry,"Timber",True,WOOD_TINTS[(index+2)%4])
    for index,geometry in enumerate(supports):
        lo,hi=kit.bounds(geometry)
        assert all(a>=b-1e-6 for a,b in zip(lo,low)) and all(a<=b+1e-6 for a,b in zip(hi,high))
        add(KIND,f"SupportingDiscardedFrame{index+1:02d}",geometry,"Timber",True,WOOD_TINTS[(index+1)%4])
    assert sum(kit.triangle_count(g) for g in chairs+snow+fragments+supports)<=14492


if __name__=="__main__":
    parts=[]
    chair_pile(lambda *args:parts.append(args))
    first=json.dumps(parts,separators=(",",":"))
    second=[]
    chair_pile(lambda *args:second.append(args))
    assert first==json.dumps(second,separators=(",",":"))
    geometry=kit.merge_all(p[2] for p in parts)
    print("CHAIR PILE OK:",kit.bounds(geometry),"triangles",kit.triangle_count(geometry),
          "signature",hashlib.sha256(first.encode()).hexdigest())
