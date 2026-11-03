"""Normal cashier's tailored surfaces; the retained Watcher is not a caller.

Source metres and named renderers remain those of the shared service rig. Face
features are deliberately separate solids: this clerk keeps his raised brows
and protruding eyes instead of adopting the normal crowd's painted face.
"""
from __future__ import annotations

import math
from mathutils import Vector

import npc_detail_geometry as detail


def _bounds(geometry):
    points, _ = geometry
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return lo, hi, (lo + hi) * .5


def _path(points, radius, sides=6):
    result = []
    for start, end in zip(points, points[1:]):
        result.append(detail.segment(start, end, ((0, radius, .72),
                                                   (1, radius, .72)), sides))
    return result


def _head(base):
    # A supported jaw, cheekbones and temples; the front stays behind the
    # separate eyes and nose. The 1.75 m crown is still owned by the hair.
    stations = ((1.5196, .019, .030, -.016), (1.535, .040, .047, -.015),
                (1.555, .058, .058, -.020), (1.585, .070, .068, -.024),
                (1.615, .084, .075, -.026), (1.643, .08925, .079, -.028),
                (1.672, .087, .076, -.026), (1.700, .081, .067, -.021),
                (1.724, .067, .054, -.018), (1.740, .041, .035, -.017),
                (1.747, .002, .002, -.017))
    vertices, faces = detail.shell(stations, 20)
    skull = ([p + Vector((.006, 0, 0)) for p in vertices], faces)
    lower_lids = []
    for x, z, width in ((.04275, 1.65016, .034), (-.04545, 1.6476, .0367)):
        points = [(x + width * math.cos(a), -.108 - .005 * math.sin(a),
                   z - .028 * math.sin(a))
                  for a in (math.pi*.12, math.pi*.30, math.pi*.50,
                            math.pi*.70, math.pi*.88)]
        lower_lids.extend(_path(points, .0032))
    return base.combine_geometry(skull, *lower_lids)


def _vest_front(base, sign):
    rings = []
    for index, (z, rx, ry, cy) in enumerate((
            (.784, .156, .104, .008), (.812, .165, .109, .006),
            (.935, .165, .114, .001), (1.075, .164, .122, -.002),
            (1.205, .181, .124, -.003), (1.305, .184, .115, -.004),
            (1.367, .151, .094, -.009))):
        ring = []
        for inner in (False, True):
            for j in (range(9) if not inner else reversed(range(9))):
                u = j / 8
                extent = rx + .010
                x = sign * (.009 + u * (extent - .009))
                y = cy - (ry + .008) * math.sqrt(max(0, 1 - (x / extent)**2))
                fold = .0025 * math.sin(u * math.pi * 4 + index*.55)
                neckline = {4:.015, 5:.090, 6:.135}.get(index, 0) * (1-u)**1.4
                ring.append((x, y + (.006 if inner else -fold), z-neckline))
        rings.append(ring)
    panel = detail.loft(rings)
    # Welt pocket and front binding have thickness, following the curved
    # garment instead of sitting on a rectangular plaque in front of it.
    seams = []
    for z in (.922, 1.007):
        points = []
        for u in range(5):
            x = sign * (.049 + u*.021)
            y = .001 - .114 * math.sqrt(max(0, 1-(x/.168)**2)) - .006
            points.append((x, y, z + .0015*math.sin(u)))
        seams.extend(_path(points, .0023))
    binding = [(sign*.011, cy-ry-.003, z) for z, ry, cy in
               ((.792, .104, .008), (.94, .114, .001),
                (1.075, .122, -.002), (1.205, .124, -.003))]
    seams.extend(_path(binding, .0022))
    return base.combine_geometry(panel, *seams)


