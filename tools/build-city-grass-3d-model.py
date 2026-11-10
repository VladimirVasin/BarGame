#!/usr/bin/env python3
"""Deterministic rooted, opaque low-poly City plants; metres in Unity axes.

Three independent silhouettes and three nested geometry LODs per family.
UV0=(normalised bend height, authored total height in metres); vertex colour
is the actual muted pigment. No alpha cards, collision, text or animation.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import sys
import bpy

ROOT = Path(__file__).resolve().parents[1]
FAMILIES = (
    # name, height, leaf width, spread, leaves, pigment, stiffness
    ("Lawn", .16, .014, .19, 18, (.15, .25, .12), .65),
    ("Soft", .32, .019, .23, 16, (.19, .28, .14), .38),
    ("Broad", .34, .040, .23, 12, (.18, .25, .13), .50),
    ("Tussock", .40, .018, .25, 16, (.26, .29, .16), .80),
    ("Tall", .72, .024, .28, 12, (.28, .30, .17), .65),
    ("Dry", .47, .018, .27, 14, (.34, .31, .19), .90),
    ("Sedge", .57, .025, .28, 15, (.22, .28, .16), .72),
    ("Reed", 1.30, .075, .36, 8, (.31, .33, .20), 1.0),
)


def geometry(family, variant, lod):
    name, height, width, spread, count, pigment, stiffness = family
    rng = random.Random(19731 + FAMILIES.index(family) * 103 + variant * 71)
    vertices, faces, colors = [], [], []
    def ribbon(points, widths, angle, tint):
        start = len(vertices)
        # Folded V section gives the leaf real volume and readable side views.
        side = (math.cos(angle), 0, math.sin(angle))
        for p, w in zip(points, widths):
            for edge in (-1, 0, 1):
                vertices.append((p[0] + side[0] * w * edge * .5,
                                 p[1] + (w * .18 if edge == 0 else 0),
                                 p[2] + side[2] * w * edge * .5))
                colors.append(tint)
        for row in range(len(points) - 1):
            for edge in range(2):
                a = start + row * 3 + edge
                faces.extend(((a, a + 3, a + 1), (a + 1, a + 3, a + 4)))
    for leaf in range(count):
        angle = rng.random() * math.tau
        h = height * rng.uniform(.52, 1.13)
        w = width * rng.uniform(.65, 1.30)
        radius = spread * math.sqrt(rng.random()) * .50
        x, z = math.cos(angle) * radius, math.sin(angle) * radius
        lean = rng.uniform(.13, .55) * h
        tint = tuple(c * rng.uniform(.88, 1.10) for c in pigment) + (1,)
        direction = angle + rng.uniform(-.5, .5)
        # Consume the same random stream before selecting a nested subset.
        if leaf % (1, 2, 4)[lod] != 0:
            continue
        if name == "Dry":
            lean *= 1.7
            h *= .70
        steps = (3, 2, 1)[lod]
        points = [(x + math.cos(direction) * lean * (t * t),
                   h * (t - .12 * t * t),
                   z + math.sin(direction) * lean * (t * t))
                  for t in (i / steps for i in range(steps + 1))]
        widths = [w * (.48 + .65 * math.sin(math.pi * i / steps)) *
                  (1 - .98 * (i / steps) ** 2) for i in range(steps + 1)]
        if name == "Reed":
            # Substantial triangular stems; several roots per irregular clump.
            base = len(vertices)
            for y, r, bend in ((0, .016, 0), (h * .5, .014, .035), (h, .006, .12)):
                for edge in range(3):
                    a = angle + edge * math.tau / 3
                    vertices.append((x + math.cos(a) * r + math.cos(direction) * bend,
                                     y, z + math.sin(a) * r + math.sin(direction) * bend))
                    colors.append(tint)
            first_face=len(faces)
            for row in range(2):
                for edge in range(3):
                    a, b = base + row * 3 + edge, base + row * 3 + (edge + 1) % 3
                    faces.extend(((a, a + 3, b), (b, a + 3, b + 3)))
            faces.extend(((base, base + 1, base + 2), (base + 6, base + 8, base + 7)))
            # Signed volume and opposite directed edges validate every solid
            # stem before the Unity-to-Blender reflection reverses winding.
            volume=0.0
            edges={}
            for tri in faces[first_face:]:
                a,b,c=(vertices[i] for i in tri)
                volume+=(a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0]))/6
                for a,b in zip(tri,tri[1:]+tri[:1]):edges[(a,b)]=edges.get((a,b),0)+1
            assert volume>0 and all(edges.get((b,a),0)==count for (a,b),count in edges.items())
            points = [(x, h * .33, z),
                      (x + math.cos(direction) * .18, h * .60, z + math.sin(direction) * .18),
                      (x + math.cos(direction) * .46, h * .48, z + math.sin(direction) * .46)]
            widths = [w * .35, w, .001]
        ribbon(points, widths, angle + math.pi * .5, tint)
        if name == "Tall" and leaf % 4 == 0:
            ribbon([(p[0], p[1] + .04, p[2]) for p in points[-2:]],
                   [w * 1.8, .001], angle, tuple(c * .95 for c in tint[:3]) + (1,))
    maximum = height * 1.13  # identical bend normalisation across all LODs
    return vertices, faces, colors, maximum, stiffness


def payload():
    result = {}
    for family in FAMILIES:
        for variant in range(3):
            for lod in range(3):
                result[f"{family[0]}_{variant}_LOD{lod}"] = geometry(family, variant, lod)
    return result


def manifest_for(plants):
    assemblies = []
    for name, (vertices, faces, colors, height, stiffness) in plants.items():
        assert vertices and faces and height > .05
        assert all(math.isfinite(c) for v in vertices for c in v)
        assert min(v[1] for v in vertices) >= 0
        # Every triangle has area; leaf sheets are deliberately double-sided,
        # reed stems are closed triangles rather than primitive cubes.
        for face in faces:
            a, b, c = (vertices[i] for i in face)
            ab, ac = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
            cross = (ab[1]*ac[2]-ab[2]*ac[1], ab[2]*ac[0]-ab[0]*ac[2], ab[0]*ac[1]-ab[1]*ac[0])
            assert sum(v*v for v in cross) > 1e-16, name
        signature = hashlib.sha256(json.dumps((vertices, faces, colors), separators=(",", ":")).encode()).hexdigest()
        assemblies.append(dict(name=name, vertices=len(vertices), triangles=len(faces),
                               bounds_min=[min(v[i] for v in vertices) for i in range(3)],
                               bounds_max=[max(v[i] for v in vertices) for i in range(3)],
                               height=height, stiffness=stiffness, signature=signature))
    assert sum(a["triangles"] for a in assemblies) < 22000
    return dict(version=1, families=[f[0] for f in FAMILIES], assemblies=assemblies)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Resources/City/Grass")
    parser.add_argument("--source-dir", type=Path, default=ROOT / "ArtSource/City/Grass")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    argv = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    args = parser.parse_args(argv)
    plants = payload()
    manifest = manifest_for(plants)
    assert manifest == manifest_for(payload()), "Geometry must be deterministic"
    path = args.model_dir / "CityGrass3D.json"
    if args.validate_only:
        assert json.loads(path.read_text()) == manifest, "Regenerate CityGrass3D"
        print("CityGrass geometry/determinism validation passed")
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    root = bpy.data.objects.new("ROOT_CityGrass3D", None)
    bpy.context.collection.objects.link(root)
    for name, (vertices, faces, colors, height, stiffness) in plants.items():
        mesh = bpy.data.meshes.new(name)
        # Axis reflection reverses winding.
        mesh.from_pydata([(x,z,y) for x,y,z in vertices], [], [tuple(reversed(f)) for f in faces])
        mesh.update()
        uv = mesh.uv_layers.new(name="Bend")
        color = mesh.color_attributes.new(name="Pigment", type="FLOAT_COLOR", domain="CORNER")
        for loop in mesh.loops:
            v = loop.vertex_index
            uv.data[loop.index].uv = (vertices[v][1] / height, height)
            color.data[loop.index].color = colors[v]
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str((args.model_dir / "CityGrass3D.fbx").resolve()),
        use_selection=True, object_types={"EMPTY","MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options="FBX_SCALE_NONE", bake_space_transform=False,
        add_leaf_bones=False, bake_anim=False, mesh_smooth_type="FACE", colors_type="LINEAR")
    path.write_text(json.dumps(manifest, indent=2) + "\n")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str((args.source_dir / "CityGrass3D.blend").resolve()))
    print("CityGrass authored geometry/determinism validation passed")


if __name__ == "__main__":
    main()
