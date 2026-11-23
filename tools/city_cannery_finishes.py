"""Metre-space formed parts and protected UV charts for the working cannery."""
import math

SIZE = (2048, 1024)
CHARTS = ("precast", "jade", "zinc", "cream", "cast", "brushed", "tin", "deck",
          "dial", "kraft", "cut", "rubber", "wood", "end", "cloth", "tile")


class Batch:
    """Keep passive pieces together; a moving assembly gets its own local batch."""
    def __init__(self, port):
        self.port = port
        self.geometry = port.Geometry()
        self.charts = []

    def box(self, chart, centre, size, bevel=.012):
        start = len(self.geometry.faces)
        self.port.chamfer(self.geometry, centre, size, self.port.CABIN,
                          min(bevel, min(size) * .21))
        self.charts.extend([chart] * (len(self.geometry.faces) - start))

    def rod(self, chart, a, b, radius, sides=10, tip=None):
        start = len(self.geometry.faces)
        self.geometry.rod(a, b, radius, self.port.CABIN, sides, end_radius=tip)
        self.charts.extend([chart] * (len(self.geometry.faces) - start))

    def band(self, chart, centre, radius, thickness, normal, sides=16):
        start = len(self.geometry.faces)
        self.port.ring(self.geometry, centre, radius, thickness, self.port.CABIN, normal, sides)
        self.charts.extend([chart] * (len(self.geometry.faces) - start))

    def solid(self, chart, vertices, faces):
        self.geometry.add(vertices, faces, self.port.CABIN)
        self.charts.extend([chart] * len(faces))

    def publish(self, parent, name, material):
        obj = self.geometry.object(name + "__CanneryFinish", parent, material)
        mesh = obj.data
        uv = mesh.uv_layers.active.data
        for polygon, chart in zip(mesh.polygons, self.charts, strict=True):
            dominant = max(range(3), key=lambda i: abs(polygon.normal[i]))
            a, b = ((1, 2), (0, 2), (0, 1))[dominant]
            points = [mesh.vertices[i].co for i in polygon.vertices]
            # The dial is read looking towards -X: camera right points along +Z.
            if chart == "wood" and dominant == 1:  # crosscut at the ends along Blender Y
                chart = "end"
            if chart == "wood" and dominant == 2:
                a, b = b, a
            lo = tuple(min(point[i] for point in points) for i in (a, b))
            span = tuple(max(point[i] for point in points)-low for i,low in zip((a,b),lo))
            pitch = {"precast":3., "wood":2., "tin":.15, "kraft":.75, "cut":.35}.get(chart,1.35)
            # Fit tiny chamfer faces as well; their UV polygon must survive FBX precision.
            pitch = min(pitch, max(*span,.002)*4)
            width, height = (max(pitch,span[0]),max(pitch*.5,span[1]))
            if chart == "dial":
                width, height = span
            column, row = CHARTS.index(chart)%4, CHARTS.index(chart)//4
            for loop, point in zip(polygon.loop_indices,points):
                u = .5 + (point[a]-lo[0]-span[0]*.5)/max(width,1.e-8)
                v = .5 + (point[b]-lo[1]-span[1]*.5)/max(height,1.e-8)
                uv[loop].uv = ((column*512+5+u*502)/SIZE[0], 1-(row*256+251-v*246)/SIZE[1])
        return obj


