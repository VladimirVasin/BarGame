"""Blender source for the working standpipe, its trough and repair fittings."""
import math


def loft(api, rings, *, closed=False):
    """Join equal vertex rings, capping the ends or returning along an inner wall."""
    width = len(rings[0])
    vertices = [point for ring in rings for point in ring]
    faces = []
    for i in range(len(rings) if closed else len(rings) - 1):
        lo, hi = i * width, (i + 1) % len(rings) * width
        for j in range(width):
            k = (j + 1) % width
            faces.append((lo + j, lo + k, hi + k, hi + j))
    if not closed:
        faces += [tuple(reversed(range(width))), tuple(range(len(vertices) - width, len(vertices)))]
    if api.signed_volume((vertices, faces)) < 0:
        faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def trough_ring(height, xmin, xmax, zmin, zmax, corner):
    c = corner
    return [(x, z, height) for x, z in (
        (xmin + c, zmin), (xmax - c, zmin), (xmax, zmin + c), (xmax, zmax - c),
        (xmax - c, zmax), (xmin + c, zmax), (xmin, zmax - c), (xmin, zmin + c))]


def discharge_elbow(api):
    # The vertical bore opens over the wet chamber. Sweep the inner wall back to the inlet.
    path = [(.64, 1.89, .121), (.36, 1.89, .119), (.13, 1.91, .108),
            (-.16, 1.91, .104), (-.28, 1.88, .101), (-.36, 1.81, .098),
            (-.395, 1.71, .097), (-.40, 1.61, .098), (-.40, 1.595, .090)]
    outer, inner = [], []
    for i, (x, y, r) in enumerate(path):
        before, after = path[max(0, i-1)], path[min(len(path)-1, i+1)]
        dx, dy = after[0] - before[0], after[1] - before[1]
        length = math.hypot(dx, dy)
        for radius, rings in ((r, outer), (r - .022, inner)):
            ring = []
            for side in range(16):
                angle = math.tau * side / 16
                shift = math.sin(angle) * radius / length
                ring.append(api.local_point(x - dy * shift, y + dx * shift, .40 + radius * math.cos(angle)))
            rings.append(ring)
    return loft(api, outer + inner[::-1], closed=True)


def handwheel(api, center=(.74, 1.50, 1.07)):
    # Four open spokes join a rounded rim. The ring has its own chart for thumb wear.
    x, height, z = center
    rings = []
    for i in range(24):
        angle = i * math.tau / 24
        ring = []
        for j in range(8):
            section = j * math.tau / 8
            radius = .216 + .030 * math.cos(section)
            ring.append(api.local_point(x + radius * math.cos(angle), height + radius * math.sin(angle),
                                        z + .029 * math.sin(section)))
        rings.append(ring)
    return loft(api, rings, closed=True)


