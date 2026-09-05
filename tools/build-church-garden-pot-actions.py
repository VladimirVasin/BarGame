"""Small Hero V2 bone-only church-garden action bank, without rebuilding its bank.

Run in Blender 5: --background --python tools/build-church-garden-pot-actions.py
The existing production builder supplies the exact anatomy, rig and relaxed
endpoint. Only these five actions are authored/exported. Hands are solved to the
two measured rim contacts at every 24 Hz sample; no runtime IK is required.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
sys.path.insert(0, str(TOOLS))
spec = importlib.util.spec_from_file_location("garden_hero_v2", TOOLS / "build-player-3d-model-v2.py")
hero = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = hero
spec.loader.exec_module(hero)
common = hero.common
OUT = ROOT / "Assets/Resources/Player/ChurchGardenPotActions.fbx"
MANIFEST = ROOT / "Assets/Resources/Player/ChurchGardenPotActions.json"
BLEND = ROOT / "ArtSource/Player/Blender/ChurchGardenPotActions.blend"
FPS = 24
DOCK_X = .34
DOCK_Y = .65
DOCK_Z = .56
POT_GRIP_Y = .255
POT_GRIP_RADIUS = .165
TRANSFER_SECONDS = 3.0
LOOP_SECONDS = 5.0
CONTACT_PROGRESS = .5
SOURCE_ROOT_HEIGHT = .04


def smooth(t):
    t = max(0., min(1., t))
    return t * t * (3. - 2. * t)


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


class PotBuilder(hero.HeroV2Builder):
    def solve_pose(self, source_center, yaw, lean, contact=True):
        B = common.BonePose
        pose = self.merge_pose(self.relaxed_pose(), {
            "spine": B(rotation_degrees=(lean[0], lean[1], 0.)),
            "chest": B(rotation_degrees=(lean[2], lean[3], 0.)),
            "neck": B(rotation_degrees=(-7., 0., 0.)),
            "head": B(rotation_degrees=(12., 0., 0.)),
        })
        self._reset_pose()
        self._apply_pose(pose)
        rig = self.result.rig
        centre = Vector(source_center)
        radius_axis = Vector((math.cos(yaw), math.sin(yaw), 0.))
        for side, sign in (("L", 1), ("R", -1)):
            grip_target = centre + radius_axis * (POT_GRIP_RADIUS * sign)
            grip_target.z += POT_GRIP_Y
            # Fingers follow the sides down from the rolled rim.
            hand_direction = Vector((-sign * .06, -.05, -1.)).normalized()
            grip_bone = rig.data.bones[f"SOCKET_Grip.{side}"]
            hand_bone = rig.data.bones[f"hand.{side}"]
            grip_distance = (grip_bone.head_local - hand_bone.head_local).length
            wrist_target = grip_target - hand_direction * grip_distance
            shoulder = rig.pose.bones[f"upper_arm.{side}"].head.copy()
            upper_length = rig.data.bones[f"upper_arm.{side}"].length
            fore_length = rig.data.bones[f"forearm.{side}"].length
            delta = wrist_target - shoulder
            distance = delta.length
            if distance >= upper_length + fore_length - .003:
                raise ValueError(f"{side} unreachable contact {source_center}: {distance:.4f} > {upper_length + fore_length:.4f}; lean {lean}")
            axis = delta.normalized()
            elbow_hint = Vector((sign * .45, .55, -.3))
            side_axis = (elbow_hint - axis * elbow_hint.dot(axis)).normalized()
            along = (upper_length**2 - fore_length**2 + distance**2) / (2. * distance)
            lateral = math.sqrt(max(0., upper_length**2 - along**2))
            elbow = shoulder + axis * along + side_axis * lateral
            pose[f"upper_arm.{side}"] = B(armature_direction=tuple(elbow - shoulder))
            pose[f"forearm.{side}"] = B(armature_direction=tuple(wrist_target - elbow))
            pose[f"hand.{side}"] = B(armature_direction=tuple(hand_direction))
        return pose

    def build_actions(self):
        relaxed = self.relaxed_pose()
        hold_center = (0., -.37 + .008, .98 - SOURCE_ROOT_HEIGHT)
        hold_lean = (4., 0., 1., 0.)
        hold = self.solve_pose(hold_center, 0., hold_lean)
        self.samples = {}
        for label, world_side in (("Left", -1), ("Right", 1)):
            source_side = -world_side
            # Shared pelvis alignment removes the source pelvis's +.008 Y.
            dock_center = (source_side * DOCK_X, -DOCK_Z + .008, DOCK_Y - SOURCE_ROOT_HEIGHT)
            dock_lean = (40., source_side * 27., 15., source_side * 4.)
            touch = self.solve_pose(dock_center, 0., dock_lean)
            for exiting in (False, True):
                name = f"ChurchPotPlace{label}" if exiting else f"ChurchPotPickup{label}"
                keys, samples = [], []
                for frame in range(round(TRANSFER_SECONDS * FPS) + 1):
                    t = frame / (TRANSFER_SECONDS * FPS)
                    pickup_t = 1. - t if exiting else t
                    if pickup_t <= CONTACT_PROGRESS:
                        blend = smooth(pickup_t / CONTACT_PROGRESS)
                        # Interpolate actual local bone quaternions from the
                        # ordinary endpoint; touch starts only at exact contact.
                        self._reset_pose()
                        self._apply_pose(relaxed)
                        start = {b.name: b.rotation_quaternion.copy() for b in self.result.rig.pose.bones}
                        self._reset_pose()
                        self._apply_pose(touch)
                        pose = {}
                        for bone in self.result.rig.pose.bones:
                            q = start[bone.name].slerp(bone.rotation_quaternion, blend)
                            pose[bone.name] = common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in q.to_euler("XYZ")))
                        centre, lean = dock_center, dock_lean
                    else:
                        blend = smooth((pickup_t - CONTACT_PROGRESS) / (1. - CONTACT_PROGRESS))
                        centre = mix(dock_center, hold_center, blend)
                        lean = mix(dock_lean, hold_lean, blend)
                        pose = self.solve_pose(centre, 0., lean)
                    if frame == 0:
                        pose = hold if exiting else relaxed
                    if frame == round(TRANSFER_SECONDS * FPS):
                        pose = relaxed if exiting else hold
                    keys.append((t, pose))
                    samples.append({"time": t, "pot_source_base": centre, "held": pickup_t >= CONTACT_PROGRESS})
                self._create_action(name, "church_garden", TRANSFER_SECONDS, False, 36, 12, keys)
                self.samples[name] = samples
        keys, samples = [], []
        for frame in range(round(LOOP_SECONDS * FPS) + 1):
            t = frame / (LOOP_SECONDS * FPS)
            yaw = math.radians(18.) * math.sin(2. * math.pi * t)
            centre = (0., hold_center[1], hold_center[2] + .009 * (1. - math.cos(2. * math.pi * t)))
            keys.append((t, self.solve_pose(centre, yaw, hold_lean)))
            samples.append({"time": t, "pot_source_base": centre, "held": True})
        self._create_action("ChurchPotInspectLoop", "church_garden", LOOP_SECONDS, True, 40, 8, keys)
        self.samples["ChurchPotInspectLoop"] = samples


def validate(builder):
    result = builder.result
    maximum_grip_error = 0.
    for name, samples in builder.samples.items():
        record = result.actions[name]
        result.rig.animation_data.action = record.action
        for sample in samples:
            frame = sample["time"] * record.action.frame_end
            bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
            bpy.context.view_layer.update()
            if sample["held"]:
                left = result.rig.pose.bones["SOCKET_Grip.L"].head.copy()
                right = result.rig.pose.bones["SOCKET_Grip.R"].head.copy()
                expected = Vector(sample["pot_source_base"]) + Vector((0., 0., POT_GRIP_Y))
                error = max(((left + right) * .5 - expected).length, abs((left - right).length - POT_GRIP_RADIUS * 2))
                maximum_grip_error = max(maximum_grip_error, error)
                if error > .0002:
                    raise ValueError(f"{name}@{frame}: two-hand contact error {error}")
        for curve in common.iter_action_fcurves(record.action):
            if not curve.data_path.startswith('pose.bones['):
                raise ValueError("Non-bone animation curve")
    # Strict endpoint equality includes all face, foot and spine bones.
    def snapshot(name, t):
        record = result.actions[name]
        result.rig.animation_data.action = record.action
        bpy.context.scene.frame_set(round(record.action.frame_end * t))
        bpy.context.view_layer.update()
        return {b.name: b.matrix.copy() for b in result.rig.pose.bones}
    relaxed = snapshot("ChurchPotPickupLeft", 0)
    held = snapshot("ChurchPotInspectLoop", 0)
    for side in ("Left", "Right"):
        for name, t, expected in ((f"ChurchPotPickup{side}", 0, relaxed), (f"ChurchPotPickup{side}", 1, held), (f"ChurchPotPlace{side}", 0, held), (f"ChurchPotPlace{side}", 1, relaxed)):
            sample = snapshot(name, t)
            for bone in sample:
                if max(abs(sample[bone][r][c] - expected[bone][r][c]) for r in range(4) for c in range(4)) > 1e-5:
                    raise ValueError(f"Endpoint mismatch {name} {bone}")
    result.rig.animation_data.action = None
    builder._reset_pose()
    return maximum_grip_error


def main():
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20260905, "apose")
    builder = PotBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    error = validate(builder)
    actions = []
    signature = hashlib.sha256()
    for name, record in sorted(builder.result.actions.items()):
        actions.append({"name": name, "duration": record.duration_seconds, "loop": record.loop, "source_frames": record.source_frame_count, "source_fps": record.source_fps})
        for curve in common.iter_action_fcurves(record.action):
            signature.update(json.dumps([name, curve.data_path, curve.array_index, [[round(v, 6) for v in point.co] for point in curve.keyframe_points]], separators=(",", ":")).encode())
    payload = {"generator": "church_garden_pot_v1", "rig": "HeroV2", "bone_count": len(builder.result.rig.data.bones), "root_motion": False, "animation_events": 0, "fps": FPS, "dock_offsets": [[-DOCK_X, DOCK_Y, DOCK_Z], [DOCK_X, DOCK_Y, DOCK_Z]], "entry_ground_offset": [0., 0., 0.], "exit_ground_offset": [0., 0., 0.], "entry_facing": [0., 0., 1.], "exit_facing": [0., 0., 1.], "pot_grip_height": POT_GRIP_Y, "pot_grip_radius": POT_GRIP_RADIUS, "transfer_contact_progress": CONTACT_PROGRESS, "maximum_grip_error": error, "clips": actions, "signature": signature.hexdigest()}
    common.export_animation_fbx(OUT, builder.result)
    common.save_blend(BLEND)
    MANIFEST.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(payload, ensure_ascii=False))
    if "--preview" in sys.argv:
        render_previews(builder)


def render_previews(builder):
    """Review the measured rig with the actual separately authored props."""
    library = ROOT / "ArtSource/ChurchGarden/Blender/ChurchGarden3D.blend"
    with bpy.data.libraries.load(str(library), link=False) as (available, selected):
        selected.objects = [name for name in available.objects if name in ("GEO_PotMedium", "GEO_StonePottingLedge")]
    imported = {}
    for obj in selected.objects:
        if obj is None:
            continue
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = None
        obj.hide_render = False
        imported[obj.name] = obj
    shelf = imported["GEO_StonePottingLedge"]
    shelf.location = (0., -.62 + .008, -SOURCE_ROOT_HEIGHT)
    pot = imported["GEO_PotMedium"]
    scene = bpy.context.scene
    scene.render.resolution_x = 850
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    for name, clip, t in (("ContactLeft", "ChurchPotPickupLeft", .5), ("Inspect", "ChurchPotInspectLoop", .25), ("ContactRight", "ChurchPotPlaceRight", .5)):
        record = builder.result.actions[clip]
        builder.result.rig.animation_data.action = record.action
        scene.frame_set(round(t * record.action.frame_end))
        bpy.context.view_layer.update()
        left = builder.result.rig.pose.bones["SOCKET_Grip.L"].head.copy()
        right = builder.result.rig.pose.bones["SOCKET_Grip.R"].head.copy()
        pot.location = (left + right) * .5 - Vector((0., 0., POT_GRIP_Y))
        pot.rotation_euler.z = math.atan2((left - right).y, (left - right).x)
        scene.render.filepath = str(BLEND.parent / f"ChurchGardenPot{name}.png")
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
