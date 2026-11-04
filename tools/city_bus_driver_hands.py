"""Mirrored, metre-authored hand grips for the 22 mm tube of the bus wheel.

The cylinder frame and one-weight blend are the same contract as NpcHandPose.
The rest shape leaves the index extended for the dashboard button. Equal ring
counts and corresponding phalanx lengths connect that rest shape to the grip.
"""
import math
from mathutils import Vector
from npc_detail_geometry import loft

RIM_RADIUS = .022
WHEEL_RADIUS = .18
SHAPE = "CylindricalGrip"


def _tube(centres, radii, across, sides=8):
    rings = []
    for i, (centre, radius) in enumerate(zip(centres, radii)):
        tangent = (centres[min(i + 1, len(centres) - 1)] - centres[max(0, i - 1)]).normalized()
        u = (across - tangent * tangent.dot(across)).normalized()
        v = tangent.cross(u)
        rings.append([centre + radius * (.87 * math.cos(math.tau * k / sides) * u +
                                          math.sin(math.tau * k / sides) * v) for k in range(sides)])
    return loft(rings)


def build(builder, base, side):
    bone = "hand." + side
    wrist = Vector(base.BONE_BY_NAME[bone].head)
    distal = (Vector(base.BONE_BY_NAME[bone].tail) - wrist).normalized()
    across = Vector((0, 1, 0))
    across = (across - distal * across.dot(distal)).normalized()
    normal = distal.cross(across) * (-1 if side == "L" else 1)
    point = lambda length, width, depth: wrist + distal * length + across * width + normal * depth
    centre = point(.061, 0, .036)
    def mark(label, position):
        builder.create_bone_anchor(f"ANCHOR_Driver{label}.{side}", bone, position, -across)

    mark("GripCentre", centre)
    mark("GripAxis", centre - .05 * across)
    mark("GripPalm", centre + .05 * normal)

    def shaped(name, opened, closed, role, color):
        obj = builder.add_part(name, opened, bone, role, color)
        obj.shape_key_add(name="Basis")
        key = obj.shape_key_add(name=SHAPE)
        for vertex, position in zip(key.data, closed[0]):
            vertex.co = position - obj.location
        return obj

    palm = loft([[point(length, width * math.cos(math.tau * j / 12), depth * math.sin(math.tau * j / 12))
                  for j in range(12)]
                 for length, width, depth in ((-.014, .025, .018), (.012, .033, .020),
                                               (.043, .037, .020), (.066, .032, .014))])
    # Wrist attachment remains at the exact sleeve end. Cup the distal palm
    # around the outer surface of the tube instead of pushing skin through it.
    palm_grip = []
    for p in palm[0]:
        along = (p - wrist).dot(distal)
        excess = (p - wrist).dot(normal) - (.036 - RIM_RADIUS + .001)
        palm_grip.append(p - normal * max(excess, 0) * min(1, max(0, (along - .025) / .018)))
    shaped("GEO_Hand." + side, palm, (palm_grip, palm[1]), "hand_palm", "skin")

    def ring(width, angle, radius):
        # Follow the curvature of the rim along the stack of fingers.
        sag = WHEEL_RADIUS - math.sqrt(WHEEL_RADIUS ** 2 - width ** 2)
        direction = distal * math.sin(angle) - normal * math.cos(angle)
        return centre + width * across + sag * normal + direction * (RIM_RADIUS + radius + .0005)

    def digit(label, name, opened, closed, radii):
        shaped(name + "." + side, _tube(opened, radii, across), _tube(closed, radii, across),
               "hand_thumb" if label == "Thumb" else "hand_finger", "skin")
        mark(label + "Root", closed[0])
        mark(label + "Tip", closed[-1])
        if label == "Finger0" and side == "R":
            mark("IndexOpenTip", opened[-1])
        segments = zip(opened, opened[1:], closed, closed[1:])
        if max(abs((b-a).length - (d-c).length) for a,b,c,d in segments) > .00005:
            raise RuntimeError(f"Driver finger {name}.{side} changes length during grip")

    for i, (width, end_angle, thick) in enumerate(((-.026, 153, .0083), (-.009, 169, .009),
                                                    (.010, 158, .0086), (.027, 141, .0074))):
        radii = [thick * factor for factor in (1, .99, .95, .89, .80, .68)]
        angles = [math.radians(12 + (end_angle - 12) * s / 5) for s in range(6)]
        closed = [ring(width, angle, radius) for angle, radius in zip(angles, radii)]
        opened = [point(.055, width, .001)]
        for j in range(1, len(closed)):
            # The right index extends to press. Its neighbours release into
            # a loose curl, leaving the fingertip clear of the other pads.
            angle = math.radians((3 if i == 0 else 10 + 10*j))
            opened.append(opened[-1] + (distal * math.cos(angle) + normal * math.sin(angle)) *
                          (closed[j] - closed[j-1]).length)
        digit("Finger" + str(i), "GEO_HandFinger" + str(i), opened, closed, radii)

    # The thumb comes around the opposite side of the section. Its distal
    # pad and tip point radially into the wheel, towards the opposite hand.
    radii = (.011, .0115, .0105, .010, .009, .0075)
    closed = [point(.023, -.025, .006), point(.027, -.029, .012)]
    closed += [ring(-.032, math.radians(a), r) for a,r in zip((-58,-83,-108,-134), radii[2:])]
    opened = [closed[0].copy()]
    for j in range(1,len(closed)):
        direction = (distal * .63 - across * .50 + normal * (.10 + .075*j)).normalized()
        opened.append(opened[-1] + direction * (closed[j] - closed[j-1]).length)
    digit("Thumb", "GEO_Thumb", opened, closed, radii)


