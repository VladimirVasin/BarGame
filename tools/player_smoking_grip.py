"""A finite cigarette grip on the original right-hand surfaces and skeleton."""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Vector
from mathutils.geometry import closest_point_on_tri, intersect_ray_tri

SHAPE = "SmokingGrip"
NAMES = ("GEO_Hand.R", "GEO_Thumb.R", *(f"GEO_Finger{i}.R" for i in range(4)))
RADIUS = .00325
FILTER_TO_GRIP = .0225
CENTRE_DISTAL = .081
CENTRE_PALMAR = .020
CENTRE_ACROSS = -.018
FINGER_JOINT_ANGLES = {0:(45.,80.,40.), 1:(10.,40.,80.), 2:(80.,65.,30.), 3:(85.,65.,30.)}
MINIMUM_PAD_OPPOSITION_DEGREES = 150.


def frame(result, scale, grip):
    wrist, distal, across, palm = grip._frame(result, "R")
    centre = wrist + (distal * CENTRE_DISTAL + palm * CENTRE_PALMAR + across * CENTRE_ACROSS) * scale
    return wrist, distal, across, palm, centre


def socket_pose(result, scale, grip, common):
    """Pose-only filter dock: canonical Hero/NPC socket rest frames stay equal."""
    _, _, across, _, centre = frame(result, scale, grip)
    socket = result.rig.data.bones["SOCKET_Cigarette.R"]
    old_axis = (socket.tail_local-socket.head_local).normalized()
    rotation = old_axis.rotation_difference(across) @ socket.matrix_local.to_quaternion()
    wanted = Matrix.Translation(centre-across*(FILTER_TO_GRIP*scale)) @ rotation.to_matrix().to_4x4()
    basis = socket.matrix_local.inverted() @ wanted
    return common.BonePose(rotation_degrees=tuple(math.degrees(value) for value in basis.to_quaternion().to_euler("XYZ")),
        location_m=tuple(value/scale for value in basis.translation))


def _finger_chain(wrist, distal, across, palm, index, scale):
    offset, length = ((-.023,.033),(-.007,.042),(.009,.039),(.024,.030))[index]
    start = wrist + (distal*.052+across*offset)*1.20*scale
    end = wrist + (distal*(.052+length)+across*offset+palm*.004)*1.20*scale
    return [start+(end-start)*fraction for fraction in (0.,.42,.80,1.)]


def _curl(chain, distal, palm, angles):
    points, angle = [chain[0]], 0.
    for degrees, start, end in zip(angles, chain, chain[1:]):
        angle += math.radians(degrees)
        points.append(points[-1]+(distal*math.cos(angle)+palm*math.sin(angle))*(end-start).length)
    return points


def _surface_gap(obj, points, centre, distal, palm, grip):
    projected = [Vector(((point-centre).dot(distal),(point-centre).dot(palm))) for point in points]
    triangles = [tuple(projected[face.vertices[j]] for j in (0,i,i+1))
        for face in obj.data.polygons for i in range(1,len(face.vertices)-1)]
    distance = min(grip._triangle_axis_distance(triangle) for triangle in triangles)
    # Recover the closest point in the actual triangle, not only its ring centre.
    nearest = min((closest_point_on_tri(Vector((0.,0.,0.)), *(Vector((point.x,point.y,0.)) for point in triangle))
        for triangle in triangles), key=lambda point: point.length_squared)
    return distance, Vector((nearest.x,nearest.y))


def _point_segment_distance(point,first,last):
    axis=last-first
    t=max(0.,min(1.,(point-first).dot(axis)/axis.length_squared)) if axis.length_squared>1e-20 else 0.
    return (point-first-axis*t).length


def _segment_distance(first,last,a,b):
    u,v,w=last-first,b-a,first-a
    uu,uv,vv,uw,vw=u.dot(u),u.dot(v),v.dot(v),u.dot(w),v.dot(w)
    distances=[_point_segment_distance(first,a,b),_point_segment_distance(last,a,b),
        _point_segment_distance(a,first,last),_point_segment_distance(b,first,last)]
    determinant=uu*vv-uv*uv
    if determinant>1e-20:
        s=(uv*vw-vv*uw)/determinant;t=(uu*vw-uv*uw)/determinant
        if 0.<=s<=1. and 0.<=t<=1.:distances.append((w+u*s-v*t).length)
    return min(distances)


