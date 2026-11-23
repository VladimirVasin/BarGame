"""Bevelled stone and metal solids with projected atlas UVs for the city bridges."""
import hashlib
import json

import bpy
from mathutils import Vector

import interior_kit as kit


class Part:
    def __init__(self, name):
        self.name = name
        self.vertices, self.faces, self.uv = [], [], []
        self.solids = 0

    def box(self, center, size, finish, bevel=.008, top=None):
        shape = kit.joint_box(center, size, bevel)
        self.append(shape, finish, top)

    def append(self, shape, finish, top=None):
        verts, faces = shape
        lo, hi = kit.bounds(shape)
        start = len(self.vertices)
        self.vertices.extend(verts)
        for face in faces:
            points = [Vector(verts[i]) for i in face]
            normal = (points[1] - points[0]).cross(points[2] - points[0]).normalized()
            axis = max(range(3), key=lambda i: abs(normal[i]))
            u, v = ((1, 2), (0, 2), (0, 1))[axis]
            cell = top if top is not None and normal.z > .7 else finish
            for point in points:
                s = (point[u] - lo[u]) / max(hi[u] - lo[u], .0001)
                t = (point[v] - lo[v]) / max(hi[v] - lo[v], .0001)
                self.uv.append(self.atlas_uv(cell, s, t, point, axis))
            self.faces.append(tuple(start+i for i in face))
        self.solids += 1

    def atlas_uv(self, cell, s, t, point, axis):
        return ((cell % 2 + .012 + .976*s)/2, 1 - (cell//2 + .985 - .97*t)/4)

    def emit(self, root):
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        uv = mesh.uv_layers.new(name="Finish")
        assert len(uv.data) == len(self.uv)
        for loop, coords in zip(uv.data, self.uv):
            loop.uv = coords
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
        mesh.calc_loop_triangles()
        payload = json.dumps((self.vertices, self.faces, self.uv), separators=(',', ':'))
        return dict(name=self.name, solids=self.solids, triangles=len(mesh.loop_triangles),
                    bounds=kit.bounds((self.vertices, self.faces)), sha256=hashlib.sha256(payload.encode()).hexdigest())