def _hands(base, side, thumb):
    sign = 1 if side == "L" else -1
    wrist = Vector((sign*.680, -.018, 1.075))
    axis = (Vector((sign*.755, -.022, 1.035))-wrist).normalized()
    across = (Vector((0, 1, 0))-axis*axis.y).normalized()
    depth = axis.cross(across).normalized() * (-1 if side == "L" else 1)
    def point(t, w=0, d=0):
        return wrist + axis*t + across*w + depth*d
    if thumb:
        return detail.segment(point(.016, -.022, .001), point(.063, -.047, .012),
            ((0,.012,.82), (.18,.013,.85), (.46,.0123,.86),
             (.64,.0106,.83), (.84,.0098,.80), (1,.007,.75)), 8)
    palm = detail.loft([[point(t, math.cos(math.tau*j/12)*width,
                                math.sin(math.tau*j/12)*thickness)
                        for j in range(12)] for t,width,thickness in
                       ((-.009,.020,.013),(.009,.025,.015),(.029,.031,.017),
                        (.047,.033,.018),(.060,.030,.014))])
    fingers = []
    for offset,length,radius in ((-.025,.041,.008),(-.008,.049,.0085),
                                 (.010,.045,.008),(.027,.034,.0068)):
        fingers.append(detail.segment(point(.053,offset),
            point(.053+length,offset,.006),
            ((0,radius,.84),(.18,radius*1.12,.88),(.40,radius,.84),
             (.56,radius*1.05,.86),(.76,radius*.88,.80),(1,radius*.58,.76)),8))
    return base.combine_geometry(palm,*fingers)


def geometry_for(base, name, geometry):
    side = name.rsplit(".", 1)[-1]
    sign = 1 if side == "L" else -1
    if name == "GEO_Head":
        return _head(base)
    if name == "GEO_Neck":
        return detail.shell(((1.398,.061,.057,-.010),(1.424,.054,.050,-.014),
            (1.460,.047,.044,-.019),(1.496,.044,.043,-.023),
            (1.525,.047,.044,-.025)),16)
    if name == "GEO_Torso":
        torso = detail.shell(((.790,.151,.091,.010),(.835,.163,.098,.008),
            (.930,.160,.102,.002),(1.06,.156,.110,-.002),
            (1.20,.172,.113,-.003),(1.29,.205,.102,-.004),
            (1.335,.173,.091,-.006),(1.375,.110,.076,-.009),
            (1.405,.078,.067,-.010)),20,folds=.012,fold_count=5)
        return torso
    if name.startswith("GEO_UpperArm."):
        shoulder=Vector((sign*.208,-.004 if side=="L" else .004,1.292))
        elbow=Vector((sign*.470,-.010,1.175))
        start=shoulder-(elbow-shoulder).normalized()*.040
        return detail.segment(start,elbow,
            ((0,.024,.94),(.07,.053,.94),(.17,.067,.94),(.31,.065,.94),
             (.46,.061,.94),(.63,.058,.94),(.81,.056,.94),(.94,.055,.94),
             (1,.055,.94)),16)
    if name.startswith("CLO_VestFront."):
        return _vest_front(base,sign)
    if name == "CLO_ShirtBib":
        # A narrow stitched shirt placket inside the V, not the old broad
        # rectangular plate floating ahead of the upper chest.
        return detail.loft([[(x,y+back,z) for x,back in
            ((-.015,0),(.015,0),(.015,.003),(-.015,.003))]
            for z,y in ((1.205,-.118),(1.255,-.116),(1.305,-.107),
                        (1.335,-.101),(1.372,-.087))])
    if name == "CLO_VestBack":
        rings=[]
        for z,rx,ry,cy in ((.787,.154,.106,.008),(.82,.164,.110,.006),
                (.96,.164,.111,.001),(1.10,.165,.119,-.002),
                (1.27,.183,.117,-.004),(1.365,.151,.094,-.009)):
            ring=[]
            for inner in (False,True):
                for j in (range(13) if not inner else reversed(range(13))):
                    a=math.pi*j/12
                    ring.append(((rx+.01)*math.cos(a),cy+(ry+.01-(.006 if inner else 0))*math.sin(a),z))
            rings.append(ring)
        return detail.loft(rings)
    if name.startswith("GEO_Hand."):
        return _hands(base,side,False)
    if name.startswith("GEO_Thumb."):
        return _hands(base,side,True)
    if name.startswith("FACE_EyeWhite."):
        lo,hi,c=_bounds(geometry)
        r=(hi-lo)*.5
        return detail.outward(base.make_ellipsoid(c,(r.x,.0195,r.z),16,8))
    if name.startswith("FACE_Pupil."):
        _,_,c=_bounds(geometry)
        c.y=-.1255 if side=="L" else -.1265
        return detail.outward(base.make_ellipsoid(c,(.010,.0055,.0125),12,6))
    if name.startswith("FACE_Brow."):
        lo,hi,c=_bounds(geometry)
        points=[(lo.x+(hi.x-lo.x)*t,c.y-.003,
                 c.z+.004*math.sin(t*math.pi)+(t-.5)*(.008 if side=="L" else .010))
                for t in (0,.25,.5,.75,1)]
        return base.combine_geometry(*_path(points,.0043,8))
    if name == "FACE_Nose":
        bridge=detail.segment((.010,-.108,1.640),(.009,-.135,1.599),
            ((0,.0075,.80),(.24,.009,.86),(.60,.011,.96),(1,.009,.80)),10)
        tip=detail.outward(base.make_ellipsoid((.009,-.133,1.600),(.013,.012,.011),12,6))
        alae=[detail.outward(base.make_ellipsoid((.009+s*.012,-.121,1.596),
                 (.008,.010,.008),10,5)) for s in (-1,1)]
        return base.combine_geometry(bridge,tip,*alae)
    if name == "FACE_Mouth":
        upper=[(-.019,-.101,1.568),(-.007,-.108,1.570),
               (.001,-.110,1.568),(.008,-.108,1.570),(.020,-.101,1.568)]
        lower=[(-.018,-.101,1.565),(-.008,-.107,1.562),
               (.008,-.107,1.562),(.019,-.101,1.565)]
        return base.combine_geometry(*_path(upper,.0022),*_path(lower,.0030))
    if name.startswith("FACE_Ear."):
        lo,hi,c=_bounds(geometry)
        r=(hi-lo)*.5
        lobe=detail.outward(base.make_ellipsoid(c,(r.x*.82,r.y*.72,r.z),12,6))
        rim=[(c.x+sign*r.x*.43,c.y-r.y*.55+r.y*.68*math.cos(a),
              c.z+r.z*.78*math.sin(a)) for a in [i*math.tau/12 for i in range(13)]]
        return base.combine_geometry(lobe,*_path(rim,.0030))
    if name.startswith("CLO_VestButton."):
        _,_,c=_bounds(geometry)
        return detail.outward(base.make_ellipsoid(c,(.0065,.003,.0065),10,5))
    return geometry