def _shaft_axis_distance(first,last,triangles):
    axis=last-first;minimum=float("inf")
    for triangle in triangles:
        hit=intersect_ray_tri(*triangle,axis,first,True)
        if hit is not None and 0.<=(hit-first).dot(axis)/axis.length_squared<=1.:return 0.
        minimum=min(minimum,(first-closest_point_on_tri(first,*triangle)).length,
            (last-closest_point_on_tri(last,*triangle)).length,
            *(_segment_distance(first,last,a,b) for a,b in zip(triangle,triangle[1:]+triangle[:1])))
    # Tiny terminal spans could sit wholly inside a closed finger. A positive
    # distance to its surface alone must not classify that as clearance.
    for point in (first,last):
        nearest=min(((point-(near:=closest_point_on_tri(point,*triangle))).length,
            (point-near).dot((triangle[1]-triangle[0]).cross(triangle[2]-triangle[0]))) for triangle in triangles)
        if nearest[1]<-1e-10:return 0.
    return minimum


def shaft_clearances(result,scale,grip,actual=None):
    parts={record.obj.name:record.obj for record in result.parts}
    _,_,across,_,centre=frame(result,scale,grip)
    filter_origin=centre-across*(FILTER_TO_GRIP*scale)
    if actual is None:
        actual={}
        for name in NAMES:
            _,matrix=grip._source_points(result,parts[name])
            actual[name]=[matrix@p.co for p in parts[name].data.shape_keys.key_blocks[SHAPE].data]
    output={}
    for name,points in actual.items():
        triangles=[tuple(points[face.vertices[j]] for j in (0,i,i+1))
            for face in parts[name].data.polygons for i in range(1,len(face.vertices)-1)]
        spans={}
        for label,first,last,radius in (("paper",-.005,.065,RADIUS),("ember",.065,.069,.0035),
                ("conservative_full_shaft",.002,.074,RADIUS)):
            distance=_shaft_axis_distance(filter_origin+across*(first*scale),filter_origin+across*(last*scale),triangles)
            spans[label]={"minimum_axis_distance_m":distance,"surface_clearance_m":distance-radius*scale}
        output[name]=spans
    return output


def profiles(result, scale, grip):
    parts = {record.obj.name: record.obj for record in result.parts}
    wrist, distal, across, palm, centre = frame(result, scale, grip)
    neutral = {name: grip._source_points(result, parts[name])[0] for name in NAMES}
    targets = {name: [point.copy() for point in points] for name, points in neutral.items()}
    segments, choices = [], {}
    # The original phalanges fold at their own joints around the filter.
    # These measured finite curls replace the nearly straight upright index;
    # their actual surfaces oppose one another across the narrow cylinder.
    for index in range(4):
        name=f"GEO_Finger{index}.R"
        chain=_finger_chain(wrist,distal,across,palm,index,scale)
        angles=FINGER_JOINT_ANGLES[index]
        centres=_curl(chain,distal,palm,angles)
        targets[name]=grip._bend(neutral[name],chain[0],chain[-1],centres)
        segments.append((name,chain,centres))
        if index<2:
            distance,radial=_surface_gap(parts[name],targets[name],centre,distal,palm,grip)
            choices[name]={"joint_angles_degrees":angles,"surface_gap_m":distance-RADIUS*scale,
                "contact_palm_m":radial.y,"contact_radial_m":list(radial)}
    # Keep the thumb on the radial/filter/face side, softly folded rather than
    # spread away from the cigarette. Its original embedded root and links stay.
    def thumb_local(along,width):
        return wrist+(distal*along+across*width)*(1.+.20*min(1.,along/.030))*scale
    start=thumb_local(.018,-.022)
    end=thumb_local(.055,-.041)+palm*(.002*1.20*scale)
    chain=[start,start+(end-start)*.45,end]
    target=wrist+(distal*.049+across*-.042+palm*.019)*scale
    a,b=(chain[1]-start).length,(end-chain[1]).length
    delta=target-start; length=delta.length; axis=delta.normalized()
    if not abs(a-b)<length<a+b:raise RuntimeError("Smoking thumb target exceeds its original reach")
    along=(a*a-b*b+length*length)/(2.*length)
    bend=(-across-axis*(-across).dot(axis)).normalized()
    centres=[start,start+axis*along+bend*math.sqrt(max(0.,a*a-along*along)),target]
    targets["GEO_Thumb.R"]=grip._bend(neutral["GEO_Thumb.R"],start,end,centres)
    segments.append(("GEO_Thumb.R",chain,centres))
    return parts,neutral,targets,segments,choices,(wrist,distal,across,palm,centre)


def author(result,scale,grip):
    parts,_,targets,_,_,_=profiles(result,scale,grip)
    for name,points in targets.items():
        _,matrix=grip._source_points(result,parts[name])
        grip._shape(parts[name],matrix,points,SHAPE)
    return validate(result,scale,grip)


