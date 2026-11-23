"""The disused street cinema. Atlas rectangles use top-left pixel coordinates."""
import math

ATLAS_SIZE = (2048, 1024)
CHARTS = {
    "shift": (0, 0, 512, 768), "balcony": (512, 0, 1024, 768),
    "marquee": (0, 768, 512, 896), "ticket": (512, 768, 768, 896),
    "times": (768, 768, 1024, 896),
    "enamel": (1024, 0, 1536, 256), "zinc": (1536, 0, 2048, 256),
    "iron": (1024, 256, 1536, 512), "wood": (1536, 256, 2048, 512),
    "plaster": (1024, 512, 1536, 768), "stone": (1536, 512, 2048, 768),
    "glass": (1024, 768, 1536, 1024), "paper": (1536, 768, 2048, 1024),
}
for index in range(4):
    CHARTS[f"letter{index}"] = (128*index, 896, 128*(index+1), 1024)


def chart_uv(chart, u, v):
    left, top, right, bottom = CHARTS[chart]
    pad = 5
    return ((left + pad + u*(right-left-2*pad))/ATLAS_SIZE[0],
            1 - (bottom-pad - v*(bottom-top-2*pad))/ATLAS_SIZE[1])


def build(api, variant):
    kind = "NightlifeCinema"
    mirror = 1 if variant == 0 else -1
    groups = {role: ([], []) for role in ("Street", "Fixture", "Masonry")}

    def add(role, shape, material, print_face=None, grain_roll=0):
        shapes, chart = groups[role]
        shapes.append(shape)
        vertices, faces = shape
        lo = tuple(min(v[i] for v in vertices) for i in range(3))
        hi = tuple(max(v[i] for v in vertices) for i in range(3))
        for face in faces:
            normal = api.face_normal(vertices, face)
            axis = max(range(3), key=lambda i: abs(normal[i]))
            pu, pv = ((1, 2), (0, 2), (0, 1))[axis]
            front = print_face and normal[1] > .98
            for index in face:
                p = vertices[index]
                if front:
                    # Camera outside faces local -Z. Screen right is Blender -X in BOTH variants.
                    u = (hi[0]-p[0])/(hi[0]-lo[0])
                    v = (p[2]-lo[2])/(hi[2]-lo[2])
                    chart.append(chart_uv(print_face, u, v))
                else:
                    # Preserve the grain aspect on long rails and small fixings.
                    pitch = max(1.0, hi[pu]-lo[pu], 2*(hi[pv]-lo[pv]))
                    u = .5 + (p[pu]-(lo[pu]+hi[pu])*.5)/pitch
                    v = .5 + 2*(p[pv]-(lo[pv]+hi[pv])*.5)/pitch
                    if material == "wood" and axis == 1:
                        dx, dz = p[0]-(lo[0]+hi[0])*.5, p[2]-(lo[2]+hi[2])*.5
                        angle = math.radians(grain_roll)
                        u = .5 + (dx*math.cos(angle)-dz*math.sin(angle))/2.02
                        v = .5 + (dx*math.sin(angle)+dz*math.cos(angle))/.39
                    chart.append(chart_uv(material, u, v))

    def box(role, mat, x, y, z, w, h, d, bevel=.022, *, roll=0, face=None):
        add(role, api.local_box(-mirror*x, y, z, w, h, d, bevel,
                                roll_forward_degrees=mirror*roll), mat, face, mirror*roll)

    def rod(start, end, radius, mat="iron"):
        def point(p):
            return -mirror*p[0], p[1], p[2]
        add("Street", api.local_tube(point(start), point(end), radius, sides=8), mat)

    def canopy(y, half_width, front, h, mat):
        # Six-sided tier with two clipped corners and a proud, bevelled drip edge.
        corner = .33
        rings = []
        for level, inset in ((y-h/2,.026), (y-h/2+.022,0), (y+h/2-.022,0), (y+h/2,.026)):
            w, f = half_width-inset, front-inset
            rings.append([(x,z,level) for x,z in ((-w,-.07+inset),(w,-.07+inset),
                (w,f-corner),(w-corner,f),(-w+corner,f),(-w,f-corner))])
        vertices = [p for ring in rings for p in ring]
        faces = [tuple(reversed(range(6))), tuple(range(18,24))]
        for row in range(3):
            for i in range(6):
                j = (i+1)%6
                faces.append((row*6+i, row*6+j, (row+1)*6+j, (row+1)*6+i))
        add("Street", (vertices,faces), mat)

    # This frontage sits to one side of the domestic doorway. Its canopy bridges the open approach.
    box("Masonry","plaster", .88,1.45,.105, 7.13,2.90,.28,.045)
    box("Masonry","stone", .88,.17,.23, 7.14,.34,.34,.032)
    for x in (-2.63,-.84,1.27,2.96,4.40):
        box("Masonry","stone",x,1.44,.28,.17,2.88,.29,.025)
        box("Masonry","stone",x,.21,.31,.23,.42,.32,.027)
        box("Masonry","stone",x,2.73,.31,.24,.18,.32)
    canopy(2.91,4.56,1.38,.18,"iron")
    canopy(3.18,4.72,1.48,.42,"enamel")
    canopy(3.43,4.77,1.51,.085,"zinc")
    canopy(2.99,4.70,1.48,.067,"zinc")
    for x in (-3.35,-1.15,1.08,3.38):
        box("Street","iron",x,3.19,1.483,.045,.35,.028,.006)
        rod((x,3.46,1.19),(x,4.32,.08),.035)
        box("Street","zinc",x,4.30,.06,.16,.21,.12,.014)
    # Dead enamel and cut-off lamp sockets. No energized material or sign light.
    box("Fixture","enamel",0,3.20,1.505,2.79,.42,.045,.009,face="marquee")
    for x in (-3.96,-2.65,-1.34,0,1.34,2.65,3.96):
        box("Fixture","iron",x,2.795,1.13,.13,.07,.12,.015)
    for y in (4.00,6.03,8.30):
        box("Street","iron",3.42,y,.10,1.23,.14,.44,.023)
    box("Street","iron",3.42,6.22,.27,1.09,5.58,.44,.067)
    box("Fixture","enamel",3.42,6.22,.51,.91,5.36,.068,.016)
    for i in range(4):
        box("Fixture","enamel",3.42,8.03-i*1.17,.558,.78,1.04,.034,.007,face=f"letter{i}")
    box("Street","zinc",3.42,9.00,.28,1.20,.10,.55)
    box("Masonry","stone",0,8.73,0,8.77,.24,.46,.035)

    # A pair of shut glazed doors, mismatched timber barricade and a bolted hasp.
    box("Masonry","stone",.23,.095,.56,2.08,.19,.83,.035)
    box("Street","iron",.23,1.38,.34,2.04,2.44,.20)
    for x in (-.24,.70):
        box("Street","enamel",x,1.37,.459,.89,2.35,.054,.011)
        box("Fixture","glass",x,1.68,.502,.71,1.32,.035,.006)
        box("Street","iron",x,.63,.508,.72,.54,.033,.006)
    for y,angle,z in ((.94,-6,.56),(1.73,10,.60)):
        box("Street","wood",.23,y,z,1.97,.22,.093,.012,roll=angle)
        for x in (-.62,1.08):
            box("Street","zinc",x,y+(x-.23)*math.tan(math.radians(angle)),z+.052,.039,.039,.017,.004)
    box("Street","iron",.23,1.30,.555,.42,.069,.052,.01)
    box("Street","zinc",.26,1.23,.594,.12,.15,.046,.013)

    # Close-set folded shutter slats and the empty programme rails.
    ticket_x = 3.66
    box("Street","iron",ticket_x,1.36,.33,1.11,1.59,.21,.016)
    for row in range(9):
        box("Fixture","zinc",ticket_x,.81+row*.13,.453,1.00,.125,.064,.012)
    box("Masonry","stone",ticket_x,.68,.44,1.30,.13,.50)
    box("Fixture","enamel",ticket_x,2.26,.404,1.06,.26,.070,.012,face="ticket")
    box("Fixture","enamel",.23,2.65,.421,1.86,.34,.11,.014,face="times")
    for x in (-.32,.23,.78):
        box("Street","iron",x,2.49,.50,.46,.043,.050,.008)

    # Tattered earlier paper projects a few centimetres beyond each last bill.
    for x,film in ((-1.73,"shift"),(2.11,"balcony")):
        box("Street","iron",x,1.50,.38,1.61,2.30,.23,.033)
        box("Fixture","paper",x-.018,1.48,.508,1.46,2.07,.025,.004,roll=1.2)
        box("Fixture","paper",x+.023,1.52,.533,1.38,2.03,.024,.004,roll=-.8)
        # Loss at opposite top corners exposes the bill beneath. Front normals face the street.
        tear = 1 if film == "shift" else -1
        outline = [(-.65,-.975),(.65,-.975),(.65,.70),(.47,.87),(.36,.975),(-.65,.975)]
        rings = [[(-mirror*x + tear*u, z, 1.53+v) for u,v in outline] for z in (.547,.567)]
        count = len(outline)
        faces = [tuple(range(count)),tuple(reversed(range(count,2*count)))]
        for i in range(count):
            j = (i+1)%count
            faces.append((j,i,i+count,j+count))
        verts = rings[0]+rings[1]
        if api.signed_volume((verts,faces)) < 0:
            faces = [tuple(reversed(face)) for face in faces]
        add("Fixture",(verts,faces),"paper",film)
        box("Street","zinc",x,2.66,.42,1.68,.08,.34,.014)
        box("Street","zinc",x,.36,.43,1.61,.071,.29,.012)
    return tuple(api.make_named_part(kind,variant,role,role,*shapes,authored_uvs=uv)
                 for role,(shapes,uv) in groups.items())
