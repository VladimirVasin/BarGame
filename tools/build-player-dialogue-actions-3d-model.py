"""Six optional dialogue actions on the unchanged production Hero V2 rig.

Run through tools/run-blender.py. Only the dialogue FBX/manifest and its
editable source are exported. --validate-only rebuilds and compares the bank.
The neutral/listen/talk seams, planted lower body and still mouth are measured
at every authored frame; this bank adds body performance, not lip sync.
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
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("dialogue_hero_v2", ROOT / "tools/build-player-3d-model-v2.py")
hero = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = hero
spec.loader.exec_module(hero)
common = hero.common
FPS = 24
CLIPS = (("DialogueEnter", .75, False), ("DialogueListenLoop", 2., True),
         ("DialogueTalkEnter", .5, False), ("DialogueTalkLoop", 1.5, True),
         ("DialogueTalkExit", .5, False), ("DialogueExit", .75, False))
OUT = ROOT / "Assets/Resources/Player/PlayerDialogueActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/PlayerDialogueActions.blend"
LOWER = ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R")


def smooth(t):
    t = max(0., min(1., t))
    return t * t * (3. - 2. * t)


class DialogueBuilder(hero.HeroV2Builder):
    def listen_pose(self, phase=0.):
        B = common.BonePose
        breath = .5 * (1. - math.cos(phase * math.tau))
        nod = math.sin(phase * math.pi) ** 2 * math.sin(phase * math.tau)
        return self.merge_pose(self.relaxed_pose(), {
            "spine": B(rotation_degrees=(-1.2 + .30 * breath, 0., .9)),
            "chest": B(rotation_degrees=(1.9 + .20 * breath, 0., -1.1)),
            "neck": B(rotation_degrees=(-1.1, 0., .5)),
            "head": B(rotation_degrees=(3.4 + .65 * nod, 0., -.4)),
            "forearm.R": B(rotation_degrees=(-8., -3., 2.)),
        })

    def talk_pose(self, phase=0.):
        B = common.BonePose
        envelope = math.sin(phase * math.pi) ** 2
        beat = math.sin(phase * math.tau * 2.) * envelope
        return self.merge_pose(self.listen_pose(), {
            "spine": B(rotation_degrees=(-.2 + .45 * envelope, .5 * beat, .6)),
            "chest": B(rotation_degrees=(2.5 + .65 * envelope, -.4 * beat, -.7)),
            "neck": B(rotation_degrees=(-.8, 0., .4)),
            "head": B(rotation_degrees=(3.6 + 1.5 * beat, .8 * envelope, -.3)),
            # The anatomical right hand speaks below the sternum. The arm
            # stays beside the jacket and never covers either participant's face.
            "upper_arm.R": B(target_direction=(-.105 - .012 * envelope, -.09 - .022 * envelope, -.31)),
            "forearm.R": B(rotation_degrees=(-39. - 5.5 * envelope - 2.5 * beat, -7., 6.)),
            "hand.R": B(rotation_degrees=(5. + 3. * beat, 12., -7.)),
        })

    def blend(self, start, end, weight):
        if weight <= 0.: return start
        if weight >= 1.: return end
        self._reset_pose(); self._apply_pose(start)
        initial = {b.name: (b.location.copy(), b.rotation_quaternion.copy(), b.scale.copy())
                   for b in self.result.rig.pose.bones}
        self._reset_pose(); self._apply_pose(end)
        return {b.name: common.BonePose(
            rotation_degrees=tuple(math.degrees(v) for v in initial[b.name][1].slerp(b.rotation_quaternion, weight).to_euler("XYZ")),
            location_m=tuple(initial[b.name][0].lerp(b.location, weight) / self.scale),
            scale=tuple(initial[b.name][2].lerp(b.scale, weight))) for b in self.result.rig.pose.bones}

    def build_actions(self):
        neutral, listen, talk = self.relaxed_pose(), self.listen_pose(), self.talk_pose()
        endpoints = {"DialogueEnter": (neutral, listen), "DialogueExit": (listen, neutral),
                     "DialogueTalkEnter": (listen, talk), "DialogueTalkExit": (talk, listen)}
        for name, duration, loop in CLIPS:
            keys = []
            count = round(duration * FPS)
            for frame in range(count + 1):
                t = frame / count
                pose = (self.listen_pose(t) if name == "DialogueListenLoop" else self.talk_pose(t)) if loop else \
                    self.blend(*endpoints[name], smooth(t))
                keys.append((t, pose))
            self._create_action(name, "dialogue", duration, loop, count, FPS, keys)
            print("Authored " + name, flush=True)


def validate(builder):
    rig = builder.result.rig
    def snapshot(name, progress):
        action = builder.result.actions[name].action
        rig.animation_data.action = action
        frame = action.frame_end * progress
        bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
        bpy.context.view_layer.update()
        return {b.name: b.matrix.copy() for b in rig.pose.bones}
    def difference(a, b, names):
        return max(abs(a[n][i][j] - b[n][i][j]) for n in names for i in range(4) for j in range(4))
    neutral = snapshot("DialogueEnter", 0.)
    pelvis = Vector(rig.pose.bones["pelvis"].head)
    # Same source-to-ground mapping as the existing optional Hero V2 banks;
    # the Unity validator also measures it on the actual imported skeleton.
    pelvis_ground = [-pelvis.x, pelvis.z + .04, -pelvis.y + .008]
    lower_error = endpoint_error = mouth_error = neutral_error = 0.
    head_path, hand_path = [], []
    for name, duration, _ in CLIPS:
        for frame in range(round(duration * FPS) + 1):
            current = snapshot(name, frame / (duration * FPS))
            lower_error = max(lower_error, difference(current, neutral, LOWER))
            mouth_error = max(mouth_error, rig.pose.bones["face.mouth"].matrix_basis.translation.length,
                              rig.pose.bones["face.mouth"].rotation_quaternion.angle)
            if name == "DialogueTalkLoop":
                head_path.append(rig.pose.bones["head"].matrix.to_quaternion().copy())
                hand_path.append(rig.pose.bones["SOCKET_Grip.R"].head.copy())
        for curve in common.iter_action_fcurves(builder.result.actions[name].action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Dialogue has object/root motion")
    seams = (("DialogueEnter", 1., "DialogueListenLoop", 0.),
             ("DialogueListenLoop", 1., "DialogueListenLoop", 0.),
             ("DialogueListenLoop", 0., "DialogueTalkEnter", 0.),
             ("DialogueTalkEnter", 1., "DialogueTalkLoop", 0.),
             ("DialogueTalkLoop", 1., "DialogueTalkLoop", 0.),
             ("DialogueTalkLoop", 0., "DialogueTalkExit", 0.),
             ("DialogueTalkExit", 1., "DialogueListenLoop", 0.),
             ("DialogueListenLoop", 0., "DialogueExit", 0.),
             ("DialogueExit", 1., "DialogueEnter", 0.))
    for a, t, b, u in seams:
        endpoint_error = max(endpoint_error, difference(snapshot(a, t), snapshot(b, u), neutral))
    rig.animation_data.action = None
    builder._reset_pose(); builder._apply_pose(builder.relaxed_pose())
    ordinary = {b.name: b.matrix.copy() for b in rig.pose.bones}
    neutral_error = difference(neutral, ordinary, neutral)
    talk_hand_travel = max((a - b).length for a in hand_path for b in hand_path)
    talk_head_degrees = max(math.degrees(a.rotation_difference(b).angle) for a in head_path for b in head_path)
    if max(lower_error, endpoint_error, neutral_error, mouth_error) > .00001:
        raise ValueError(f"Dialogue seams/lower body/mouth moved: {lower_error}, {endpoint_error}, {neutral_error}, {mouth_error}")
    if talk_hand_travel < .025 or talk_head_degrees < 1.:
        raise ValueError("Dialogue speaking loop has no readable body performance")
    builder._reset_pose()
    return dict(maximum_lower_body_error=lower_error, maximum_endpoint_error=endpoint_error,
                maximum_neutral_error=neutral_error, maximum_mouth_motion=mouth_error,
                talk_hand_travel_m=talk_hand_travel, talk_head_travel_degrees=talk_head_degrees), pelvis_ground


def make_payload(builder):
    report, pelvis = validate(builder)
    signature = hashlib.sha256()
    for name, _, _ in CLIPS:
        for curve in common.iter_action_fcurves(builder.result.actions[name].action):
            signature.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in p.co] for p in curve.keyframe_points]], separators=(",", ":")).encode())
    return dict(generator="player_dialogue_v1", rig="HeroV2", bone_count=len(builder.result.rig.data.bones),
        fps=FPS, root_motion=False, animation_events=0, lip_sync=False,
        entry_ground_offset=[0., 0., 0.], exit_ground_offset=[0., 0., 0.],
        entry_facing=[0., 0., 1.], exit_facing=[0., 0., 1.],
        entry_pelvis_from_ground=pelvis, action_pelvis_from_ground=pelvis, exit_pelvis_from_ground=pelvis,
        clips=[dict(name=n, duration_seconds=d, loop=loop) for n, d, loop in CLIPS],
        signature=signature.hexdigest(), **report)


def main():
    protected = [ROOT / "Assets/Resources/Player/Player3DV2.prefab"]
    protected += list((ROOT / "Assets/Player3D/V2").rglob("*.fbx"))
    before = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20260911, "apose")
    builder = DialogueBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    payload = make_payload(builder)
    if "--validate-only" in sys.argv:
        if json.loads(MANIFEST.read_text(encoding="utf8")) != payload:
            raise ValueError("Dialogue action bank differs from its deterministic manifest")
    else:
        common.export_animation_fbx(OUT, builder.result)
        bpy.context.preferences.filepaths.save_version = 0
        common.save_blend(BLEND)
        MANIFEST.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
        for path in (OUT, MANIFEST):
            meta = path.with_name(path.name + ".meta")
            if not meta.exists():
                meta.write_text("fileFormatVersion: 2\nguid: " + hashlib.sha256(path.relative_to(ROOT).as_posix().encode()).hexdigest()[:32] + "\n", encoding="utf8")
    if before != {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}:
        raise ValueError("Dialogue generation changed a production hero asset")
    # Rebuild the bank in the same process to prove its curve signature and
    # measured endpoints do not depend on previous Blender scene contents.
    repeated = DialogueBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    repeated.build()
    if make_payload(repeated) != payload: raise ValueError("Dialogue rebuild is not deterministic")
    print("PLAYER DIALOGUE ART CONTRACT OK " + json.dumps(payload), flush=True)


if __name__ == "__main__": main()
