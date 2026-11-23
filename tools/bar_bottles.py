"""Reusable moulded bottles and their registered UVs for the backbar and service rig."""
import math

import bar_parts as bp
import interior_kit as kit
from bar_furniture import shell

# Full source envelopes are also used by the bartender's hand and mouth sockets.
# Width, depth, height, shoulder height fraction, neck radius fraction, section exponent.
STYLES = {
    "WaterBottle": (.21, .21, .68, .60, .40, 1.0),
    "BeerLongneck": (.21, .21, .72, .56, .38, 1.0),
    "WineBottle": (.23, .23, .82, .60, .32, 1.0),
    "VodkaBottle": (.25, .18, .67, .55, .37, .39),
    "CognacBottle": (.30, .24, .61, .51, .275, .65),
}
STOCK = ("BeerLongneck", "WineBottle", "VodkaBottle", "CognacBottle")
GLASS = ((.34,.22,.105), (.19,.30,.205), (.50,.61,.61), (.41,.245,.12))


def components(style, center=(0, 0, 0), scale=1., yaw=0., detailed=False):
    width, depth, height, shoulder, neck, exponent = STYLES[style]
    sides = 16 if detailed else 8
    def form(levels, finish):
        rings, coordinates = [], []
        for rise, radius in levels:
            ring = []
            neck_weight = min(1., max(0., (rise-shoulder)/.18))
            shape = exponent + (1.-exponent)*neck_weight
            for side in range(sides):
                angle = math.tau * side / sides + math.pi/2
                dx, dz = math.cos(angle), math.sin(angle)
                x = math.copysign(abs(dx)**shape, dx)*radius*width*.5*scale
                z = math.copysign(abs(dz)**shape, dz)*radius*depth*.5*scale
                ring.append((center[0]+x*math.cos(yaw)+z*math.sin(yaw), center[1]+rise*height*scale,
                             center[2]+z*math.cos(yaw)-x*math.sin(yaw)))
                coordinates.append((side/sides, (rise-levels[0][0])/(levels[-1][0]-levels[0][0])))
            rings.append(ring)
        return shell(rings), coordinates, list(STYLES).index(style), finish

    body = [(0,.88),(.026,.98),(.065,1.),(shoulder,1.),
            (shoulder+.076,.85),(shoulder+.18,neck),( .90,neck),(.945,neck*1.10)]
    if detailed:
        body[3:3] = [(.084,.983),(.10,1.)]
    cap = [(.932,neck*1.08),(.946,neck*1.24),(.985,neck*1.24),(1.,neck*1.05)]
    # Closed paper sleeve ends are inside the solid glass. Only the outer band is visible.
    paper = [(.16,.993),(.163,1.014),(shoulder-.049,1.014),(shoulder-.046,.993)]
    if not detailed:
        # The relief in the small shelf labels and cap crimps is baked into the shared sheet.
        paper = paper[1:3]
        cap = [cap[0], cap[1], cap[-1]]
    return form(body,0), form(cap,1), form(paper,2)


def assign_uv(obj, copies):
    # One mesh may carry a whole row. The seam of each bottle faces the mirror.
    vertices = [(u,v,tile,finish) for _,coords,tile,finish in copies for u,v in coords]
    mesh = obj.data
    uv = mesh.uv_layers[0]
    for polygon in mesh.polygons:
        loops = [(loop,vertices[mesh.loops[loop].vertex_index]) for loop in polygon.loop_indices]
        seam = max(a[0] for _,a in loops) - min(a[0] for _,a in loops) > .5
        for loop,(u,v,tile,finish) in loops:
            if seam and u == 0: u = 1.
            # Two-pixel minimum inset protects the material and printed edge at mips.
            uv.data[loop].uv = ((tile+.034+.932*u)/len(STYLES), (finish+.023+.954*v)/3)


def shelf_stock(add):
    bodies = [[] for _ in STOCK]
    caps, labels = [], []
    for row, level in enumerate((1.55, 2.17)):
        for index in range(18):
            x = -2.74 + index*.37
            if row == 0 and -1.9 < x < .55:
                continue
            kind = (index+row) % len(STOCK)
            style = STOCK[kind]
            height = (.93+((index*7+row)%4)*.038)*STYLES[style][2]*.402
            parts = components(style,(x,level,3.41+.014*math.sin(index*2.)),
                               height/STYLES[style][2], .18*math.sin(index*4.2+row))
            bodies[kind].append(parts[0])
            caps.append(parts[1])
            labels.append(parts[2])
    def batch(name, copies, role, tint, sheet):
        obj = add(name, kit.merge_all([part[0] for part in copies]), role,
                  bp.rgb(*tint), sheet=sheet, shadows=False)
        assign_uv(obj, copies)
    for kind, copies in enumerate(bodies):
        batch("Shelf Bottle "+STOCK[kind], copies, "shelf_bottle_body", GLASS[kind], "BottleGlass")
    batch("Shelf Bottle Caps", caps, "shelf_bottle_closure", (.53,.47,.38), "PaintedMetal")
    batch("Shelf Bottle Labels", labels, "shelf_bottle_label", (.83,.77,.64), "Paper")
