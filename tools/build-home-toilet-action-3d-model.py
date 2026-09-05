#!/usr/bin/env python3
"""Author the Home toilet action's fixed-metre anatomy, hinged lid and liquid kit.

Blender --background --factory-startup --python this-file -- [--validate-only]
The anatomy is a neutral adult, nonsexual urination prop. The production Hero V2
owns the hand/arm: this kit deliberately does not author a replacement hand.
All game meshes are Blender exports; runtime only places/scales these assets.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/HomeToiletAction"
RESOURCES = ROOT / "Assets/Resources/HomeToiletAction"
MODELS = RESOURCES / "Models"
VERSION = "1.3.1"
ANCHORS = {"AimPivot": (0, 0, 0), "Grip": (0, -.0015, .025),
           "Outlet": (0, -.020, .130)}
COLORS = {"Skin": "AE8D7B", "SkinDark": "392D2E", "Enamel": "919982", "Water": "3F4941",
          "Paper": "C5BEAD", "Cardboard": "80684D",
          "Urine": "B9A343"}


def source_point(point):
    """Match the project's existing fixed-metre Home FBX Y/Z swap contract."""
    return point[0], point[2], point[1]


def bounds(geometry):
    vertices, _ = geometry
    return ([min(p[i] for p in vertices) for i in range(3)],
            [max(p[i] for p in vertices) for i in range(3)])


