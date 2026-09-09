"""Author only three additional Hero V2 bone-only workroom actions.

The production builder supplies the unchanged rig and neutral posture in this
temporary Blender scene. No existing hero model, prefab or action bank is saved.
Run with tools/run-blender.py; --preview also renders the measured hold posture.
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
spec = importlib.util.spec_from_file_location("village_help_source", TOOLS / "build-church-garden-pot-actions.py")
source = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = source
spec.loader.exec_module(source)
hero, common = source.hero, source.common
source.POT_GRIP_RADIUS = .22
source.POT_GRIP_Y = 0.
FPS = 24
TRANSFER = 1.5
HOLD = 6.
OUT = ROOT / "Assets/Resources/Player/VillageWorkroomPlayerActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/VillageWorkroomPlayerActions.blend"
PREVIEW = BLEND.with_suffix(".png")
CENTRE = (0., -.42 + .008, 1. - .04)


class HelpBuilder(source.PotBuilder):
    def blended(self, start, end, weight):
        self._reset_pose()
        self._apply_pose(start)
        initial = {bone.name: bone.rotation_quaternion.copy() for bone in self.result.rig.pose.bones}
        self._reset_pose()
        self._apply_pose(end)
        return {bone.name: common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in
            initial[bone.name].slerp(bone.rotation_quaternion, weight).to_euler("XYZ")))
            for bone in self.result.rig.pose.bones}

    def build_actions(self):
        relaxed = self.relaxed_pose()
        held = self.solve_pose(CENTRE, 0., (4., 0., 1., 0.))
        self.samples = {}
        for exit_action in (False, True):
            name = "VillageChairHelpExit" if exit_action else "VillageChairHelpEnter"
            keys, samples = [], []
            for frame in range(round(TRANSFER * FPS) + 1):
                t = frame / (TRANSFER * FPS)
                weight = source.smooth(1. - t if exit_action else t)
                pose = self.blended(relaxed, held, weight)
                if weight == 0.: pose = relaxed
                if weight == 1.: pose = held
                keys.append((t, pose))
                samples.append({"time": t, "contact": weight == 1.})
            self._create_action(name, "village_workroom", TRANSFER, False, 36, FPS, keys)
            self.samples[name] = samples
        keys, samples = [], []
        for frame in range(round(HOLD * FPS) + 1):
            t = frame / (HOLD * FPS)
            breathe = .3 * (1. - math.cos(2. * math.pi * t))
            keys.append((t, self.solve_pose(CENTRE, 0., (4. + breathe, 0., 1., 0.))))
            samples.append({"time": t, "contact": True})
        self._create_action("VillageChairHelpLoop", "village_workroom", HOLD, True, 144, FPS, keys)
        self.samples["VillageChairHelpLoop"] = samples


def validate(builder):
    rig = builder.result.rig
    maximum_error = 0.
    def snapshot(name, progress):
        record = builder.result.actions[name]
        rig.animation_data.action = record.action
        frame = record.action.frame_end * progress
        bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
        bpy.context.view_layer.update()
        return {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    neutral = snapshot("VillageChairHelpEnter", 0.)
    held = snapshot("VillageChairHelpLoop", 0.)
    for name, samples in builder.samples.items():
        for sample in samples:
            current = snapshot(name, sample["time"])
            if sample["contact"]:
                for side, sign in (("L", 1.), ("R", -1.)):
                    expected = Vector(CENTRE) + Vector((.22 * sign, 0., 0.))
                    error = (rig.pose.bones[f"SOCKET_Grip.{side}"].head - expected).length
                    maximum_error = max(maximum_error, error)
                    if error > .0002: raise ValueError(f"{name} {side} grip error {error}")
            for bone in ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R"):
                if bone in current and max(abs(current[bone][r][c] - neutral[bone][r][c])
                    for r in range(4) for c in range(4)) > .00001:
                    raise ValueError(f"Unplanted lower body: {name} {bone}")
        for curve in common.iter_action_fcurves(builder.result.actions[name].action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Root/object motion is forbidden")
    for name, t, expected in (("VillageChairHelpEnter", 1., held), ("VillageChairHelpExit", 0., held),
                             ("VillageChairHelpExit", 1., neutral), ("VillageChairHelpLoop", 1., held)):
        pose = snapshot(name, t)
        if any(max(abs(pose[bone][r][c] - expected[bone][r][c]) for r in range(4) for c in range(4)) > .00001
               for bone in pose): raise ValueError(f"Endpoint mismatch: {name}@{t}")
    rig.animation_data.action = None
    builder._reset_pose()
    return maximum_error


def main():
    protected = [ROOT / "Assets/Resources/Player/Player3DV2.prefab",
                 ROOT / "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx",
                 ROOT / "Assets/Player3D/V2/Animations/PlayerCharacter3DV2Animations.fbx"]
    before = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in protected if path.exists()}
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20260908, "apose")
    builder = HelpBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    error = validate(builder)
    clips, signature = [], hashlib.sha256()
    for name, record in sorted(builder.result.actions.items()):
        clips.append({"name": name, "duration": record.duration_seconds, "loop": record.loop,
                      "source_frames": record.source_frame_count, "source_fps": record.source_fps})
        for curve in common.iter_action_fcurves(record.action):
            signature.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 6) for v in point.co] for point in curve.keyframe_points]], separators=(",", ":")).encode())
    payload = {"generator": "village_workroom_player_v1", "rig": "HeroV2", "bone_count": len(builder.result.rig.data.bones),
        "root_motion": False, "animation_events": 0, "fps": FPS, "entry_ground_offset": [0., 0., 0.],
        "exit_ground_offset": [0., 0., 0.], "entry_facing": [0., 0., 1.], "exit_facing": [0., 0., 1.],
        "left_grip_from_ground": [-.22, 1., .42], "right_grip_from_ground": [.22, 1., .42],
        "maximum_grip_error": error, "clips": clips, "signature": signature.hexdigest()}
    common.export_animation_fbx(OUT, builder.result)
    common.save_blend(BLEND)
    MANIFEST.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    if "--preview" in sys.argv:
        record = builder.result.actions["VillageChairHelpLoop"]
        builder.result.rig.animation_data.action = record.action
        bpy.context.scene.frame_set(round(record.action.frame_end * .5))
        bpy.context.scene.render.resolution_x = 850
        bpy.context.scene.render.resolution_y = 900
        bpy.context.scene.render.resolution_percentage = 100
        bpy.context.scene.render.filepath = str(PREVIEW)
        bpy.ops.render.render(write_still=True)
    after = {path: hashlib.sha256(Path(path).read_bytes()).hexdigest() for path in before}
    if before != after: raise ValueError("An existing hero asset changed")
    print(json.dumps(payload))


if __name__ == "__main__": main()
