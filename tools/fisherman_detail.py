"""The fisherman's painted face and two physical grips on his existing rod.

The face uses the established 4x4 expression atlas contract. Grip construction
uses the authored FishermanLean pose, then returns every vertex to its own
canonical hand's bind space; neither the shared rig nor the prop moves.
"""
from __future__ import annotations

import math
from mathutils import Vector

import atlas_kit
import npc_detail_geometry as detail

FACE_ATLAS_NAME = "LakeFishermanFaceAtlas.png"
MOUTH_RAISE = .075  # Metres in the final model/head bind space.
BEARD_DROP = .018  # The accepted moustache stays fixed above the painted lip.
PIPE_INLET_BIND = (.00688, -.1212, 1.5016)  # Existing shared smoking_pipe stem.
PIPE_SOCKET_BIND = (.002, -.141, 1.538)
PIPE_SCALE = .72
FACE_CELLS = ((0, 0, "Neutral"), (1, 0, "HalfBlink"),
              (2, 0, "ClosedBlink"), (0, 1, "Watchful"), (1, 1, "Tense"))


def face_cells():
    return [dict(expression=expression, column=column, row=3-row)
            for column, row, expression in FACE_CELLS]


def paint_face_atlas():
    canvas = atlas_kit.PixelCanvas(256, 256)
    expressions = {(column, row): expression for column, row, expression in FACE_CELLS}
    skin = (169, 141, 121, 255)
    shadow = (139, 113, 96, 255)
    deep = (102, 84, 73, 255)
    highlight = (182, 154, 132, 255)
    brow = (113, 108, 96, 255)
    pupil = (45, 43, 38, 255)
    eye = (145, 143, 126, 255)
    for row in range(4):
        for col in range(4):
            expression = expressions.get((col, row), "Neutral")
            ox, oy = col*64, row*64
            def rect(x0, y0, x1, y1, color):
                canvas.rect(ox+x0, oy+y0, ox+x1, oy+y1, color)
            def line(x0, y0, x1, y1, color, thickness=1):
                canvas.line(ox+x0, oy+y0, ox+x1, oy+y1, color, thickness)
            rect(0, 0, 64, 64, skin)
            for x in range(64):
                edge = abs(x-31.5)/31.5
                for y in range(64):
                    grain = ((x*7 + y*13) % 5)-2
                    shade = int(max(0, edge-.62)*35)
                    canvas.put(ox+x, oy+y, tuple(max(0, c-shade+grain) for c in skin[:3])+(255,))
            line(19, 14, 41, 13, shadow)
            line(21, 18, 43, 17, shadow)
            line(24, 19, 38, 19, highlight)
            for side, cx in enumerate((20, 43)):
                by = 22 + (1 if side else 0)
                if expression == "Watchful": by -= 1
                if expression == "Tense": by += 1
                line(cx-7, by, cx+6, by-1, brow, 1)
                line(cx-6, by+1, cx+4, by, deep)
                eyelid = 27 + (1 if side else 0)
                if expression == "ClosedBlink":
                    line(cx-6, eyelid, cx+5, eyelid, deep)
                else:
                    opening = 1 if expression == "HalfBlink" else (4 if expression == "Watchful" else 3)
                    rect(cx-6, eyelid, cx+6, eyelid+opening, eye)
                    rect(cx-1, eyelid, cx+2, eyelid+opening, pupil)
                    line(cx-6, eyelid-1, cx+5, eyelid-1, deep)
                    line(cx-5, eyelid+opening, cx+4, eyelid+opening, shadow)
                line(cx-7, 33, cx+5, 34, shadow)
                line(cx-6, 35, cx+3, 36, highlight)
                outward = -1 if side == 0 else 1
                line(cx+outward*9, 27, cx+outward*12, 26, shadow)
                line(cx+outward*9, 30, cx+outward*12, 32, shadow)
            # Nose, lids, eyes and brows are entirely pixels on this surface.
            line(30, 30, 28, 40, highlight)
            line(35, 31, 37, 40, shadow)
            line(28, 41, 31, 42, deep)
            line(35, 42, 38, 41, deep)
            rect(31, 39, 35, 42, highlight)
            line(25, 43, 22, 50, shadow)
            line(39, 43, 42, 50, shadow)
            line(27, 50, 37, 50, deep)
            line(29, 52, 35, 52, highlight)
    return canvas