def hall(port, root, mat):
    b = Batch(port)
    # Precast plinth units end at each real opening. Cut ends return around the jambs.
    for x, runs in ((.13, [(-7,-6.5),(-4.5,3.9),(6.1,7)]),
                    (-8.13, [(-7,1.95),(3.35,7)])):
        for start, end in runs:
            count = math.ceil((end-start)/1.55)
            for n in range(count):
                width = (end-start)/count
                z = start + (n+.5)*width
                b.box("precast", (x,.365,z), (.099,.56,width-.016), .021)
                b.box("jade", (x,.667,z), (.113,.065,width-.012))
    for z in (-7.14, 7.14):
        for start, end in ((-8,-1.95),(-.25,0)):
            count = math.ceil((end-start)/1.55)
            for i in range(count):
                width = (end-start)/count
                x = start+(i+.5)*width
                b.box("precast", (x,.36,z), (width-.015,.55,.10), .019)
                b.box("jade", (x,.667,z), (width-.012,.065,.114))
        # End fascia in three lengths; standing seams turn over the drip edge.
        for i in range(3):
            x = -4+(i-1)*2.86
            b.box("jade", (x,4.12,z), (2.84,.26,.18), .017)
            b.box("zinc", (x,4.254,z), (2.85,.055,.22))
    for side in (-8.13,.13):
        for i in range(5):
            z = -5.8+i*2.9
            b.box("jade", (side+(.16 if side>0 else -.16),4.13,z), (.12,.25,2.88))
            b.box("zinc", (side+(.16 if side>0 else -.16),4.266,z), (.16,.045,2.89))
    # The end bays are warm washed precast between steel mullions. The
    # sign and public doors keep their own recesses beyond the panel faces.
    for side in (-1,1):
        z=7.125*side
        for i in range(4):
            x=-7.23+i*1.48
            b.box("precast",(x,1.61,z),(1.45,1.81,.105),.029)
            b.box("cream",(x,2.576,z),(1.45,.068,.114))
        b.box("jade",(-2.10,1.71,z),(.085,1.99,.135))
    # A folded weather hood and louvers over the cold-store roof. Slats face
    # the roadway; the sanitary exhaust still uses its own anchored flue.
    b.box("zinc",(-6.35,4.328,-4.78),(1.62,.13,2.25))
    b.box("cast",(-6.35,4.52,-4.78),(1.41,.31,2.04))
    for y in (4.43,4.50,4.57,4.64):
        for x in (-7.075,-5.625):
            b.box("jade",(x,y,-4.78),(.067,.033,1.98),.006)
    b.box("zinc",(-6.35,4.718,-4.78),(1.63,.092,2.21))
    # Sloped loading hoods and real end brackets, entirely above head height.
    for z,w in ((-5.5,2.45),(5.,2.65)):
        for i in range(3):
            lo,hi = z-w/2+i*w/3+.008,z-w/2+(i+1)*w/3-.008
            points = [(x,y+dy,zz) for dy in (0,.065) for x,y,zz in
                      ((.12,3.88,lo),(.38,3.76,lo),(.38,3.76,hi),(.12,3.88,hi))]
            b.solid("zinc", points, [(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)])
        b.box("jade", (.367,3.73,z), (.061,.12,w+.055))
        for zz in (z-w*.41,z+w*.41):
            b.rod("cast",(.16,3.30,zz),(.348,3.74,zz),.032)
            b.box("cast", (.157,3.38,zz), (.086,.23,.105))
        for zz in (z-w*.43,z+w*.43):
            b.box("jade", (.158,1.64,zz), (.14,2.86,.080))
    # Recessed window stops, glazing bars, sloped sill edges and riveted shoes.
    for z in (-2.5,.65):
        b.box("zinc", (.11,.815,z), (.28,.08,2.81))
        for dz in (-1.31,0,1.31):
            b.box("jade", (.079,1.99,z+dz), (.088,2.36,.061))
        b.box("jade", (.080,2.65,z), (.089,.057,2.69))
    # Strapped pipe below the flue, with its outlet and steam anchor clear.
    for h in (3.71,4.65):
        b.band("zinc",(-7.65,h,1.5),.148,.019,(0,1,0))
    for z in (-3.7,-.9,2.2,5.2):
        for x in (-4.97,-3.43):
            b.box("cream",(x,3.73,z),(.077,.13,.254))
    b.publish(root,"HallFoldedMetalAndPlinth",mat)


def ramp(port, root, mat):
    b = Batch(port)
    for z,w in ((-5.5,2.),(5.,2.2)):
        # Inset angle irons follow the 1:15 concrete surface, including the toe.
        for side in (-1,1):
            zz = z+side*(w*.5-.026)
            b.rod("zinc",(.015,.180,zz),(1.48,.083,zz),.019,8)
        for offset in (-.22,.22):
            # Short scuffs end within the slab used by the two fork load wheels.
            for x in (.32,.79,1.17):
                x1=x+.18
                a=z+offset
                pts=[(xx,.184-xx/15+dy,zz) for dy in (-.003,0) for xx,zz in
                     ((x,a-.018),(x1,a-.012),(x1+.025,a+.014),(x+.02,a+.020))]
                b.solid("rubber",pts,
                        [(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)])
    b.publish(root,"RampEdgeAndWheelScuffs",mat)


def scale(port, parent, mat):
    b=Batch(port)
    # The 32 cm load plane remains continuous; the flush deck replaces its top.
    for i in range(3):
        b.box("deck",(.92,.298,6.95+(i-1)*.40),(1.405,.044,.391),.009)
    b.box("jade",(.42,.74,7.55),(.105,.98,.16))
    b.box("jade",(.43,1.29,7.55),(.141,.16,.21))
    b.rod("jade",(.335,1.57,7.56),(.447,1.57,7.56),.208,20)
    b.rod("dial",(.445,1.57,7.56),(.465,1.57,7.56),.169,24)
    b.band("zinc",(.453,1.57,7.56),.182,.014,(1,0,0),24)
    for z in (6.42,7.46):
        for x in (.27,1.57):
            b.box("cast",(x,.132,z),(.13,.104,.13))
    b.publish(parent,"ScaleHeadAndDeck",mat)