def build(api, kind):
    # The inner bowl is offset toward the left. Its other end is a solid landing for the column.
    bowl = loft(api, [trough_ring(*r) for r in (
        (.09, -2.85, 1.23, -.31, 1.11, .17), (.15, -2.91, 1.29, -.36, 1.16, .19),
        (.57, -2.94, 1.32, -.375, 1.175, .20), (.61, -2.98, 1.36, -.415, 1.215, .22),
        (.67, -2.98, 1.36, -.415, 1.215, .22), (.70, -2.95, 1.33, -.385, 1.185, .20),
        (.70, -2.76, .33, -.225, 1.025, .18), (.663, -2.73, .30, -.195, .995, .16),
        (.29, -2.67, .24, -.135, .935, .14), (.255, -2.63, .20, -.095, .895, .13))])
    water = loft(api, [trough_ring(h, -2.705, .275, -.17, .97, .16) for h in (.425, .443)])
    masonry = [bowl]
    for x in (-2.38, -.77, .90):
        masonry.append(api.local_box(x, .092, .40, .57, .184, 1.55, .04))
    masonry.append(api.ring_solid_z([(h, .74, .40, r, r, 0) for h, r in
                    ((.686,.44),(.718,.475),(.79,.475),(.835,.423),(.87,.407))], 12))

    cast, zinc, wheel, drains = [], [], [], []
    def turning(shapes, x, z, profile, sides=16):
        shapes.append(api.ring_solid_z([(h,x,z,r,r,0) for h,r in profile], sides))

    turning(cast, .74, .40, ((.853,.33),(.875,.36),(.947,.36),(.975,.31),(1.02,.285),
            (1.12,.248),(1.38,.226),(1.69,.226),(1.81,.248),(1.96,.249),(2.00,.215),
            (2.13,.203),(2.17,.255),(2.22,.258),(2.26,.221),(2.32,.180),(2.36,.107),(2.37,.073)))
    # A plain cast rib and a bolted flange break up the rounded housing.
    for i in range(6):
        angle = i * math.tau / 6
        x, z = .74 + .30*math.cos(angle), .40 + .30*math.sin(angle)
        turning(zinc, x, z, ((.944,.034),(.976,.034),(.980,.028)), 6)
    for z in (.20,.60):
        cast.append(api.local_tube((.82,1.04,z),(.82,2.12,z),.029,8))
    cast.append(discharge_elbow(api))
    cast.append(api.local_tube((.52,1.89,.4),(.45,1.89,.4),.170,16))
    # An actual repair bypass with two wraparound bands, lugs and fasteners.
    zinc.append(api.local_tube((1.085,.88,.40),(1.085,2.06,.40),.053,12))
    zinc.append(api.local_tube((1.085,2.04,.40),(.91,2.13,.40),.055,12))
    for y in (1.22,1.76):
        turning(zinc,.74,.40,((y-.048,.246),(y-.039,.266),(y+.039,.266),(y+.048,.246)))
        zinc.append(api.local_box(1.016,y,.40,.198,.071,.123,.011))
        turning(zinc,1.085,.40,((y-.052,.067),(y+.052,.067)),12)
        zinc.append(api.local_tube((1.01,y,.442),(1.01,y,.493),.026,6))
    cast.append(api.local_tube((.74,1.50,.55),(.74,1.50,.88),.095,12,.075))
    zinc.append(api.local_tube((.74,1.50,.85),(.74,1.50,1.08),.048,12))
    wheel.append(handwheel(api))
    for i in range(4):
        angle = i * math.tau/4 + .30
        cast.append(api.local_tube((.74,1.50,1.03),
            (.74+.21*math.cos(angle),1.50+.21*math.sin(angle),1.07),.021,8))
    zinc.append(api.local_tube((.74,1.50,.99),(.74,1.50,1.10),.056,8))

    # A caged amber maintenance lamp on a metal stay, and a recessed drain with crossbars.
    cast.append(api.local_tube((.83,2.22,.39),(.83,2.75,.39),.043,10))
    cast.append(api.local_tube((.83,2.71,.39),(.83,2.84,.68),.043,10))
    turning(cast,.83,.68,((2.77,.19),(2.80,.193),(2.845,.151),(2.87,.065)))
    lens = api.ring_solid_z([(h,.83,.68,r,r,0) for h,r in
           ((2.602,.084),(2.62,.12),(2.68,.134),(2.762,.124),(2.78,.12))],12)
    turning(cast,.83,.68,((2.595,.098),(2.606,.115),(2.622,.105)))
    for i in range(4):
        a = math.tau*i/4 + math.pi/4
        guard = [(.83+r*math.cos(a),h,.68+r*math.sin(a)) for h,r in
                 ((2.607,.110),(2.646,.157),(2.750,.157),(2.788,.153))]
        for lower, upper in zip(guard, guard[1:]):
            cast.append(api.local_tube(lower, upper, .011, 8))
    for x in (-1.77,-.77,.23):
        # The shallow, dark collector covers the paving under the slotted top.
        drains.append(api.local_box(x,.061,-.93,.247,.012,1.115,.004))
        for side in (-1,1):
            drains.append(api.local_box(x+side*.108,.081,-.93,.034,.038,1.09,.006))
        for j in range(9):
            drains.append(api.local_box(x,.081,-1.427+j*.124,.218,.031,.034,.006))

    geometry, uvs = [], []
    groups = (cast, zinc, wheel, drains)
    for cell, shapes in enumerate(groups):
        for shape in shapes:
            if cell == 2:
                # Radius on the front of the handwheel lines up with the polish in its atlas cell.
                raw = [(shape[0][vi][0]-.74, shape[0][vi][2]-1.50) for face in shape[1] for vi in face]
                center_u, center_v, span = 0., 0., .492
            else:
                raw = api.box_projected_uvs(shape)
                us, vs = zip(*raw)
                center_u, center_v = (min(us)+max(us))*.5, (min(vs)+max(vs))*.5
                span = max(max(us)-min(us), max(vs)-min(vs), .40)
            receiver = cell == 3 and max(p[2] for p in shape[0]) < .07
            for u, v in raw:
                chart_u = .5 + .94*(u-center_u)/span
                chart_v = .5 + .94*(v-center_v)/span
                if cell == 3:
                    # The dark receiver beneath the iron bars has its own pigment band.
                    chart_v = .03 + .12*chart_v if receiver else .22 + .75*chart_v
                uvs.append(((cell%2 + chart_u)*.5, (cell//2 + chart_v)*.5))
            geometry.append(shape)
    return (api.make_part(kind,0,"Masonry",*masonry),
            api.make_named_part(kind,0,"Street","Street",*geometry,authored_uvs=uvs),
            api.make_part(kind,0,"Water",water), api.make_part(kind,0,"LampGlass",lens))
