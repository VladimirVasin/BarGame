"""Author eight full alternating aim walks on the retained production Hero V2 rig.

Unlike melee opening/closing shuffles, one full cycle advances both feet by a
stride. Facing, pelvis yaw and the upper-body local pose never follow travel.
The motor owns translation; linear virtual root travel cancels each sole's
stance. The dedicated bone-only bank does not refresh any melee/firearm bank.
"""
from __future__ import annotations

import argparse
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
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("firearm_walk_combat", ROOT / "tools/build-combat-test-3d-model.py")
combat = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = combat
spec.loader.exec_module(combat)
common = combat.common
OUT = ROOT / "Assets/Resources/CombatFirearmWalk"
SOURCE = ROOT / "ArtSource/CombatFirearmWalk"
SOURCE_RIG = ROOT / "ArtSource/Combat/CombatActions.blend"
FPS, DURATION, CYCLE_DISTANCE, FOOT_LIFT = 100, 1., 1.6, .050
SIDE_STAGGER = .150
PELVIS_LOAD, PELVIS_REACH_MARGIN = .004, .001
PELVIS_RANGE_LIMIT, PELVIS_SPEED_LIMIT = .008, .035
MINIMUM_FOOT_LIFT = .045
ENDPOINT_BLEND_FRACTION = .15
ENDPOINT_VELOCITY_LIMIT, ENDPOINT_ACCELERATION_LIMIT = .003, .15
CLIPS = (("FirearmWalkForward", (0., -1., 0.)),
         ("FirearmWalkForwardRight", (-1., -1., 0.)),
         ("FirearmWalkRight", (-1., 0., 0.)),
         ("FirearmWalkBackwardRight", (-1., 1., 0.)),
         ("FirearmWalkBackward", (0., 1., 0.)),
         ("FirearmWalkBackwardLeft", (1., 1., 0.)),
         ("FirearmWalkLeft", (1., 0., 0.)),
         ("FirearmWalkForwardLeft", (1., -1., 0.)))
