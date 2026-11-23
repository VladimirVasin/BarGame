"""Fixed-metre profiles in the apartment's furniture-local coordinate system."""
import math
import bar_parts as bp
import interior_kit as kit


def block(center, size, bevel=.004):
    return bp.to_source(bp.u_box(center, size, bevel))


def section(axis, length, profile):
    """Run a cross section along X or Z; its ordinate is always vertical."""
    vertices, faces = kit.prism(profile, length)
    points = [(v, u, y) if axis == 0 else (u, v, y) for u, v, y in vertices]
    if bp.signed_volume((points, faces)) < 0:
        faces = [tuple(reversed(f)) for f in faces]
    return points, faces


def padded(size):
    x, y, z = size
    # Closed upholstery shell: tapered foot, stuffed shoulder and rolled crown.
    levels = [(-.50,.88),(-.43,.98),(-.31,1),(.22,1),(.39,.95),(.47,.81),(.50,.60)]
    rings = []
    for height, scale in levels:
        a, b = x*scale, z*scale
        rings.append([(u,v,height*y) for u,v in kit.profile_chamfered(a,b,min(.052,a*.23,b*.23))])
    return ring_shell(rings)


def ring_shell(rings):
    count = len(rings[0])
    vertices = [p for ring in rings for p in ring]
    faces = [tuple(reversed(range(count)))]
    for i in range(len(rings)-1):
        for j in range(count):
            k=(j+1)%count
            faces.append((i*count+j,i*count+k,(i+1)*count+k,(i+1)*count+j))
    faces.append(tuple(range(len(vertices)-count,len(vertices))))
    if bp.signed_volume((vertices,faces)) < 0:
        faces = [tuple(reversed(f)) for f in faces]
    return vertices,faces


def seat_cushions(size, cols, rows):
    x,y,z = size
    divisions=(0,6,11,cols)
    heights=[]; bottom=[-y/2]*((cols+1)*(rows+1))
    for r in range(rows+1):
        front=max(0,1-r/3)**2
        for c in range(cols+1):
            seam=min(abs(c-s) for s in divisions)
            edge = .014*max(0, 1-seam)**2
            sag = front*(.009 + .009*math.sin(math.pi*c/cols)**2)
            heights.append(y/2 - edge - sag)
    parts=[]
    for left,right in zip(divisions,divisions[1:]):
        vertices=[]; faces=[]
        width=right-left+1
        for upper in (False,True):
            for r in range(rows+1):
                for c in range(left,right+1):
                    # Each cushion is individually closed. Its two lips track
                    # the same contact sample across the narrow stitched joint.
                    shift=.0006 if c==left and left else -.0006 if c==right and right!=cols else 0.
                    vertices.append(((c/cols-.5)*x+shift,(r/rows-.5)*z,
                                     heights[r*(cols+1)+c] if upper else -y/2))
        layer=(rows+1)*width
        for r in range(rows):
            for c in range(width-1):
                a=r*width+c
                face=(a,a+1,a+width+1,a+width)
                faces.extend([tuple(reversed(face)),tuple(i+layer for i in face)])
        rim=list(range(width))+[r*width+width-1 for r in range(1,rows+1)]
        rim += [rows*width+c for c in range(width-2,-1,-1)]
        rim += [r*width for r in range(rows-1,0,-1)]
        for a,b in zip(rim,rim[1:]+rim[:1]):
            faces.append((a,b,b+layer,a+layer))
        parts.append((vertices,faces))
    return kit.merge_all(parts),heights,bottom


def sofa_base(size):
    x,y,z=size
    parts=[block((0,.018,0),(x-.012,y-.080,z-.026),.032),
           block((0,y*.5-.035,0),(x,.07,z),.024)]
    for px in (-x*.44,x*.44):
        for pz in (-z*.39,z*.39):
            parts.append(block((px,-y*.5+.035,pz),(.105,.070,.098),.014))
    return kit.merge_all(parts)


def hemmed_blanket(size, cloth):
    x,y,z=size
    return kit.merge_all([kit.translated(cloth((x,y*.76,z)),(0,0,-y*.12)),
        kit.translated(cloth((x*.965,y*.29,z*.39)),(x*.008,z*.282,y*.355))])


def cabinet_front(size):
    x,y,z=size
    # A thin proud lipping protects the panel's recessed, laminated face.
    t=min(.014,y*.09)
    parts=[block((0,0,z*.06),(x-2*t,y-2*t,z*.88))]
    for side in (-1,1):
        parts.append(block((side*(x-t)/2,0,0),(t,y,z),.002))
        parts.append(block((0,side*(y-t)/2,0),(x-2*t,t,z),.002))
    return kit.merge_all(parts)


def pull(size, vertical=False):
    x,y,z=size
    length=y if vertical else x
    depth=z
    bar=block((0,0,-depth*.34),(x if not vertical else x*.88,
        y if vertical else y*.88, depth*.32),.006)
    pieces=[bar]
    for side in (-1,1):
        u=side*(length*.5-.011)
        pieces.append(block((0 if vertical else u,u if vertical else 0,depth*.09),
            (x if vertical else .022,.022 if vertical else y,depth*.82),.004))
    return kit.merge_all(pieces)


