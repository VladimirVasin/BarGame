"""Three optional karaoke actions on the unchanged production Hero V2 rig.

Run through tools/run-blender.py. The microphone stays on its stand: this bank
keeps both feet planted, hands free and the mouth still for shared live voice
presentation. --validate-only rebuilds and compares the deterministic manifest.
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
spec = importlib.util.spec_from_file_location("karaoke_hero_v2", ROOT / "tools/build-player-3d-model-v2.py")
hero = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = hero
spec.loader.exec_module(hero)
common = hero.common
FPS = 24
CLIPS = (("KaraokeEnter", .5, False), ("KaraokeLoop", 2., True), ("KaraokeExit", .5, False))
OUT = ROOT / "Assets/Resources/Player/PlayerKaraokeActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/PlayerKaraokeActions.blend"
LOWER = ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R")


def smooth(t):
    t = max(0., min(1., t))
    return t * t * (3. - 2. * t)


class KaraokeBuilder(hero.HeroV2Builder):
    def singing_pose(self, phase=0.):
        B = common.BonePose
        breath = .5 * (1. - math.cos(phase * math.tau))
        return self.merge_pose(self.relaxed_pose(), {
            # Positive X on this production rig leans the torso toward -Y
            # (Unity forward), while the neck compensates to keep the face up.
            "spine": B(rotation_degrees=(4.2 + .25 * breath, 0., .8)),
            "chest": B(rotation_degrees=(4.6 + .65 * breath, 0., -.9)),
            "neck": B(rotation_degrees=(-3.4 - .25 * breath, 0., .5)),
            "head": B(rotation_degrees=(-.8, 0., -.4)),
            "forearm.L": B(rotation_degrees=(-5.5, 3., -2.)),
            "forearm.R": B(rotation_degrees=(-6.5, -3., 2.)),
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
        neutral, singing = self.relaxed_pose(), self.singing_pose()
        endpoints = {"KaraokeEnter": (neutral, singing), "KaraokeExit": (singing, neutral)}
        for name, duration, loop in CLIPS:
            count = round(duration * FPS)
            keys = [(frame / count,
                     self.singing_pose(frame / count) if loop else
                     self.blend(*endpoints[name], smooth(frame / count))) for frame in range(count + 1)]
            self._create_action(name, "karaoke", duration, loop, count, FPS, keys)
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
    neutral = snapshot("KaraokeEnter", 0.)
    pelvis = Vector(rig.pose.bones["pelvis"].head)
    pelvis_ground = [-pelvis.x, pelvis.z + .04, -pelvis.y + .008]
    lower_error = endpoint_error = mouth_error = 0.
    chest_path = []
    for name, duration, _ in CLIPS:
        for frame in range(round(duration * FPS) + 1):
            current = snapshot(name, frame / (duration * FPS))
            lower_error = max(lower_error, difference(current, neutral, LOWER))
            mouth_error = max(mouth_error, rig.pose.bones["face.mouth"].matrix_basis.translation.length,
                              rig.pose.bones["face.mouth"].rotation_quaternion.angle)
            if name == "KaraokeLoop": chest_path.append(rig.pose.bones["chest"].matrix.to_quaternion().copy())
        for curve in common.iter_action_fcurves(builder.result.actions[name].action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Karaoke contains object motion")
    for a, t, b, u in (("KaraokeEnter", 1., "KaraokeLoop", 0.),
                       ("KaraokeLoop", 1., "KaraokeLoop", 0.),
                       ("KaraokeLoop", 0., "KaraokeExit", 0.),
                       ("KaraokeExit", 1., "KaraokeEnter", 0.)):
        endpoint_error = max(endpoint_error, difference(snapshot(a, t), snapshot(b, u), neutral))
    rig.animation_data.action = None
    builder._reset_pose(); builder._apply_pose(builder.relaxed_pose())
    bpy.context.view_layer.update()
    ordinary = {b.name: b.matrix.copy() for b in rig.pose.bones}
    neutral_error = difference(neutral, ordinary, neutral)
    chest_degrees = max(math.degrees(a.rotation_difference(b).angle) for a in chest_path for b in chest_path)
    if max(lower_error, endpoint_error, neutral_error, mouth_error) > .00001:
        raise ValueError(f"Karaoke seams/support/mouth moved: {lower_error}, {endpoint_error}, {neutral_error}, {mouth_error}")
    if not .5 < chest_degrees < 2.:
        raise ValueError("Karaoke breathing must be visible but restrained")
    builder._reset_pose()
    return dict(maximum_lower_body_error=lower_error, maximum_endpoint_error=endpoint_error,
                maximum_neutral_error=neutral_error, maximum_mouth_motion=mouth_error,
                breathing_chest_travel_degrees=chest_degrees), pelvis_ground


def make_payload(builder):
    report, pelvis = validate(builder)
    signature = hashlib.sha256()
    for name, _, _ in CLIPS:
        for curve in common.iter_action_fcurves(builder.result.actions[name].action):
            signature.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in p.co] for p in curve.keyframe_points]], separators=(",", ":")).encode())
    return dict(generator="player_karaoke_v1", rig="HeroV2", bone_count=len(builder.result.rig.data.bones),
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
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20261026, "apose")
    builder = KaraokeBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    payload = make_payload(builder)
    if "--validate-only" in sys.argv:
        if json.loads(MANIFEST.read_text(encoding="utf8")) != payload:
            raise ValueError("Karaoke action bank differs from its deterministic manifest")
    else:
        common.export_animation_fbx(OUT, builder.result)
        common.save_blend(BLEND)
        MANIFEST.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
        for path in (OUT, MANIFEST):
            meta = path.with_name(path.name + ".meta")
            if not meta.exists():
                meta.write_text("fileFormatVersion: 2\nguid: " + hashlib.sha256(path.relative_to(ROOT).as_posix().encode()).hexdigest()[:32] + "\n", encoding="utf8")
    if before != {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}:
        raise ValueError("Karaoke generation changed a production hero asset")
    repeated = KaraokeBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    repeated.build()
    if make_payload(repeated) != payload: raise ValueError("Karaoke rebuild is not deterministic")
    print("PLAYER KARAOKE ART CONTRACT OK " + json.dumps(payload), flush=True)


if __name__ == "__main__": main()
