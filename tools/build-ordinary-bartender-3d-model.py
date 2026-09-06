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
from pathlib import Path
import sys

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender-only entry.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "3.1.0"
DESIGN_ID = "bar_bartender_v2"
DISPLAY_NAME = "Bar Bartender"
SEED = 460918
TOTAL_HEIGHT = 1.75
MIN_TRIANGLES = 900
MAX_TRIANGLES = 2600
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

    if not MIN_TRIANGLES <= triangle_count <= MAX_TRIANGLES:
        errors.append(
            f"Triangle budget is {triangle_count}; expected "
            f"{MIN_TRIANGLES}-{MAX_TRIANGLES}"
        )
    if mesh_count < 28 or mesh_count > 58:
        errors.append(f"Mesh count is {mesh_count}; expected 28-58 parts")

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
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    # Every runtime part shares one material and receives its authored colour
    # through a MaterialPropertyBlock.  Workbench OBJECT colour mirrors that
    # contract in the source preview instead of flattening the whole uniform
    # to the shared material's neutral swatch.
    scene.display.shading.color_type = "OBJECT"
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
    result = OrdinaryBartenderBuilder().build()
    report = validate_result(result)
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
