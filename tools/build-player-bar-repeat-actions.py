"""An isolated, bone-only two-tap action bank on the unchanged production hero."""
from __future__ import annotations

import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("bar_repeat_hero", ROOT / "tools/build-player-3d-model-v2.py")
hero = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = hero
spec.loader.exec_module(hero)
common = hero.common
seat = hero.player_counter_seat_actions
OUT = ROOT / "Assets/Resources/Player/PlayerBarRepeatActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/PlayerBarRepeatActions.blend"
CLIPS = (("BarRepeatEnter", 1.8, False), ("BarRepeatTapLoop", 1., True), ("BarRepeatExit", .75, False))
LOWER = ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R")
TAP_KEYS = ((0., .024), (.20, .024), (.28, 0.), (.34, 0.), (.47, .024),
            (.64, .024), (.72, 0.), (.78, 0.), (1., .024))


def smooth(value):
    value = max(0., min(1., value))
    return value * value * (3. - 2. * value)


def hand_rotation(result, side, palm, distal):
    """Orient the original anatomical hand, independently of imported bone axes."""
    parts = {obj.name: obj for obj in bpy.data.objects if obj.type == "MESH"}
    def centre(name):
        points, _ = hero.player_hand_grip._source_points(result, parts[name + "." + side])
        return sum(points, Vector()) / len(points)
    fingers = (sum((centre(f"GEO_Finger{i}") for i in range(4)), Vector()) / 4 - centre("GEO_Hand")).normalized()
    toward_thumb = centre("GEO_Thumb") - centre("GEO_Finger0")
    toward_thumb = (toward_thumb - fingers * toward_thumb.dot(fingers)).normalized()
    normal = fingers.cross(toward_thumb).normalized() * (1 if side == "L" else -1)
    neutral = result.rig.data.bones["hand." + side].matrix_local.to_quaternion()
    def frame(forward, up):
        forward = forward.normalized()
        up = (up - forward * up.dot(forward)).normalized()
        return Matrix((up.cross(forward), up, forward)).transposed().to_quaternion()
    return frame(distal, palm) @ frame(neutral.inverted() @ fingers, neutral.inverted() @ normal).inverted()


def pose_dict(builder):
    return {bone.name: common.BonePose(
        location_m=tuple(bone.location / builder.scale),
        rotation_degrees=tuple(math.degrees(value) for value in bone.rotation_quaternion.to_euler("XYZ")),
        scale=tuple(bone.scale)) for bone in builder.result.rig.pose.bones}