def validate(result,scale,grip):
    parts,neutral,expected,segments,choices,(wrist,distal,across,palm,centre)=profiles(result,scale,grip)
    actual={}
    for name in NAMES:
        obj=parts[name];keys=obj.data.shape_keys.key_blocks
        if SHAPE not in keys:raise RuntimeError("Missing cigarette grip on "+name)
        _,matrix=grip._source_points(result,obj)
        points=[matrix@point.co for point in keys[SHAPE].data];actual[name]=points
        if max((a-b).length for a,b in zip(points,expected[name]))>1e-7*scale:
            raise RuntimeError("Non-deterministic cigarette finger profile: "+name)
        volume=sum(points[p.vertices[0]].dot(points[p.vertices[i]].cross(points[p.vertices[i+1]]))/6.
            for p in obj.data.polygons for i in range(1,len(p.vertices)-1))
        if volume<=0:raise RuntimeError("Smoking grip flips a digit volume: "+name)
        if keys[SHAPE].value!=0:raise RuntimeError("Smoking grip must be neutral by default")
    for name,row in choices.items():
        if not -.0005*scale<=row["surface_gap_m"]<=.0015*scale:
            raise RuntimeError("Cigarette misses/intersects the real finger surface: "+name+repr(row))
    if choices["GEO_Finger0.R"]["contact_palm_m"]>=0 or choices["GEO_Finger1.R"]["contact_palm_m"]<=0:
        raise RuntimeError("Index and middle must clamp opposite cigarette surfaces: "+repr(choices))
    index_radial=Vector(choices["GEO_Finger0.R"]["contact_radial_m"]).normalized()
    middle_radial=Vector(choices["GEO_Finger1.R"]["contact_radial_m"]).normalized()
    opposition_dot=max(-1.,min(1.,index_radial.dot(middle_radial)))
    opposition_degrees=math.degrees(math.acos(opposition_dot))
    if opposition_degrees<MINIMUM_PAD_OPPOSITION_DEGREES:
        raise RuntimeError(f"Smoking pads must visibly oppose across the cigarette: {opposition_degrees:.3f} degrees")
    thumb=sum(actual["GEO_Thumb.R"],Vector())/len(actual["GEO_Thumb.R"])
    finger_centres=[sum(actual[name],Vector())/len(actual[name]) for name in ("GEO_Finger0.R","GEO_Finger1.R")]
    face_side_margin=min((point-thumb).dot(across) for point in finger_centres)
    if face_side_margin<.004*scale:raise RuntimeError("Smoking thumb must stay closer to filter/face than the gripping fingers")
    error=max(abs((a[i+1]-a[i]).length-(b[i+1]-b[i]).length) for _,a,b in segments for i in range(len(a)-1))
    if error>1e-6*scale:raise RuntimeError("Smoking grip stretches a finger link")
    if max((a-b).length for a,b in zip(actual["GEO_Hand.R"],neutral["GEO_Hand.R"]))>1e-8:
        raise RuntimeError("Smoking grip moves the palm/wrist")
    full_shaft=shaft_clearances(result,scale,grip,actual)
    for name,spans in full_shaft.items():
        for label,row in spans.items():
            if row["surface_clearance_m"] < -1e-7*scale:
                raise RuntimeError(f"Smoking {label} intersects {name}: {row}")
    return {"shape_name":SHAPE,"side":"R","renderers":list(NAMES),"radius_m":RADIUS*scale,
        "filter_to_grip_m":FILTER_TO_GRIP*scale,"contacts":choices,
        "pad_opposition_dot":opposition_dot,"pad_opposition_degrees":opposition_degrees,
        "minimum_pad_opposition_degrees":MINIMUM_PAD_OPPOSITION_DEGREES,"thumb_face_side_margin_m":face_side_margin,
        "full_shaft_clearances":full_shaft,
        "maximum_link_length_error_m":error,"palm_wrist_unchanged":True,"closed_positive_volumes":True,
        "socket_rest_unchanged":True,"socket_delta_in_smoking_clips":True}


def manifest(result,scale,grip):
    proof=validate(result,scale,grip)
    rows={part.obj.name:part.obj for part in result.parts}
    content={name:[[round(float(c),7) for c in vertex.co] for vertex in rows[name].data.shape_keys.key_blocks[SHAPE].data] for name in NAMES}
    return {**proof,"shape_signature":hashlib.sha256(json.dumps(content,sort_keys=True).encode()).hexdigest(),
        "authoring_sha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