def ring_loft(rings, sides=12, axis="z"):
    """Closed profiled, flat-shaded shell; all parameters are Unity metres."""
    vertices = []
    for center, rx, ry in rings:
        for i in range(sides):
            angle = math.tau * i / sides
            dx, dy = rx * math.cos(angle), ry * math.sin(angle)
            vertices.append((center[0] + dx, center[1] + dy, center[2])
                            if axis == "z" else
                            (center[0] + dx, center[1], center[2] + dy))
    faces = [tuple(reversed(range(sides)))]
    for row in range(len(rings) - 1):
        for col in range(sides):
            a = row * sides + col
            b = row * sides + (col + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces.append(tuple(range((len(rings) - 1) * sides, len(rings) * sides)))
    if axis == "y":
        faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def patch(wall=False, splash=False):
    """Concave rim, one filled centre and a thin back; normal is local +Z."""
    count = 32
    if wall:
        outline = [(-.5,.26),(-.44,.36),(-.34,.32),(-.28,.43),(-.17,.37),
                   (-.05,.48),(.06,.39),(.14,.5),(.24,.43),(.33,.47),
                   (.42,.32),(.5,.29),(.44,.17),(.30,.19),(.28,-.27),
                   (.23,-.43),(.18,-.38),(.17,.11),(.10,.17),(.06,-.42),
                   (.01,-.5),(-.03,-.45),(-.04,.05),(-.12,.13),
                   (-.17,-.19),(-.22,-.29),(-.27,-.25),(-.25,.11),
                   (-.33,.17),(-.39,.07),(-.44,.13),(-.47,.18)]
        # This boundary is clockwise; the other generated outline is CCW.
        outline.reverse()
    else:
        outline = []
        for i in range(count):
            angle = math.tau * i / count
            radius = .39 + .060 * math.sin(angle * 3 + .5) + .041 * math.cos(angle * 7)
            if splash:
                radius += .06 if i % 4 == 0 else -.025
            outline.append((radius * math.cos(angle), radius * math.sin(angle)))
        for axis in range(2):
            lo = min(p[axis] for p in outline)
            hi = max(p[axis] for p in outline)
            outline = [tuple((p[axis] - lo) / (hi - lo) - .5 if c == axis else p[c]
                             for c in range(2)) for p in outline]
    count = len(outline)
    # A wall stain's concave drips cannot use a fan through their outside.
    # Blender tessellates the authored simple concave polygon correctly.
    front = .018 if splash else .001
    vertices = [(x, y, front + (.055 if splash and i % 4 == 0 else 0))
                for i, (x, y) in enumerate(outline)]
    vertices += [(x, y, 0) for x, y in outline]
    faces = [tuple(range(count)), tuple(reversed(range(count, count * 2)))]
    for a in range(count):
        b = (a + 1) % count
        faces.append((a, a + count, b + count, b))
    return vertices, faces


def hollow_vertical_profile(profile, sides=16):
    """A closed annular cross-section, with no cap spanning its through-hole."""
    vertices, faces = ring_loft([((0,y,0), radius, radius)
                                 for radius, y in profile], sides=sides, axis="y")
    faces = faces[1:-1]
    last = (len(profile) - 1) * sides
    for col in range(sides):
        nxt = (col + 1) % sides
        faces.append((col, nxt, last+nxt, last+col))
    return vertices, faces


def definitions():
    anatomy = ring_loft([
        ((0, 0, 0), .025, .021),
        ((0, -.001, .018), .0195, .0185),
        ((0, -.003, .044), .0185, .0175),
        ((0, -.008, .072), .017, .0165),
        ((0, -.014, .097), .0165, .0155),
        ((0, -.017, .103), .019, .0175),
        ((0, -.019, .113), .0195, .017),
        ((0, -.020, .124), .014, .013),
        ((0, -.020, .130), .003, .0025),
    ])
    # One tiny recessed opening, colocated with the actual flight outlet.
    outlet = ring_loft([((0, -.020, .13005), .0013, .0006),
                        ((0, -.020, .13015), .0011, .00045)], sides=8)
    lid = ring_loft([((0, -.0125, -.255), .255, .240),
                     ((0, -.0065, -.255), .270, .255),
                     ((0, .0065, -.255), .270, .255),
                     ((0, .0125, -.255), .255, .240)], sides=24, axis="y")
    segment = ring_loft([((0, 0, 0), .5, .5), ((0, 0, 1), .5, .5)], sides=8)
    droplet = ring_loft([((0, 0, -.5), .025, .025),
                         ((0, 0, -.30), .36, .36),
                         ((0, 0, 0), .5, .5),
                         ((0, 0, .30), .36, .36),
                         ((0, 0, .5), .025, .025)], sides=8)
    water = ring_loft([((0, -.001, 0), .17, .157),
                       ((0, 0, 0), .17, .157)], sides=24, axis="y")
    # Footprint centre is X4.15; bowl centre is X4.05. The hollow mouth is
    # deliberately offset -0.10 X, and its floor stays below the water level.
    pedestal = ring_loft([
        ((0, -.240, 0), .390, .408),
        ((0, -.225, 0), .410, .429),
        ((0, -.195, 0), .410, .429),
        ((0, -.170, 0), .330, .350),
        ((-.020, -.120, 0), .150, .170),
        ((-.050, .080, 0), .140, .170),
        ((-.100, .150, 0), .240, .225),
        ((-.100, .240, 0), .280, .258),
        ((-.100, .240, 0), .265, .244),
        ((-.100, .140, 0), .215, .198),
        ((-.060, .020, 0), .100, .110),
    ], sides=16, axis="y")
    paper = hollow_vertical_profile([
        (.050,-.0475),(.050,.0470),(.0495,.0475),(.046,.0475),
        (.046,.04725),(.038,.04725),(.038,.0475),(.030,.0475),
        (.030,.04725),(.021,.04725),(.019,.0475),(.019,-.0475)])
    paper_core = hollow_vertical_profile([
        (.019,-.0475),(.019,.0475),(.014,.0475),(.014,-.0475)])
    seat_vertices, seat_faces = hollow_vertical_profile([
        (.270,-.025),(.270,.025),(.189,.025),(.189,-.025)], sides=24)
    seat = ([(x,y,z * (.506 / .54)) for x,y,z in seat_vertices], seat_faces)
    return {
        "Anatomy": {"meshes": [("Anatomy_Skin", anatomy, "Skin"),
                                ("Anatomy_Outlet", outlet, "SkinDark")],
                    "anchors": ANCHORS, "contract": "fixed metres; held adult anatomy"},
        "ToiletLid": {"meshes": [("ToiletLid", lid, "Enamel")], "anchors": {},
                      "contract": "hinge at origin; extends -Z; local X +90 raises to +Y"},
        "BowlWater": {"meshes": [("BowlWater", water, "Water")], "anchors": {},
                      "contract": "horizontal XZ oval .34 x .314; top Y0; place world (4.05,.4373,1.40) to meet existing inner bowl slope"},
        "ToiletPedestal": {"meshes": [("ToiletPedestal", pedestal, "Enamel")], "anchors": {},
                           "contract": "fixed .82 x .48 x .858 m; footprint centre (4.15,.24,1.40); hollow mouth centred localX -.10; water disk at worldY .4373 unobstructed"},
        "ToiletPaperRoll": {"meshes": [("ToiletPaper", paper, "Paper"),
                                       ("ToiletPaperCore", paper_core, "Cardboard")], "anchors": {},
                            "contract": "upright localY roll; diameter .10m height .095m; open .028m core; place centreY cistern top + .0475m"},
        "ToiletSeat": {"meshes": [("ToiletSeat", seat, "Enamel")], "anchors": {},
                       "contract": "true annular .54 x .05 x .506m seat; 70% open inner width/depth; fixed metres at existing Seat transform (4.05,.62,1.40)"},
        "StreamSegment": {"meshes": [("StreamSegment", segment, "Urine")], "anchors": {},
                          "contract": "start 0; end +Z 1; XY radius 0.5"},
        "Droplet": {"meshes": [("Droplet", droplet, "Urine")], "anchors": {},
                    "contract": "centred unit diameter"},
        "Splash": {"meshes": [("Splash", patch(splash=True), "Urine")], "anchors": {},
                   "contract": "XY unit envelope; crown faces +Z"},
        "Stain": {"meshes": [("Stain", patch(), "Urine")], "anchors": {},
                  "contract": "XY unit envelope; thin closed relief faces +Z"},
        "WallStain": {"meshes": [("WallStain", patch(wall=True), "Urine")], "anchors": {},
                      "contract": "XY unit envelope; drips toward -Y; faces +Z"},
    }


def canonical(defs):
    data = {"version": VERSION, "palette": COLORS, "definitions": defs}
    return hashlib.sha256(json.dumps(data, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def material(name):
    mat = bpy.data.materials.get("MAT_" + name)
    if mat:
        return mat
    mat = bpy.data.materials.new("MAT_" + name)
    rgb = [int(COLORS[name][i:i+2], 16) / 255 for i in (0, 2, 4)]
    rgba = tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4
                 for v in rgb) + (1,)
    mat.diffuse_color = rgba
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = rgba
    shader.inputs["Roughness"].default_value = .88 if name in ("Skin", "Paper", "Cardboard") else .32
    mat["bp_palette_hex"] = COLORS[name]
    return mat


def create_mesh(name, geometry, material_name, parent):
    mesh = bpy.data.meshes.new(name + "_Mesh")
    vertices, faces = geometry
    mesh.from_pydata([source_point(p) for p in vertices], [],
                     [tuple(reversed(face)) for face in faces])
    mesh.update(calc_edges=True)
    uv = mesh.uv_layers.new(name="UVMap")
    for face in mesh.polygons:
        face.use_smooth = False
        for loop_index in face.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv.data[loop_index].uv = (point.x + .5, point.z + .5)
    mesh.materials.append(material(material_name))
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    obj["bp_contract"] = "Blender authored; Unity metres; runtime transform only"
    return obj


def validate(defs, objects):
    assert canonical(defs) == canonical(definitions()), "Authoring is nondeterministic"
    total_triangles = 0
    for name, definition in defs.items():
        for mesh_name, geometry, _ in definition["meshes"]:
            mesh = objects[mesh_name].data
            mesh.calc_loop_triangles()
            assert mesh.vertices and mesh.loop_triangles, mesh_name
            for triangle in mesh.loop_triangles:
                a, b, c = [mesh.vertices[i].co for i in triangle.vertices]
                assert (b-a).cross(c-a).length > 1e-10, (mesh_name, "degenerate face")
            assert all(math.isfinite(v) for point in geometry[0] for v in point)
            assert all(not p.use_smooth for p in mesh.polygons), mesh_name
            total_triangles += len(mesh.loop_triangles)
    assert total_triangles < 2500, total_triangles
    anatomy_bounds = bounds(defs["Anatomy"]["meshes"][0][1])
    assert abs(anatomy_bounds[1][2] - ANCHORS["Outlet"][2]) < 1e-8
    assert all(anatomy_bounds[0][i] <= ANCHORS["Grip"][i] <= anatomy_bounds[1][i]
               for i in range(3)), "Grip is outside anatomy"
    lid_lo, lid_hi = bounds(defs["ToiletLid"]["meshes"][0][1])
    assert abs(lid_hi[0] - lid_lo[0] - .54) < 1e-7
    assert abs(lid_lo[2] + .51) < 1e-7 and abs(lid_hi[2]) < 1e-7
    # Unity's +90 about X maps every closed-lid -Z point into raised +Y.
    assert min(-p[2] for p in defs["ToiletLid"]["meshes"][0][1][0]) >= -1e-7
    for name in ("Splash", "Stain", "WallStain"):
        lo, hi = bounds(defs[name]["meshes"][0][1])
        assert abs(hi[0] - lo[0] - 1) < 1e-7 and abs(hi[1] - lo[1] - 1) < 1e-7
        assert lo[2] == 0 and 0 < hi[2] < .08
    pedestal = objects["ToiletPedestal"]
    bpy.context.view_layer.update()
    # These actual mesh rays cover the centre and near the full water rim.
    # Each must reach below world Y0.40; a hidden solid footprint cap fails.
    for index in range(17):
        angle = math.tau * index / 16
        radius = 0 if index == 16 else .98
        x = -.10 + .17 * radius * math.cos(angle)
        z = .157 * radius * math.sin(angle)
        origin = Vector(source_point((x, .55, z)))
        hit, point, _, _ = pedestal.ray_cast(origin, Vector(source_point((0, -1, 0))))
        assert hit and point.z < .16, ("pedestal blocks water", index, list(point))
    start = Vector(source_point((-.59, .685, 0)))
    water_target = Vector(source_point((-.10, .1973, 0)))
    direction = water_target - start
    hit, _, _, _ = pedestal.ray_cast(start, direction.normalized(), distance=direction.length - .0001)
    assert not hit, "Pedestal obstructs the incoming default urine path"
    lo, hi = bounds(defs["ToiletPedestal"]["meshes"][0][1])
    assert all(abs(hi[i]-lo[i]-size) < 1e-7 for i, size in enumerate((.82,.48,.858)))
    for name in ("ToiletPaper", "ToiletPaperCore"):
        hit, _, _, _ = objects[name].ray_cast(Vector((0,0,.10)), Vector((0,0,-1)))
        assert not hit, (name, "Paper roll core is capped")
    for name, radius in (("ToiletPaper", .03), ("ToiletPaperCore", .016)):
        hit, _, _, _ = objects[name].ray_cast(Vector((radius,0,.10)), Vector((0,0,-1)))
        assert hit, (name, "Missing physical paper/core rim")
    seat_object = objects["ToiletSeat"]
    for index in range(25):
        angle = math.tau * (index + .5) / 24
        radius = 0 if index == 24 else .98
        x, z = .189 * radius * math.cos(angle), .1771 * radius * math.sin(angle)
        origin = Vector(source_point((x, .10, z)))
        hit, _, _, _ = seat_object.ray_cast(origin, Vector(source_point((0,-1,0))))
        assert not hit, ("Seat aperture has an invisible cap", index)
    hit, point, _, _ = seat_object.ray_cast(Vector(source_point((.23,.10,0))),
                                            Vector(source_point((0,-1,0))))
    assert hit and abs(point.z - .025) < 1e-6, "Seat must retain its physical annular rim"
    lo, hi = bounds(defs["ToiletSeat"]["meshes"][0][1])
    assert all(abs(hi[i]-lo[i]-size) < 1e-7 for i,size in enumerate((.54,.05,.506)))
    return {"models": len(defs), "meshes": len(objects), "triangles": total_triangles,
            "deterministic_geometry": True, "grip_and_outlet": True,
            "lid_hinge_raise_sign": "+90 local X", "unit_vfx_bounds": True,
            "pedestal_hollow_water_clearance_rays": 18, "paper_roll_open_core": True,
            "seat_through_aperture_rays": 25}


def guid(path):
    return hashlib.md5(("HomeToiletAction/" + path.as_posix()).encode()).hexdigest()


def write_meta(path, folder=False, model=False):
    target = Path(str(path) + ".meta")
    if target.exists():
        return
    relative = path.relative_to(ROOT)
    if model:
        # Same importer as the proven Home authored meshes, with unique IDs.
        text = (ROOT / "Assets/Home/Interior/Models/HomeInterior3D.fbx.meta").read_text(encoding="utf-8")
        text = text.replace("guid: a841a60cd444e79438c87fd91713c304", "guid: " + guid(relative))
    else:
        text = "fileFormatVersion: 2\nguid: " + guid(relative) + "\n"
        if folder:
            text += "folderAsset: yes\nDefaultImporter:\n"
        else:
            text += "TextScriptImporter:\n"
        text += "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    target.write_text(text, encoding="utf-8")


def validate_exported_geometry(defs):
    """FBX round-trip catches changed metre scale, hierarchy, pivots or axes."""
    for name, definition in defs.items():
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        bpy.ops.import_scene.fbx(filepath=str(MODELS / (name + ".fbx")),
                                 use_custom_normals=True)
        for mesh_name, geometry, _ in definition["meshes"]:
            obj = next(obj for obj in bpy.context.scene.objects
                       if obj.type == "MESH" and obj.name == mesh_name)
            actual = sorted(tuple(round(c, 5) for c in obj.matrix_world @ vertex.co)
                            for vertex in obj.data.vertices)
            expected = sorted(tuple(round(c, 5) for c in source_point(point))
                              for point in geometry[0])
            assert actual == expected, (name, "FBX vertex/pivot/axis mismatch")
        for anchor_name, point in definition["anchors"].items():
            obj = bpy.data.objects[anchor_name]
            assert (obj.matrix_world.translation - Vector(source_point(point))).length < 1e-6, anchor_name


def compose_preview(roots):
    """One true-metre view: normalization must never resemble liquid diameter."""
    collection = bpy.data.collections.new("AUTHORING_PREVIEW")
    bpy.context.scene.collection.children.link(collection)
    for root in roots.values():
        root.hide_render = True
        for child in root.children:
            child.hide_render = True

    def copy_model(name, position, scale=(1, 1, 1), rotation=None):
        copies = []
        for child in roots[name].children:
            if child.type != "MESH": continue
            duplicate = child.copy()
            duplicate.data = child.data
            duplicate.name = "PREVIEW_" + child.name
            duplicate.parent = None
            collection.objects.link(duplicate)
            duplicate.hide_render = False
            duplicate.scale = scale
            duplicate.location = position
            if rotation is not None: duplicate.rotation_euler = rotation
            copies.append(duplicate)
        return copies

    # Every prop retains its physical metre scale. The normalized liquid meshes
    # use their real runtime dimensions: 3 mm stream, 4 mm drop, 28 mm splash.
    anatomy_origin = Vector((-.35, -.26, .24))
    copy_model("Anatomy", anatomy_origin)
    copy_model("ToiletLid", (.20, .25, .01))
    copy_model("BowlWater", (.20, .50, .002))
    copy_model("ToiletPaperRoll", (.44, .50, .0475))
    # A separate close camera records the true opening without changing the
    # readable main scale-comparison sheet.
    pedestal_preview = copy_model("ToiletPedestal", (1.30, .10, .24))
    water_preview = copy_model("BowlWater", (1.20, .10, .4373))
    seat_preview = copy_model("ToiletSeat", (2.50, .10, .025))
    outlet = anatomy_origin + Vector(source_point(ANCHORS["Outlet"]))
    velocity = Vector((0, 1.5, -.10))
    gravity = Vector((0, 0, -9.81))
    flight = (-.10 + math.sqrt(.10 ** 2 + 2 * 9.81 * (outlet.z - .002))) / 9.81
    for index in range(24):
        begin = flight * index / 24
        end = flight * (index + 1) / 24
        start = outlet + velocity * begin + gravity * (.5 * begin * begin)
        finish = outlet + velocity * end + gravity * (.5 * end * end)
        direction = finish - start
        copy_model("StreamSegment", start, (.003, direction.length, .003),
                   direction.to_track_quat("Y", "Z").to_euler())
    impact = outlet + velocity * flight + gravity * (.5 * flight * flight)
    copy_model("Stain", impact, (.06,) * 3, (math.pi / 2, 0, 0))
    copy_model("Splash", impact + Vector((0, 0, .001)), (.028,) * 3,
               (math.pi / 2, 0, .35))
    for dx, dy, dz in ((-.012,.005,.014),(.011,.008,.020),(.007,-.012,.009)):
        copy_model("Droplet", impact + Vector((dx,dy,dz)), (.004,) * 3)
    copy_model("WallStain", (.48, .22, .10), (.10,) * 3)

    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("AuthoringCamera")
    camera = bpy.data.objects.new("AuthoringCamera", camera_data)
    collection.objects.link(camera)
    camera.location = (-1.1, -1.8, 1.65)
    camera.rotation_euler = (Vector((.02,.10,.04)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 1.12
    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.background_type = "WORLD"
    scene.world.color = (.045, .045, .045)
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(SOURCE / "HomeToiletAction-Authoring.png")
    bpy.ops.render.render(write_still=True)
    for obj in collection.objects:
        if obj.type == "MESH": obj.hide_render = obj not in pedestal_preview + water_preview
    camera.location = (2.10, -.80, 1.10)
    camera.rotation_euler = (Vector((1.25,.10,.24)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.ortho_scale = 1.10
    scene.render.filepath = str(SOURCE / "ToiletPedestal-Opening.png")
    bpy.ops.render.render(write_still=True)
    for obj in collection.objects:
        if obj.type == "MESH": obj.hide_render = obj not in seat_preview
    camera.location = (3.10, -.65, .75)
    camera.rotation_euler = (Vector((2.50,.10,0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.ortho_scale = .68
    scene.render.filepath = str(SOURCE / "ToiletSeat-Opening.png")
    bpy.ops.render.render(write_still=True)
    for obj in collection.objects:
        if obj.type == "MESH": obj.hide_render = False


def main():
    args_list = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate-only", "--validate", action="store_true")
    parser.add_argument("--preview", action="store_true")
    parser.add_argument("--preview-only", action="store_true",
                        help="Render true-scale authoring view; do not touch Unity assets or manifests")
    parser.add_argument("--only-model", choices=tuple(definitions()),
                        help="Export only this model; refresh full manifests and Blender source")
    args = parser.parse_args(args_list)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1
    defs = definitions()
    roots, objects = {}, {}
    for name, definition in defs.items():
        root = bpy.data.objects.new(name + "_Root", None)
        scene.collection.objects.link(root)
        roots[name] = root
        for mesh_name, geometry, mat_name in definition["meshes"]:
            objects[mesh_name] = create_mesh(mesh_name, geometry, mat_name, root)
        for anchor_name, point in definition["anchors"].items():
            anchor = bpy.data.objects.new(anchor_name, None)
            scene.collection.objects.link(anchor)
            anchor.parent = root
            anchor.location = source_point(point)
            anchor.empty_display_type = "PLAIN_AXES"
            anchor.empty_display_size = .012
    report = validate(defs, objects)
    signature = canonical(defs)
    if args.preview_only:
        SOURCE.mkdir(parents=True, exist_ok=True)
        compose_preview(roots)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "HomeToiletAction.blend"))
        print("HOME_TOILET_ACTION_PREVIEW_TRUE_METRES stream=.003m drop=.004m splash=.028m", flush=True)
        return
    if args.validate_only:
        manifest = json.loads((SOURCE / "home-toilet-action-3d-model.json").read_text(encoding="utf-8"))
        assert manifest["signature"] == signature, "Export is stale; regenerate assets"
        for name in defs:
            path = MODELS / (name + ".fbx")
            assert path.exists() and path.stat().st_size > 1000, path
            meta = Path(str(path) + ".meta").read_text(encoding="utf-8")
            for setting in ("globalScale: 1", "bakeAxisConversion: 1", "useFileUnits: 1", "preserveHierarchy: 1"):
                assert setting in meta, (path, setting)
        validate_exported_geometry(defs)
        report["fbx_round_trip_metres_axes_and_anchors"] = True
        print("HOME_TOILET_ACTION_VALIDATED " + json.dumps(report) + " sha256=" + signature, flush=True)
        return
    SOURCE.mkdir(parents=True, exist_ok=True)
    MODELS.mkdir(parents=True, exist_ok=True)
    write_meta(RESOURCES, folder=True)
    write_meta(MODELS, folder=True)
    model_records = []
    for name, definition in defs.items():
        root = roots[name]
        bpy.ops.object.select_all(action="DESELECT")
        root.select_set(True)
        for child in root.children:
            child.select_set(True)
        bpy.context.view_layer.objects.active = root
        path = MODELS / (name + ".fbx")
        if args.only_model is None or args.only_model == name:
            bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
                object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
                apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
                add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True,
                mesh_smooth_type="FACE", use_custom_props=True, path_mode="STRIP",
                embed_textures=False)
        write_meta(path, model=True)
        model_records.append({"name": name, "resource": "HomeToiletAction/Models/" + name,
            "contract": definition["contract"], "anchors": definition["anchors"],
            "meshes": [{"name": mesh_name, "material": mat_name,
                        "bounds_min": bounds(geometry)[0], "bounds_max": bounds(geometry)[1],
                        "triangles": len(objects[mesh_name].data.loop_triangles)}
                       for mesh_name, geometry, mat_name in definition["meshes"]]})
    payload = {"schema_version": 1, "generator_version": VERSION,
               "signature": signature, "coordinates": "Unity local metres +Y up +Z forward",
               "source_conversion": "Y/Z swap and face rewinding; FBX -Z forward Y up",
               "palette_srgb_hex": COLORS, "report": report, "models": model_records,
               "runtime_contract": "Reuse shared materials. No generated runtime geometry. Hero arm remains Player3DV2. Use model hierarchy for unit and axis conversion."}
    text = json.dumps(payload, indent=2) + "\n"
    (SOURCE / "home-toilet-action-3d-model.json").write_text(text, encoding="utf-8")
    runtime_manifest = RESOURCES / "HomeToiletAction.json"
    runtime_manifest.write_text(text, encoding="utf-8")
    write_meta(runtime_manifest)
    if args.preview:
        compose_preview(roots)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "HomeToiletAction.blend"))
    print("HOME_TOILET_ACTION_EXPORTED " + json.dumps(report) + " sha256=" + signature, flush=True)


if __name__ == "__main__":
    main()
