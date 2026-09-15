"""Six optional, bone-only Hero V2 actions for the City's physical fair props.

Run with tools/run-blender.py; --validate-only reconstructs the exact manifest.
The existing production hero and action banks are never rewritten.
"""
from __future__ import annotations
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
import bpy

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("fair_action_source", ROOT / "tools/build-village-outdoor-player-actions-3d-model.py")
source = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = source
spec.loader.exec_module(source)
hero, common = source.hero, source.common
FPS = 24
TRANSFER = 1.25
CLIPS = tuple((kind + phase, 4. if kind == "Organ" and phase == "Loop" else
               2. if phase == "Loop" else TRANSFER, phase == "Loop")
              for kind in ("Organ", "Bell") for phase in ("Enter", "Loop", "Exit"))
OUT = ROOT / "Assets/Resources/Player/CityFairPlayerActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/CityFairPlayerActions.blend"


def sample(action, seconds):
    organ = action.startswith("Organ")
    loop = action.endswith("Loop")
    weight = 1. if loop else source.smooth((TRANSFER - seconds if action.endswith("Exit") else seconds) / TRANSFER)
    progress = max(0., min(1., seconds / (4. if organ else 2.))) if loop else 0.
    # Each turn eases into and out of the loop endpoint. Both mechanisms finish
    # at their original contact, so Exit begins on the exact same held pose.
    angle = 720. * source.smooth(progress) if organ else 0.
    pull = .24 * math.sin(math.pi * progress) ** 2 if not organ else 0.
    right = source.vec((.22 + .18 * math.sin(math.radians(angle)),
                        1.10 + .18 * math.cos(math.radians(angle)), .50)) if organ else source.vec((.22, 1.13 - pull, .52))
    return dict(right=right, right_weight=weight, left=source.vec((0, 0, 0)), left_weight=0.,
                _lean=(18., -2., 4., 0.) if organ else (25., -2., 5., 0.),
                crank_degrees=angle, rope_drop=pull)


class FairBuilder(source.OutdoorBuilder):
    def build_actions(self):
        self.samples = {}
        neutral = self.relaxed_pose()
        for action, duration, loop in CLIPS:
            keys, frames = [], []
            for frame in range(round(duration * FPS) + 1):
                value = sample(action, frame / FPS)
                held = self.solve_contacts(value)
                weight = value["right_weight"]
                pose = neutral if weight == 0. else held if weight == 1. else self.blended(neutral, held, weight)
                keys.append((frame / (duration * FPS), pose))
                frames.append({"right": [round(v, 7) for v in value["right"]],
                               "contact_weight": round(weight, 7),
                               "crank_degrees": round(value["crank_degrees"], 7),
                               "rope_drop": round(value["rope_drop"], 7)})
            self._create_action("Fair" + action, "city_fair", duration, loop, round(duration * FPS), FPS, keys)
            self.samples[action] = frames
            print("Authored Fair" + action, flush=True)


def validate(builder):
    rig = builder.result.rig
    grip_error = lower_error = endpoint_error = 0.
    def snapshot(action, progress):
        clip = builder.result.actions["Fair" + action].action
        rig.animation_data.action = clip
        frame = clip.frame_end * progress
        bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
        bpy.context.view_layer.update()
        return {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    neutral = snapshot("OrganEnter", 0.)
    for action, duration, loop in CLIPS:
        for index, value in enumerate(builder.samples[action]):
            current = snapshot(action, index / (duration * FPS))
            if value["contact_weight"] == 1.:
                grip_error = max(grip_error, (rig.pose.bones["SOCKET_Grip.R"].head - source.source(value["right"])).length)
            for bone in source.LOWER:
                lower_error = max(lower_error, max(abs(current[bone][i][j] - neutral[bone][i][j]) for i in range(4) for j in range(4)))
        for curve in common.iter_action_fcurves(builder.result.actions["Fair" + action].action):
            if not curve.data_path.startswith('pose.bones['):
                raise ValueError("Fair actions must contain only bone motion")
    for kind in ("Organ", "Bell"):
        for action, t, other, u in ((kind + "Enter", 0, "OrganEnter", 0),
                                   (kind + "Enter", 1, kind + "Loop", 0),
                                   (kind + "Loop", 1, kind + "Exit", 0),
                                   (kind + "Exit", 1, "OrganEnter", 0)):
            a, b = snapshot(action, t), snapshot(other, u)
            endpoint_error = max(endpoint_error, max(abs(a[bone][i][j] - b[bone][i][j]) for bone in a for i in range(4) for j in range(4)))
    if grip_error > .0002 or lower_error > .00001 or endpoint_error > .00001:
        raise ValueError(f"Fair contact/lower body/endpoint failure: {grip_error}, {lower_error}, {endpoint_error}")
    rig.animation_data.action = None
    builder._reset_pose()
    return dict(maximum_grip_error=grip_error, maximum_lower_body_error=lower_error, maximum_endpoint_error=endpoint_error)


def main():
    protected = [ROOT / "Assets/Resources/Player/Player3DV2.prefab",
                 ROOT / "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx",
                 ROOT / "Assets/Player3D/V2/Animations/PlayerCharacter3DV2Animations.fbx"]
    before = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20260915, "apose")
    builder = FairBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    evidence = validate(builder)
    signature = hashlib.sha256()
    for name, record in sorted(builder.result.actions.items()):
        for curve in common.iter_action_fcurves(record.action):
            signature.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 6) for v in point.co] for point in curve.keyframe_points]], separators=(",", ":")).encode())
    pelvis = [round(v, 7) for v in source.ground(builder.result.rig.pose.bones["pelvis"].head)]
    payload = dict(generator="city_fair_player_v1", rig="HeroV2", bone_count=len(builder.result.rig.data.bones),
                   fps=FPS, root_motion=False, animation_events=0, entry_ground_offset=[0, 0, 0], exit_ground_offset=[0, 0, 0],
                   entry_facing=[0, 0, 1], exit_facing=[0, 0, 1], entry_pelvis_from_ground=pelvis,
                   action_pelvis_from_ground=pelvis, exit_pelvis_from_ground=pelvis,
                   organ_dock=[.54, 0, 1.08], bell_dock=[.47, 0, .76], signature=signature.hexdigest(),
                   tracks=[dict(name=action, clip="Fair" + action, duration_seconds=duration, loop=loop,
                                frames=builder.samples[action]) for action, duration, loop in CLIPS], **evidence)
    if "--validate-only" in sys.argv:
        if json.loads(MANIFEST.read_text(encoding="utf-8")) != payload:
            raise ValueError("Fair action manifest differs from deterministic reconstruction")
    else:
        common.export_animation_fbx(OUT, builder.result)
        common.save_blend(BLEND)
        MANIFEST.write_text(json.dumps(payload, separators=(",", ":")) + "\n", encoding="utf-8")
    after = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    if before != after:
        raise ValueError("An existing production hero asset changed")
    print(json.dumps(dict(signature=signature.hexdigest(), **evidence)), flush=True)


if __name__ == "__main__":
    main()
