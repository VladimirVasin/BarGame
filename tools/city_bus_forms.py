"""Pressed body sections, wheel openings and rounded working surfaces, in metres."""
from __future__ import annotations

import math

from mathutils import Vector


def loft(mesh, rings):
    """Append one closed solid and orient it outwards regardless of sweep axis."""
    vertices = [Vector(v) for ring in rings for v in ring]
    count = len(rings[0])
    last = (len(rings) - 1) * count
    faces = [tuple(reversed(range(count)))]
    for i in range(0, last, count):
        for j in range(count):
            k = (j + 1) % count
            faces.append((i+j, i+k, i+k+count, i+j+count))
    faces.append(tuple(range(last, last + count)))
    # Centring the volume makes the winding test robust on narrow panel strips.
    origin = vertices[0]
    local = [v - origin for v in vertices]
    volume6 = sum(local[f[0]].dot(local[f[i]].cross(local[f[i+1]]))
                  for f in faces for i in range(1, len(f)-1))
    if abs(volume6) < 1e-9:
        raise ValueError("Degenerate bus solid")
    start = len(mesh.vertices)
    mesh.vertices.extend(tuple(v) for v in vertices)
    mesh.faces.extend(tuple(start+i for i in (f if volume6 > 0 else reversed(f))) for f in faces)


def roof(mesh):
    section = ((-1.18, 2.70), (-1.18, 2.76), (-1.10, 2.86), (-.94, 2.915),
               (-.52, 2.942), (0., 2.95), (.52, 2.942), (.94, 2.915),
               (1.10, 2.86), (1.18, 2.76), (1.18, 2.70), (1.10, 2.68), (-1.10, 2.68))
    loft(mesh, [[(x*width, y, 2.70+(z-2.70)*height) for x, z in section]
                for y, width, height in ((-4.095, .91, .56), (-4., .98, .90),
                    (-3.84, 1., 1.), (3.80, 1., 1.), (4., .98, .87), (4.095, .91, .55))])


def end_panel(mesh, end, bottom, top, depth, half_width=1.18):
    """A flat centre face pulls back into the quarter panels at both ends."""
    rings = []
    for x, pull in ((-half_width, .17), (-half_width+.065, .085), (-.96, .013),
                    (.96, .013), (half_width-.065, .085), (half_width, .17)):
        y = end * (4.125 - pull)
        lip = min(.037, (top-bottom)*.11)
        rings.append([(x, y-end*dy, z) for dy, z in (
            (depth, bottom), (lip, bottom), (0., bottom+lip), (0., top-lip),
            (lip, top), (depth, top))])
    loft(mesh, rings)


def side_panel(mesh, side, start, end, top, bottom, arches=(), outside=1.19, depth=.060):
    """One pressed panel with true open arches; the outer skin rolls inward at the sill."""
    radius = .53
    stations = {start, end}
    for y in arches:
        stations.update(p for i in range(13) if start < (p := y+radius*math.cos(math.pi*i/12)) < end)
    rings = []
    for y in sorted(stations):
        z = max([bottom] + [.43 + math.sqrt(max(0., radius**2-(y-axis)**2))
                            for axis in arches if abs(y-axis) <= radius+1e-7])
        lip = min(.019, (top-z)*.15)
        x = outside - max(0., abs(y)-3.87)*.43
        rings.append([(side*(x-d), y, h) for d, h in (
            (depth, z), (lip, z), (0., z+lip), (0., top-lip), (lip, top), (depth, top))])
    loft(mesh, rings)


def arch_rim(mesh, side, y, start=-math.inf, end=math.inf):
    rings = []
    for i in range(13):
        theta = math.pi*i/12
        c, s = math.cos(theta), math.sin(theta)
        if not start <= y + .582*c <= end:
            continue
        rings.append([(side*x, y+r*c, .43+r*s) for x,r in (
            (1.168, .527), (1.192, .527), (1.211, .544),
            (1.211, .564), (1.199, .582), (1.168, .582))])
    loft(mesh, rings)


def wheel_housing(mesh, side, axle, outside, bottom, profile):
    """Press a closed wheel tub into the cabin lining, with a soft shoulder."""
    rings = []
    stations = [(-dy, inner, top) for dy, inner, top in reversed(profile[1:])] + list(profile)
    for dy, inner, top in stations:
        bevel = min(.022, (top-bottom)*.24)
        rings.append([(side*x, axle+dy, z) for x, z in (
            (outside, bottom), (outside, top), (inner+bevel, top),
            (inner, top-bevel), (inner, bottom+bevel), (inner+bevel, bottom))])
    loft(mesh, rings)


def rolling_profile(mesh, rings, segments=24):
    """A closed lathed profile. The end disks are recessed behind the sidewall."""
    loft(mesh, [[(x, radius*math.cos(i*math.tau/segments), radius*math.sin(i*math.tau/segments))
                 for i in range(segments)] for x,radius in rings])