LOWER = {"pelvis", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R"}


def endpoint_quintic(t, value, velocity, acceleration):
    """Zero V/A at contact; match the unchanged middle's position, V and A."""
    x = t/ENDPOINT_BLEND_FRACTION
    v = ENDPOINT_BLEND_FRACTION*velocity
    a = ENDPOINT_BLEND_FRACTION**2*acceleration
    c3, c4, c5 = 10.*value-4.*v+.5*a, -15.*value+7.*v-a, 6.*value-3.*v+.5*a
    return x*x*x*(c3+x*(c4+x*c5))


def smooth(t):
    # Retain the original stride's middle and reachable extrema. A full
    # minimum-jerk replacement moves the ankle farther behind a travelling
    # root; short quintic splices soften only liftoff and touchdown instead.
    edge = min(t, 1.-t)
    if edge >= ENDPOINT_BLEND_FRACTION:
        return t*t*(3.-2.*t)
    w = ENDPOINT_BLEND_FRACTION
    blend = endpoint_quintic(edge, w*w*(3.-2.*w), 6.*w*(1.-w), 6.-12.*w)
    return blend if t <= .5 else 1.-blend


def lift_profile(t):
    edge = min(t, 1.-t)
    if edge >= ENDPOINT_BLEND_FRACTION:
        return math.sin(t*math.pi)**2
    w = ENDPOINT_BLEND_FRACTION
    return endpoint_quintic(edge, math.sin(w*math.pi)**2,
        math.pi*math.sin(w*math.tau), 2.*math.pi**2*math.cos(w*math.tau))


def trajectory(builder, side, direction, t):
    """L supports [0,.5], R supports [.5,1]; swing endpoints have zero world speed."""
    stance = t <= .5 if side == "L" else t >= .5
    swing_phase = 2.*t-1. if side == "L" else 2.*t
    swing_phase = min(1., max(0., swing_phase))
    if stance:
        progress = .25-t if side == "L" else .75-t
        lift = 0.
    else:
        progress = smooth(swing_phase)-t+(.25 if side == "L" else -.25)
        lift = lift_profile(swing_phase)
    centre_y = (builder.support("L").y+builder.support("R").y)*.5
    ankle = builder.support(side)
    # Aiming is a narrow walking base, not the crowbar's broad defensive base.
    ankle.x = builder.result.rig.data.bones["foot."+side].head_local.x+(1. if side == "L" else -1.)*.035
    # L passes ahead, R behind when crossing laterally. The anatomical knees
    # remain forward rather than rotating the complete lower body to travel.
    lateral = abs(direction.x)
    side_stagger = max(0., abs(direction.x)-abs(direction.y))
    ankle.y = centre_y+(-1. if side == "L" else 1.)*SIDE_STAGGER*side_stagger
    ankle += direction*(CYCLE_DISTANCE*progress)
    ankle.z += FOOT_LIFT*lift
    ankle.x += (1. if side == "L" else -1.)*.055*lift
    ankle.y += (-1. if side == "L" else 1.)*.025*lateral*lift
    rotation = Quaternion(Vector((1., 0., 0.)), math.radians(-8.*lift)) @ builder.footwork_support_rotations[side]
    return ankle, rotation, stance


def fixed_pelvis_drop(builder, direction):
    """Choose one reachable height for the complete stride before authoring.

    Both original-length legs retain their 18 mm bent-knee reserve. Only the
    small authored load moves vertically; reach must not pump the torso up and
    down as the supporting leg changes. The first pass includes the sideways
    weight shift and samples more densely than the published pose validator.
    """
    rig = builder.result.rig
    rig.animation_data.action = None
    builder._reset_pose(); builder._apply_pose(builder.neutral)
    bpy.context.view_layer.update()
    hips = {side: rig.pose.bones["thigh."+side].head.copy() for side in ("L", "R")}
    drop = 0.
    for sample in range(401):
        t = sample/400
        sway = Vector((.020*math.sin(t*math.tau), 0., 0.))
        load = PELVIS_LOAD*math.sin(t*math.tau)**2
        for side in ("L", "R"):
            ankle, _, _ = trajectory(builder, side, direction, t)
            delta = hips[side]+sway-ankle
            thigh, shin = (rig.pose.bones[n+"."+side] for n in ("thigh", "shin"))
            reach = thigh.bone.length+shin.bone.length-.018
            required = delta.z-math.sqrt(max(.0001, reach*reach-delta.x*delta.x-delta.y*delta.y))
            drop = max(drop, required-load)
    return drop+PELVIS_REACH_MARGIN


class FirearmWalkBuilder(combat.CombatBuilder):
    def author(self, clips=CLIPS):
        rig = self.result.rig
        count = round(DURATION*FPS)
        for name, axis in clips:
            if name in self.result.actions:
                previous = self.result.actions.pop(name)
                bpy.data.actions.remove(previous.action)
            direction = Vector(axis).normalized()
            fixed_drop = fixed_pelvis_drop(self, direction)
            keys = []
            for frame in range(count+1):
                t = frame/count
                rig.animation_data.action = None
                self._reset_pose(); self._apply_pose(self.neutral)
                pelvis = rig.pose.bones["pelvis"]
                matrix = pelvis.matrix.copy()
                # A full-cycle safe height prevents repeated reach-driven
                # crouching. Keep only a 4 mm load and the same lateral sway.
                matrix.translation += Vector((.020*math.sin(t*math.tau), 0., -fixed_drop-PELVIS_LOAD*math.sin(t*math.tau)**2))
                pelvis.matrix = matrix
                bpy.context.view_layer.update()
                targets = {side: trajectory(self, side, direction, t) for side in ("L", "R")}
                for side, (ankle, rotation, _) in targets.items():
                    pole = ((1. if side == "L" else -1.)*.18*abs(direction.x), -1., 0.)
                    self.recovery_leg(side, ankle, pole, rotation)
                keys.append((t, self.snapshot_pose()))
            self._create_action(name, "firearm_aim_walk", DURATION, True, count, FPS, keys)
            print("Authored " + name, flush=True)


def load_builder():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_RIG))
    rig = bpy.data.objects["RIG_Player"]
    config = common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20261009, "apose")
    builder = FirearmWalkBuilder(config, combat.hero.DEFAULT_FACE_ATLAS, combat.hero.DEFAULT_CLOTHING_ATLAS)
    builder.hero_profile = True
    builder.result = common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={}, parts=[])
    builder.points = builder.create_pose_points()
    rig.animation_data.action = bpy.data.actions["CombatReady"]
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    builder.footwork_support_targets = {side: rig.pose.bones["foot."+side].head.copy() for side in ("L", "R")}
    builder.footwork_support_rotations = {side: rig.pose.bones["foot."+side].matrix.to_quaternion() for side in ("L", "R")}
    builder.neutral = builder.snapshot_pose()
    builder.neutral_pelvis = rig.pose.bones["pelvis"].head.copy()
    builder.neutral_matrices = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    builder.hinge_references = common.calibrate_hinge_references(rig)
    rig.animation_data.action = None
    builder.deformation = []
    for obj in bpy.context.scene.objects:
        for modifier in obj.modifiers:
            if modifier.type == "ARMATURE":
                builder.deformation.append((modifier, modifier.show_viewport))
                modifier.show_viewport = False
    # The export's bake-all-actions option must see only this isolated bank.
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    bpy.context.scene.render.fps = FPS
    common.ANIMATION_FPS = FPS
    return builder