def finish_surfaces(result):
    """Smooth curved skin/glass, retaining cloth facets and hard sole edges."""
    for part in result.parts:
        name=part.obj.name
        smooth=name.startswith(("GEO_Head","GEO_Neck","FACE_","GEO_Hand.",
                                "GEO_Thumb.","GEO_Foot.","GEO_Torso","GEO_Pelvis",
                                "GEO_UpperArm.","GEO_Forearm.","GEO_Thigh.",
                                "GEO_Shin.","CLO_Vest"))
        if smooth:
            for polygon in part.obj.data.polygons:
                polygon.use_smooth=len(polygon.vertices)<=4


def assign_surface_uv(obj, region, atlas_size, prop_name):
    """Full-density front X/Z UVs and a continuous cylindrical skin island.

    The legacy shoe-oriented box mapper projects front faces through X/Y;
    that removes almost all vertical artwork from a shirt or a human face.
    """
    mesh=obj.data
    layer=mesh.uv_layers.new(name="UVMap")
    obj[prop_name]=region.name
    points=[obj.matrix_world@vertex.co for vertex in mesh.vertices]
    lo=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    for polygon in mesh.polygons:
        local=[]
        normal=obj.matrix_world.to_3x3()@polygon.normal
        for index in polygon.loop_indices:
            point=points[mesh.loops[index].vertex_index]
            if region.kind=="head":
                u=.5+math.atan2(point.x-.006,-(point.y+.028))/math.tau
                v=(point.z-1.5196)/(.2274)
            else:
                # Front/back keep height. End caps and the sides have their
                # own dominant plane, each using the whole padded cell.
                axis=max(range(3),key=lambda i:abs(normal[i]))
                a,b=((1,2) if axis==0 else (0,2) if axis==1 else (0,1))
                u=(point[a]-lo[a])/max(1e-6,hi[a]-lo[a])
                v=(point[b]-lo[b])/max(1e-6,hi[b]-lo[b])
            local.append([index,max(0,min(1,u)),max(0,min(1,v))])
        if region.kind=="head" and max(p[1] for p in local)-min(p[1] for p in local)>.5:
            for point in local:
                if point[1]<.5: point[1]=1
        for index,u,v in local:
            layer.data[index].uv=((region.x+3+u*(region.width-6))/atlas_size,
                                  (region.y+3+v*(region.height-6))/atlas_size)
    layer.active_render=True