def build():
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20261022, "apose")
    # The body donor may predate the separately published bar takes. Recover
    # their exact endpoint through the same BusRideLoop + bar-leg authoring,
    # without refreshing or saving that donor or its shared action bank.
    bpy.ops.wm.open_mainfile(filepath=str(hero.DEFAULT_OUTPUT))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    source_manifest = json.loads(hero.DEFAULT_MANIFEST.read_text(encoding="utf8"))
    row = next(row for row in source_manifest["actions"] if row["name"] == "BusRideLoop")
    result = common.BuildResult(rig.parent, rig, {}, {}, parts=[], actions={"BusRideLoop": common.ActionRecord(
        bpy.data.actions["BusRideLoop"], row["category"], row["duration_seconds"], row["loop"],
        row["source_frame_count"], row["source_fps"])})
    builder = hero.HeroV2Builder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.result = result
    seat.sample(result, "BusRideLoop", 0.)
    rig.animation_data.action = None
    seat.lower_bar_thighs(result)
    base = seat.snapshot(result)
    baseline = {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    wrist = rig.pose.bones["hand.R"].head.copy()
    elbow = rig.pose.bones["forearm.R"].head.copy()
    rotation = rig.pose.bones["hand.R"].matrix.to_quaternion()
    raised = wrist + Vector((0., -.16, .15))
    left_wrist = rig.pose.bones["hand.L"].head.copy()
    left_elbow = rig.pose.bones["forearm.L"].head.copy()
    left_rotation = rig.pose.bones["hand.L"].matrix.to_quaternion()
    left_raised = rig.pose.bones["upper_arm.L"].head + Vector((-.06, -.22, .10))
    left_wave_rotation = hand_rotation(result, "L", Vector((0., -1., 0.)), Vector((0., 0., 1.)))
    rig.animation_data.action = None
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    result.actions = {}
    # Only the unchanged root/skeleton and new curves belong to this derivative.
    for obj in list(bpy.data.objects):
        if obj not in (result.root, rig):
            bpy.data.objects.remove(obj, do_unlink=True)
    result.parts = []
    for name, duration, loop in CLIPS:
        keys = []
        count = round(duration * 24)
        for frame in range(count + 1):
            progress = frame / count
            seat.restore_mix(result, base, base, 0.)
            if name == "BarRepeatEnter":
                weight = smooth(progress)
            elif name == "BarRepeatExit":
                weight = 1. - smooth(progress)
            else:
                weight = 1.
            if weight > 0.:
                # The wrist stays still while authored hand morphs articulate
                # the index alone. No whole-hand dipping remains in this bank.
                target = wrist.lerp(raised, weight)
                seat.solve_chain(rig.pose.bones["upper_arm.R"], rig.pose.bones["forearm.R"],
                    rig.pose.bones["hand.R"], target, rotation, elbow)
            if name == "BarRepeatEnter":
                if progress < .20:
                    amount = smooth(progress / .20)
                    target = left_wrist.lerp(left_raised, amount)
                    turn = left_rotation.slerp(left_wave_rotation, amount)
                elif progress < .60:
                    time = (progress - .20) / .40
                    wave = math.sin(time * math.pi * 2) * math.sin(time * math.pi)
                    target = left_raised + Vector((-.055 * wave, 0., 0.))
                    turn = Quaternion(Vector((0., -1., 0.)), math.radians(wave * 12)) @ left_wave_rotation
                else:
                    amount = smooth((progress - .60) / .30)
                    target = left_raised.lerp(left_wrist, amount)
                    turn = left_wave_rotation.slerp(left_rotation, amount)
                seat.solve_chain(rig.pose.bones["upper_arm.L"], rig.pose.bones["forearm.L"],
                    rig.pose.bones["hand.L"], target, turn, left_elbow)
            keys.append((progress, pose_dict(builder)))
        builder._create_action(name, "bar_repeat", duration, loop, count, 24., keys)
    report = validate(builder, baseline)
    curves = [[name, curve.data_path, curve.array_index,
        [[round(value, 8) for value in point.co] for point in curve.keyframe_points]]
        for name, _, _ in CLIPS for curve in common.iter_action_fcurves(result.actions[name].action)]
    payload = dict(generator="player_bar_repeat_v2", rig="HeroV2", bone_count=len(rig.data.bones), fps=24,
        root_motion=False, animation_events=0, anatomical_hand="R", index_renderer="GEO_Finger0.R", tap_count=2,
        attention_hand="L", wave_before_taps=True, level_palm=True, index_only_tap=True,
        index_tap_keys=[(time, 1 - height / .024) for time, height in TAP_KEYS],
        source_path=hero.DEFAULT_OUTPUT.relative_to(ROOT).as_posix(),
        source_sha256=hashlib.sha256(hero.DEFAULT_OUTPUT.read_bytes()).hexdigest(),
        parent_pose_authoring_sha256=hashlib.sha256(Path(seat.__file__).read_bytes()).hexdigest(),
        entry_parent_clip="CounterSeatLoop", entry_parent_progress=0., exit_parent_progress=0.,
        entry_pelvis=list(baseline["pelvis"].translation), action_pelvis=list(baseline["pelvis"].translation),
        exit_pelvis=list(baseline["pelvis"].translation),
        clips=[dict(name=name, duration_seconds=duration, loop=loop) for name, duration, loop in CLIPS],
        signature=hashlib.sha256(json.dumps(curves, separators=(",", ":")).encode()).hexdigest(), **report)
    return builder, payload


def validate(builder, baseline):
    result, rig = builder.result, builder.result.rig
    def sample(name, progress):
        seat.sample(result, name, progress)
        return {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    def error(first, last, names):
        return max(abs(first[name][row][column] - last[name][row][column])
                   for name in names for row in range(4) for column in range(4))
    lower_error = max(error(baseline, sample(name, frame / 24), LOWER)
        for name, _, _ in CLIPS for frame in range(25))
    seams = (("BarRepeatEnter", 1., "BarRepeatTapLoop", 0.),
             ("BarRepeatTapLoop", 1., "BarRepeatExit", 0.))
    endpoint_error = max(error(sample(a, first), sample(b, last), baseline) for a, first, b, last in seams)
    endpoint_error = max(endpoint_error, error(baseline, sample("BarRepeatEnter", 0.), baseline),
        error(baseline, sample("BarRepeatExit", 1.), baseline),
        error(sample("BarRepeatTapLoop", 0.), sample("BarRepeatTapLoop", 1.), baseline))
    hover = sample("BarRepeatTapLoop", 0.)["hand.R"].translation
    wrist_error = max((hover - sample("BarRepeatTapLoop", frame / 24)["hand.R"].translation).length for frame in range(25))
    hand_lift = (sample("BarRepeatEnter", 1.)["hand.R"].translation - baseline["hand.R"].translation).z
    left_lift = max((sample("BarRepeatEnter", frame / 24)["hand.L"].translation - baseline["hand.L"].translation).z for frame in range(25))
    wave_points = [sample("BarRepeatEnter", progress)["hand.L"].translation for progress in (.3, .4, .5)]
    wave_width = max(point.x for point in wave_points) - min(point.x for point in wave_points)
    if max(lower_error, endpoint_error, wrist_error) > 1e-5 or hand_lift < .10 or left_lift < .10 or wave_width < .02:
        raise RuntimeError(f"Bar repeat lost its seated seams, steady wrist or left wave: {lower_error}, {endpoint_error}, {wrist_error}, {hand_lift}, {left_lift}, {wave_width}")
    return dict(maximum_lower_body_error=lower_error, maximum_endpoint_error=endpoint_error,
        hand_lift_m=hand_lift, maximum_tapping_wrist_motion_m=wrist_error,
        left_wave_lift_m=left_lift, left_wave_width_m=wave_width)


def main():
    protected = [hero.DEFAULT_OUTPUT, hero.DEFAULT_MANIFEST, ROOT / "Assets/Resources/Player/Player3DV2.prefab"]
    protected += list((ROOT / "Assets/Player3D/V2").rglob("*.fbx"))
    before = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in protected}
    builder, payload = build()
    if "--validate-only" in sys.argv:
        if json.loads(MANIFEST.read_text(encoding="utf8")) != payload:
            raise RuntimeError("Bar repeat action bank differs from deterministic source")
    else:
        common.export_animation_fbx(OUT, builder.result)
        bpy.context.preferences.filepaths.save_version = 0
        common.save_blend(BLEND)
        MANIFEST.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
        for path in (OUT, MANIFEST):
            meta = path.with_name(path.name + ".meta")
            if not meta.exists():
                guid = hashlib.sha256(path.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]
                meta.write_text("fileFormatVersion: 2\nguid: " + guid + "\n", encoding="utf8")
    _, repeated = build()
    if repeated != payload:
        raise RuntimeError("Bar repeat curves are not deterministic")
    if before != {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in protected}:
        raise RuntimeError("Bar repeat generation changed the canonical hero")
    print("PLAYER BAR REPEAT CONTRACT OK " + json.dumps(payload), flush=True)


if __name__ == "__main__":
    main()
