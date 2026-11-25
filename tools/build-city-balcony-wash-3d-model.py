"""Metre-authored balcony wash, fixings and a rope with 17 longitudinal stations."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as bp
import interior_kit as kit
import bpy


def garment(name, width, height, columns, rows):
    vertices, faces, indices = [], [], {}
    shirt = name == "Shirt"
    for row in range(rows):
        for col in range(columns):
            # Shoulders support loose short sleeves, and a shallow neck cuts into the top.
            if shirt and (col < 2 or col >= columns - 2) and row >= 3:
                continue
            face = []
            for c, r in ((col, row), (col, row + 1), (col + 1, row + 1), (col + 1, row)):
                if (c, r) not in indices:
                    u, v = c / columns, r / rows
                    x = (u - .5) * width
                    pin = abs(abs(x) - width * (.3 if shirt else .5)) < .001
                    dip = 0 if pin else (.065 if shirt else .027)
                    if shirt and (c < 2 or c > columns - 2):
                        dip += .08 * abs(c - (2 if c < 2 else columns - 2))
                    y = -height * v - dip * (1 - v)
                    z = (.018 + .031 * v) * math.sin(math.pi * u * 6) * (v if pin else 1)
                    indices[c, r] = len(vertices)
                    vertices.append((x, y, z))
                face.append(indices[c, r])
            faces.append(tuple(face))
    return vertices, faces


def tube_path(centres, radius, ring_axis, closed=False):
    # All paths lie in a plane. Sweep around its tangent with a fixed plane normal.
    from mathutils import Vector
    points = [Vector(p) for p in centres]
    axis = Vector(ring_axis)
    vertices, faces = [], []
    for i, p in enumerate(points):
        prev = points[(i-1) % len(points)] if closed or i else p
        nxt = points[(i+1) % len(points)] if closed or i+1 < len(points) else p
        across = (nxt-prev).normalized().cross(axis)
        for j in range(8):
            a = math.tau*j/8
            vertices.append(tuple(p + radius * (math.cos(a)*axis + math.sin(a)*across)))
    for i in range(len(points) if closed else len(points)-1):
        k = (i+1) % len(points)
        for j in range(8):
            n = (j+1) % 8
            faces.append((8*i+j, 8*i+n, 8*k+n, 8*k+j))
    if not closed:
        faces.extend((tuple(reversed(range(8))), tuple(range(len(vertices)-8, len(vertices)))))
    if bp.signed_volume((vertices, faces)) < 0:
        faces = [tuple(reversed(f)) for f in faces]
    return vertices, faces


def catalog():
    parts = {name: garment(name, *dims) for name, dims in
             {"Sheet": (1.28, .89, 10, 8), "Towel": (.56, .70, 6, 8), "Shirt": (.8, .67, 10, 8)}.items()}
    parts["Rope"] = tube_path([(i/16, 0, 0) for i in range(17)], .014, (0, 1, 0))
    for name, radius in (("PoleLoop", .146), ("RailLoop", .044)):
        parts[name] = tube_path([(radius*math.cos(i*math.tau/16), 0, radius*math.sin(i*math.tau/16))
                                 for i in range(16)], .013, (0, 1, 0), True)
    parts["WallEye"] = kit.merge(bp.u_box((0, 0, -.044), (.085, .115, .018), .006),
        tube_path([(0, .031*math.cos(i*math.tau/16), -.017+.033*math.sin(i*math.tau/16))
                   for i in range(16)], .008, (1, 0, 0), True))
    parts["Peg"] = kit.merge_all([bp.u_box((0, .020, z), (.021, .081, .014), .004)
                                  for z in (-.010, .010)] + [bp.u_box((0, .025, 0), (.023, .013, .029), .003)])
    return parts


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Resources/City/Laundry")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    parts = catalog()
    manifest = {"sha256": hashlib.sha256(json.dumps(parts, sort_keys=True).encode()).hexdigest(),
                "parts": {n: {"triangles": sum(len(f)-2 for f in faces),
                          "size_m": [round(max(p[i] for p in vs)-min(p[i] for p in vs), 4) for i in range(3)]}
                          for n, (vs, faces) in parts.items()}}
    assert parts == catalog()
    path = args.model_dir / "BalconyWash.json"
    if args.validate_only:
        assert json.loads(path.read_text()) == manifest
        return
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    root = bpy.data.objects.new("BalconyWashKit", None)
    bpy.context.collection.objects.link(root)
    for name, geo in parts.items():
        mesh = bpy.data.meshes.new(name)
        vs, faces = bp.to_source(geo)
        mesh.from_pydata(vs, [], faces)
        mesh.update()
        uv = mesh.uv_layers.new(name="UVMap")
        for poly in mesh.polygons:
            poly.use_smooth = name != "Peg"
            for loop in poly.loop_indices:
                x, y, z = geo[0][mesh.loops[loop].vertex_index]
                uv.data[loop].uv = (x, y if name in ("Shirt", "Towel", "Sheet") else y+z)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
    args.model_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir / "BalconyWash.fbx"), object_types={"EMPTY", "MESH"},
        axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_NONE", bake_anim=False, add_leaf_bones=False)
    path.write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