def measure(builder, clips=CLIPS):
    rig = builder.result.rig
    records = []
    checksum = hashlib.sha256()
    for name, axis in clips:
        direction = Vector(axis).normalized()
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['):
                raise ValueError("Firearm aim walk has object/root motion")
            checksum.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(value, 7) for value in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode())
        rig.animation_data.action = action
        support_error = upper_error = seam_error = facing_error = pelvis_lower = 0.
        foot_separation = knee_separation = leg_separation = 100.
        knee_max = hip_swing = hip_abduction = ankle_max = 0.
        pelvis_heights = []
        ankle_at = None
        lifts = {"L": 0., "R": 0.}
        first = None
        samples = round(DURATION*FPS)*2
        for sample in range(samples+1):
            t = sample/samples
            frame = DURATION*FPS*t
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame%1.)
            bpy.context.view_layer.update()
            if sample == 0: first = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
            for bone in rig.pose.bones:
                if bone.name not in LOWER:
                    upper_error = max(upper_error, max(abs(bone.matrix_basis[i][j]-builder.neutral_matrices[bone.name][i][j]) for i in range(4) for j in range(4)))
                if sample == samples:
                    seam_error = max(seam_error, max(abs(bone.matrix_basis[i][j]-first[bone.name][i][j]) for i in range(4) for j in range(4)))
            pelvis = rig.pose.bones["pelvis"]
            neutral_q = builder.neutral_matrices["pelvis"].to_quaternion()
            facing_error = max(facing_error, math.degrees(neutral_q.rotation_difference(pelvis.rotation_quaternion).angle))
            pelvis_lower = max(pelvis_lower, builder.neutral_pelvis.z-pelvis.head.z)
            pelvis_heights.append(pelvis.head.z)
            for side in ("L", "R"):
                foot = rig.pose.bones["foot."+side]
                target, _, stance = trajectory(builder, side, direction, t)
                lifts[side] = max(lifts[side], foot.head.z-builder.support(side).z)
                if stance: support_error = max(support_error, (foot.head-target).length)
                thigh, shin = (rig.pose.bones[n+"."+side] for n in ("thigh", "shin"))
                knee_max = max(knee_max, math.degrees((shin.head-thigh.head).angle(foot.head-shin.head)))
                anatomy = builder.recovery_leg_anatomy(side)
                hip_swing = max(hip_swing, anatomy["hip_swing_degrees"])
                hip_abduction = max(hip_abduction, abs(anatomy["hip_abduction_degrees"]))
                if anatomy["ankle_turn_degrees"] > ankle_max:
                    ankle_max = anatomy["ankle_turn_degrees"]
                    ankle_at = (side, t, anatomy)
            left = [rig.pose.bones[n+".L"].head.copy() for n in ("thigh", "shin", "foot")]
            right = [rig.pose.bones[n+".R"].head.copy() for n in ("thigh", "shin", "foot")]
            foot_separation = min(foot_separation, (left[2]-right[2]).length)
            knee_separation = min(knee_separation, (left[1]-right[1]).length)
            for a,b in zip(left,left[1:]):
                for c,d in zip(right,right[1:]):
                    leg_separation = min(leg_separation, builder.segment_distance(a,b,c,d))
        if support_error > .001 or upper_error > .00001 or seam_error > .00001 or facing_error > .001:
            raise ValueError(f"{name} support/upper/loop/facing failed: {support_error}, {upper_error}, {seam_error}, {facing_error}")
        if min(lifts.values()) < MINIMUM_FOOT_LIFT or knee_max > 115. or hip_swing > 65. or hip_abduction > 45. or ankle_max > 75. or foot_separation < .18 or knee_separation < .07 or leg_separation < .055 or pelvis_lower > .18:
            raise ValueError(f"{name} anatomy failed: lifts={lifts}, knee={knee_max:.2f}, hip={hip_swing:.2f}/{hip_abduction:.2f}, ankle={ankle_max:.2f}, separations={foot_separation:.4f}/{knee_separation:.4f}/{leg_separation:.4f}, pelvis={pelvis_lower:.4f}, ankle_at={ankle_at}")
        pelvis_range = max(pelvis_heights)-min(pelvis_heights)
        pelvis_speed = max(abs(b-a)*samples/DURATION for a,b in zip(pelvis_heights, pelvis_heights[1:]))
        if pelvis_range > PELVIS_RANGE_LIMIT or pelvis_speed > PELVIS_SPEED_LIMIT:
            raise ValueError(f"{name} torso stability failed: height range={pelvis_range:.6f}m, vertical speed={pelvis_speed:.6f}m/s")
        endpoint = measure_contact_endpoints(builder, direction)
        records.append(dict(name=name, duration_seconds=DURATION, loop=True,
            direction_unity=[-direction.x, direction.z, -direction.y],
            maximum_support_error_m=support_error, maximum_upper_local_error=upper_error,
            maximum_loop_seam_error=seam_error, maximum_pelvis_rotation_degrees=facing_error,
            maximum_pelvis_lowering_m=pelvis_lower, maximum_knee_flexion_degrees=knee_max,
            minimum_pelvis_height_m=min(pelvis_heights), maximum_pelvis_height_m=max(pelvis_heights),
            pelvis_height_range_m=pelvis_range, maximum_pelvis_vertical_speed_m_s=pelvis_speed,
            maximum_hip_swing_degrees=hip_swing, maximum_hip_abduction_degrees=hip_abduction,
            maximum_ankle_turn_degrees=ankle_max, minimum_foot_separation_m=foot_separation,
            minimum_knee_separation_m=knee_separation, minimum_leg_separation_m=leg_separation,
            left_foot_lift_m=lifts["L"], right_foot_lift_m=lifts["R"],
            contact_endpoint_smoothness=endpoint))
        print("Validated "+name+f": support={support_error:.6f}m, knee={knee_max:.1f}, hip={hip_swing:.1f}/{hip_abduction:.1f}, ankle={ankle_max:.1f}, legs={leg_separation:.3f}m, pelvis range={pelvis_range:.6f}m, speed={pelvis_speed:.4f}m/s, endpoint speed={endpoint['maximum_velocity_m_s']:.6f}m/s, acceleration={endpoint['maximum_acceleration_m_s2']:.4f}m/s2", flush=True)
    return dict(generator="tools/build-combat-firearm-walk-3d-model.py", generator_version="1.2.0",
        rig="HeroV2", bone_only=True, root_motion=False, animation_events=0, source_fps=FPS,
        cycle_distance_m=CYCLE_DISTANCE, duration_seconds=DURATION, validation_hz=200,
        stance_contract="L supports [0,.5]; R supports [.5,1]; full alternate strides; linear virtual root cancels support travel",
        smoothness_contract="authored C2 quintic contact blends preserve cubic travel and sin^2 lift in the middle; actual rig world soles checked with linear virtual root and six-point one-sided endpoint derivatives at source-frame spacing",
        endpoint_blend_fraction_of_swing=ENDPOINT_BLEND_FRACTION,
        foot_lift_m=FOOT_LIFT, minimum_foot_lift_m=MINIMUM_FOOT_LIFT, side_stagger_m=SIDE_STAGGER,
        pelvis_contract="fixed per-direction full-cycle reach floor sampled at 400 Hz; 18 mm leg reserve plus 1 mm margin; only 4 mm smooth vertical load",
        pelvis_load_m=PELVIS_LOAD, pelvis_reach_margin_m=PELVIS_REACH_MARGIN,
        pelvis_height_range_limit_m=PELVIS_RANGE_LIMIT, pelvis_vertical_speed_limit_m_s=PELVIS_SPEED_LIMIT,
        contact_endpoint_velocity_limit_m_s=ENDPOINT_VELOCITY_LIMIT,
        contact_endpoint_acceleration_limit_m_s2=ENDPOINT_ACCELERATION_LIMIT,
        facing_contract="original aimed frame; pelvis yaw and torso local pose fixed for all directions",
        signature=checksum.hexdigest(), clips=records)