def machines(port, root, mat):
    b=Batch(port)
    # A rolled rim, splashback and draining ledge keep the worker's west hand
    # and the arriving crate clear. Waste leaves through the actual drain.
    b.box("brushed",(-5,1.277,-2.635),(2.19,.158,.049),.023)
    for z in (-2.636,-1.463):
        b.rod("brushed",(-6.09,1.227,z),(-3.92,1.227,z),.021)
    for x in (-6.105,-3.895):
        b.rod("brushed",(x,1.227,-2.62),(x,1.227,-1.48),.020)
    for i in range(6):
        b.rod("brushed",(-4.28+i*.053,.966,-2.42),(-4.28+i*.053,.966,-1.7),.012,8)
    for h in (1.28,1.45):
        b.band("zinc",(-5.75,h,-2.57),.041,.012,(0,1,0))
    b.rod("jade",(-5.75,1.535,-2.22),(-5.75,1.458,-2.22),.045)
    # Raised hopper collar follows the taper; the low spout remains above the cans.
    b.band("brushed",(-5.72,2.10,-.35),.451,.031,(0,1,0),18)
    for x in (-5.93,-5.51):
        b.rod("jade",(x,1.18,-.78),(x,1.70,-.63),.047)
    b.box("jade",(-4.36,2.19,-.47),(.64,.23,.91),.046)
    for z in (-.73,-.59,-.45,-.31):
        b.box("cast",(-4.027,2.20,z),(.045,.081,.066))
    for z in (.94,2.15):
        b.band("zinc",(-5,1.35,z),.808,.019,(0,0,1),24)
        for side in (-1,1):
            x=-5+side*.56
            b.box("jade",(x,.42,z),(.27,.46,.22),.03)
            b.box("cast",(x,.219,z),(.42,.075,.35))
    b.band("jade",(-5,1.35,2.789),.799,.031,(0,0,1),24)
    b.rod("dial",(-4.192,1.9,1.28),(-4.183,1.9,1.28),.117,20)
    b.band("zinc",(-4.185,1.9,1.28),.135,.010,(1,0,0),20)
    for z in (-2.30,2.45):
        for zz in (z-.104,z+.104):
            b.box("brushed",(-4.9,.199,zz),(3.92,.029,.037))
    b.publish(root,"WashAndProductionFittings",mat)


def retort_cover(port,door,mat):
    b=Batch(port)
    # Shallow drawn cover behind the handwheel, inside the sliding shoes.
    b.rod("jade",(.82,0,.068),(.82,0,.096),.748,24,tip=.657)
    b.rod("jade",(.82,0,.095),(.82,0,.128),.655,24,tip=.305)
    b.rod("zinc",(.82,0,.129),(.82,0,.145),.095,12,tip=.076)
    for n in range(8):
        angle=(n+.5)*math.tau/8
        x,y=.82+.70*math.cos(angle),.70*math.sin(angle)
        b.rod("zinc",(x,y,.083),(x,y,.10),.027,6)
    b.publish(door,"DrawnRetortCover",mat)


def transport(port, truck, mat):
    b=Batch(port)
    for side in (-1,1):
        x=side*1.193
        for z in (-1.14,-.18,.78,1.74):
            b.box("zinc",(x,2.16,z),(.012,1.78,.035),.003)
        b.box("jade",(x,1.54,.39),(.012,.235,3.56),.003)
        for y in (1.41,2.99):
            b.box("zinc",(x,y,.39),(.012,.043,3.56),.002)
    b.box("cast",(0,1.995,3.898),(1.36,.09,.087),.017)
    for x in (.13,.37):
        b.rod("rubber",(x,1.49,3.35),(x,1.75,3.42),.022)
        b.rod("cast",(x,1.744,3.42),(x,1.79,3.43),.033)
    b.publish(truck,"CargoSkinAndCabControls",mat)


def doors(port, pivot, sign, mat):
    b=Batch(port)
    for h in (.30,1.12,1.68):
        b.box("zinc",(sign*.755,h,-.068),(.124,.083,.055))
    b.rod("rubber",(sign*.55,.68,-.099),(sign*.68,.717,-.092),.026,8)
    b.publish(pivot,"RearDoorClamps"+("L" if sign>0 else "R"),mat)


def bench(port, yard, mat):
    seat=port.empty("FactoryRestBench",yard,(.65,.08,-2.25))
    b=Batch(port)
    for i in range(4):
        x=(i-1.5)*.128
        b.box("wood",(x,.484,0),(.120,.072,1.85),.012)
        for z in (-.66,.66):
            b.rod("cast",(x,.510,z),(x,.521,z),.012,8)
    for z in (-.66,.66):
        b.box("jade",(0,.419,z),(.5,.058,.102))
        for x in (-.18,.18):
            b.box("cast",(x,.218,z),(.055,.40,.077))
            b.box("zinc",(x,.027,z),(.113,.054,.154))
    b.publish(seat,"RestSeatAndFrame",mat)


