"""Five remnants of the municipal shelter. Coordinates are in the POI recipe's metres."""
import math

SIZE = 1024
CHARTS = {
    "departures": (0, 0, 512, 256), "transfer": (512, 0, 768, 256), "bill": (768, 0, 1024, 256),
    "roof": (0, 256, 512, 512), "enamel": (512, 256, 768, 512), "zinc": (768, 256, 1024, 512),
    "wood": (0, 512, 512, 768), "end": (512, 512, 768, 768), "iron": (768, 512, 1024, 768),
    "timetable": (0, 768, 256, 1024), "cloth": (256, 768, 512, 1024),
    "glass": (512, 768, 768, 1024), "paper": (768, 768, 1024, 1024),
}


def build(api):
    kind = "PoiNightlifeLastRouteIslandShell"
    shapes = {role: [] for role in ("Masonry", "Street", "Residential", "Timber")}
    uvs = {role: [] for role in shapes}
    origin = (0, 0, 0)
    yaw = 0

    def place(role, chart, shape, front=None, top=None, grain=0.):
        # Chart in the component frame before yawing it into the ring.
        verts, faces = shape
        lo, hi = api.geometry_bounds(shape)
        shapes[role].append(api.transform_geometry(shape, origin, yaw_up_degrees=yaw))
        for face in faces:
            normal = api.face_normal(verts, face)
            axis = max(range(3), key=lambda i: abs(normal[i]))
            a, b = ((1, 2), (0, 2), (0, 1))[axis]
            tile = chart
            if front and normal[1] > .97:
                tile = front
            if top and normal[2] > .65:
                tile = top
            if chart == "wood" and axis == 0:
                tile = "end"
            l, t, r, d = CHARTS[tile]
            for i in face:
                u = (verts[i][a]-lo[a])/max(.0001, hi[a]-lo[a])
                v = (verts[i][b]-lo[b])/max(.0001, hi[b]-lo[b])
                if front and normal[1] > .97:
                    u = 1-u  # Looking back toward the board from local +Z.
                if tile == "wood":
                    v = grain + .30*v
                uvs[role].append(((l+5+u*(r-l-10))/SIZE, 1-(d-5-v*(d-t-10))/SIZE))

    def box(mat, x, y, z, w, h, dep, bevel=.018, role="Street", **charts):
        place(role, mat, api.local_box(x,y,z,w,h,dep,bevel), **charts)

    def rod(mat, a, b, radius, end=None):
        place("Street", mat, api.local_tube(a,b,radius,8,end_radius=end))

    def profile(points, depth, z, bevel=.012):
        # Extrude a clipped X/height outline; bevel both visible arrises.
        cx = sum(x for x,y in points)/len(points)
        cy = sum(y for x,y in points)/len(points)
        sx = max(abs(x-cx) for x,y in points)
        sy = max(abs(y-cy) for x,y in points)
        rings = [[(cx+(x-cx)*(1-inset/sx), level, cy+(y-cy)*(1-inset/sy)) for x,y in points]
                 for level,inset in ((z-depth/2,bevel),(z-depth/2+bevel,0),
                                      (z+depth/2-bevel,0),(z+depth/2,bevel))]
        return solid(rings)

    def solid(rings):
        n = len(rings[0])
        faces = [tuple(reversed(range(n))), tuple(range((len(rings)-1)*n,len(rings)*n))]
        for row in range(len(rings)-1):
            for i in range(n):
                j = (i+1)%n
                faces.append((row*n+i,row*n+j,(row+1)*n+j,(row+1)*n+i))
        mesh = ([v for ring in rings for v in ring],faces)
        if api.signed_volume(mesh) < 0:
            return mesh[0], [tuple(reversed(face)) for face in faces]
        return mesh

    # Mortared bed, splayed flags and a deeper dressed curb. Running joints
    # end at each concentric course, below the tops of the adjacent stones.
    ground = shapes["Masonry"]
    ground.append(api.ring_solid_z([(h,0,0,r,r,0) for h,r in
        ((.02,5.35),(.045,5.39),(.173,5.39),(.181,5.37))], 64))
    ground.append(api.ring_solid_z([(h,0,0,r,r,0) for h,r in
        ((.18,.67),(.261,.67),(.275,.65))], 16))
    for row,(inner,outer,count,height) in enumerate(((.67,1.58,10,.271),(1.58,2.50,15,.259),
            (2.50,3.39,20,.244),(3.39,4.27,24,.233),(4.27,4.98,26,.218),(4.98,5.40,32,.212))):
        for i in range(count):
            start = (i+.39*(row%2))*math.tau/count
            end = start+math.tau/count
            rings = []
            for h,inset in ((.176,.005),(height-.012,0),(height,.013)):
                r0, r1 = inner+.006+inset, outer-.006-inset
                gap = (.007+inset)/((r0+r1)*.5)
                angles = (start+gap,(start+end)*.5,end-gap)
                rings.append([(r*math.cos(a),r*math.sin(a),h)
                    for r,arc in ((r1,angles),(r0,reversed(angles))) for a in arc])
            ground.append(solid(rings))

    # Folded sheet bays sit above the truss. The open fifth/right and third/left
    # bays leave the original cloth attachment rails and all five gaps intact.
    for index,angle in enumerate((48,102,168,226,292)):
        yaw = angle
        origin = (4.70*math.sin(math.radians(yaw)),4.70*math.cos(math.radians(yaw)),0)
        box("iron",0,.15,0,.41,.30,.43,.025)
        box("zinc",0,.33,0,.39,.075,.42)
        rod("enamel",(0,.34,0),(0,3.42,0),.144,.106)
        box("zinc",0,2.12,0,.26,.085,.26,.009)
        for side in (-1,1):
            rod("enamel",(0,2.72,0),(side*1.36,3.47,0),.066,.043)
            rod("zinc",(0,2.99,0),(0,3.50,side*.64),.055,.038)
            box("iron",0,3.49,side*.52,3.63,.094,.15,.015)
        box("enamel",0,3.47,0,3.72,.16,.22)
        # Camber and kicked eaves give the surviving roof a folded silhouette.
        for bay in range(4):
            if (index,bay) in ((2,0),(4,3)):
                continue
            left = -1.85+bay*.925+.012
            right = left+.902
            section = [(-.91,3.81),(-.82,3.84),(.10,3.62),(.77,3.76),(.86,3.73),
                       (.86,3.67),(.77,3.70),(.10,3.55),(-.82,3.77),(-.91,3.74)]
            place("Street","roof",solid([[(x,z,h) for z,h in section] for x in (left,right)]))
            for z,h in ((-.87,3.755),(.81,3.69)):
                box("zinc",(left+right)/2,h,z,right-left,.055,.078,.009)
        for x in (-1.86,-.925,0,.925,1.86):
            for z,h in ((-.86,3.74),(.80,3.67)):
                rod("enamel",(x,3.50,0),(x,h,z),.043)
        if index in (1,4):
            box("iron",0,2.43,.15,.61,.51,.075)
            box("enamel",0,2.43,.199,.56,.46,.034,.006,role="Residential",
                front="transfer" if index==1 else "bill")

    # Cast shoe, telescoping mast and damaged route marker. Its missing corner
    # exposes a back rail. The speaker and lamp stay at their sound/light mounts.
    yaw, origin = 0, (-2.75,-1.25,0)
    box("iron",0,.41,0,1.02,.38,.94,.055)
    for h,w in ((.62,.81),(.73,.64)):
        box("zinc",0,h,0,w,.13,w,.026)
    rod("enamel",(0,.74,0),(0,5.48,0),.202,.141)
    for h in (1.05,3.74,4.57):
        box("zinc",0,h,0,.36,.095,.36)
    for x in (-.33,.33):
        for z in (-.31,.31):
            box("iron",x,.704,z,.10,.056,.10,.009)
    outline = [(-.84,4.84),(.81,4.84),(.81,5.74),(.46,5.72),(.31,5.95),
               (.08,5.93),(.08,6.29),(-.71,6.29),(-.84,6.16)]
    place("Street","zinc",profile(outline,.29,0,.027))
    place("Residential","enamel",profile(outline,.045,.162,.006),front="transfer")
    place("Residential","enamel",profile(outline,.035,-.159,.006))
    for h in (5.19,6.07):
        box("iron",0,h,-.19,1.54,.073,.08)
    box("iron",0,4.18,.22,.74,.42,.25,.044)
    for j in range(5):
        box("zinc",0,4.035+.067*j,.352,.60,.032,.045,.007)
    rod("zinc",(.06,4.35,0),(.37,4.46,.18),.048)
    # Orient the body to the same target as the runtime lens (+Z is its optical axis).
    head = api.local_box(0,0,-.14,.48,.31,.30,.036)
    head = api.transform_geometry(head,(.35,.20,4.42),pitch_x_degrees=39.47,yaw_up_degrees=59.56)
    place("Street","iron",head)

    yaw, origin = -12, (2.45,-2.55,0)
    for x in (-.87,.87):
        box("iron",x,.26,0,.44,.12,.40)
        box("zinc",x,.36,0,.26,.10,.29)
        box("enamel",x,1.09,0,.134,1.43,.16)
        rod("iron",(x,.99,0),(x+(.28 if x<0 else -.28),1.65,0),.035)
    box("iron",0,2.10,0,2.68,1.23,.26,.055)
    box("enamel",0,2.065,.155,2.53,1.045,.073,.015,role="Residential",front="departures")
    box("zinc",0,2.73,.046,2.80,.087,.45,.019)
    for x in (-1.29,1.29):
        box("enamel",x,2.11,.15,.053,1.17,.082,.009)
    # Scraps of earlier bills remain on the back of the case.
    for x,h,w,chart in ((-.67,.72,.90,"bill"),(.04,.83,.68,"transfer"),(.64,.61,.52,"timetable")):
        scraps = [(x-w/2,2.51),(x+w/2,2.51),(x+w/2,2.51-h*.77),
                  (x+.19*w,2.51-h*.79),(x+.07*w,2.51-h),(x-.10*w,2.51-h*.87),(x-w/2,2.51-h*.9)]
        piece = profile(scraps,.019,.151,.003)
        # Chart the outward face in its own frame before turning it toward the rear approach.
        yaw += 180
        place("Residential","paper",piece,front=chart)
        yaw -= 180

    # Backless dock bench. The broad top remains at the authored seat contact .77.
    yaw, origin = 22, (2.85,2.55,0)
    for x in (-.83,.83):
        box("iron",x,.264,0,.29,.11,.62)
        for z in (-.21,.21):
            rod("enamel",(x,.29,z),(x,.65,z*.76),.058)
        box("iron",x,.657,0,.15,.11,.70)
    for i,z in enumerate((-.272,-.091,.091,.272)):
        box("wood",0,.72,z,2.50,.10,.170,.013,role="Timber",grain=(.04,.53,.24,.65)[i])
        for x in (-.83,.83):
            box("iron",x,.770,z,.047,.009,.047,.002)
    # Open ribbed basket with a rolled lip and a recessed liner.
    yaw, origin = 8, (4.70,1.28,0)
    place("Street","iron",api.local_vertical_solid(0,.20,.35,0,.32,.32,sides=12))
    place("Street","iron",api.local_vertical_solid(0,.34,.89,0,.263,.263,sides=12))
    for i in range(12):
        a = i*math.tau/12
        c,s = math.cos(a),math.sin(a)
        rod("enamel",(.27*c,.33,.27*s),(.36*c,1.18,.36*s),.041)
        b = (i+1)*math.tau/12
        rod("zinc",(.36*c,1.18,.36*s),(.36*math.cos(b),1.18,.36*math.sin(b)),.043)

    yaw, origin = 0, (0,0,0)
    bottle = api.ring_solid_z([(h,0,0,r,r,0) for h,r in
        ((0,.052),(.014,.064),(.19,.064),(.231,.050),(.257,.027),(.303,.026),(.311,.033),(.328,.031))],10)
    for x,y,z,pitch,angle in ((2.08,.22,3.82,0,0),(1.72,.285,3.68,90,28)):
        place("Residential","glass",api.transform_geometry(bottle,(x,z,y),pitch_x_degrees=pitch,yaw_up_degrees=angle))
    # Folded paper and a long knit scarf, following the local crown of the paving.
    yaw, origin = 12, (-1.20,-3.65,.234)
    place("Residential","paper",solid([[(x,z,h+thick) for x,z,h in
        ((-.33,-.24,.006),(.32,-.23,.004),(.32,.14,.015),(.16,.23,.033),(-.32,.20,.047))]
        for thick in (0,.009)]),top="timetable")
    yaw, origin = -18, (-.35,1.05,.273)
    for x,z,length,angle,level in ((-.17,.01,.76,-7,.013),(.32,.085,.38,24,.026)):
        piece = api.local_box(x,level,z,length,.026,.253,.005,yaw_up_degrees=angle)
        place("Residential","cloth",piece)
    return (api.make_part(kind,0,"Masonry",*ground), *(api.make_named_part(
        kind,0,role,role,*shapes[role],authored_uvs=uvs[role]) for role in ("Street","Residential","Timber")))