def measure_contact_endpoints(builder, direction):
    """Evaluate actual bone tracks on both sides of every support transition.

    The rig is sampled across the cyclic seam, while the virtual root retains
    its unwrapped linear travel. Six-point stencils distinguish a softened
    acceleration from the old cubic/lift impulse without testing the target
    function against itself. They retain the 100 Hz source-frame resolution;
    this is a measured sampled-rig contract, not a claim about FBX interpolation.
    """
    rig = builder.result.rig
    step = DURATION/FPS
    velocity = acceleration = velocity_jump = acceleration_jump = 0.
    points = {}

    def world_sole(side, seconds):
        key = (side, round(seconds, 8))
        if key not in points:
            phase = (seconds/DURATION) % 1.
            frame = phase*DURATION*FPS
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
            bpy.context.view_layer.update()
            sole = rig.pose.bones["foot."+side].head
            # Add the virtual root in Python doubles so numerical cancellation
            # in the derivative is not dominated by float32 world coordinates.
            points[key] = tuple(float(sole[i])+float(direction[i])*CYCLE_DISTANCE*seconds/DURATION for i in range(3))
        return points[key]

    def derivative(side, seconds, sign, order):
        weights = (-137., 300., -300., 200., -75., 12.) if order == 1 else (45., -154., 214., -156., 61., -10.)
        samples = [world_sole(side, seconds+sign*index*step) for index in range(6)]
        factor = sign/(60.*step) if order == 1 else 1./(12.*step*step)
        return Vector(tuple(math.fsum(weight*point[axis] for weight, point in zip(weights, samples))*factor for axis in range(3)))

    for side, seconds in (("L", .5*DURATION), ("L", DURATION), ("R", 0.), ("R", .5*DURATION)):
        before_v, after_v = (derivative(side, seconds, sign, 1) for sign in (-1., 1.))
        before_a, after_a = (derivative(side, seconds, sign, 2) for sign in (-1., 1.))
        velocity = max(velocity, before_v.length, after_v.length)
        acceleration = max(acceleration, before_a.length, after_a.length)
        velocity_jump = max(velocity_jump, (after_v-before_v).length)
        acceleration_jump = max(acceleration_jump, (after_a-before_a).length)
    if velocity > ENDPOINT_VELOCITY_LIMIT or acceleration > ENDPOINT_ACCELERATION_LIMIT:
        raise ValueError(f"{rig.animation_data.action.name} contact endpoint smoothing failed: velocity={velocity:.6f}m/s, acceleration={acceleration:.4f}m/s2, jumps={velocity_jump:.6f}/{acceleration_jump:.4f}")
    return dict(sample_step_seconds=step, maximum_velocity_m_s=velocity,
        maximum_acceleration_m_s2=acceleration, maximum_velocity_jump_m_s=velocity_jump,
        maximum_acceleration_jump_m_s2=acceleration_jump)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, default=OUT)
    parser.add_argument("--source-dir", type=Path, default=SOURCE)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--probe-clip", choices=[name for name,_ in CLIPS])
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    builder = load_builder()
    if args.probe_clip:
        selection = [entry for entry in CLIPS if entry[0] == args.probe_clip]
        builder.author(selection); measure(builder, selection)
        return
    for clip in CLIPS:
        builder.author((clip,)); measure(builder, (clip,))
    payload = measure(builder)
    signature = payload["signature"]
    builder.author()
    repeated = measure(builder)
    if repeated != payload or repeated["signature"] != signature:
        raise ValueError("Firearm walks are nondeterministic")
    manifest = args.output_dir/"Actions.json"
    if args.validate_only:
        if json.loads(manifest.read_text(encoding="utf8")) != json.loads(json.dumps(payload)):
            raise ValueError("Published firearm walk manifest differs from deterministic source")
    else:
        common.export_animation_fbx(args.output_dir/"Actions.fbx", builder.result)
        for modifier, visible in builder.deformation:
            modifier.show_viewport = visible
        common.save_blend(args.source_dir/"Actions.blend")
        manifest.write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")
    print("FIREARM AIM WALK CONTRACT OK " + signature, flush=True)


if __name__ == "__main__":
    main()
