"""Shared finite bowl-grasp profiles; the original wrist and phalange lengths stay fixed."""
from __future__ import annotations

import math
from mathutils import Vector
from mathutils.geometry import closest_point_on_tri

VARIANTS = (("WineBowlGrip", .037), ("SnifterBowlGrip", .046))


def outside_surface_gap(point, triangles):
    candidates=[]
    for a,b,c in triangles:
        nearest=closest_point_on_tri(point,a,b,c)
        delta=point-nearest
        candidates.append((delta.length,delta.dot((b-a).cross(c-a))))
    distance,signed=min(candidates,key=lambda item:item[0])
    return distance if signed>0 else 0.


def finger_centres(points, centre, direction, normal, radius, thickness):
    """Open the same finger around the larger surface, retaining every link length."""
    contact_radius = radius + thickness + .0025
    delta = points[0] - centre
    angle = math.atan2(delta.dot(direction), -delta.dot(normal))
    angles = [angle]
    for before, after in zip(points, points[1:]):
        angles.append(angles[-1] + 2 * math.asin(min(1., (after-before).length / (2*contact_radius))))
    axial = delta - direction * delta.dot(direction) - normal * delta.dot(normal)
    return [centre + axial + direction * (contact_radius*math.sin(value)) -
            normal * (contact_radius*math.cos(value)) for value in angles]


def thumb_centres(points, centre, direction, normal, radius, thickness):
    """Oppose the fingers with a two-link thumb, keeping its embedded base fixed."""
    start = points[0]
    first = (points[1]-start).length
    second = (points[2]-points[1]).length
    axial = points[-1]-centre
    axial -= direction*axial.dot(direction)+normal*axial.dot(normal)
    contact_radius = radius + thickness + .0025
    target = None
    # A larger object permits less closure. Prefer the furthest natural pad
    # contact reachable by this thumb, rather than stretching it across a bowl.
    for degrees in range(100, 49, -1):
        angle = math.radians(-degrees)
        candidate = centre + axial + direction*(contact_radius*math.sin(angle)) - normal*(contact_radius*math.cos(angle))
        distance = (candidate-start).length
        if abs(first-second)+1e-5 < distance < (first+second)*.985:
            target = candidate
            break
    if target is None:
        raise RuntimeError("Authored bowl is outside the original thumb's reach")
    axis = (target-start).normalized()
    distance = (target-start).length
    along = (first*first-second*second+distance*distance)/(2*distance)
    height = math.sqrt(max(0., first*first-along*along))
    # The middle pad travels outside the shell on the thumb side of the hand.
    hint = -direction
    bend = (hint-axis*hint.dot(axis)).normalized()
    return [start, start+axis*along+bend*height, target]
