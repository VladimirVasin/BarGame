"""Lodge-only wood roles and member-aligned metre UVs; no geometry changes.

Every albedo has its grain along V. Connected authored solids retain their
own longitudinal grain even after furniture has been turned into the room.
The floor and hatch use explicit perpendicular directions in lodge space.
"""
from __future__ import annotations

import hashlib
import json
import math


WOOD_PITCH = {
    "LodgeFloorWood": 2.4,
    "LodgeHatchWood": 1.2,
    "LodgePaintedWood": .6,
    "LodgeOakWood": 1.6,
    "LodgePaleWood": 1.6,
    "LodgeDarkWood": 1.25,
    "LodgeRoughWood": 1.8,
}
UV_MODE = "member_projected_metres"


def appearance_for(name):
    if name == "Floor":
        return "LodgeFloorWood"
    if name == "CellarHatchLid":
        return "LodgeHatchWood"
    if name in ("CellarHatchFrame", "CellarHatchBacking"):
        return "LodgeDarkWood"
    if name == "LodgeStoveChair":
        return "LodgePaintedWood"
    if name.startswith("LodgeDining"):
        return "LodgeOakWood"
    if name.startswith(("LodgeBunk", "LodgeCot")) or name == "EmptyRentalRacks":
        return "LodgePaleWood"
    if name.startswith(("LodgeMinibar", "LodgeLounge")):
        return "LodgeDarkWood"
    return "LodgeRoughWood"


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def unit(value):
    length = math.sqrt(dot(value, value))
    assert length > 1e-12, "Degenerate lodge wood projection"
    return tuple(v/length for v in value)


def components(vertices, faces):
    """The existing merge helper retains each member's vertex connectivity."""
    parents = list(range(len(vertices)))

    def root(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index

    for face in faces:
        first = root(face[0])
        for index in face[1:]:
            parents[root(index)] = first
    groups = {}
    for index in range(len(vertices)):
        groups.setdefault(root(index), []).append(index)
    return list(groups.values())


def member_axis(points, center):
    """Principal length, including the already rotated chair's narrow legs."""
    relative = [tuple(p[i]-center[i] for i in range(3)) for p in points]
    covariance = [[sum(p[i]*p[j] for p in relative) for j in range(3)] for i in range(3)]
    longest = max(range(3), key=lambda i: covariance[i][i])
    direction = tuple(float(i == longest) for i in range(3))
    for _ in range(24):
        direction = unit(tuple(dot(row, direction) for row in covariance))
    if direction[max(range(3), key=lambda i: abs(direction[i]))] < 0:
        direction = tuple(-v for v in direction)
    return direction


def face_normal(points):
    # Newell handles cap n-gons whose first three vertices might be collinear.
    normal = [0., 0., 0.]
    for a, b in zip(points, points[1:]+points[:1]):
        for i in range(3):
            j, k = (i+1) % 3, (i+2) % 3
            normal[i] += (a[j]-b[j])*(a[k]+b[k])
    return unit(normal)


def authored_uv(part):
    vertices, faces = part["geometry"]
    frames = {}
    name = part["name"]
    fixed = ((1., 0., 0.) if name == "Floor" else
             (0., 0., 1.) if name in ("CellarHatchLid", "CellarHatchBacking") else None)
    for number, indices in enumerate(components(vertices, faces)):
        points = [vertices[index] for index in indices]
        center = tuple(sum(p[i] for p in points)/len(points) for i in range(3))
        grain = fixed or member_axis(points, center)
        # Physical floor seams must remain continuous across the hatch cutout.
        # Other members sample different places in their own repeatable sheet.
        digest = hashlib.sha256((name+":"+str(number)).encode()).digest()
        phase = (digest[0]/255., digest[1]/255.)
        if name == "Floor":
            center, phase = (0., 0., 0.), (0., 0.)
        frame = center, grain, phase
        for index in indices:
            frames[index] = frame
    result = []
    for face in faces:
        center, grain, phase = frames[face[0]]
        normal = face_normal([vertices[index] for index in face])
        along = tuple(grain[i]-normal[i]*dot(grain, normal) for i in range(3))
        if dot(along, along) < .0025:
            # A member's end face needs two surface axes, not collapsed UVs.
            axis = min(range(3), key=lambda i: abs(normal[i]))
            along = tuple(float(i == axis)-normal[i]*normal[axis] for i in range(3))
        along = unit(along)
        across = unit(cross(along, normal))
        uv = []
        for index in face:
            offset = tuple(vertices[index][i]-center[i] for i in range(3))
            uv.append((dot(offset, across)+phase[0], dot(offset, along)+phase[1]))
        result.append(uv)
    return result


def uv_signature(uv):
    measured = [[[round(u, 7), round(v, 7)] for u, v in face] for face in uv]
    return hashlib.sha256(json.dumps(measured, separators=(",", ":")).encode()).hexdigest()


def assign_wood(parts):
    for part in parts:
        if part["kind"] != "SkiLodge" or part["surface"] != "Timber":
            continue
        part["appearance"] = appearance_for(part["name"])
        part["wood_uv"] = authored_uv(part)
        part["wood_uv_mode"] = UV_MODE
        part["wood_uv_loop_count"] = sum(map(len, part["wood_uv"]))
        part["wood_uv_signature"] = uv_signature(part["wood_uv"])


def validate_wood(parts):
    woods = []
    for part in parts:
        expected = part["kind"] == "SkiLodge" and part["surface"] == "Timber"
        assert bool(part.get("appearance")) == expected, "Lodge wood mapping escaped its owner"
        if not expected:
            continue
        woods.append(part)
        assert part["appearance"] == appearance_for(part["name"])
        assert part["wood_uv_mode"] == UV_MODE
        assert len(part["wood_uv"]) == len(part["geometry"][1])
        assert part["wood_uv_loop_count"] == sum(map(len, part["geometry"][1]))
        assert part["wood_uv_signature"] == uv_signature(part["wood_uv"])
        for face, uv in zip(part["geometry"][1], part["wood_uv"]):
            assert len(face) == len(uv) and all(math.isfinite(v) for pair in uv for v in pair)
            area = sum(a[0]*b[1]-b[0]*a[1] for a, b in zip(uv, uv[1:]+uv[:1]))
            assert abs(area) > 1e-12, "Collapsed lodge wood UV: " + part["name"]
    assert {part["appearance"] for part in woods} == set(WOOD_PITCH), "Missing lodge wood role"
    # Read the actual authored UVs of the top faces, not just their role names.
    for name, longitudinal in (("Floor", 0), ("CellarHatchLid", 2)):
        part = next(p for p in woods if p["name"] == name)
        for face, uv in zip(part["geometry"][1], part["wood_uv"]):
            points = [part["geometry"][0][i] for i in face]
            if face_normal(points)[1] < .999:
                continue
            for point, coordinate in zip(points[1:], uv[1:]):
                assert abs((point[longitudinal]-points[0][longitudinal])-
                           (coordinate[1]-uv[0][1])) < 1e-7, "Floor/hatch grain direction drift"
