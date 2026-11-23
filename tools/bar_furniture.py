"""Fixed-metre joinery and turned fittings for the compact pub. Geometry uses Unity axes."""
import math

import bar_parts as bp
import interior_kit as kit


def turned(center, profile, sides=16):
    return kit.translated(bp.to_source(kit.lathe(profile, sides)), center)


def shell(outlines):
    """Close horizontal, counter-clockwise XZ rings from bottom to top."""
    count = len(outlines[0])
    vertices = [point for ring in outlines for point in ring]
    faces = [tuple(range(count))]
    for row in range(len(outlines)-1):
        for i in range(count):
            a = row*count + i
            b = row*count + (i+1)%count
            faces.append((b,a,a+count,b+count))
    faces.append(tuple(reversed(range(len(vertices)-count,len(vertices)))))
    return vertices, faces


def edged_slab(outline, y, height, joint=-1):
    # Offset each edge of the actual polygon. The diagonal joint only gets
    # a small eased arris; an outer lip has a thicker rolled wood profile.
    edges = []
    for i,(x,z) in enumerate(outline):
        nx,nz = outline[(i+1)%len(outline)]
        length = math.hypot(nx-x,nz-z)
        edges.append(((z-nz)/length,(nx-x)/length))
    rings = []
    for rise, step in ((0,.023),(.015,.012),(.032,.012),(.039,.004),
                       (.048,0),(height-.028,0),(height-.013,.012),(height,.030)):
        ring = []
        for i,(x,z) in enumerate(outline):
            a,b = edges[i-1],edges[i]
            left = min(step,.0015) if (i-1)%len(edges)==joint else step
            right = min(step,.0015) if i==joint else step
            det = a[0]*b[1]-a[1]*b[0]
            ring.append((x+(left*b[1]-right*a[1])/det, y+rise, z+(a[0]*right-b[0]*left)/det))
        rings.append(ring)
    return shell(rings)


def stool(x,z,top,diameter,thickness,foot_diameter,stem_diameter):
    bottom = top-thickness
    r,f,s = diameter/2,foot_diameter/2,stem_diameter/2
    seat = turned((x,bottom,z),[(r-.024,0),(r-.012,.004),(r-.009,.010),(r-.009,.016),
        (r-.003,.019),(r,.025),(r,thickness-.012),(r-.005,thickness-.004),
        (r-.015,thickness),(r*.74,thickness)],24)
    foot = turned((x,0,z),[(f-.008,0),(f,.006),(f,.016),(f-.005,.023),(f-.025,.027),
        (f*.62,.040),(s+.017,.061),(s+.012,.070)],20)
    stem = turned((x,0,z),[(s,.056),(s+.009,.068),(s+.009,.093),(s,.098),(s,bottom-.080),
        (s+.011,bottom-.076),(s+.011,bottom-.058),(s,bottom-.054),(s+.012,bottom-.035),
        (.105,bottom-.011),(.112,bottom-.006),(.112,bottom+.003)],16)
    return stem,foot,seat


def panel(width,height):
    """Raised timber field with a rebated centre, local outer face toward -Z."""
    trim = .048
    bars = [bp.u_box((0,0,0),(width-.072,height-.072,.035),.012)]
    for sign in (-1,1):
        bars.append(bp.u_box((sign*(width-trim)/2,0,-.013),(trim,height,.061),.006))
        bars.append(bp.u_box((0,sign*(height-trim)/2,-.013),(width-2*trim,trim,.061),.006))
    bars.append(bp.u_box((0,0,-.019),(width-.17,height-.16,.027),.012))
    return kit.merge_all(bars)


def rail(start, end, brackets, return_run=False):
    length=end-start
    # Local X follows the rail and +Z goes towards its mounting panel.
    tube = bp.u_rotated(turned((0,0,0),[(.027,0),(.034,.006),(.034,length-.006),(.027,length)],12),(0,0,-90))
    pieces = [tube]
    for offset in brackets:
        collar = turned((0,0,0),[(.036,-.027),(.041,-.021),(.041,.021),(.036,.027)],12)
        socket = turned((0,0,0),[(.023,.025),(.023,.072),(.035,.089),(.039,.090),(.039,.117)],12)
        pieces += [kit.translated(bp.u_rotated(collar,(0,0,-90)),(offset,0,0)),
                   kit.translated(bp.u_rotated(socket,(90,0,0)),(offset,0,0))]
    piece = kit.merge_all(pieces)
    if return_run:
        return kit.translated(bp.u_rotated(piece,(0,90,0)),(2.925,.181,start))
    return kit.translated(piece,(start,.181,1.33))