def face_surface():
    """A curved skin patch, with no separately modelled facial features."""
    vertices = []
    # The old short patch put the eyes near the bottom of the hood opening.
    # Distribute the same complete 0..1 expression grid over the full face,
    # keeping the forehead short and the painted mouth at final z~1.576.
    rows = (1.475, 1.521, 1.574, 1.614, 1.646, 1.662, 1.673)
    for z in rows:
        height = (z-1.552)/.122
        radius = .098*math.sqrt(max(.001, 1-height*height))
        for j in range(7):
            x = (j/6*2-1)*radius*.94
            skull_x = x*.80/.86
            y = -.034-.092*math.sqrt(max(.001, 1-(skull_x/.098)**2-height*height))-.002
            vertices.append(Vector((x, y, z)))
    faces = [(r*7+j, r*7+j+1, (r+1)*7+j+1, (r+1)*7+j)
             for r in range(6) for j in range(6)]
    return vertices, faces


def mouth_position(result):
    """Painted lip centre, sampled on the actual remapped 7x7 surface."""
    face = next(part.obj for part in result.parts if part.obj.name == "GEO_FaceSurface")
    fraction = 6*(1-50.5/64)-1
    a = face.matrix_world @ face.data.vertices[10].co
    b = face.matrix_world @ face.data.vertices[17].co
    return [round(value, 9) for value in a.lerp(b, fraction)]


def pipe_mount_offset(result):
    """Total bind-space prop translation: inlet touches the real upper lip."""
    face=next(part.obj for part in result.parts if part.obj.name == "GEO_FaceSurface")
    points=[face.matrix_world @ vertex.co for vertex in face.data.vertices]
    x=PIPE_INLET_BIND[0]
    z=mouth_position(result)[2]+.005
    for polygon in face.data.polygons:
        ids=polygon.vertices
        for corners in ((ids[0],ids[1],ids[2]),(ids[0],ids[2],ids[3])):
            a,b,c=(points[i] for i in corners)
            det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z)
            if abs(det)<1e-12: continue
            wa=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/det
            wb=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/det
            wc=1-wa-wb
            if min(wa,wb,wc)>=-1e-6:
                y=wa*a.y+wb*b.y+wc*c.y-.001
                target=Vector((x,y,z))
                socket=Vector(PIPE_SOCKET_BIND)
                scaled_inlet=socket+PIPE_SCALE*(Vector(PIPE_INLET_BIND)-socket)
                return [round(value,9) for value in target-scaled_inlet]
    raise RuntimeError("Pipe inlet target lies outside the painted lip surface")


