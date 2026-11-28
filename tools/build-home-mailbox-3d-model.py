"""Author the City mailbox and its inventory envelope in metres."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector
import numpy as np

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))

from atlas_kit import PixelCanvas
from world_contact_finishes import BARE_METAL, PAINTED_STEEL, patch, response

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets/Resources/City/HomeMailbox"
SOURCE = ROOT / "ArtSource/HomeMailbox"
TILE = 64
COLORS = {
    "paint": (77, 103, 99),
    "grip": (87, 109, 103),
    "zinc": (115, 118, 110),
    "inside": (70, 78, 74),
    "envelope": (196, 184, 155),
    "sheet": (222, 215, 188),
    "seam": (164, 151, 119),
}


def atlases():
    u, v = np.meshgrid((np.arange(TILE)+.5)/TILE, 1-(np.arange(TILE)+.5)/TILE)
    pigment = PixelCanvas(TILE*4, TILE*2)
    finish = PixelCanvas(TILE*4, TILE*2)
    tooth = np.sin(92*u + np.cos(43*v))*np.cos(101*v + 19*u)
    for index, (name, rgb) in enumerate(COLORS.items()):
        lift = 2.1*tooth + 1.4*np.sin(8*u+3*v)
        if name in {"paint", "grip", "zinc", "inside"}:
            contact = .82*patch(u, v, .51, .50, .28, .44) if name == "grip" else 0.
            chip = np.clip((patch(u, v, .74, .47, .095, .32) + .16*tooth - .63)*2.7, 0, 1) if name == "grip" else 0.
            smooth, metal = response(BARE_METAL if name in {"zinc", "inside"} else PAINTED_STEEL,
                                     contact, chip)
            lift += 14*chip + 4*contact
            if name == "paint":
                lift -= 4.3*(1-v)*np.sin(16*u+.4)**6
        else:
            smooth, metal = .14, 0.
        row, col = divmod(index, 4)
        for image, channels in [
            (pigment, [value+lift for value in rgb]+[255]),
            (finish, [metal*255, 255, 0, smooth*255]),
        ]:
            pixels = np.frombuffer(image.pixels, np.uint8).reshape(TILE*2, TILE*4, 4)
            tile = pixels[row*TILE:(row+1)*TILE, col*TILE:(col+1)*TILE]
            tile[:] = np.rint(np.stack(np.broadcast_arrays(*channels, lift)[:4], axis=-1)).clip(0,255)
    return {"HomeMailboxPigment.png": pigment.png_bytes(), "HomeMailboxFinish.png": finish.png_bytes()}


def anchor(name, position=(0, 0, 0), parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj.location = position
    return obj


def block(name, center, size, radius, finish, parent):
    bpy.ops.mesh.primitive_cube_add(size=1)
    obj = bpy.context.object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = obj.modifiers.new("Soft stamped edges", "BEVEL")
    bevel.width = radius
    bevel.segments = 3
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    for face in obj.data.polygons:
        face.use_smooth = True
    normals = obj.modifiers.new("Weighted panel normals", "WEIGHTED_NORMAL")
    normals.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=normals.name)

    uv = obj.data.uv_layers.active.data
    row, col = divmod(tuple(COLORS).index(finish), 4)
    for face in obj.data.polygons:
        axis = max(range(3), key=lambda i: abs(face.normal[i]))
        first, second = ((1, 2), (0, 2), (0, 1))[axis]
        for loop in face.loop_indices:
            point = obj.data.vertices[obj.data.loops[loop].vertex_index].co
            u = .5 + point[first]/size[first]
            v = .5 + point[second]/size[second]
            uv[loop].uv = ((col + .07 + .86*u)/4, (1-row + .07 + .86*v)/2)
    obj.data.materials.append(bpy.data.materials["MailboxFinishes"])
    obj.parent = parent
    obj.location = center
    return obj


def merge(name, parts):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    return result


def mailbox():
    # Blender y points toward the walk. The open slot is between the two front flanges.
    root = anchor("MailboxModel")
    body = [
        block("Post", (0, 0, .429), (.104, .100, .898), .013, "paint", root),
        block("Foot", (0, 0, .018), (.166, .152, .036), .010, "zinc", root),
        block("Mount", (0, 0, .858), (.212, .155, .044), .008, "zinc", root),
        block("Floor", (0, 0, .885), (.496, .355, .035), .009, "paint", root),
        block("Back", (0, -.167, 1.018), (.480, .020, .251), .006, "paint", root),
        block("Lower flange", (0, .166, .978), (.470, .028, .174), .008, "paint", root),
        block("Upper flange", (0, .166, 1.129), (.470, .027, .023), .004, "paint", root),
        block("Slot lip", (0, .180, 1.064), (.340, .014, .010), .003, "inside", root),
        block("Mail shelf", (0, .062, 1.061), (.400, .200, .010), .003, "inside", root),
        block("Nameplate", (-.077, .183, .995), (.152, .009, .052), .004, "zinc", root),
        block("Nameplate inset", (-.077, .188, .995), (.132, .002, .035), .0008, "envelope", root),
    ]
    for sign in (-1, 1):
        body += [
            block("Folded side", (sign*.232, 0, 1.015), (.031, .355, .255), .008, "paint", root),
            block("Hinge bracket", (sign*.171, -.174, 1.145), (.060, .048, .038), .009, "zinc", root),
            block("Plate rivet", (-.077+sign*.065, .19, .995), (.007, .004, .007), .0017, "inside", root),
        ]
    merge("Cabinet", body)

    hinge = anchor("LidHinge", (0, -.165, 1.151), root)
    cap = [
        block("Cap", (0, .155, .012), (.519, .360, .031), .013, "paint", hinge),
        block("Rolled front", (0, .331, .005), (.501, .035, .029), .011, "paint", hinge),
        block("Handle", (0, .319, .043), (.121, .021, .020), .008, "grip", hinge),
        block("Handle foot L", (-.045, .319, .029), (.016, .019, .020), .004, "zinc", hinge),
        block("Handle foot R", (.045, .319, .029), (.016, .019, .020), .004, "zinc", hinge),
    ]
    lid = merge("StampedLid", cap)
    # Store the hinge directly as the mesh origin. The FBX axis bake then
    # converts it at the same hierarchy depth as the cabinet and mail rest.
    lid.data.transform(Matrix.Translation(lid.location))
    lid.parent = root
    lid.location = hinge.location
    bpy.data.objects.remove(hinge, do_unlink=True)
    lid.name = "LidHinge"
    anchor("MailRest", (-.046, .156, 1.084), root)
    return root


def bills():
    root = anchor("HomeBillsModel")
    parts = [
        block("Envelope", (0, .006, 0), (.203, .118, .007), .0025, "envelope", root),
        block("Overlapping flap", (0, .028, .004), (.193, .058, .003), .001, "seam", root),
        block("Flap stock", (0, .030, .0055), (.193, .052, .002), .0008, "envelope", root),
        block("Folded first sheet", (.007, -.051, .0015), (.176, .049, .003), .0009, "sheet", root),
        block("Folded second sheet", (-.001, -.054, .0034), (.171, .042, .002), .0007, "sheet", root),
    ]
    for index, width in enumerate((.071, .047, .057)):
        parts.append(block("Faint address line", (-.025, -.002-index*.008, .0037),
                           (width, .0014, .0006), .00024, "seam", root))
    merge("FoldedBills", parts)
    return root


def export(asset_dir, name, root):
    bpy.ops.object.select_all(action="DESELECT")
    for member in (root, *root.children_recursive):
        member.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(asset_dir / (name+".fbx")), use_selection=True,
                             object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
                             apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="FACE")


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(point) for point in obj.bound_box]
    return tuple(bound(p[i] for p in corners) for bound in (min, max) for i in range(3))


def check_export(path, root):
    bpy.context.view_layer.update()
    expected = {obj.name: world_bounds(obj) if obj.type == "MESH" else tuple(obj.matrix_world.translation)
                for obj in root.children_recursive}
    existing = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path))
    bpy.context.view_layer.update()
    imported = set(bpy.data.objects) - existing
    try:
        for obj in imported:
            name = obj.name.split(".")[0]
            if name not in expected:
                continue
            actual = world_bounds(obj) if obj.type == "MESH" else tuple(obj.matrix_world.translation)
            assert np.allclose(actual, expected.pop(name), atol=.0002), f"{path.name}: misplaced {name}: {actual}"
        assert not expected, f"{path.name}: missing parts {list(expected)}"
    finally:
        for obj in imported:
            bpy.data.objects.remove(obj, do_unlink=True)


def describe(root):
    return {object_.name: {"vertices": len(object_.data.vertices),
                           "triangles": sum(len(face.vertices)-2 for face in object_.data.polygons)}
            for object_ in root.children_recursive if object_.type == "MESH"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset-dir", type=Path, default=ASSETS)
    parser.add_argument("--source-dir", type=Path, default=SOURCE)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.data.materials.new("MailboxFinishes")
    models = {"HomeMailbox": mailbox(), "HomeBills": bills()}
    maps = atlases()
    manifest = {"metres": {"hinge": [0, 1.151, -.165], "mail_rest": [-.046, 1.084, .156],
                           "slot_height": [1.069, 1.117], "lid_open_degrees": 99},
                "meshes": {name: describe(root) for name, root in models.items()},
                "maps": {name: hashlib.sha256(data).hexdigest() for name, data in maps.items()}}
    manifest_text = json.dumps(manifest, indent=2)+"\n"
    manifest_path = args.source_dir / "HomeMailbox.manifest.json"
    if args.validate_only:
        assert manifest_path.read_text() == manifest_text, "Mailbox manifest differs from source"
        for name, data in maps.items():
            assert (args.asset_dir / name).read_bytes() == data, name
    else:
        args.asset_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        for name, root in models.items():
            export(args.asset_dir, name, root)
        for name, data in maps.items():
            (args.asset_dir / name).write_bytes(data)
        manifest_path.write_text(manifest_text)
    for name, root in models.items():
        check_export(args.asset_dir / (name+".fbx"), root)
    print("HOME MAILBOX MODEL CONTRACT OK")


if __name__ == "__main__":
    main()