def tap_spout(x,z,branch_y,tip_z,tip_y):
    run = tip_z-z
    body = bp.u_rotated(turned((0,0,0),[(.041,0),(.052,.021),(.052,.048),(.037,.057),
        (.032,run-.055),(.042,run-.040),(.045,run-.017),(.040,run)],12),(90,0,0))
    # The open nozzle turns back inside by 17 mm. The liquid starts at its mouth.
    tip = turned((x,tip_y,tip_z),[(.014,.017),(.014,0),(.020,0),(.023,.005),
        (.020,.021),(.029,.042),(.037,branch_y-tip_y+.003)],12)
    return kit.merge(kit.translated(body,(x,branch_y,z)),tip)


def drip_tray(cx, top, cz):
    # Thin pressed tray. The central slat supports the service vessel at its dock.
    pan = bp.u_box((cx,top+.0009,cz),(.95,.0018,.255),.0007)
    pieces = []
    for sign in (-1,1):
        pieces.append(bp.u_box((cx+sign*.469,top+.003,cz),(.012,.004,.255),.001))
        pieces.append(bp.u_box((cx,top+.003,cz+sign*.122),( .928,.004,.011),.001))
    for i in range(-15,16):
        pieces.append(bp.u_box((cx+i*.030,top+.0033,cz),(.014,.0034,.235),.001))
    return pan,kit.merge_all(pieces)


def upholstery(x,z,base,width,depth,height,cut):
    levels=((0,.021),(.08,.011),(.18,.004),(.29,0),(.37,.003),
            (.42,.004),(.70,.004),(.88,.010),(1,.033))
    rings=[]
    for fraction,inset in levels:
        contour=kit.profile_chamfered(width-2*inset,depth-2*inset,max(.008,cut-inset*.35))
        rings.append([(x+u,base+height*fraction,z+v) for u,v in contour])
    return shell(rings)


def booth_back(x,z,sign):
    # Three stitched channels with a narrow valley between adjacent rolls.
    rolls=[]
    for column in range(3):
        geom=upholstery(0,0,0,.598,.95,.102,.037)
        geom=bp.u_rotated(geom,(-90*sign,0,0))
        rolls.append(kit.translated(geom,(x+(column-1)*.601,.90,z+sign*.051)))
    return kit.merge_all(rolls)


def table_pedestal(x,z,top):
    return turned((x,0,z),[(.178,.014),(.185,.020),(.185,.039),(.173,.052),(.105,.062),
        (.074,.101),(.062,.132),(.057,.149),(.044,.158),(.039,.227),(.039,top-.13),
        (.054,top-.103),(.061,top-.080),(.104,top-.031),(.128,top-.020),(.128,top)],16)


def validate(parts, surface_y, hero_seat, vessel_dock, problems):
    by_name={part.name:part.geometry for part in parts}
    def top(name):
        return kit.bounds(by_name[name])[1][2]
    for name in ("Counter Top","Counter Return Top"):
        if abs(top(name)-surface_y)>.0001:
            problems.append(name+" misses the service plane")
    if abs(top("Tap Slotted Grate")-vessel_dock[1])>.0001 or top("Tap Drip Pan")>=top("Tap Slotted Grate"):
        problems.append("The grate must support the pint above the drip pan.")
    if abs(top("Bar Drink Service Stool")-hero_seat[1])>.0001:
        problems.append("The stool no longer supports the hero at the seat dock.")
    for i in (1,2):
        for side in ("Near","Far"):
            if abs(top(f"Booth Cushion {i} {side}")-.48)>.0001:
                problems.append("Booth cushion misses the patron's seated pose.")
        for prefix in ("Social Pub Table","Booth Table"):
            name=f"{prefix} {i}" if prefix.startswith("Social") else f"{prefix} Top {i}"
            stand=f"{name} Leg" if prefix.startswith("Social") else f"{prefix} Leg {i}"
            if abs(kit.bounds(by_name[name])[0][2]-top(stand))>.0001:
                problems.append(name+" has no contact with its pedestal.")
