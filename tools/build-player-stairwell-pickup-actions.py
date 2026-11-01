"""Three tactile pickup performances on the unchanged production Hero V2 rig.

Run through tools/run-blender.py. The optional bone-only bank never rewrites
the hero prefab/model. Grip targets are floor-relative Unity metres, measured
against the same sockets which the runtime uses to carry each source model.
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
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("pickup_outdoor_source", ROOT / "tools/build-village-outdoor-player-actions-3d-model.py")
source = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = source
spec.loader.exec_module(source)
hero, common = source.hero, source.common
FPS = 24
ENTER, LOOP, EXIT = 1.25, 2., .75
CONTACT = .65
ITEMS = ("Flashlight", "Crowbar", "Pistol")
REACH = {"Flashlight": (.16, 1.25, .72), "Crowbar": (.16, 1.155, .72), "Pistol": (.16, 1.02, .72)}
HELD = {"Flashlight": (.12, 1.28, .24), "Crowbar": (.21, 1.20, .27), "Pistol": (.16, 1.10, .32)}
OUT = ROOT / "Assets/Resources/Player/PlayerStairwellPickupActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/PlayerStairwellPickupActions.blend"
LOWER = ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R")


def smooth(value):
    value = min(1., max(0., value))
    return value * value * (3. - 2. * value)


def frame(item, phase, t):
    right, left = Vector(HELD[item]), Vector((-.12, 1.13, .27))
    lean = {"Flashlight": 5., "Crowbar": 9., "Pistol": 11.}[item]
    right_weight, left_weight = 1., 0.
    if phase == "Enter":
        if t <= CONTACT:
            right = Vector(REACH[item])
            right_weight = smooth(t / CONTACT)
            lean = {"Flashlight": 35., "Crowbar": 40., "Pistol": 43.}[item]
        else:
            travel = smooth((t - CONTACT) / (1. - CONTACT))
            right = Vector(REACH[item]).lerp(right, travel)
            lean = {"Flashlight": 35., "Crowbar": 40., "Pistol": 43.}[item] * (1 - travel) + lean * travel
    elif phase == "Loop":
        envelope = math.sin(t * math.pi) ** 2
        beat = math.sin(t * math.tau) * envelope
        if item == "Flashlight":
            # The thumb-side wrist turns toward the switch and then presses
            # the lamp against the real chest position, with the left hand
            # briefly supporting the jacket at the mounting point.
            right += Vector((-.025 * envelope, -.018 * envelope, -.025 * envelope))
            left = Vector((-.055, 1.23, .22))
            left_weight = .85 * envelope
        elif item == "Crowbar":
            # A small loaded pull, release and belated second-hand catch.
            right += Vector((.035 * beat, -.035 * envelope, -.055 * envelope))
            left = right + Vector((-.13, -.10, .08))
            left_weight = smooth(t * 5 - .4) * (1. - smooth((t - .70) / .3))
            lean += -2.5 * beat
        else:
            # An inexperienced wrist sags before the other hand arrives.
            right += Vector((.012 * beat, -.055 * envelope, .025 * envelope))
            left = right + Vector((-.055, -.065, -.015))
            left_weight = smooth(t * 4 - .6) * (1. - smooth((t - .75) / .25))
            lean += 1.2 * envelope
    else:
        if item == "Flashlight":
            right = right.lerp(Vector((.08, 1.24, .17)), smooth(t / .45))
        else:
            right = right.lerp(Vector((.17, .95, .16)), smooth(t / .55))
        right_weight = 1. - smooth((t - .45) / .55)
    return dict(right=right, left=left, right_weight=right_weight,
                left_weight=left_weight, _lean=(lean, 0., 2., 0.))


class PickupBuilder(source.OutdoorBuilder):
    def build_actions(self):
        neutral = self.relaxed_pose()
        for item in ITEMS:
            for phase, duration, loop in (("Enter", ENTER, False), ("Loop", LOOP, True), ("Exit", EXIT, False)):
                keys = []
                for index in range(round(duration * FPS) + 1):
                    t = index / (duration * FPS)
                    sample = frame(item, phase, t)
                    weight = sample["right_weight"]
                    posed = self.solve_contacts(sample)
                    # The asymmetric left support enters independently, while
                    # full-body entry/exit still meet ordinary relaxed stance.
                    if sample["left_weight"] < 1.:
                        no_left = dict(sample, left_weight=0.)
                        posed = self.blended(self.solve_contacts(no_left), posed, sample["left_weight"])
                    posed = neutral if weight == 0. else posed if weight == 1. else self.blended(neutral, posed, weight)
                    keys.append((t, posed))
                name = "Stairwell" + item + phase
                self._create_action(name, "stairwell_pickup", duration, loop, round(duration * FPS), FPS, keys)
                print("Authored " + name, flush=True)


def payload(builder):
    rig = builder.result.rig
    def snapshot(name, t):
        action = builder.result.actions[name].action
        rig.animation_data.action = action
        value = action.frame_end * t
        bpy.context.scene.frame_set(int(value), subframe=value % 1.)
        bpy.context.view_layer.update()
        return {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    def delta(a, b, names):
        return max(abs(a[n][r][c] - b[n][r][c]) for n in names for r in range(4) for c in range(4))
    neutral = snapshot("StairwellFlashlightEnter", 0.)
    pelvis = source.ground(rig.pose.bones["pelvis"].head)
    lower_error = seam_error = grip_error = 0.
    clips, signature = [], hashlib.sha256()
    for item in ITEMS:
        prefix = "Stairwell" + item
        for phase, duration, loop in (("Enter", ENTER, False), ("Loop", LOOP, True), ("Exit", EXIT, False)):
            name = prefix + phase
            clips.append(dict(name=name, duration_seconds=duration, loop=loop))
            for index in range(round(duration * FPS) + 1):
                current = snapshot(name, index / (duration * FPS))
                lower_error = max(lower_error, delta(neutral, current, LOWER))
                if index == round(duration * FPS) and phase == "Enter":
                    grip_error = max(grip_error, (source.ground(rig.pose.bones["SOCKET_Grip.R"].head) - Vector(HELD[item])).length)
            for curve in common.iter_action_fcurves(builder.result.actions[name].action):
                if not curve.data_path.startswith('pose.bones['): raise ValueError("Pickup contains object/root motion")
                signature.update(json.dumps([name, curve.data_path, curve.array_index,
                    [[round(v, 7) for v in p.co] for p in curve.keyframe_points]], separators=(",", ":")).encode())
        for a, ta, b, tb in ((prefix + "Enter", 0., "StairwellFlashlightEnter", 0.),
                             (prefix + "Enter", 1., prefix + "Loop", 0.),
                             (prefix + "Loop", 0., prefix + "Loop", 1.),
                             (prefix + "Loop", 1., prefix + "Exit", 0.),
                             (prefix + "Exit", 1., "StairwellFlashlightEnter", 0.)):
            seam_error = max(seam_error, delta(snapshot(a, ta), snapshot(b, tb), neutral))
    rig.animation_data.action = None
    builder._reset_pose(); builder._apply_pose(builder.relaxed_pose())
    ordinary = {b.name: b.matrix.copy() for b in rig.pose.bones}
    neutral_error = delta(neutral, ordinary, neutral)
    if max(lower_error, seam_error, neutral_error) > .00001 or grip_error > .0002:
        raise ValueError(f"Pickup contract: lower={lower_error}, seam={seam_error}, neutral={neutral_error}, grip={grip_error}")
    builder._reset_pose()
    return dict(generator="player_stairwell_pickup_v1", rig="HeroV2", bone_count=len(rig.data.bones), fps=FPS,
        root_motion=False, animation_events=0, entry_ground_offset=[0, 0, 0], exit_ground_offset=[0, 0, 0],
        entry_facing=[0, 0, 1], exit_facing=[0, 0, 1], entry_pelvis_from_ground=list(pelvis),
        action_pelvis_from_ground=list(pelvis), exit_pelvis_from_ground=list(pelvis),
        take_progress=CONTACT, items=[dict(name=item, reach_from_ground=REACH[item], held_from_ground=HELD[item]) for item in ITEMS],
        clips=clips, maximum_lower_body_error=lower_error, maximum_endpoint_error=seam_error,
        maximum_neutral_error=neutral_error, maximum_grip_error=grip_error, signature=signature.hexdigest())


def main():
    protected = [ROOT / "Assets/Resources/Player/Player3DV2.prefab"]
    protected += [p for p in (ROOT / "Assets/Resources/Player").glob("*.fbx") if p != OUT]
    protected += list((ROOT / "Assets/Player3D/V2").rglob("*.fbx"))
    before = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20261101, "apose")
    builder = PickupBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    result = payload(builder)
    if "--validate-only" in sys.argv:
        if json.loads(MANIFEST.read_text()) != json.loads(json.dumps(result)):
            raise ValueError("Pickup bank differs from its deterministic manifest")
    else:
        common.export_animation_fbx(OUT, builder.result)
        bpy.context.preferences.filepaths.save_version = 0
        common.save_blend(BLEND)
        MANIFEST.write_text(json.dumps(result, indent=2) + "\n")
        for path in (OUT, MANIFEST):
            meta = path.with_name(path.name + ".meta")
            if not meta.exists(): meta.write_text("fileFormatVersion: 2\nguid: " + hashlib.sha256(path.relative_to(ROOT).as_posix().encode()).hexdigest()[:32] + "\n")
    if before != {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}:
        raise ValueError("Pickup generation changed a production hero asset")
    print("STAIRWELL PICKUP ART CONTRACT OK " + json.dumps(result), flush=True)


if __name__ == "__main__": main()