def goods(port,parent,name,mat):
    b=Batch(port)
    if name=="Pallet":
        for side in (-1,1):
            for z in (-.34,0,.34):
                b.box("jade",(side*.369,.47,z),(.023,.49,.039))
            b.box("zinc",(0,.76,side*.552),(.16,.078,.029))
        for x in (-.32,-.16,0,.16,.32):
            for z in (-.597,.597):
                b.box("end",(x,.15,z),(.131,.034,.006),.001)
    elif name=="RetortBasket":
        for x in (-.47,.47):
            for z in (-.48,.48):
                b.rod("brushed",(x,.053,z),(x,.287,z*.69),.016,8)
            b.rod("brushed",(x,.602,-.49),(x,.602,.49),.025)
    elif name=="CanTray":
        for x in (-.369,.369):
            b.box("tin",(x,.036,0),(.018,.025,.443),.004)
    elif name=="Trolley":
        for x in (-.32,.32):
            b.rod("rubber",(x-.064,1.08,-.88),(x+.064,1.08,-.88),.035)
        b.rod("zinc",(-.142,.11,-.85),(.142,.11,-.85),.038)
        b.box("jade",(0,.336,-.769),(.24,.056,.19))
    b.publish(parent,name+"Fittings",mat)


def carton(port,body,mat):
    b=Batch(port)
    for x in (-.126,.126):
        b.box("kraft",(x,.149,0),(.005,.282,.693),.001)
        b.box("cut",(x,.294,0),(.007,.004,.692),.0006)
    for z in (-.347,.347):
        b.box("kraft",(0,.150,z),(.238,.28,.007),.001)
    b.publish(body,"CorrugatedPanels",mat)


def flap(port,pivot,sign,mat):
    b=Batch(port)
    b.box("kraft",(-sign*.064,.004,0),(.124,.003,.696),.0005)
    b.box("cut",(-sign*.126,.0001,0),(.002,.006,.693),.0004)
    b.publish(pivot,"FoldEdge"+("Left" if sign<0 else "Right"),mat)


def crew(port,root,mat):
    b=Batch(port)
    for y in (1.7,2.18):
        b.box("jade",(-7.587,y+.006,.25),(.019,.052,.948))
    for x in (-7.505,-7.295):
        b.box("cloth",(x,1.51,-2.743),(.124,.115,.013),.0025)
        b.rod("cloth",(x-.058,1.563,-2.737),(x+.058,1.563,-2.737),.006,8)
    for i in range(3):
        b.box("wood",(-7.52+(i-1)*.139,.683,.75),(.133,.034,.397),.006)
    b.publish(root,"CrewShelfCoatAndSeat",mat)


def source_materials(bpy, folder):
    result={}
    for role,stem,gain,tint in (("CanneryFinish","CanneryFittings",1,(1,1,1)),
                                 ("WashWall","CanneryGlaze",1,(1,1,1)),
                                 ("CanneryFloor","CanneryResin",.62,(1,1,1)),
                                 ("WetFloor","CanneryResin",1,(.68,.78,.74)),
                                 ("Stainless","CanneryWorkSteel",1,(1,1,1))):
        material=bpy.data.materials.new("Formed_"+role)
        material.use_nodes=True
        tree=material.node_tree
        shader=tree.nodes.get("Principled BSDF")
        def texture(suffix):
            node=tree.nodes.new("ShaderNodeTexImage")
            node.image=bpy.data.images.load(str(folder/(stem+suffix+".png")),check_existing=True)
            node.extension="EXTEND" if role=="CanneryFinish" else "REPEAT"
            if suffix=="Mask":
                node.image.colorspace_settings.name="Non-Color"
            if not node.image.packed_file:
                node.image.pack()
            return node
        colour,mask=texture("Albedo"),texture("Mask")
        mix=tree.nodes.new("ShaderNodeMixRGB")
        mix.blend_type="MULTIPLY"
        mix.inputs[0].default_value=1
        mix.inputs[2].default_value=(*tint,1)
        tree.links.new(colour.outputs["Color"],mix.inputs[1])
        tree.links.new(mix.outputs[0],shader.inputs["Base Color"])
        channels=tree.nodes.new("ShaderNodeSeparateColor")
        tree.links.new(mask.outputs["Color"],channels.inputs[0])
        tree.links.new(channels.outputs["Red"],shader.inputs["Metallic"])
        roughness=tree.nodes.new("ShaderNodeMath")
        roughness.operation="MULTIPLY_ADD"
        roughness.inputs[1].default_value=-gain
        roughness.inputs[2].default_value=1
        tree.links.new(mask.outputs["Alpha"],roughness.inputs[0])
        tree.links.new(roughness.outputs[0],shader.inputs["Roughness"])
        result[role]=material
    return result
