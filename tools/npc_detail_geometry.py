"""Small shared source-metre constructions for the ordinary character models.

No rig, palette or character identity belongs here. The caller owns placement,
bone bindings and materials. Solids are returned with outward face winding.
"""
from __future__ import annotations

import math
from mathutils import Vector


def signed_volume(geometry):
    points, faces = geometry
    points = [Vector(p) for p in points]
    return sum(points[f[0]].dot(points[f[i]].cross(points[f[i + 1]])) / 6.0
               for f in faces for i in range(1, len(f) - 1))


def outward(geometry):
    points, faces = geometry
    points = [Vector(p) for p in points]
    return points, ([tuple(reversed(f)) for f in faces]
                    if signed_volume((points, faces)) < 0 else list(faces))


def loft(rings):
    """Close equal-length rings; their order and plane may be arbitrary."""
    sides = len(rings[0])
    if len(rings) < 2 or sides < 3 or any(len(r) != sides for r in rings):
        raise ValueError("A loft needs at least two matching rings")
    points = [Vector(p) for ring in rings for p in ring]
    faces = [tuple(reversed(range(sides)))]
    faces.extend((r * sides + j, r * sides + (j + 1) % sides,
                  (r + 1) * sides + (j + 1) % sides, (r + 1) * sides + j)
                 for r in range(len(rings) - 1) for j in range(sides))
    faces.append(tuple((len(rings) - 1) * sides + j for j in range(sides)))
    return outward((points, faces))


def shell(stations, sides=16, folds=0.0, fold_count=8):
    """Horizontal (z, half-width, half-depth, centre-y) cloth/body sections."""
    rings = []
    for index, (z, rx, ry, cy) in enumerate(stations):
        ring = []
        for j in range(sides):
            a = math.tau * j / sides
            # A fold changes the silhouette and light, not just polygon count.
            ripple = 1.0 + folds * math.cos(a * fold_count + index * .18)
            ring.append((rx * math.cos(a) * ripple, cy + ry * math.sin(a) * ripple, z))
        rings.append(ring)
    return loft(rings)


def segment(start, end, profiles, sides=12):
    """A limb/finger with (distance fraction, radius, depth ratio) stations."""
    start, end = Vector(start), Vector(end)
    axis = (end - start).normalized()
    reference = Vector((0, 1, 0)) if abs(axis.y) < .95 else Vector((1, 0, 0))
    across = (reference - axis * reference.dot(axis)).normalized()
    depth = axis.cross(across).normalized()
    return loft([[start.lerp(end, t) + radius * math.cos(math.tau * j / sides) * across
                  + radius * ratio * math.sin(math.tau * j / sides) * depth
                  for j in range(sides)] for t, radius, ratio in profiles])


def hand(wrist, end, side, scale=1.0):
    """Shaped palm, four separated fingers and opposed thumb on one hand bone.

    `end` supplies the unchanged bind direction; the modest 10 cm hand is
    independent of a legacy mitten's length. Anatomical left is source +X.
    Keys: palm, thumb, finger0..finger3 (index through little finger).
    """
    wrist, end = Vector(wrist), Vector(end)
    axis = (end - wrist).normalized()
    across = (Vector((0, 1, 0)) - axis * axis.y).normalized()
    depth = axis.cross(across).normalized() * (-1 if side == "L" else 1)

    def point(along, width=0.0, thick=0.0):
        return wrist + scale * (axis * along + across * width + depth * thick)

    rings = [[point(t, math.cos(math.tau * j / 12) * width,
                    math.sin(math.tau * j / 12) * thickness)
              for j in range(12)]
             for t, width, thickness in ((-.009, .020, .013), (.014, .029, .016),
                                          (.040, .032, .017), (.057, .029, .014))]
    result = {"palm": loft(rings)}
    for i, (offset, length, radius) in enumerate(((-.024, .037, .0080),
                                                (-.008, .045, .0083),
                                                (.009, .041, .0078),
                                                (.025, .031, .0068))):
        result[f"finger{i}"] = segment(point(.050, offset), point(.050 + length, offset, .004),
            ((0, radius * scale, .85), (.42, radius * 1.06 * scale, .88),
             (.77, radius * .90 * scale, .83), (1, radius * .66 * scale, .8)), 8)
    result["thumb"] = segment(point(.017, -.023, .001), point(.057, -.047, .010),
        ((0, .012 * scale, .85), (.42, .0122 * scale, .86),
         (.75, .0102 * scale, .82), (1, .008 * scale, .8)), 8)
    return result


def replace_mesh(obj, geometry):
    """Replace a rigid-bound part in place, retaining its object/rig contract."""
    import bpy
    vertices, faces = geometry
    old = obj.data
    materials = list(old.materials)
    matrix = obj.matrix_world.inverted()
    mesh = bpy.data.meshes.new(obj.name + "_DetailedMesh")
    mesh.from_pydata([tuple(matrix @ Vector(p)) for p in vertices], [], faces)
    mesh.update()
    for material in materials:
        mesh.materials.append(material)
    obj.data = mesh
    bone = obj.get("bp_bone")
    if bone:
        group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
        group.add(list(range(len(mesh.vertices))), 1.0, "REPLACE")
    if old.users == 0:
        bpy.data.meshes.remove(old)
    return obj
