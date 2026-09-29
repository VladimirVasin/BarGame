#!/usr/bin/env python3
"""Build the deterministic ordinary two-armed bar bartender.

Run through Blender, not CPython::

    blender --background --factory-startup --python-exit-code 1 --python \
        tools/build-ordinary-bartender-3d-model.py

The legacy six-armed bartender has its own generator and assets.  This
parallel source deliberately keeps that pipeline untouched while sharing its
NpcHumanV2-compatible 31-bone body substrate.  The active bartender is an
ordinary publican in a dark green waistcoat, rolled sleeves and apron, with
the standard left-vessel and right-bottle sockets used by the authored cafe
service set. Long counter travel reuses Hero V2's full ordinary walk cycle.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender-only entry.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "4.0.0"
DESIGN_ID = "bar_bartender_v2"
DISPLAY_NAME = "Bar Bartender"
SEED = 460918
TOTAL_HEIGHT = 1.75
MIN_TRIANGLES = 4500
MAX_TRIANGLES = 8000
SHARED_MATERIAL_ASSET = "Assets/Player3D/Materials/Player3DLit.mat"
SERVICE_ANIMATION_ASSET = (
    "Assets/Pedestrians/Animations/MountainRoadCafeCast.fbx"
)
SERVICE_ANIMATION_CLIPS = (
    "CafeAttendantWipe",
    "CafeAttendantWalk",
    "CafeAttendantPour",
    "CafeAttendantNotice",
)
LOCOMOTION_ANIMATION_ASSET = (
    "Assets/Player3D/V2/Animations/PlayerCharacter3DV2Animations.fbx"
)
LOCOMOTION_ANIMATION_CLIP = "Walk"
SOCKET_NAMES = (
    "SOCKET_Grip.L",
    "SOCKET_Vessel.L",
    "SOCKET_Grip.R",
    "SOCKET_Bottle.R",
)
ANCHOR_NAMES = (
    "ANCHOR_BartenderVesselGrip",
    "ANCHOR_BartenderBottleGrip",
)


def load_module(filename: str, module_name: str):
    source_path = Path(__file__).with_name(filename)
    spec = importlib.util.spec_from_file_location(module_name, source_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load generator helpers from {source_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


legacy = load_module(
    "build-bartender-3d-model.py",
    "bp_legacy_bar_bartender_build",
)
base = legacy.base
detail = load_module("npc_detail_geometry.py", "bp_ordinary_bartender_detail")
surface_atlas = load_module("npc_detail_atlas.py", "bp_ordinary_bartender_atlas")


def outward_faces(vertices, faces):
    return detail.outward((vertices, faces))[1]


def merge_geometry(*geometries):
    vertices, faces = [], []
    for points, polygons in geometries:
        offset = len(vertices)
        vertices.extend(points)
        faces.extend(tuple(index + offset for index in face) for face in polygons)
    return vertices, faces


def closed_panel(rows, thickness):
    """A shaped fabric panel with two surfaces and a visible sewn edge."""
    columns, count = len(rows[0]), sum(len(row) for row in rows)
    points = [point for row in rows for point in row]
    vertices = points + [point + thickness for point in points]
    faces = []
    for row in range(len(rows)-1):
        for column in range(columns-1):
            a = row * columns + column
            quad = (a, a+1, a+1+columns, a+columns)
            faces.extend((quad, tuple(index+count for index in reversed(quad))))
    perimeter = (list(range(columns)) +
                 [row*columns+columns-1 for row in range(1, len(rows))] +
                 [count-1-column for column in range(1, columns)] +
                 [row*columns for row in range(len(rows)-2, 0, -1)])
    for a, b in zip(perimeter, perimeter[1:]+perimeter[:1]):
        faces.append((a, a+count, b+count, b))
    return detail.outward((vertices, faces))


def sole_geometry(center_x):
    footprint = ((-.255, .046), (-.236, .060), (-.192, .068),
                 (-.120, .069), (-.050, .061), (.020, .053), (.036, .049))
    outline = [(center_x-width, y*.92) for y, width in footprint]
    outline += [(center_x+width, y*.92) for y, width in reversed(footprint)]
    return detail.loft([[(x, y, z) for x, y in outline] for z in (0, .017, .025)])

PALETTE = dict(legacy.PALETTE)
PALETTE.update(
    {
        "waistcoat": (0.095, 0.205, 0.145, 1.0),
        "waistcoat_dark": (0.055, 0.125, 0.088, 1.0),
        "shirt": (0.610, 0.585, 0.500, 1.0),
        "shirt_shadow": (0.455, 0.430, 0.365, 1.0),
        "apron": (0.135, 0.120, 0.095, 1.0),
        "towel": (0.535, 0.550, 0.500, 1.0),
    }
)

# The shared builder reads these module globals when it creates rigidly
# skinned parts.  Keep the legacy publican proportions, but stamp every new
# object with this parallel source identity and palette.
base.NPC_PROFILE_KEY = "six_armed_bartender"
base.GENERATOR_VERSION = GENERATOR_VERSION
base.DESIGN_ID = DESIGN_ID
base.SEED = SEED
base.PALETTE = PALETTE


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(
            "ArtSource/Bar/Bartender/Blender/"
            "BarBartenderOrdinary3D.blend"
        ),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path(
            "Assets/Bar/Bartender/Models/"
            "BarBartenderOrdinary3D.fbx"
        ),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path(
            "Assets/Bar/Bartender/Models/"
            "BarBartenderOrdinary3D.json"
        ),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path(
            "ArtSource/Bar/Bartender/Blender/"
            "BarBartenderOrdinary3D.png"
        ),
    )
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    arguments = (
        sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    )
    config = parser.parse_args(arguments)
    for field_name in ("output", "fbx", "manifest", "preview"):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


class OrdinaryBartenderBuilder(legacy.BartenderBuilder):
    def build(self):
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        bartender = bpy.data.collections.new("BP_BarBartenderOrdinary3D")
        scene_root.children.link(bartender)
        export_collection = bpy.data.collections.new(
            "EXPORT_BarBartenderOrdinary"
        )
        bartender.children.link(export_collection)
        presentation = bpy.data.collections.new(
            "PRESENTATION_BarBartenderOrdinary"
        )
        bartender.children.link(presentation)

        material = self.create_shared_material()
        root = bpy.data.objects.new("ROOT_Player", None)
        export_collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root["bp_export"] = True
        root["bp_generator"] = (
            "tools/build-ordinary-bartender-3d-model.py"
        )
        root["bp_generator_version"] = GENERATOR_VERSION
        root["bp_design_id"] = DESIGN_ID
        root["bp_seed"] = SEED
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_pose"] = "apose"
        root["bp_has_own_animations"] = False

        rig = self.create_armature(export_collection, root)
        self.result = base.BuildResult(
            root, rig, export_collection, material
        )
        super().build_body()
        self.build_rolled_sleeves()
        super().build_head()
        self.build_uniform()
        self.refine_detailed_geometry()
        for part in self.result.parts:
            palette, name = part.palette_name, part.obj.name
            kind = ("skin_white" if palette.startswith("skin") or palette in {"eye", "pupil", "button"}
                    else "leather" if palette == "leather"
                    else "hair" if palette in {"hair", "moustache"} and "Cap" not in name
                    else "cloth")
            surface_atlas.apply_uv(part.obj, kind)
        self.build_service_anchors()
        self.configure_scene_metadata()
        return self.result

    @staticmethod
    def reset_scene() -> None:
        base.PedestrianBuilder.reset_scene()
        bpy.context.scene.world.name = "WORLD_BarBartenderOrdinaryPreview"

    @staticmethod
    def create_shared_material():
        material = base.PedestrianBuilder.create_shared_material()
        material["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        return material

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = (
            "tools/build-ordinary-bartender-3d-model.py"
        )
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = DESIGN_ID
        scene["bp_seed"] = SEED
        scene["bp_has_own_animations"] = False
        scene["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        scene["bp_animation_asset"] = SERVICE_ANIMATION_ASSET
        scene["bp_locomotion_animation_asset"] = LOCOMOTION_ANIMATION_ASSET
        scene["bp_anatomy_standard"] = base.NPC_ANATOMY_STANDARD
        scene["bp_rest_pelvis_height_m"] = base.NPC_PELVIS_HEIGHT
        scene["bp_arm_design"] = "ordinary_two_armed_v2"
        scene["bp_signature_anatomy"] = "[]"

    def build_rolled_sleeves(self) -> None:
        # The legacy publican body has shirt-coloured forearms.  A slightly
        # wider skin sleeve masks those surfaces from below the elbow, while a
        # thick cloth ring makes the rolled cuff readable at PS1 distance.
        points = {
            "L": (
                (0.480, -0.011, 1.168),
                (0.680, -0.018, 1.075),
            ),
            "R": (
                (-0.478, -0.009, 1.162),
                (-0.678, -0.016, 1.069),
            ),
        }
        for side in ("L", "R"):
            cuff, wrist = points[side]
            self.add_part(
                f"GEO_ExposedForearm.{side}",
                base.make_frustum_between(cuff, wrist, 0.061, 0.046, 12),
                f"forearm.{side}",
                "body",
                "skin",
            )
            sign = 1.0 if side == "L" else -1.0
            self.add_part(
                f"CLO_RolledCuff.{side}",
                base.make_frustum_between(
                    (sign * 0.445, -0.010, 1.184),
                    (sign * 0.505, -0.012, 1.153),
                    0.075,
                    0.067,
                    10,
                ),
                f"forearm.{side}",
                "uniform",
                "shirt_shadow",
            )

    def build_uniform(self) -> None:
        self.add_part(
            "CLO_WaistcoatFront",
            base.make_tapered_box(
                (0, -0.088, 0.820),
                (0, -0.102, 1.300),
                (0.300, 0.036, 0),
                (0.330, 0.036, 0),
            ),
            "chest",
            "uniform",
            "waistcoat",
        )
        self.add_part(
            "CLO_WaistcoatBack",
            base.make_tapered_box(
                (0, 0.104, 0.820),
                (0, 0.096, 1.300),
                (0.320, 0.036, 0),
                (0.350, 0.036, 0),
            ),
            "chest",
            "uniform",
            "waistcoat_dark",
        )
        self.add_part(
            "CLO_Apron",
            base.make_tapered_box(
                (0, -0.110, 0.500),
                (0, -0.096, 0.830),
                (0.285, 0.024, 0),
                (0.305, 0.024, 0),
            ),
            "pelvis",
            "uniform",
            "apron",
        )
        self.add_part(
            "CLO_ApronTie",
            base.make_box((0, 0.112, 0.815), (0.390, 0.028, 0.035)),
            "pelvis",
            "uniform",
            "apron",
        )
        for index in range(3):
            self.add_part(
                f"CLO_Button.{index + 1}",
                base.make_box(
                    (0.0, -0.124, 1.215 - index * 0.120),
                    (0.026, 0.014, 0.026),
                ),
                "chest",
                "uniform",
                "button",
            )

        # The towel is a real model part bound to the left hand.  Runtime
        # hides it only while that hand steadies a served vessel.
        self.add_part(
            "ACC_ServiceTowel",
            base.make_tapered_box(
                (0.748, -0.030, 0.940),
                (0.748, -0.030, 1.100),
                (0.180, 0.025, 0),
                (0.145, 0.025, 0),
            ),
            "hand.L",
            "held_prop",
            "towel",
        )

    def replace_geometry(self, name, geometry, *, smooth=True):
        """Replace the ordinary source only, retaining its renderer/rig contract."""
        part = next(part for part in self.result.parts if part.obj.name == name)
        obj = part.obj
        vertices, faces = geometry
        vertices = [self.remap_geometry_point(point, part.bone, part.role, name)
                    - obj.location for point in vertices]
        mesh = bpy.data.meshes.new(name + "_DetailedMesh")
        mesh.from_pydata(vertices, [], outward_faces(vertices, faces))
        mesh.update(calc_edges=True)
        mesh.materials.append(self.result.material)
        for polygon in mesh.polygons:
            polygon.use_smooth = smooth
        previous = obj.data
        obj.data = mesh
        group = obj.vertex_groups.get(part.bone) or obj.vertex_groups.new(name=part.bone)
        group.add(range(len(vertices)), 1.0, "REPLACE")
        if previous.users == 0:
            bpy.data.meshes.remove(previous)
        return obj

    def detail_part(self, name, geometry, bone, role, palette, *, smooth=True):
        vertices, faces = geometry
        obj = self.add_part(name, (vertices, outward_faces(vertices, faces)),
                            bone, role, palette)
        for polygon in obj.data.polygons:
            polygon.use_smooth = smooth
        return obj

    def refine_detailed_geometry(self):
        """Tailored work clothes and human forms on the unchanged service rig.

        Stations describe shoulder, ribcage, waist, knee, calf and wrist shape;
        they are construction, not subdivision of the former boxes. Each part
        retains one canonical bone so existing service/locomotion clips work.
        """
        torso = (
            (.790, .154, .096, .009), (.850, .174, .105, .009),
            (.970, .192, .115, .009), (1.090, .193, .112, .008),
            (1.210, .191, .106, .003), (1.280, .187, .099, -.002),
            (1.330, .158, .087, -.010), (1.375, .075, .062, -.020),
        )
        self.replace_geometry("GEO_Torso", base.make_vertical_shell(torso, 16))
        self.replace_geometry("GEO_Pelvis", base.make_vertical_shell((
            (.665, .122, .086, .010), (.715, .154, .100, .010),
            (.765, .166, .104, .010), (.815, .159, .100, .010),
            (.850, .150, .096, .010)), 16))
        self.replace_geometry("GEO_NeckStub", base.make_vertical_shell((
            (1.325, .078, .064, -.018), (1.365, .069, .061, -.022),
            (1.410, .062, .057, -.024), (1.470, .065, .061, -.026)), 14))

        for side, sign in (("L", 1), ("R", -1)):
            shoulder = Vector((sign * .208, -.004 if sign > 0 else .004, 1.292))
            elbow = Vector((sign * .470, -.010, 1.175))
            wrist = Vector((sign * .680, -.018, 1.075))
            self.replace_geometry(f"GEO_UpperArm.{side}", base.make_profiled_segment(
                shoulder, elbow, ((0, .070, 1.0), (.16, .079, 1.04),
                (.37, .076, 1.04), (.60, .070, 1.02), (.80, .062, 1.0),
                (.94, .060, 1.0), (1, .061, 1.0)), 12))
            # The cuff has real rolled layers; it no longer hides a full
            # shirt-coloured cylinder inside the whole exposed forearm.
            self.replace_geometry(f"GEO_Forearm.{side}", base.make_profiled_segment(
                elbow, elbow.lerp(wrist, .19),
                ((0, .059, 1), (.42, .062, 1.02), (.76, .064, 1.01), (1, .058, 1)), 12))
            self.replace_geometry(f"GEO_ExposedForearm.{side}", base.make_profiled_segment(
                elbow.lerp(wrist, .08), wrist,
                ((0, .057, .92), (.18, .059, .94), (.38, .055, .94),
                 (.62, .047, .92), (.85, .038, .90), (1, .034, .92)), 12))
            self.replace_geometry(f"CLO_RolledCuff.{side}", base.make_profiled_segment(
                elbow.lerp(wrist, -.06), elbow.lerp(wrist, .22),
                ((0, .063, 1), (.12, .070, 1), (.28, .071, 1),
                 (.42, .066, 1), (.57, .071, 1), (.78, .070, 1),
                 (.94, .064, 1), (1, .060, 1)), 12))
            hip = (sign * .083, .012 if sign > 0 else -.004, .750)
            knee = (sign * .103, -.012 if sign > 0 else .012, .354)
            ankle = (sign * .112, -.026 if sign > 0 else .018, .095)
            self.replace_geometry(f"GEO_Thigh.{side}", base.make_profiled_segment(
                hip, knee, ((0, .094, .95), (.13, .099, .96), (.30, .093, .98),
                (.53, .081, 1), (.75, .071, 1.01), (.89, .070, 1), (1, .070, .98)), 12))
            self.replace_geometry(f"GEO_Shin.{side}", base.make_profiled_segment(
                knee, ankle, ((0, .070, .99), (.18, .072, 1.0), (.36, .075, 1.04),
                (.58, .067, 1), (.80, .057, 1), (.92, .061, 1), (1, .058, 1)), 12))
            self.replace_geometry(f"GEO_Foot.{side}", base.make_cafe_shoe(
                sign * .112, length_scale=.92, width_scale=1.02, height_scale=.91))
            self.detail_part(f"CLO_ShoeSole.{side}", sole_geometry(sign * .112),
                             f"foot.{side}", "footwear_detail", "leather", smooth=False)
            self.detail_part(f"CLO_ShoeTongue.{side}", base.make_ellipsoid(
                (sign * .112, -.056, .121), (.041, .058, .014), 10, 4),
                f"foot.{side}", "footwear_detail", "leather")
            # Short stitched seams describe the shoe panels, without laces
            # or new costume objects changing this worker's silhouette.
            for index, z in enumerate((.116, .121)):
                self.detail_part(f"CLO_ShoeStitch{index}.{side}", base.make_profiled_segment(
                    (sign * .112 - .032, -.096 + index * .013, z),
                    (sign * .112 + .032, -.096 + index * .013, z),
                    ((0, .0015, 1), (.2, .0018, 1), (.8, .0018, 1), (1, .0015, 1)), 5),
                    f"foot.{side}", "footwear_detail", "shirt_shadow")

        self.refine_hands()
        self.refine_head()
        self.refine_work_clothes()

    def refine_head(self):
        self.replace_geometry("GEO_Head", base.make_vertical_shell((
            (1.451, .041, .052, -.030), (1.474, .073, .074, -.030),
            (1.510, .101, .089, -.030), (1.552, .115, .104, -.030),
            (1.598, .114, .108, -.030), (1.641, .107, .102, -.025),
            (1.677, .083, .084, -.019), (1.701, .037, .047, -.014)), 16))
        for side, sign in (("L", 1), ("R", -1)):
            self.replace_geometry(f"FACE_Ear.{side}", base.make_ellipsoid(
                (sign * .112, -.030, 1.575), (.022, .019, .040), 10, 6))
            self.detail_part(f"FACE_EarFold.{side}", base.make_ellipsoid(
                (sign * .121, -.043, 1.577), (.009, .008, .023), 8, 5),
                "head", "human_face", "skin_shadow")
            self.detail_part(f"FACE_UpperLid.{side}", base.make_ellipsoid(
                (sign * .050, -.134, 1.617), (.024, .011, .0075), 10, 4),
                "head", "human_face", "skin")
            self.detail_part(f"FACE_LowerLid.{side}", base.make_ellipsoid(
                (sign * .050, -.134, 1.594), (.023, .008, .005), 10, 4),
                "head", "human_face", "skin_shadow")
            self.detail_part(f"FACE_Brow.{side}", base.make_profiled_segment(
                (sign * .025, -.127, 1.638), (sign * .079, -.114, 1.632),
                ((0, .004, .65), (.25, .007, .7), (.72, .006, .7), (1, .002, .6)), 6),
                "head", "human_face", "hair")
        self.replace_geometry("FACE_Nose", base.make_vertical_shell((
            (1.547, .012, .011, -.145), (1.555, .023, .020, -.148),
            (1.567, .024, .023, -.148), (1.584, .014, .017, -.142),
            (1.610, .009, .011, -.132), (1.627, .007, .007, -.124)), 10))
        moustache = []
        for sign in (-1, 1):
            moustache.append(base.make_profiled_segment(
                (sign * .002, -.148, 1.540), (sign * .051, -.136, 1.530),
                ((0, .008, 1.5), (.22, .010, 1.25), (.55, .010, 1),
                 (.82, .007, .9), (1, .002, .7)), 8))
        self.replace_geometry("FACE_Moustache", merge_geometry(*moustache))
        self.detail_part("FACE_LowerLip", base.make_ellipsoid(
            (0, -.128, 1.516), (.026, .009, .005), 10, 4),
            "head", "human_face", "skin_shadow")
        self.replace_geometry("HAIR_FlatCap", base.make_vertical_shell((
            (1.686, .112, .105, -.005), (1.694, .117, .109, -.012),
            (1.708, .119, .113, -.013), (1.730, .106, .105, -.021),
            (1.745, .075, .081, -.025), (1.750, .035, .042, -.024)), 16))
        self.detail_part("CLO_CapBill", closed_panel([
            [Vector((x, y, z)) for x, y, z in row] for row in (
                ((-.088, -.090, 1.698), (-.044, -.119, 1.698), (0, -.125, 1.698), (.044, -.119, 1.698), (.088, -.090, 1.698)),
                ((-.085, -.115, 1.697), (-.044, -.155, 1.695), (0, -.167, 1.694), (.044, -.155, 1.695), (.085, -.115, 1.697)),
            )], Vector((0, 0, -.005))), "head", "hair", "hair", smooth=False)

    def refine_hands(self):
        # Unlike the old inherited mitten coordinates these are already in
        # the production skeleton's metre space: do not remap them twice.
        bpy.context.view_layer.update()
        for side in ("L", "R"):
            bone = base.BONE_BY_NAME[f"hand.{side}"]
            shapes = detail.hand(bone.head, bone.tail, side)
            for suffix, geometry in shapes.items():
                name = (f"GEO_Hand.{side}" if suffix == "palm" else
                        f"GEO_Thumb.{side}" if suffix == "thumb" else
                        f"GEO_Finger{suffix[-1]}.{side}")
                obj = bpy.data.objects.get(name)
                if obj is None:
                    obj = self.add_part(name, base.make_box((0, 0, 0), (.01, .01, .01)),
                                        f"hand.{side}", "hand_finger", "skin")
                    bpy.context.view_layer.update()
                detail.replace_mesh(obj, geometry)
                for polygon in obj.data.polygons:
                    polygon.use_smooth = True

    def refine_work_clothes(self):
        front = []
        # Constructed front panels leave a modest V of the original shirt.
        rows = ((.805, .157, .099, .009, .010), (.860, .178, .110, .009, .006),
                (.970, .197, .119, .009, .006), (1.080, .198, .116, .008, .006),
                (1.170, .196, .111, .005, .030), (1.250, .193, .106, 0, .081),
                (1.302, .177, .095, -.006, .118))
        for sign in (-1, 1):
            grid = []
            for z, rx, ry, cy, gap in rows:
                grid.append([Vector((sign * x, cy - ry * math.sqrt(max(0, 1-(x/rx)**2)) - .003, z))
                             for x in (gap + (.96 * rx - gap) * col / 7 for col in range(8))])
            front.append(closed_panel(grid, Vector((0, .004, 0))))
        self.replace_geometry("CLO_WaistcoatFront", merge_geometry(*front))
        grid = []
        for z, rx, ry, cy, _ in rows:
            grid.append([Vector((x, cy + ry * math.sqrt(max(0, 1-(x/rx)**2)) + .003, z))
                         for x in (-.96 * rx + 1.92 * rx * col / 12 for col in range(13))])
        self.replace_geometry("CLO_WaistcoatBack", closed_panel(grid, Vector((0, -.004, 0))))
        for side, sign in (("L", 1), ("R", -1)):
            side_rows = [[Vector((sign * rx * math.cos(angle), cy + ry * math.sin(angle), z))
                          for angle in (-.30 + .60*column/4 for column in range(5))]
                         for z, rx, ry, cy, _ in rows]
            self.detail_part(f"CLO_WaistcoatSide.{side}",
                             closed_panel(side_rows, Vector((-sign*.004, 0, 0))),
                             "chest", "uniform", "waistcoat_dark")
        apron_rows = []
        for row in range(6):
            fraction = row / 5
            width = .158 + .012 * (1 - fraction)
            apron_rows.append([Vector(((-1+2*col/12)*width,
                -.125 - .007 * math.cos(col * math.pi/2) * (1-fraction),
                .500 + fraction * .330 + (.005 * math.cos(col*.8) if row == 0 else 0)))
                for col in range(13)])
        self.replace_geometry("CLO_Apron", closed_panel(apron_rows, Vector((0, .004, 0))))
        self.replace_geometry("CLO_ApronTie", base.make_vertical_shell((
            (.803, .175, .113, .01), (.809, .181, .116, .01),
            (.827, .181, .116, .01), (.833, .176, .113, .01)), 16))
        for side, sign in (("L", 1), ("R", -1)):
            self.detail_part(f"CLO_ShirtCollar.{side}", closed_panel([
                [Vector((sign * .018, -.090, 1.356)), Vector((sign * .065, -.080, 1.362)), Vector((sign * .082, -.061, 1.351))],
                [Vector((sign * .052, -.115, 1.293)), Vector((sign * .093, -.103, 1.320)), Vector((sign * .113, -.080, 1.332))]
            ], Vector((0, .005, 0))), "chest", "uniform_detail", "shirt", smooth=False)
            pocket_x = sign * .113
            self.detail_part(f"CLO_VestPocket.{side}", closed_panel([
                [Vector((pocket_x + x, -.112 + abs(pocket_x+x)*.14, z))
                 for x in (-.034, -.016, .016, .034)]
                for z in (.950, .978, 1.001)
            ], Vector((0, .006, 0))), "chest", "uniform_detail", "waistcoat_dark", smooth=False)
            self.detail_part(f"CLO_VestPocketWelt.{side}", base.make_profiled_segment(
                (pocket_x-.037, -.116 + abs(pocket_x-.037)*.14, 1.003),
                (pocket_x+.037, -.116 + abs(pocket_x+.037)*.14, 1.003),
                ((0, .004, .6), (.08, .005, .6), (.92, .005, .6), (1, .004, .6)), 6),
                "chest", "uniform_detail", "waistcoat")
            self.detail_part(f"CLO_VestEdge.{side}", base.make_profiled_segment(
                (sign * .121, -.081, 1.303), (sign * .009, -.112, 1.105),
                ((0, .003, 1), (.2, .004, 1), (.5, .004, 1), (.8, .004, 1), (1, .003, 1)), 6),
                "chest", "uniform_detail", "waistcoat_dark")
        for index in range(3):
            self.replace_geometry(f"CLO_Button.{index+1}", base.make_ellipsoid(
                (0, -.126, 1.080-index*.093), (.009, .004, .009), 8, 4))
        towel_rows = [[Vector((.748 + (-.09 + .18*col/8) * (.81+.19*row/5),
                              -.035 + .006*math.sin(col*math.pi/2), .940+.160*row/5))
                       for col in range(9)] for row in range(6)]
        self.replace_geometry("ACC_ServiceTowel", closed_panel(towel_rows, Vector((0, .006, 0))))

    def build_service_anchors(self) -> None:
        vessel = base.BONE_BY_NAME["SOCKET_Vessel.L"].head
        bottle = base.BONE_BY_NAME["SOCKET_Bottle.R"].head
        self.create_bone_anchor(
            ANCHOR_NAMES[0],
            "SOCKET_Vessel.L",
            vessel,
            (0.0, 0.0, -1.0),
        )
        self.create_bone_anchor(
            ANCHOR_NAMES[1],
            "SOCKET_Bottle.R",
            bottle,
            (0.0, 0.0, -1.0),
        )


def validate_result(result):
    bpy.context.view_layer.update()
    errors: list[str] = []
    bones = list(result.rig.data.bones)
    if [bone.name for bone in bones] != [
        specification.name for specification in base.SKELETON
    ]:
        errors.append("Bone order/names diverge from NpcHumanV2")

    for specification in base.SKELETON:
        bone = result.rig.data.bones.get(specification.name)
        if bone is None:
            continue
        actual_parent = bone.parent.name if bone.parent is not None else None
        if actual_parent != specification.parent:
            errors.append(
                f"{specification.name} parent is {actual_parent!r}, "
                f"expected {specification.parent!r}"
            )
        if (bone.head_local - base.v(specification.head)).length > .000001 or \
                (bone.tail_local - base.v(specification.tail)).length > .000001:
            errors.append(f"{specification.name} rest pose diverges from NpcHumanV2")

    if bpy.data.actions:
        errors.append("Bartender model must contain no authored Actions")
    if result.pivots:
        errors.append("Ordinary bartender must not contain extra-arm pivots")
    if tuple(result.anchors) != ANCHOR_NAMES:
        errors.append(
            f"Service anchors are {tuple(result.anchors)!r}; "
            f"expected {ANCHOR_NAMES!r}"
        )

    skeleton_names = {bone.name for bone in bones}
    missing_sockets = sorted(set(SOCKET_NAMES).difference(skeleton_names))
    if missing_sockets:
        errors.append(f"Missing service socket bones: {missing_sockets}")

    parts = {part.obj.name: part for part in result.parts}
    required = {
        "GEO_Head",
        "FACE_EyeWhite.L",
        "FACE_EyeWhite.R",
        "FACE_Moustache",
        "GEO_Hand.L",
        "GEO_Hand.R",
        "GEO_ExposedForearm.L",
        "GEO_ExposedForearm.R",
        "CLO_RolledCuff.L",
        "CLO_RolledCuff.R",
        "CLO_WaistcoatFront",
        "CLO_Apron",
        "ACC_ServiceTowel",
    }
    missing = sorted(required.difference(parts))
    if missing:
        errors.append(f"Missing required bartender design parts: {missing}")
    if any(name.startswith("ARM2_") or name.startswith("ARM3_") for name in parts):
        errors.append("Ordinary bartender contains a legacy extra-arm mesh")
    for side in ("L", "R"):
        for finger in range(4):
            part = parts.get(f"GEO_Finger{finger}.{side}")
            if part is None or part.bone != f"hand.{side}":
                errors.append(f"Missing separated finger {finger} on unchanged {side} hand bone")

    mesh_count = len(result.parts)
    triangle_count = 0
    world_vertices: list[Vector] = []
    seen_meshes: set[int] = set()
    for part in sorted(result.parts, key=lambda item: item.obj.name):
        obj = part.obj
        mesh = obj.data
        if mesh.as_pointer() in seen_meshes:
            errors.append(f"{obj.name} reuses another part's mesh")
        seen_meshes.add(mesh.as_pointer())
        if len(mesh.materials) != 1 or mesh.materials[0] != result.material:
            errors.append(f"{obj.name} does not use the one shared material")
        if len(obj.vertex_groups) != 1 or obj.vertex_groups[0].name != part.bone:
            errors.append(
                f"{obj.name} must have one rigid group for {part.bone}"
            )
        for vertex in mesh.vertices:
            world_vertices.append(obj.matrix_world @ vertex.co)
        triangle_count += base.triangulated_count(mesh)
        volume = detail.signed_volume(([vertex.co for vertex in mesh.vertices],
                                       [tuple(polygon.vertices) for polygon in mesh.polygons]))
        if volume <= 0:
            errors.append(f"{obj.name} is not an outward closed volume")
        kind = obj.get("bp_detail_kind")
        uv = mesh.uv_layers.active
        if kind not in surface_atlas.CELLS or uv is None or len(uv.data) != len(mesh.loops):
            errors.append(f"{obj.name} is missing its declared surface UVs")
        else:
            origin = surface_atlas.CELLS[kind]
            if any(not (origin[axis]+1)/256 - .000001 <= loop.uv[axis] <=
                       (origin[axis]+127)/256 + .000001 for loop in uv.data for axis in (0, 1)):
                errors.append(f"{obj.name} UVs escape the inset material cell")

    if not MIN_TRIANGLES <= triangle_count <= MAX_TRIANGLES:
        errors.append(
            f"Triangle budget is {triangle_count}; expected "
            f"{MIN_TRIANGLES}-{MAX_TRIANGLES}"
        )
    if mesh_count < 60 or mesh_count > 110:
        errors.append(f"Mesh count is {mesh_count}; expected 60-110 parts")

    if world_vertices:
        bounds_min = Vector(
            tuple(min(vertex[axis] for vertex in world_vertices) for axis in range(3))
        )
        bounds_max = Vector(
            tuple(max(vertex[axis] for vertex in world_vertices) for axis in range(3))
        )
        if abs(bounds_min.z) > 0.00001:
            errors.append(
                f"Footwear must ground at z=0, got {bounds_min.z:.6f}"
            )
        if abs(bounds_max.z - TOTAL_HEIGHT) > 0.00001:
            errors.append(
                f"Resting silhouette must top out at {TOTAL_HEIGHT} m, "
                f"got {bounds_max.z:.6f}"
            )
    else:
        errors.append("Bartender contains no mesh vertices")
        bounds_min = Vector((0, 0, 0))
        bounds_max = Vector((0, 0, 0))

    if any(
        obj.type in {"LIGHT", "CAMERA"}
        for obj in result.export_collection.objects
    ):
        errors.append("Export collection contains a light or camera")

    if errors:
        formatted = "\n".join(f"  - {error}" for error in errors)
        raise RuntimeError(
            f"Ordinary bar bartender validation failed:\n{formatted}"
        )

    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "seed": SEED,
        "detail_atlas_sha256": hashlib.sha256(surface_atlas.png_bytes()).hexdigest(),
        "anatomy_standard": base.NPC_ANATOMY_STANDARD,
        "skeleton": [
            {
                "name": specification.name,
                "head": list(specification.head),
                "tail": list(specification.tail),
                "parent": specification.parent,
                "connected": specification.connected,
                "deform": specification.deform,
            }
            for specification in base.SKELETON
        ],
        "parts": [
            {
                "name": part.obj.name,
                "bone": part.bone,
                "role": part.role,
                "palette_name": part.palette_name,
                "color": [
                    base.stable_float(component) for component in part.color
                ],
                "vertices": [
                    [
                        base.stable_float(component)
                        for component in (part.obj.matrix_world @ vertex.co)
                    ]
                    for vertex in part.obj.data.vertices
                ],
                "triangles": base.triangulated_count(part.obj.data),
                "faces": [list(polygon.vertices) for polygon in part.obj.data.polygons],
                "atlas_region": part.obj.get("bp_atlas_region", ""),
                "uvs": [[base.stable_float(value) for value in loop.uv]
                        for loop in part.obj.data.uv_layers.active.data],
            }
            for part in sorted(result.parts, key=lambda item: item.obj.name)
        ],
        "anchors": list(result.anchors),
        "animations": {
            "service_asset": SERVICE_ANIMATION_ASSET,
            "service_clips": list(SERVICE_ANIMATION_CLIPS),
            "locomotion_asset": LOCOMOTION_ANIMATION_ASSET,
            "locomotion_clip": LOCOMOTION_ANIMATION_CLIP,
        },
    }
    signature = hashlib.sha256(
        json.dumps(
            signature_payload,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
    ).hexdigest()
    return base.ValidationReport(
        mesh_count,
        triangle_count,
        tuple(base.stable_float(component) for component in bounds_min),
        tuple(base.stable_float(component) for component in bounds_max),
        signature,
    )


def render_preview(path: Path, result) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("CAM_BartenderOrdinaryPreview")
    camera = bpy.data.objects.new("CAM_BartenderOrdinaryPreview", camera_data)
    scene.collection.objects.link(camera)
    camera.location = Vector((1.65, -2.35, 1.35))
    direction = (Vector((0.0, 0.0, 1.02)) - camera.location).normalized()
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    light_data = bpy.data.lights.new(
        "LIGHT_BartenderOrdinaryPreview", type="SUN"
    )
    light_data.energy = 3.4
    light = bpy.data.objects.new(
        "LIGHT_BartenderOrdinaryPreview", light_data
    )
    scene.collection.objects.link(light)
    light.rotation_euler = (0.9, 0.25, 0.6)
    scene.camera = camera
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = "Standard"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 800
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera)
    bpy.data.cameras.remove(camera_data)
    bpy.data.objects.remove(light)
    bpy.data.lights.remove(light_data)


def write_manifest(path: Path, result, report) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-ordinary-bartender-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "height_m": TOTAL_HEIGHT,
        "anatomy_standard": base.NPC_ANATOMY_STANDARD,
        "rest_pelvis_height_m": base.stable_float(base.NPC_PELVIS_HEIGHT),
        "signature_anatomy": [],
        "pose": "apose",
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "triangle_budget": [MIN_TRIANGLES, MAX_TRIANGLES],
        "pool_eligible": False,
        "pivot_names": [],
        "anchor_names": list(result.anchors),
        "socket_names": list(SOCKET_NAMES),
        "material_asset": SHARED_MATERIAL_ASSET,
        "texture_bindings": [surface_atlas.texture_binding(
            surface_atlas.ASSET_PATH, [part.obj for part in result.parts])],
        "emissive": False,
        "colliders": False,
        "lights": False,
        "rigidbodies": False,
        "animation_count": 0,
        "animations": [],
        "shared_animation_asset": SERVICE_ANIMATION_ASSET,
        "shared_clips": list(SERVICE_ANIMATION_CLIPS),
        "locomotion_animation_asset": LOCOMOTION_ANIMATION_ASSET,
        "locomotion_clip": LOCOMOTION_ANIMATION_CLIP,
        "build_signature": report.build_signature,
        "arm_design": "ordinary_two_armed_v2",
        "extra_arm_pairs": 0,
        "bones": [
            {
                "name": specification.name,
                "parent": specification.parent or "",
                "head": list(specification.head),
                "tail": list(specification.tail),
                "deform": specification.deform,
            }
            for specification in base.SKELETON
        ],
        "parts": [
            {
                "name": part.obj.name,
                "role": part.role,
                "bone": part.bone,
                "palette_name": part.palette_name,
                "atlas_region": part.obj.get("bp_atlas_region", ""),
                "base_color": [
                    base.stable_float(component) for component in part.color
                ],
                "vertices": len(part.obj.data.vertices),
                "triangles": base.triangulated_count(part.obj.data),
            }
            for part in sorted(result.parts, key=lambda item: item.obj.name)
        ],
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    temporary.replace(path)


def main() -> None:
    config = parse_args()
    if config.validate_only:
        atlas_path = Path(surface_atlas.ASSET_PATH)
        if not atlas_path.is_file() or atlas_path.read_bytes() != surface_atlas.png_bytes():
            raise RuntimeError("Ordinary character atlas differs from its deterministic source")
    else:
        surface_atlas.publish(surface_atlas.ASSET_PATH)
    result = OrdinaryBartenderBuilder().build()
    report = validate_result(result)
    first_signature = report.build_signature
    result = OrdinaryBartenderBuilder().build()
    report = validate_result(result)
    if first_signature != report.build_signature:
        raise RuntimeError("Ordinary bartender geometry is not deterministic")
    if config.validate_only:
        print(f"ORDINARY BAR BARTENDER VALIDATION OK: {report.triangle_count} triangles; {report.build_signature}")
        return
    surface_atlas.attach_preview(result.material, surface_atlas.ASSET_PATH)
    if not config.no_preview:
        render_preview(config.preview, result)
    base.export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report)
    base.save_blend(config.output)
    print("ORDINARY BAR BARTENDER 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print(f"  Skeleton bones: {len(base.SKELETON)}")
    print(f"  Service anchors: {len(result.anchors)}")
    print(f"  Meshes: {report.mesh_count}")
    print(f"  Triangles: {report.triangle_count}/{MAX_TRIANGLES}")
    print(
        f"  Shared clips: {len(SERVICE_ANIMATION_CLIPS)} service + "
        "1 locomotion"
    )
    print(f"  Signature: {report.build_signature}")
    print(f"  Blend: {config.output}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")


if __name__ == "__main__":
    main()