def beard_geometry(name, result):
    """A short full beard and two moustache lobes, built as hair masses.

    Construction is in the final canonical head space. Invert the existing
    headwear remap only at the return boundary so add_part still applies it
    once, like every other part. This is intentionally not a clothing loft.
    """
    mouth = 1.5016 + MOUTH_RAISE
    if name == "ACC_Beard":
        import bpy
        bpy.context.view_layer.update()
        head=next(part.obj for part in result.parts if part.obj.name == "GEO_Head")
        skull=[head.matrix_world @ vertex.co for vertex in head.data.vertices]
        triangles=[(polygon.vertices[0],polygon.vertices[i],polygon.vertices[i+1])
                   for polygon in head.data.polygons for i in range(1,len(polygon.vertices)-1)]
        def skull_width(z):
            intersections=[]
            for a,b in ((skull[edge.vertices[0]],skull[edge.vertices[1]]) for edge in head.data.edges):
                if abs(a.z-b.z)>1e-9 and min(a.z,b.z)<=z<=max(a.z,b.z):
                    intersections.append(abs(a.x+(b.x-a.x)*(z-a.z)/(b.z-a.z)))
            return max(intersections) if intersections else None
        def skull_front(x,z):
            hits=[]
            for ids in triangles:
                a,b,c=(skull[i] for i in ids)
                det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z)
                if abs(det)<1e-12: continue
                wa=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/det
                wb=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/det
                wc=1-wa-wb
                if min(wa,wb,wc)>=-1e-6:
                    hits.append(wa*a.y+wb*b.y+wc*c.y)
            return min(hits) if hits else None
        front = []
        columns, rows = 13, 5
        fringe = (.002, -.002, .002, -.004, .001, -.002, -.004,
                  .001, -.003, .002, -.001, .003, .001)
        for row in range(rows):
            t = row/(rows-1)
            half_width = .028 + .039*t
            outer_z=(mouth-.080+.028)*(1-t)+(mouth-.009+.049)*t-BEARD_DROP
            outline=skull_width(outer_z)
            side_weight=min(1.,t/.75)
            if outline is not None:
                half_width += (outline+.003-half_width)*side_weight
            for column in range(columns):
                u = column/(columns-1)*2-1
                bottom = mouth-.080 + .028*abs(u)**1.2 + fringe[column]
                top = mouth-.009 + .049*abs(u)**1.65
                z = bottom+(top-bottom)*t
                y = -.133 + .045*abs(u)**1.75
                y -= .006*math.sin(math.pi*t)
                y += .003*math.cos(column*math.pi)*math.sin(math.pi*t)
                x=u*half_width
                outline_at_z=skull_width(z-BEARD_DROP)
                if outline_at_z is not None:
                    sample_x=max(-outline_at_z*.97,min(outline_at_z*.97,x))
                    skin=skull_front(sample_x,z-BEARD_DROP)
                    if skin is not None:
                        edge=max(0.,min(1.,(abs(u)-.55)/.45))
                        edge=edge*edge*(3-2*edge)*side_weight
                        # Turn around the actual jaw, rather than extending a
                        # flat white plate sideways beneath the hood.
                        y += (skin-.003-y)*edge
                front.append(Vector((x,y,z)))
        # The lowered hair still grows from the jaw: bury its upper rear
        # surface into the chin instead of suspending a separate shell.
        back = [p+Vector((0,.022+.022*(index//columns/(rows-1))**2,0))
                for index,p in enumerate(front)]
        count=len(front)
        faces=[]
        for row in range(rows-1):
            for column in range(columns-1):
                a=row*columns+column; b=a+columns
                faces.extend(((a,a+1,b+1,b),(a+count,b+count,b+1+count,a+1+count)))
        boundary=list(range(columns))
        boundary += [row*columns+columns-1 for row in range(1,rows)]
        boundary += list(range(count-2,count-columns-1,-1))
        boundary += [row*columns for row in range(rows-2,0,-1)]
        for a,b in zip(boundary,boundary[1:]+boundary[:1]):
            faces.append((a,b,b+count,a+count))
        geometry=detail.outward((front+back,faces))
    elif name == "ACC_Moustache":
        import bpy
        bpy.context.view_layer.update()
        face = next(part.obj for part in result.parts if part.obj.name == "GEO_FaceSurface")
        skin_points = [face.matrix_world @ vertex.co for vertex in face.data.vertices]
        def skin_y(x,z):
            for polygon in face.data.polygons:
                ids=polygon.vertices
                for corners in ((ids[0],ids[1],ids[2]),(ids[0],ids[2],ids[3])):
                    a,b,c=(skin_points[i] for i in corners)
                    det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z)
                    if abs(det)<1e-12: continue
                    wa=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/det
                    wb=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/det
                    wc=1-wa-wb
                    if min(wa,wb,wc)>=-1e-6:
                        return wa*a.y+wb*b.y+wc*c.y
            raise RuntimeError(f"Moustache root is outside its actual skin patch: {x}, {z}")
        pieces=[]
        for sign in (-1,1):
            rings=[]
            for x,dz,radius,depth in ((.002,.007,.002,.001),(.010,.009,.004,.0025),
                                      (.020,.008,.0042,.0025),(.032,.006,.0026,.0017),
                                      (.040,.004,.0007,.0005)):
                ring=[]
                for j in range(9):
                    a=math.tau*j/9
                    px=sign*x
                    pz=mouth+dz+radius*math.sin(a)
                    # The rear surface follows the actual triangulated skin
                    # at 1 mm clearance, not a guessed constant depth plane.
                    py=skin_y(px,pz)-.001-depth*(1+math.cos(a))
                    ring.append((px,py,pz))
                rings.append(ring)
            pieces.append(detail.loft(rings))
        points,faces=[],[]
        for vertices,polygons in pieces:
            offset=len(points); points.extend(vertices)
            faces.extend(tuple(index+offset for index in polygon) for polygon in polygons)
        geometry=(points,faces)
    elif name.startswith("ACC_BeardLock"):
        index=int(name[len("ACC_BeardLock")])
        sign=1 if name.endswith(".L") else -1
        # Follow the beard's OWN curved front, not a parallel plane. Shallow,
        # short ridges are embedded in the chin and cannot become loose fangs.
        u=sign*(.12+index*.23)
        def on_beard(t):
            bottom=mouth-.080+.028*abs(u)**1.2
            top=mouth-.009+.049*abs(u)**1.65
            y=-.133+.045*abs(u)**1.75-.006*math.sin(math.pi*t)+.001
            return (u*(.028+.039*t),y,bottom+(top-bottom)*t)
        geometry=detail.segment(on_beard(.72),on_beard(.30),
            ((0,.002,.40),(.35,.003,.42),(.70,.002,.35),(1,.0006,.32)),7)
    else:
        return None
    scale_x=.80 if name == "ACC_Moustache" else .86
    points,faces=geometry
    if name.startswith("ACC_Beard"):
        points=[p-Vector((0,0,BEARD_DROP)) for p in points]
    return ([Vector((p.x/scale_x,(p.y+.020)/.92-.040,
                     1.75-(1.75-p.z)/.90)) for p in points],faces)


def _tube(points, radii, axis, sides=8):
    rings = []
    for i, (point, radius) in enumerate(zip(points, radii)):
        direction = points[min(i+1,len(points)-1)]-points[max(0,i-1)]
        across = (axis-direction.normalized()*axis.dot(direction.normalized())).normalized()
        normal = direction.normalized().cross(across)
        rings.append([point+radius*(across*math.cos(math.tau*j/sides)+normal*math.sin(math.tau*j/sides))
                      for j in range(sides)])
    return detail.loft(rings)


def _pipe_bowl_frame():
    start,end=Vector((.022,-.252,1.428)),Vector((.024,-.262,1.492))
    axis=(end-start).normalized()
    rotation=axis.to_track_quat("Z","Y")
    return start,end,axis,rotation@Vector((1,0,0)),rotation@Vector((0,1,0))


def pipe_bowl_geometry():
    """Preserve the old outer octagon, opening a real cavity below its rim."""
    start,end,axis,basis_x,basis_y=_pipe_bowl_frame()
    points=[]
    for center,radius in ((start,.026),(end,.030),(end,.024),(end-axis*.022,.019)):
        points.extend(center+radius*(basis_x*math.cos(math.tau*i/8)+
                                     basis_y*math.sin(math.tau*i/8)) for i in range(8))
    faces=[tuple(reversed(range(8)))]
    for a,b in ((0,8),(8,16),(16,24)):
        faces.extend((a+i,a+(i+1)%8,b+(i+1)%8,b+i) for i in range(8))
    faces.append(tuple(range(24,32)))
    return detail.outward((points,faces))


def pipe_ember_geometry():
    """Five broken, shallow coals with visible gaps in the existing bowl.

    The coals fit the hollow bowl and lie below the lip. The part-center
    runtime light/smoke anchor follows their recessed center automatically.
    """
    # Adjacent fractured cells, not five regularly spaced round dots. Their
    # shared split lines leave narrow seams; different cell areas and sloping
    # caps make one irregular bed of coal inside the existing bowl.
    outline=[Vector(p) for p in ((-1,-.45),(-.65,-1),(.35,-1),(1,-.6),
                                 (1,.38),(.56,1),(-.58,1),(-1,.53))]
    seeds=[Vector(p) for p in ((-.53,-.50),(.38,-.63),(.52,.32),
                               (-.06,.32),(-.66,.48))]
    def clip(poly, normal, limit):
        result=[]
        for a,b in zip(poly,poly[1:]+poly[:1]):
            da,db=a.dot(normal)-limit,b.dot(normal)-limit
            if da<=0: result.append(a)
            if (da<0)!=(db<0): result.append(a.lerp(b,da/(da-db)))
        return result
    cells=[]
    for seed in seeds:
        cell=outline[:]
        for other in seeds:
            if other==seed: continue
            cell=clip(cell,other-seed,(other.length_squared-seed.length_squared)*.5)
        cells.append(cell)
    centers=[sum(cell,Vector((0,0)))/len(cell) for cell in cells]
    def inset(scale):
        return [[c+(p-c)*scale for p in cell] for c,cell in zip(centers,cells)]
    def fill(polygons):
        area=sum(abs(sum(a.x*b.y-b.x*a.y for a,b in zip(poly,poly[1:]+poly[:1])))*.5
                 for poly in polygons)
        flat=[p for poly in polygons for p in poly]
        return area/((max(p.x for p in flat)-min(p.x for p in flat))*
                     (max(p.y for p in flat)-min(p.y for p in flat)))
    low,high=.5,1.
    for _ in range(28):
        middle=(low+high)*.5
        if fill(inset(middle))>.62: high=middle
        else: low=middle
    cells=inset((low+high)*.5)
    points,faces=[],[]
    for index,(cell,center) in enumerate(zip(cells,centers)):
        rings=[]
        z=(-.0006,.0003,-.0002,.0007,-.0001)[index]
        for scale,dz in ((.86,-.004),(1.,-.0005),(.91,.0035)):
            rings.append([Vector(((center.x+(p.x-center.x)*scale)*.018,
                                  (center.y+(p.y-center.y)*scale)*.018,
                                  z+dz+(.0008*math.sin(j*1.7+index) if dz>0 else 0)))
                          for j,p in enumerate(cell)])
        vertices,polygons=detail.loft(rings)
        offset=len(points); points.extend(vertices)
        faces.extend(tuple(i+offset for i in polygon) for polygon in polygons)
    lo=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    _,top,axis,basis_x,basis_y=_pipe_bowl_frame()
    center=top-axis*.014
    extent=Vector((.030,.030,.010))
    normalized=[Vector(tuple(extent[i]*((p[i]-lo[i])/(hi[i]-lo[i])-.5)
                             for i in range(3))) for p in points]
    points=[center+basis_x*p.x+basis_y*p.y+axis*p.z for p in normalized]
    return points,faces


def rod_reel_geometry(api):
    """Small spinning reel in the clear span between the two existing grips.

    Author in metres along/below/across the posed rod, then return to the
    legacy prop source space. This measured inverse frame preserves the
    unchanged right-hand pose/anatomy; raw source -Z is NOT below the rod.
    """
    parts=[
        api.make_frustum_between((.034,.019,0),(.063,.019,0),.004,.004,6,1),
        api.make_frustum_between((.048,.018,0),(.043,.042,0),.0045,.005,6,1),
        api.make_ellipsoid((.039,.052,0),(.016,.017,.014),10,5),
        api.make_profiled_segment((.048,.052,0),(.076,.052,0),
            ((0,.014,1),(.14,.020,1),(.27,.015,1),(.73,.014,1),(.86,.019,1),(1,.016,1)),12),
        api.make_frustum_between((.036,.052,.010),(.036,.052,.030),.004,.003,6,1),
        api.make_frustum_between((.036,.052,.030),(.022,.069,.030),.003,.003,6,1),
        api.make_frustum_between((.022,.069,.029),(.022,.069,.041),.0055,.005,8,1),
    ]
    points,faces=api.combine_geometry(*parts)
    origin=Vector(api.ROD_GRIP)
    along=Vector((.134391770,-.992431819,.258445919))
    down=Vector((-.792822003,.022809170,.623696268))
    across=Vector((-.440019608,-.314534187,-.748090386))
    return detail.outward(([origin+along*p.x+down*p.y+across*p.z for p in points],faces))


def _supporting_hand_geometry(wrist, center, axis, radius, api):
    """A full palm with opposed thumb, closed about the left support contact."""
    radial=wrist-center
    radial-=axis*radial.dot(axis)
    radial.normalize()
    around=axis.cross(radial).normalized()
    knuckles=center+radial*(radius+.008)
    direction=(knuckles-wrist).normalized()
    palm_length=(knuckles-wrist).length
    if not .050<=palm_length<=.085:
        raise RuntimeError(f"Left support requires an anatomical palm length, got {palm_length}")
    across=(axis-direction*axis.dot(direction)).normalized()
    depth_axis=direction.cross(across).normalized()
    rings=[]
    # Wrist to metacarpals: a long palm, narrower at the cuff, not a short
    # round pad. Distal palm overlaps all four proximal finger segments.
    for t,width,depth in ((-.10,.020,.013),(0,.021,.013),(.25,.027,.015),
                          (.65,.034,.0145),(1,.032,.011)):
        c=wrist.lerp(knuckles,t)
        rings.append([c+across*width*math.cos(math.tau*j/12)+
                      depth_axis*depth*math.sin(math.tau*j/12) for j in range(12)])
    pieces=[detail.loft(rings)]
    for index,(offset,end_angle) in enumerate(((.024,2.08),(.008,2.25),(-.008,2.18),(-.023,1.92))):
        size=(1.,1.03,.97,.86)[index]
        r=radius+.0065*size
        angles=(-.23,.48,1.24,end_angle)
        centers=[center+axis*offset+r*(radial*math.cos(a)+around*math.sin(a)) for a in angles]
        pieces.append(_tube(centers,tuple(r*size for r in (.0075,.008,.0068,.0054)),axis))
    # The fleshy thumb root belongs to the palm. Its two short phalanges
    # oppose the curled fingers rather than forming an arch above the rod.
    thumb=[wrist.lerp(knuckles,.38)+axis*.019-around*.004,
           wrist.lerp(knuckles,.73)+axis*.034-around*.010,
           center+axis*.035+radial*(radius+.002)-around*(radius+.008),
           center+axis*.021+radial*.001-around*(radius+.005)]
    pieces.append(_tube(thumb,(.012,.011,.009,.007),axis))
    return api.combine_geometry(*pieces)


def fit_grips(builder, api):
    """Close both hands about the same world-space rod and preserve the wrists."""
    rig = builder.result.rig
    api.reset_pose(rig)
    api.apply_pose(rig, api.fisherman_base_pose())
    deltas = {side: rig.pose.bones[f"hand.{side}"].matrix @
              rig.data.bones[f"hand.{side}"].matrix_local.inverted() for side in ("L", "R")}
    def rod_bind(p):
        return builder.remap_geometry_point(p, "hand.R", "signature_silhouette", "ACC_RodGrip")
    rod_origin = deltas["R"] @ rod_bind(api.ROD_GRIP)
    rod_tip = deltas["R"] @ rod_bind(api._rod_along(1))
    axis = (rod_tip-rod_origin).normalized()
    head_delta = rig.pose.bones["head"].matrix @ rig.data.bones["head"].matrix_local.inverted()
    look = (head_delta.to_3x3() @ Vector((0,-1,0))).normalized()
    replacements = {}
    contact_centers = {}
    measurements = []
    for side in ("R", "L"):
        bone = api.BONE_BY_NAME[f"hand.{side}"]
        wrist = deltas[side] @ Vector(bone.head)
        preferred = deltas[side] @ Vector(bone.head).lerp(Vector(bone.tail), .55)
        center = rod_origin + axis*(.12 if side=="L" else (preferred-rod_origin).dot(axis))
        radial = wrist-center
        radial -= axis*radial.dot(axis)
        radial.normalize()
        around = axis.cross(radial).normalized()
        # The cork is wider at the right palm; the supporting left palm meets
        # the same continuing handle/cane, never an imaginary parallel rod.
        along = (center-rod_origin).dot(axis)
        radius = .0195 if along < .105 else .013
        palm_end = center + radial*(radius+.010)
        direction = (palm_end-wrist).normalized()
        across = (axis-direction*axis.dot(direction)).normalized()
        thickness = direction.cross(across).normalized()
        rings=[]
        for t, width, depth in ((0,.021,.014),(.28,.026,.016),(.65,.033,.015),(1,.031,.013)):
            c=wrist.lerp(palm_end,t)
            rings.append([c+across*width*math.cos(math.tau*j/12)+thickness*depth*math.sin(math.tau*j/12)
                          for j in range(12)])
        pieces=[detail.loft(rings)]
        for index, offset in enumerate((-.024,-.008,.009,.025)):
            r=radius+.0070
            # Curl three phalanges around the actual cylinder. Four broad
            # knuckles read separately; tips finish below the handle.
            angles=(-.18,.62,1.45,2.18 if index<3 else 1.95)
            points=[center+axis*offset+r*(radial*math.cos(a)+around*math.sin(a)) for a in angles]
            pieces.append(_tube(points,(.008,.0083,.0073,.0058),axis))
        thumb_points=[wrist.lerp(palm_end,.58)+axis*.028,
                      center+axis*.038+radial*(radius+.011),
                      center+axis*.034+radial*(radius*.63)-around*(radius+.006),
                      center+axis*.020-radial*.003-around*(radius+.007)]
        pieces.append(_tube(thumb_points,(.011,.011,.0095,.0075),axis))
        geometry = (_supporting_hand_geometry(wrist,center,axis,radius,api)
                    if side=="L" else api.combine_geometry(*pieces))
        inverse = deltas[side].inverted()
        replacements[side]=([inverse@p for p in geometry[0]],geometry[1])
        contact_centers[side]=inverse@center
        measurements.append((side,round(along,5),round((wrist-center).length,5)))
    largest_error = 0.0
    # The nine authored breath/lift keys move both grips with the chest. Prove
    # that the real second contact never drifts off the right-hand rod axis.
    for _, pose in api.animation_keys()["FishermanLean"]:
        api.reset_pose(rig)
        api.apply_pose(rig, pose)
        motion = {side: rig.pose.bones[f"hand.{side}"].matrix @
                  rig.data.bones[f"hand.{side}"].matrix_local.inverted() for side in ("L", "R")}
        start = motion["R"]@rod_bind(api.ROD_GRIP)
        direction = ((motion["R"]@rod_bind(api._rod_along(1)))-start).normalized()
        for side in ("L", "R"):
            offset = motion[side]@contact_centers[side]-start
            largest_error=max(largest_error,(offset-direction*offset.dot(direction)).length)
    if largest_error > .0001:
        raise RuntimeError(f"Fisherman closed grip escapes its rod during the authored breath: {largest_error}")
    api.reset_pose(rig)
    for side, geometry in replacements.items():
        obj=next(p.obj for p in builder.result.parts if p.obj.name==f"GEO_Hand.{side}")
        detail.replace_mesh(obj,geometry)
        obj["bp_grip_axis"]="fishing_rod"
        obj["bp_grip_closed_fingers"]=4
    print(f"  Fisherman grip stations/wrist reaches: {measurements}; contact error={largest_error:.7f}m; face look Z={look.z:.5f}")


def add_preview_props(result, presentation, api):
    """Show the existing pipe and rod in Blender without publishing props."""
    import bpy
    for prop in api.HAND_PROPS:
        if prop.id not in {"fishing_rod", "smoking_pipe"}:
            continue
        source = api.hand_prop_rest_geometry(prop)
        for part in prop.parts:
            points, faces = source[part.name]
            if prop.id == "smoking_pipe":
                offset=Vector(pipe_mount_offset(result))
                socket=Vector(PIPE_SOCKET_BIND)
                points = [socket+offset+PIPE_SCALE*(p-socket) for p in points]
            mesh=bpy.data.meshes.new("PREVIEW_"+part.name)
            mesh.from_pydata(points, [], faces)
            mesh.update()
            obj=bpy.data.objects.new("PREVIEW_"+part.name, mesh)
            presentation.objects.link(obj)
            obj.parent=result.rig
            obj.color=api.PALETTE[part.palette]
            obj.data.materials.append(result.material)
            obj.vertex_groups.new(name=prop.bone).add(list(range(len(points))),1.,"REPLACE")
            obj.modifiers.new("PreviewRig","ARMATURE").object=result.rig