def seal(size, vertical):
    x,y,z=size
    width=x if vertical else y
    # Two flexible bellows and a flat fastening foot, one continuous extrusion.
    outline=[(-.50,-.50),(.50,-.50),(.50,-.29),(.32,-.17),(.48,.20),
             (.42,.43),(.22,.50),(.12,.33),(-.02,.45),(-.22,.50),
             (-.44,.39),(-.46,.20),(-.31,-.16),(-.5,-.29)]
    geom=section(0,y if vertical else x,[(a*width,b*z) for a,b in outline])
    # Source axes: section originally along X, width along Y, depth along Z.
    pts,faces=geom
    points=[(v,h,l) if vertical else (l,h,v) for l,v,h in pts]
    if bp.signed_volume((points,faces))<0: faces=[tuple(reversed(f)) for f in faces]
    return points,faces


def threshold(size):
    x,y,z=size
    profile=[(-.5,-.5),(.50,-.5),(.50,-.06),(.44,.16),(.36,.28),
             (.12,.28),(.09,.20),(.06,.20),(.035,.50),(-.36,.50),(-.45,.35),(-.50,-.07)]
    return section(2,z,[(u*x,h*y) for u,h in profile])


def rail_cap(size):
    x,y,z=size
    w=min(x,z)
    profile=[(-.50,-.33),(-.40,-.50),(.40,-.50),(.50,-.33),(.50,.20),
             (.39,.40),(.21,.50),(-.21,.50),(-.39,.40),(-.50,.20)]
    return section(0 if x>z else 2,max(x,z),[(u*w,h*y) for u,h in profile])


def rail_post(size):
    x,y,z=size
    parts=[block((0,0,0),(x*.59,y,z*.59),.008),
           block((0,-y*.5+.017,0),(x,.034,z),.008),
           block((0,y*.5-.019,0),(x*.85,.038,z*.85),.006)]
    for a in (-1,1):
        for b in (-1,1):
            foot=kit.lathe([(.008,0),(.010,.002),(.010,.007),(.008,.009)],6)
            parts.append(kit.translated(foot,(a*x*.375,b*z*.375,-y*.5+.034)))
    return kit.merge_all(parts)


def household_cloth(size, day, style):
    """A closed, folded textile or paper sheet. Its visible creases follow its silhouette."""
    x,y,z=size
    cols,rows=12,9
    verts=[];faces=[]
    for side in (0,1):
        for r in range(rows+1):
            v=r/rows
            for c in range(cols+1):
                u=c/cols
                if style=="paper":
                    crease=abs(u-.53)*.36+max(0,v+u-1.45)*1.8+abs(v-.41)*.2
                    outline=.99-.08*v
                elif style=="shirt":
                    outline=.61+.39*math.exp(-((v-.70)/.19)**2)
                    crease=.24*abs(math.sin(u*9+v*3))+.45*math.exp(-((u-.35-.28*v)/.09)**2)
                else:
                    outline=.94+.04*math.cos(v*7+day)
                    phase=u*(12+day*.8)+v*(2+day*.21)+.96*math.sin(v*5)
                    crease=.54*abs(math.sin(phase))**2+.27*math.sin(v*9+u*3)**2
                crease+=.25*max(0,.23-u)/.23*abs(math.sin(v*9+day))
                verts.append(((u-.5)*x*outline,(v-.5)*z+math.sin(u*5+day)*z*.023,
                              y*(crease*(1 if side else .45)+(.12 if side else 0))))
    stride=cols+1; layer=stride*(rows+1)
    for r in range(rows):
        for c in range(cols):
            a=r*stride+c
            face=(a,a+1,a+stride+1,a+stride)
            faces.extend([tuple(reversed(face)),tuple(i+layer for i in face)])
    rim=list(range(stride))+[r*stride+cols for r in range(1,rows+1)]
    rim += [rows*stride+c for c in range(cols-1,-1,-1)]
    rim += [r*stride for r in range(rows-1,0,-1)]
    for a,b in zip(rim,rim[1:]+rim[:1]): faces.append((a,b,b+layer,a+layer))
    lo,hi=kit.bounds((verts,faces))
    scales=[s/(b-a) for s,a,b in zip((x,z,y),lo,hi)]
    return kit.scaled(kit.translated((verts,faces),[-(a+b)/2 for a,b in zip(lo,hi)]),scales)


def household_plate(size):
    width,height,depth=size
    profile=((.38,-.5),(.46,-.5),(.48,-.40),(.51,-.37),(.70,-.17),
             (.96,.26),(1,.39),(.98,.50),(.93,.50),(.70,.12),(.48,-.18),(.25,-.19))
    geometry=kit.lathe([(r*width/2,h*height) for r,h in profile],24)
    return kit.scaled(geometry,(1,depth/width,1))