def validate(result):
    parts = {part.obj.name: part for part in result.parts}
    for side in ("L", "R"):
        def anchor(label):
            return result.anchors[f"ANCHOR_Driver{label}.{side}"].matrix_world.translation
        centre = anchor("GripCentre")
        axis = (anchor("GripAxis") - centre).normalized()
        normal = (anchor("GripPalm") - centre).normalized()
        if abs(axis.dot(normal)) > .0001: raise RuntimeError("Driver grip axes are not perpendicular")
        for digit in ("Finger0","Finger1","Finger2","Finger3","Thumb"):
            name = ("GEO_Thumb" if digit == "Thumb" else "GEO_Hand"+digit) + "." + side
            part = parts[name]
            obj = part.obj
            palm = parts["GEO_Hand." + side]
            if part.color != palm.color or obj.get("bp_detail_kind") != palm.obj.get("bp_detail_kind"):
                raise RuntimeError(f"Driver hand has inconsistent skin tint or detail atlas: {name}")
            if part.bone != "hand."+side: raise RuntimeError(f"Driver digit {name} attached to wrong hand")
            closed = obj.data.shape_keys.key_blocks[SHAPE]
            vertices = [obj.matrix_world @ row.co for row in closed.data]
            root = sum(vertices[:8], Vector()) / 8
            tip = sum(vertices[-8:], Vector()) / 8
            if max((root-anchor(digit+"Root")).length, (tip-anchor(digit+"Tip")).length) > .00005:
                raise RuntimeError(f"Closed rendered pad disagrees with its landmark: {name}")
            if digit == "Thumb" and (tip-root).dot(normal) < .035:
                raise RuntimeError("Driver thumb does not point toward opposite hand")
            if digit != "Thumb" and (tip-root).dot(normal) < .045:
                raise RuntimeError("Driver finger does not wrap past the tube centre")
            def rim_gap(p):
                offset = p - centre
                axial = offset.dot(axis)
                sag = WHEEL_RADIUS - math.sqrt(WHEEL_RADIUS**2 - axial**2)
                return (offset - axis*axial - normal*sag).length - RIM_RADIUS
            gap = min(rim_gap(p) for p in vertices[-8:])
            if not -.003 < gap < .006:
                raise RuntimeError(f"Driver {name} fingertip misses rim: {gap:.5f}m")
            if digit == "Finger0" and side == "R":
                open_tip = sum((obj.matrix_world @ v.co for v in obj.data.vertices[-8:]), Vector()) / 8
                if (open_tip-anchor("IndexOpenTip")).length > .00005:
                    raise RuntimeError("Button fingertip disagrees with extended index")
