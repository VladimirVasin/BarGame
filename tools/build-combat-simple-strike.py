"""One arm-led swing and matching raised grip; preserve Ready and unrelated actions."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
import importlib.util

spec = importlib.util.spec_from_file_location("simple_strike_combat", ROOT / "tools/build-combat-test-3d-model.py")
m = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = m
spec.loader.exec_module(m)
r = m.module("simple_strike_retained", "combat_forehand_repair.py")
import combat_bat_continuity
STRIKE_NAMES = ("CombatAttack", "CombatReleaseLight", "CombatReleaseHeavy", "CombatCharge", "CombatRecoil", "CombatReady")
GUARD_NAMES = ("CombatBlock", "CombatGuardImpact", "CombatGuardBreak")
NAMES = STRIKE_NAMES + GUARD_NAMES
BAT_GRIP = -.085
RIGHT_GRIP_STATION = .08
LEFT_GRIP_STATION = RIGHT_GRIP_STATION + BAT_GRIP
WINDUP_SECONDS, ACTIVE_SECONDS, RECOVERY_SECONDS = .22, .14, .42
ATTACK_SECONDS = WINDUP_SECONDS + ACTIVE_SECONDS + RECOVERY_SECONDS
CONTACT_SECONDS, RECOIL_ENTRY_SECONDS = .36, .36
SHOULDER_LOAD_SECONDS = .11
CENTRE_SECONDS, OVERRUN_SECONDS = .300, .395
BRAKE_END_SECONDS = .430
EARLY_SHOULDER_SECONDS = SHOULDER_LOAD_SECONDS + .03
CONTACT_SAMPLE_HZ = 240
HELD_SHAFT_ROLL_DEGREES = -148.4
SOURCE_HASH = hashlib.sha256(Path(__file__).read_bytes() +
    Path(combat_bat_continuity.__file__).read_bytes()).hexdigest()


def smooth(value):
    t = max(0., min(1., value))
    return t * t * (3. - 2. * t)


def configure_weapon_frame(builder, *, physical=False):
    """Separate the anatomical right dock from the physical metal origin."""
    hand_frame = builder.weapon_frame
    clearance = builder.weapon_clearance
    builder.hand_weapon_frame = hand_frame
    builder.bat_physical_frame = False
    def physical_clearance(origin=None, rotation=None, arms=None, ignore_arm_sides=()):
        if origin is None: origin, rotation = builder.weapon_frame()
        if not builder.bat_physical_frame:
            origin = origin-(rotation @ Vector((0., 1., 0.)))*RIGHT_GRIP_STATION
        return clearance(origin, rotation, arms, ignore_arm_sides)
    builder.weapon_clearance = physical_clearance
    def activate():
        def frame():
            centre, rotation = hand_frame()
            rotation = rotation @ Quaternion((0., 1., 0.), math.radians(-HELD_SHAFT_ROLL_DEGREES))
            return centre-(rotation @ Vector((0., 1., 0.)))*RIGHT_GRIP_STATION, rotation
        builder.weapon_frame = frame
        builder.bat_physical_frame = True
    if physical: activate()
    return activate


def retarget_guard_tracks(source, profile):
    """Move only the supporting arm to the raised physical guard station."""
    guard = r._canonical(m, profile)
    configure_weapon_frame(guard, physical=True)
    tracks = {}
    for name in GUARD_NAMES:
        record = source.result.actions[name]
        duration = record.duration_seconds
        break_poses = None
        if name == "CombatGuardBreak":
            block = r._apply_matrices(guard, tracks["CombatBlock"][1][0])
            original = r._sample(source.result.rig, record.action, .12)
            source.result.rig.animation_data.action = None
            peak = r._apply_matrices(guard, original)
            physical_frame = guard.weapon_frame
            guard.weapon_frame = guard.hand_weapon_frame
            guard.bat_physical_frame = False
            _, bar = guard.weapon_frame()
            bar = (bar @ Vector((0., 1., 0.))).rotation_difference(Vector((.98, -.10, .12)).normalized()) @ bar
            clearance = guard.weapon_clearance
            guard.weapon_clearance = lambda origin=None, rotation=None, arms=None: clearance(origin, rotation, arms, ("L",))
            try:
                peak = guard.align_right_grip(peak, frame=(Vector((-.28, -.22, 1.12)), bar),
                    support=False, right_pole=(-.7, -.35, -.7))
            finally:
                guard.weapon_clearance = clearance
                guard.weapon_frame = physical_frame
                guard.bat_physical_frame = True
            # A broken guard releases L down/forward; its fingers follow the
            # forearm instead of maintaining a fictitious cylindrical dock.
            bones = guard.result.rig.pose.bones
            wrist = Vector((.28, -.20, 1.00))
            rest = bones["hand.L"].bone
            rotation = (rest.tail_local-rest.head_local).normalized().rotation_difference(Vector((0., -.8, -.6))) @ rest.matrix_local.to_quaternion()
            guard.solve_arm("L", wrist, rotation.to_matrix(), Vector((.7, -.35, -.7)))
            lower, hand = (bones[n+".L"] for n in ("forearm", "hand"))
            rotation = (rest.tail_local-rest.head_local).normalized().rotation_difference((hand.head-lower.head).normalized()) @ rest.matrix_local.to_quaternion()
            guard.solve_arm("L", wrist, rotation.to_matrix(), lower.head-bones["upper_arm.L"].head)
            peak = guard.snapshot_pose()
            ready = r._sample(source.result.rig, source.result.actions["CombatReady"].action, 0.)
            source.result.rig.animation_data.action = None
            break_poses = block, peak, r._apply_matrices(guard, ready)
        values, continuation = [], None
        changed_roots = {"upper_arm.L", "upper_arm.R"} if name == "CombatGuardBreak" else {"upper_arm.L"}
        preserved = []
        for bone in guard.result.rig.pose.bones:
            parent = bone
            while parent is not None and parent.name not in changed_roots: parent = parent.parent
            if parent is None: preserved.append(bone.name)
        for frame in range(round(duration*m.FPS)+1):
            seconds = frame/m.FPS
            original = r._sample(source.result.rig, record.action, seconds)
            source.result.rig.animation_data.action = None
            pose = r._apply_matrices(guard, original)
            if name != "CombatGuardBreak":
                pose = guard.pin_left_grip(pose, m.BLOCK_GRIP, move_right=False,
                    continuation=continuation, runtime_support=continuation is None)
                continuation = guard.support_branch(pose)
            else:
                block, peak, ready = break_poses
                release = guard.linear_key_pose(block, peak, smooth(seconds/.12)) if seconds < .12 else \
                    guard.linear_key_pose(peak, ready, smooth((seconds-.30)/(.70-.30)))
                # Both arms exit the new dock continuously while the authored
                # broken-guard body and planted feet remain unchanged.
                pose.update({joint+"."+side: release[joint+"."+side]
                    for side in ("L", "R") for joint in ("upper_arm", "forearm", "hand")})
                guard._reset_pose(); guard._apply_pose(pose)
            current = {bone.name: bone.matrix.copy() for bone in guard.result.rig.pose.bones}
            if max(abs(current[bone][i][j]-original[bone][i][j])
                for bone in preserved for i in range(4) for j in range(4)) > 2.e-5:
                raise ValueError(f"Raised guard changed a preserved body/arm channel: {name}@{seconds}")
            values.append(current)
            if frame % 100 == 0: print(f"RAISED GUARD {name} {frame}/{round(duration*m.FPS)}", flush=True)
        # Retain an exact block loop and the shared impact endpoints.
        if name == "CombatBlock": values[-1] = {k:v.copy() for k,v in values[0].items()}
        elif name == "CombatGuardImpact":
            values[0] = {k:v.copy() for k,v in tracks["CombatBlock"][1][0].items()}
            values[-1] = {k:v.copy() for k,v in values[0].items()}
        elif name == "CombatGuardBreak":
            values[0] = {k:v.copy() for k,v in tracks["CombatBlock"][1][0].items()}
        tracks[name] = duration, values
    bpy.data.objects.remove(guard.result.rig, do_unlink=True)
    bpy.data.objects.remove(guard.result.root, do_unlink=True)
    return tracks


def body_pose(builder, ready, turn, pelvis_turn=None):
    builder._reset_pose(); builder._apply_pose(ready)
    pelvis_turn = turn*.15 if pelvis_turn is None else pelvis_turn
    upper_turn = turn-pelvis_turn
    for name, degrees in (("pelvis", pelvis_turn), ("spine", upper_turn*.35/.85), ("chest", upper_turn*.50/.85)):
        bone = builder.result.rig.pose.bones[name]
        rotation = Quaternion((0., 0., 1.), math.radians(degrees)) @ bone.matrix.to_quaternion()
        bone.matrix = Matrix.Translation(bone.head.copy()) @ rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()
    return builder.pin_supports(builder.snapshot_pose())


def pin_bat_left(builder, pose):
    builder._reset_pose(); builder._apply_pose(pose)
    rig=builder.result.rig; hand=rig.pose.bones["hand.L"]; rest=hand.bone
    centre,rest_axis,_=builder.hand_frame("L")
    origin,bar=builder.weapon_frame();axis=bar @ Vector((0.,1.,0.))
    initial=hand.matrix.to_quaternion()
    delta=initial.to_matrix() @ rest.matrix_local.to_3x3().inverted()
    initial=(delta @ rest_axis).rotation_difference(axis) @ initial
    pole=rig.pose.bones["forearm.L"].head-rig.pose.bones["upper_arm.L"].head
    closest=None
    for degrees in (0.,*(sign*angle for angle in range(3,181,3) for sign in (1.,-1.))):
        rotation=(Quaternion(axis,math.radians(degrees)) @ initial).to_matrix()
        delta=rotation @ rest.matrix_local.to_3x3().inverted()
        wrist=origin+axis*BAT_GRIP-delta @ (centre-rest.head_local)
        choice=builder.grip_elbow("L",wrist,rotation,pole,total_limit=65.,support_head=True,weapon_check=True)
        if choice is not None and builder.weapon_clearance(arms={"L":(rig.pose.bones["upper_arm.L"].head,choice[2],wrist)})["body_gap_m"] >= .005:
            builder.solve_arm("L",wrist,rotation,choice[3]);return builder.snapshot_pose()
    raise ValueError("No continuous cylindrical bat grasp for the left hand")


def align_bat(builder, pose, **kwargs):
    kwargs.pop("left_pole",None);kwargs.pop("support_offset",None)
    complete_clearance=builder.weapon_clearance
    def pending_support_clearance(origin=None,rotation=None,arms=None):
        return complete_clearance(origin,rotation,arms,ignore_arm_sides=("L",))
    builder.weapon_clearance=pending_support_clearance
    try: right=builder.align_right_grip(pose,support=False,**kwargs)
    finally: builder.weapon_clearance=complete_clearance
    return pin_bat_left(builder,right)


def author_stops(builder, ready):
    global HELD_SHAFT_ROLL_DEGREES
    builder.grip_backhand = True  # Include the actual head in both arm clearance gates.
    builder._reset_pose(); builder._apply_pose(ready)
    origin, ready_bar = builder.weapon_frame()
    def hands(seconds, origin, axis, turn, pelvis_turn):
        builder.bat_seconds = seconds
        bar = (ready_bar @ Vector((0., 1., 0.))).rotation_difference(Vector(axis).normalized()) @ ready_bar
        return align_bat(builder, body_pose(builder, ready, turn, pelvis_turn),
            frame=(Vector(origin), bar), right_pole=(-.7, -.35, -.7))
    # The shaft travels across the front through the arms. Each torso pose is
    # authored from Ready, so shoulder assistance never compounds into a spin.
    loaded = hands(SHOULDER_LOAD_SECONDS, (-.26, -.13, 1.31), (-.20, .67, .71), -12., -5.)
    # Add the deeper coil to the proven connected arm chain, retaining its
    # wrist roll and two palm contacts instead of solving a new grip branch.
    loaded = body_pose(builder, loaded, -8., -2.)
    side = hands(WINDUP_SECONDS, (-.12, -.34, 1.18), (-.80, -.30, .52), -10., -3.)
    side = body_pose(builder, side, -4., -2.)
    contact = hands(CENTRE_SECONDS, (.02, -.41, 1.19), (-.05, -.98, .18), 6., 4.)
    contact = body_pose(builder, contact, 3., 1.)
    finish = hands(CONTACT_SECONDS, (.00, -.35, 1.00), (.80, -.55, .25), 15., 6.)
    finish = body_pose(builder, finish, 5., 2.)
    overrun = body_pose(builder, finish, 2., 1.)
    builder.bat_seconds = OVERRUN_SECONDS
    bones = builder.result.rig.pose.bones
    upper, lower, hand = (bones[name+".R"] for name in ("upper_arm", "forearm", "hand"))
    wrist = hand.head + Vector((.020, .015, -.010))
    rotation = (Quaternion((0., 0., 1.), math.radians(3.)) @ hand.matrix.to_quaternion()).to_matrix()
    elbow = builder.grip_elbow("R", wrist, rotation, lower.head-upper.head, nearest=True, maximum_turn=20.)
    if elbow is None: raise ValueError("No natural continuation of the low finish elbow")
    builder.solve_arm("R", wrist, rotation, elbow[3])
    overrun = pin_bat_left(builder, builder.snapshot_pose())
    returning = hands(.60, (-.10, -.31, 1.24), (-.50, -.20, .84), 0., 0.)
    stops = [(0., ready), (SHOULDER_LOAD_SECONDS, loaded), (WINDUP_SECONDS, side),
             (CENTRE_SECONDS, contact), (CONTACT_SECONDS, finish), (OVERRUN_SECONDS, overrun),
             (BRAKE_END_SECONDS, overrun), (.60, returning), (ATTACK_SECONDS, ready)]
    # Keep the validated spatial C1 curve while extending anticipation and
    # punctuation. Recomputing its tangents from the longer clock changed the
    # forearm path into the chest by less than a millimetre.
    curve_seconds = (0., .08, .16, .240, .30, .335, .335, .49, .62)
    result = []
    for seconds, pose in stops:
        builder._reset_pose(); builder._apply_pose(pose)
        bones = builder.result.rig.pose.bones
        origin, bar = builder.weapon_frame()
        result.append(dict(seconds=seconds, curve_seconds=curve_seconds[len(result)], pose=pose, origin=origin.copy(),
            bar=bar.copy(),
            hand=bones["hand.R"].matrix.to_quaternion().copy(),
            poles={side:(bones["forearm."+side].head-bones["upper_arm."+side].head).normalized()
                   for side in ("R", "L")}))
    return result


def weight_transfer(builder, pose, seconds):
    """Move the connected torso/arms over planted feet, retaining both grips.

    The pelvis leads the forward delivery; the upper body completes its lean
    through the follow-through. This adds weight without additional yaw or
    solving either arm onto a different elbow/wrist branch.
    """
    keys = ((0., 0., 0., 0.), (SHOULDER_LOAD_SECONDS, -4., .015, -.028),
        (WINDUP_SECONDS, 3., -.012, -.020), (CENTRE_SECONDS, 5., -.020, -.022),
        (CONTACT_SECONDS, 7., -.030, -.025), (OVERRUN_SECONDS, 8., -.035, -.030),
        (BRAKE_END_SECONDS, 8., -.035, -.030), (.60, 2., -.008, -.008),
        (ATTACK_SECONDS, 0., 0., 0.))
    if seconds <= 0. or seconds >= ATTACK_SECONDS-1.e-8: return dict(pose)
    for start, end in zip(keys, keys[1:]):
        if seconds <= end[0]+1.e-8:
            blend = smooth((seconds-start[0])/(end[0]-start[0]))
            pitch, forward, height = (start[i]+(end[i]-start[i])*blend for i in range(1,4))
            break
    builder._reset_pose(); builder._apply_pose(pose)
    pelvis = builder.result.rig.pose.bones["pelvis"]
    matrix = pelvis.matrix.copy()
    matrix.translation += Vector((0., forward, height))
    pelvis.matrix = matrix
    bpy.context.view_layer.update()
    for name, fraction in (("spine", .45), ("chest", .55)):
        bone = builder.result.rig.pose.bones[name]
        rotation = Quaternion((1., 0., 0.), math.radians(pitch*fraction)) @ bone.matrix.to_quaternion()
        bone.matrix = Matrix.Translation(bone.head.copy()) @ rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()
    return builder.pin_supports(builder.snapshot_pose())


def shoulder_metrics(builder, poses, fps, label):
    result = dict(maximum_chest_turn_degrees=0., maximum_pelvis_turn_degrees=0.,
        minimum_chest_yaw_degrees=10., maximum_chest_yaw_degrees=-10.,
        maximum_shoulder_speed_degrees_s=0., maximum_early_shoulder_speed_degrees_s=0.,
        maximum_early_left_shoulder_twist_degrees=0., maximum_left_elbow_above_shoulder_m=-10.,
        maximum_right_elbow_above_shoulder_m=-10., maximum_late_shoulder_speed_degrees_s=0.,
        minimum_left_elbow_forward_m=10., minimum_chest_lean_degrees=0., maximum_chest_lean_degrees=0.,
        maximum_pelvis_displacement_m=0., maximum_foot_slide_m=0.)
    previous = None
    for seconds, pose in poses:
        builder._reset_pose(); builder._apply_pose(pose)
        bones = builder.result.rig.pose.bones
        line = bones["upper_arm.L"].head-bones["upper_arm.R"].head
        line.z = 0.; line.normalize()
        reference = builder.bat_ready_shoulder_line
        yaw = math.degrees(math.atan2(reference.x*line.y-reference.y*line.x, reference.dot(line)))
        result["minimum_chest_yaw_degrees"] = min(result["minimum_chest_yaw_degrees"], yaw)
        result["maximum_chest_yaw_degrees"] = max(result["maximum_chest_yaw_degrees"], yaw)
        forward = bones["chest"].matrix.to_quaternion() @ builder.bat_ready_body["chest"].inverted() @ Vector((0., -1., 0.))
        lean = -math.degrees(math.asin(max(-1., min(1., forward.z))))
        result["minimum_chest_lean_degrees"] = min(result["minimum_chest_lean_degrees"], lean)
        result["maximum_chest_lean_degrees"] = max(result["maximum_chest_lean_degrees"], lean)
        for name in ("pelvis", "foot.L", "foot.R"):
            travel = (bones[name].head-builder.bat_ready_locations[name]).length
            key = "maximum_pelvis_displacement_m" if name == "pelvis" else "maximum_foot_slide_m"
            result[key] = max(result[key], travel)
        for name in ("chest", "pelvis"):
            angle = math.degrees(2.*math.acos(min(1., abs(bones[name].matrix.to_quaternion().dot(builder.bat_ready_body[name])))))
            result["maximum_"+name+"_turn_degrees"] = max(result["maximum_"+name+"_turn_degrees"], angle)
        states = {side: combat_bat_continuity.arm_state(builder, side) for side in ("L", "R")}
        result["maximum_left_elbow_above_shoulder_m"] = max(result["maximum_left_elbow_above_shoulder_m"], states["L"][2])
        result["maximum_right_elbow_above_shoulder_m"] = max(result["maximum_right_elbow_above_shoulder_m"], states["R"][2])
        result["minimum_left_elbow_forward_m"] = min(result["minimum_left_elbow_forward_m"], states["L"][3])
        speed = max((math.degrees(2.*math.acos(min(1.,abs(states[side][0].dot(previous[side])))))*fps for side in states), default=0.) if previous else 0.
        result["maximum_shoulder_speed_degrees_s"] = max(result["maximum_shoulder_speed_degrees_s"], speed)
        if label == "CombatAttack" and seconds <= EARLY_SHOULDER_SECONDS+1.e-8:
            result["maximum_early_shoulder_speed_degrees_s"] = max(result["maximum_early_shoulder_speed_degrees_s"], speed)
            result["maximum_early_left_shoulder_twist_degrees"] = max(result["maximum_early_left_shoulder_twist_degrees"], states["L"][1])
        if label == "CombatAttack" and seconds > CONTACT_SECONDS+.02+1.e-8:
            result["maximum_late_shoulder_speed_degrees_s"] = max(result["maximum_late_shoulder_speed_degrees_s"], speed)
        previous = {side: state[0] for side, state in states.items()}
    result["chest_yaw_span_degrees"] = result["maximum_chest_yaw_degrees"]-result["minimum_chest_yaw_degrees"]
    print("BAT SHOULDER PROOF "+label+" "+json.dumps(result), flush=True)
    if label == "CombatAttack" and result["chest_yaw_span_degrees"] < 37.:
        raise ValueError("Strike torso no longer visibly loads and uncoils")
    if result["maximum_chest_turn_degrees"] > 24. or result["maximum_pelvis_turn_degrees"] > 9.3 or \
        max(abs(result["minimum_chest_yaw_degrees"]), abs(result["maximum_chest_yaw_degrees"])) > 22.3 or \
        result["minimum_chest_lean_degrees"] < -4.3 or result["maximum_chest_lean_degrees"] > 8.3 or \
        result["maximum_pelvis_displacement_m"] > .051 or result["maximum_foot_slide_m"] > .0002 or \
        result["maximum_shoulder_speed_degrees_s"] > 1600. or result["maximum_early_shoulder_speed_degrees_s"] > 900. or \
        result["maximum_early_left_shoulder_twist_degrees"] > 75.1 or result["maximum_left_elbow_above_shoulder_m"] > .022 or \
        result["minimum_left_elbow_forward_m"] < .008 or result["maximum_right_elbow_above_shoulder_m"] > .022 or \
        result["maximum_late_shoulder_speed_degrees_s"] > 1200.:
        raise ValueError("Arm-led strike lost its shoulder/torso continuity: "+json.dumps(result))
    return result


def metrics(builder, poses, fps, label):
    shoulders = shoulder_metrics(builder, poses, fps, label)
    maximum_wrist = maximum_deviation = maximum_flexion = 0.
    left_wrist = left_deviation = left_flexion = support_error = support_angle = 0.
    gap = floor = native = 10.
    tips = []
    horizontal_angles = []
    tip_speeds = []
    previous_tip = None
    tip_samples, tip_velocity = [], []
    previous = None
    maximum_speed = 0.
    backward = 0.
    for seconds, pose in poses:
        builder._reset_pose()
        builder._apply_pose(pose)
        rig = builder.result.rig
        upper, lower, hand = (rig.pose.bones[n+".R"] for n in ("upper_arm", "forearm", "hand"))
        direction = (hand.head-lower.head).normalized()
        fingers = (hand.tail-hand.head).normalized()
        delta = hand.matrix.to_3x3() @ hand.bone.matrix_local.to_3x3().inverted()
        across = (delta @ builder.hand_frame("R")[1]).normalized()
        palm = (delta @ builder.hand_frame("R")[2]).normalized()
        deviation = abs(math.degrees(math.asin(max(-1., min(1., direction.dot(across))))))
        flexion = abs(math.degrees(math.atan2(direction.dot(palm), direction.dot(fingers))))
        wrist = math.degrees(direction.angle(fingers))
        clearance = builder.weapon_clearance()
        right_gap = builder.right_arm_native_gap(upper.head, lower.head, hand.head)
        if deviation > 25.01 or flexion > 55.01 or wrist > 65.01 or \
            clearance["body_gap_m"] < .004 or clearance["floor_gap_m"] < .025 or right_gap < -.004:
            raise ValueError(f"{label}@{seconds:.5f}: wrist={wrist:.3f}/{deviation:.3f}/{flexion:.3f}, body={clearance}, native={right_gap:.6f}")
        builder.weapon_anatomy(f"{label}@{seconds:.5f}", 65.)
        ll, lh = (rig.pose.bones[n+".L"] for n in ("forearm", "hand"))
        ld = (lh.head-ll.head).normalized(); lf = (lh.tail-lh.head).normalized()
        ldelta = lh.matrix @ lh.bone.matrix_local.inverted()
        la = (ldelta.to_3x3() @ builder.hand_frame("L")[1]).normalized()
        lp = (ldelta.to_3x3() @ builder.hand_frame("L")[2]).normalized()
        left_wrist = max(left_wrist, math.degrees(ld.angle(lf)))
        left_deviation = max(left_deviation, abs(math.degrees(math.asin(max(-1., min(1., ld.dot(la)))))))
        left_flexion = max(left_flexion, abs(math.degrees(math.atan2(ld.dot(lp), ld.dot(lf)))))
        origin, rotation = builder.weapon_frame(); shaft = rotation @ Vector((0., 1., 0.))
        support_error = max(support_error, ((ldelta @ builder.hand_frame("L")[0])-(origin+shaft*LEFT_GRIP_STATION)).length)
        support_angle = max(support_angle, math.degrees(la.angle(shaft)))
        tip = origin + rotation @ Vector((0., .570, .145))
        tip_samples.append((seconds, tip.copy()))
        if previous_tip is not None: tip_velocity.append((seconds-.5/fps, (tip-previous_tip).length*fps))
        if previous_tip is not None and WINDUP_SECONDS + .02 <= seconds <= CONTACT_SECONDS - .01:
            tip_speeds.append((tip-previous_tip).length * fps)
        previous_tip = tip.copy()
        if SHOULDER_LOAD_SECONDS-.02 <= seconds <= SHOULDER_LOAD_SECONDS+.02: backward = max(backward, tip.y)
        if previous is not None:
            speed=max((lower.head-previous[0]).length * fps, (ll.head-previous[1]).length * fps)
            if speed > maximum_speed:
                maximum_speed = speed
                if speed > 6.01: print(f"ELBOW CONTINUITY {seconds:.4f} {speed:.4f} R={tuple(lower.head)} L={tuple(ll.head)}",flush=True)
        previous = (lower.head.copy(), ll.head.copy())
        maximum_wrist = max(maximum_wrist, wrist)
        maximum_deviation = max(maximum_deviation, deviation)
        maximum_flexion = max(maximum_flexion, flexion)
        gap = min(gap, clearance["body_gap_m"])
        floor = min(floor, clearance["floor_gap_m"])
        native = min(native, right_gap)
        if WINDUP_SECONDS-1.e-8 <= seconds <= CONTACT_SECONDS+1.e-8:
            tips.append(tip)
            offset = tip-rig.pose.bones["chest"].head
            if math.hypot(offset.x, offset.y) >= .55:
                horizontal_angles.append(math.degrees(math.atan2(-offset.x, -offset.y)))
    travel = max(((tip-tips[0]).length for tip in tips), default=0.)
    if label == "CombatAttack" and (travel < .80 or backward < .09):
        raise ValueError(f"Two-hand strike lost its broad shoulder load: travel={travel}, backward={backward}")
    reach = max((-tip.y for tip in tips), default=0.)
    right_angle = max(horizontal_angles, default=0.)
    left_angle = min(horizontal_angles, default=0.)
    horizontal_coverage = right_angle-left_angle
    horizontal_travel = sum(math.hypot(b.x-a.x, b.y-a.y) for a,b in zip(tips,tips[1:]))
    print(f"BAT ARC PROOF {label}: right={right_angle:.4f}, left={left_angle:.4f}, span={horizontal_coverage:.4f}, path={horizontal_travel:.5f}, elbow={maximum_speed:.4f}, tip={travel:.5f}, reach={reach:.5f}", flush=True)
    if label == "CombatAttack" and (horizontal_coverage < 70. or right_angle < 35. or left_angle > -35.):
        raise ValueError(f"Strike lost its broad front crossing at useful reach: right={right_angle:.3f}, left={left_angle:.3f}, coverage={horizontal_coverage:.3f}")
    if label == "CombatAttack" and reach < .80:
        raise ValueError(f"Simple strike cannot reach beyond automatic shove: {reach}")
    minimum_tip_speed = min(tip_speeds, default=0.)
    if label == "CombatAttack" and minimum_tip_speed < 1.:
        raise ValueError(f"Strike stops inside its active arc: {minimum_tip_speed:.4f}m/s")
    if maximum_speed > 6.01:
        raise ValueError(f"Simple shoulder arc exceeds elbow speed: {maximum_speed}")
    if support_error > .012 or support_angle > 12.01:
        raise ValueError(f"Shared bat contact exceeded its existing dock: {support_error:.6f}m/{support_angle:.4f}deg")
    impact = {}
    if label == "CombatAttack":
        nearest_tip = lambda seconds: min(tip_samples, key=lambda sample: abs(sample[0]-seconds))[1]
        nearest_speed = lambda seconds: min(tip_velocity, key=lambda sample: abs(sample[0]-seconds))[1]
        peak_time, peak_speed = max((sample for sample in tip_velocity if WINDUP_SECONDS <= sample[0] <= CONTACT_SECONDS), key=lambda sample: sample[1])
        impact = dict(overrun_tip_travel_m=(nearest_tip(OVERRUN_SECONDS)-nearest_tip(CONTACT_SECONDS)).length,
            maximum_active_tip_speed_m_s=peak_speed, peak_active_tip_speed_seconds=peak_time,
            centre_tip_speed_m_s=nearest_speed(CENTRE_SECONDS), active_exit_tip_speed_m_s=nearest_speed(CONTACT_SECONDS),
            braked_tip_speed_m_s=nearest_speed(OVERRUN_SECONDS),
            maximum_brake_hold_tip_speed_m_s=max(speed for seconds,speed in tip_velocity if OVERRUN_SECONDS+.01 <= seconds <= BRAKE_END_SECONDS-.01),
            maximum_return_tip_speed_m_s=max(speed for seconds,speed in tip_velocity if seconds > BRAKE_END_SECONDS))
        print("BAT IMPACT PROFILE "+json.dumps(impact), flush=True)
    return dict(maximum_right_wrist_degrees=maximum_wrist, maximum_right_deviation_degrees=maximum_deviation,
        maximum_right_flexion_degrees=maximum_flexion, minimum_body_clearance_m=gap,
        minimum_floor_clearance_m=floor, minimum_native_right_arm_gap_m=native,
        maximum_right_elbow_speed_m_s=maximum_speed, active_tip_travel_m=travel,
        maximum_forward_reach_m=reach, maximum_left_wrist_degrees=left_wrist,
        maximum_left_deviation_degrees=left_deviation, maximum_left_flexion_degrees=left_flexion,
        maximum_support_error_m=support_error, maximum_support_angle_degrees=support_angle,
        minimum_windup_tip_backward_m=backward, minimum_active_tip_speed_m_s=minimum_tip_speed,
        active_horizontal_coverage_degrees=horizontal_coverage,
        active_right_angle_degrees=right_angle, active_left_angle_degrees=left_angle,
        active_horizontal_path_m=horizontal_travel, **shoulders, **impact)


def metal_chord_error(builder, rig, action):
    cache = {}
    def points(seconds):
        key = round(seconds, 9)
        if key not in cache:
            matrices = r._sample(rig, action, seconds)
            rig.animation_data.action = None
            r._apply_matrices(builder, matrices)
            origin, rotation = builder.weapon_frame()
            cache[key] = [origin+rotation @ Vector(point) for point in m.CROWBAR_PATH]
        return cache[key]
    maximum, worst = 0., None
    for phase in (0., .25, .5, .75):
        boundaries = sorted(set([WINDUP_SECONDS, CONTACT_SECONDS]+[
            WINDUP_SECONDS+(index+phase)/CONTACT_SAMPLE_HZ
            for index in range(math.ceil(ACTIVE_SECONDS*CONTACT_SAMPLE_HZ))
            if WINDUP_SECONDS < WINDUP_SECONDS+(index+phase)/CONTACT_SAMPLE_HZ < CONTACT_SECONDS]))
        for start, end in zip(boundaries, boundaries[1:]):
            before, after = points(start), points(end)
            for fraction in (.25, .5, .75):
                actual = points(start+(end-start)*fraction)
                error = max((point-a.lerp(b,fraction)).length for point,a,b in zip(actual,before,after))
                if error > maximum: maximum, worst = error, (phase, start, end, fraction)
    print(f"BAT METAL CHORD PROOF {CONTACT_SAMPLE_HZ}Hz/{maximum:.7f}m at {worst}", flush=True)
    if maximum > .005:
        raise ValueError(f"Actual metal leaves the {CONTACT_SAMPLE_HZ} Hz sweep chord by {maximum:.6f}m")
    return maximum


def guard_metrics(builder, poses, label):
    result = dict(maximum_support_error_m=0., maximum_support_angle_degrees=0.,
        maximum_opposed_palm_error=0., maximum_right_dock_error_m=0.,
        maximum_guard_wrist_degrees=0., minimum_body_clearance_m=10.,
        maximum_released_left_wrist_degrees=0., minimum_left_arm_clearance_m=10.,
        maximum_elbow_speed_m_s=0.)
    previous = None
    for seconds, pose in poses:
        builder._reset_pose(); builder._apply_pose(pose)
        bones = builder.result.rig.pose.bones
        origin, rotation = builder.weapon_frame()
        shaft = rotation @ Vector((0., 1., 0.))
        deltas = {side: bones["hand."+side].matrix @ bones["hand."+side].bone.matrix_local.inverted() for side in ("L", "R")}
        right_centre = deltas["R"] @ builder.hand_frame("R")[0]
        result["maximum_right_dock_error_m"] = max(result["maximum_right_dock_error_m"], (right_centre-origin-shaft*RIGHT_GRIP_STATION).length)
        # GuardBreak releases support after its shared first Block pose.
        supported = label != "CombatGuardBreak" or seconds < 1.e-8
        lower, hand = (bones[n+".L"] for n in ("forearm", "hand"))
        result["minimum_left_arm_clearance_m"] = min(result["minimum_left_arm_clearance_m"], builder.arm_core_clearance("L"))
        if not supported:
            result["maximum_released_left_wrist_degrees"] = max(result["maximum_released_left_wrist_degrees"],
                math.degrees((hand.head-lower.head).angle(hand.tail-hand.head)))
        elbows = tuple(bones["forearm."+side].head.copy() for side in ("L", "R"))
        if previous is not None:
            result["maximum_elbow_speed_m_s"] = max(result["maximum_elbow_speed_m_s"],
                max((a-b).length for a,b in zip(elbows, previous[1]))/(seconds-previous[0]))
        previous = seconds, elbows
        if supported:
            left_centre = deltas["L"] @ builder.hand_frame("L")[0]
            left_axis = (deltas["L"].to_3x3() @ builder.hand_frame("L")[1]).normalized()
            palms = {side: (deltas[side].to_3x3() @ builder.hand_frame(side)[2]).normalized() for side in ("L", "R")}
            result["maximum_support_error_m"] = max(result["maximum_support_error_m"], (left_centre-origin-shaft*m.BLOCK_GRIP).length)
            result["maximum_support_angle_degrees"] = max(result["maximum_support_angle_degrees"], math.degrees(left_axis.angle(shaft)))
            result["maximum_opposed_palm_error"] = max(result["maximum_opposed_palm_error"], 1.+palms["L"].dot(palms["R"]))
            for side in ("L", "R"):
                lower, hand = (bones[n+"."+side] for n in ("forearm", "hand"))
                result["maximum_guard_wrist_degrees"] = max(result["maximum_guard_wrist_degrees"], math.degrees((hand.head-lower.head).angle(hand.tail-hand.head)))
        clearance = builder.weapon_clearance()
        result["minimum_body_clearance_m"] = min(result["minimum_body_clearance_m"], clearance["body_gap_m"])
        native = builder.native_right_arm
        # The retained Block/Impact R chain predates the strike-only native
        # envelope. Keep its established arm/core gate; the new Break arms
        # also satisfy the stricter native envelope.
        builder.native_right_arm = native and label == "CombatGuardBreak"
        try: builder.weapon_anatomy(f"{label}@{seconds:.5f}", 65.)
        finally: builder.native_right_arm = native
    print("RAISED GUARD EXPORTED PROOF "+label+" "+json.dumps(result), flush=True)
    if result["maximum_support_error_m"] > .0015 or result["maximum_support_angle_degrees"] > 1. or \
        result["maximum_opposed_palm_error"] > .001 or result["maximum_right_dock_error_m"] > .0001 or \
        result["maximum_guard_wrist_degrees"] > 35.01 or result["minimum_body_clearance_m"] < .004 or \
        result["maximum_released_left_wrist_degrees"] > 65.01 or result["minimum_left_arm_clearance_m"] < .005 or \
        result["maximum_elbow_speed_m_s"] > 6.01:
        raise ValueError("Raised guard lost its physical palm contact: "+json.dumps(result))
    return result


def build(output, source_output):
    global HELD_SHAFT_ROLL_DEGREES
    output.mkdir(parents=True, exist_ok=True)
    source_output.mkdir(parents=True, exist_ok=True)
    payload = json.loads((m.OUT / "CombatTest3D.json").read_text(encoding="utf8"))
    m.common.ANIMATION_FPS = m.FPS
    # Opponents are unarmed zombies. Their retained fall/rise bank is not a
    # second crowbar authoring target.
    for profile, bank in ((True, "CombatActions"),):
        bpy.ops.wm.open_mainfile(filepath=str(m.SOURCE / (bank+".blend")))
        source = r._builder(m, bpy.data.objects["RIG_Player"], profile)
        data = payload["actions"] if profile else payload["actions"]["npc"]
        for clip in m.bank_clip_specs(data):
            action = bpy.data.actions[clip["name"]]
            source.result.actions[clip["name"]] = m.common.ActionRecord(action=action, category=action.get("bp_category", "combat"),
                duration_seconds=clip["duration_seconds"], loop=clip["loop"], source_frame_count=round(clip["duration_seconds"]*m.FPS), source_fps=m.FPS)
        retarget_guard = abs(data["holding"].get("right_grip_station_m", 0.)-RIGHT_GRIP_STATION) > 1.e-6 or \
            any(name not in data.get("simple_strike", {}).get("clips", {}) for name in GUARD_NAMES)
        replaced = NAMES if retarget_guard else STRIKE_NAMES
        preserved = tuple(name for name in source.result.actions if name not in replaced or name == "CombatReady")
        signature = m.action_curve_signature(source, preserved)
        source.suspend_mesh_deformation()
        ready_matrices = r._sample(source.result.rig, source.result.actions["CombatReady"].action, 0.)
        source.result.rig.animation_data.action = None
        builder = r._canonical(m, profile)
        ready = r._apply_matrices(builder, ready_matrices)
        HELD_SHAFT_ROLL_DEGREES = data["simple_strike"]["held_shaft_roll_degrees"]
        combat_bat_continuity.install_anatomy(builder, ready, EARLY_SHOULDER_SECONDS)
        activate_physical = configure_weapon_frame(builder)
        stops = author_stops(builder, ready)
        activate_physical()
        print(f"BAT FIXED MOUNT ROLL {HELD_SHAFT_ROLL_DEGREES:.9f}", flush=True)
        poses = combat_bat_continuity.build_poses(builder, stops, LEFT_GRIP_STATION,
            contact_seconds=CONTACT_SECONDS, recoil_seconds=RECOIL_ENTRY_SECONDS)
        poses = [(seconds, weight_transfer(builder, pose, seconds)) for seconds, pose in poses]
        authored = metrics(builder, poses, 100., "CombatAttack")
        builder._reset_pose(); builder._apply_pose(poses[0][1])
        ready_height = builder.hand_weapon_frame()[0].z / builder.scale
        def sampled_pose(seconds):
            last = len(poses)-1
            position = max(0., min(float(last), seconds*100.)); index = min(last-1, int(position))
            return builder.linear_key_pose(poses[index][1], poses[index+1][1], position-index)
        tracks = {}
        for name in STRIKE_NAMES:
            duration = 4. if name == "CombatReady" else 1. if name == "CombatCharge" else ATTACK_SECONDS-RECOIL_ENTRY_SECONDS if name == "CombatRecoil" else ATTACK_SECONDS
            matrices = []
            for frame in range(round(duration*m.FPS)+1):
                seconds = frame/m.FPS
                time = 0. if name == "CombatReady" else SHOULDER_LOAD_SECONDS*seconds if name == "CombatCharge" else RECOIL_ENTRY_SECONDS + seconds if name == "CombatRecoil" else seconds
                pose = sampled_pose(time)
                builder._reset_pose(); builder._apply_pose(pose)
                matrices.append({bone.name: bone.matrix.copy() for bone in builder.result.rig.pose.bones})
            tracks[name] = (duration, matrices)
        bpy.data.objects.remove(builder.result.rig, do_unlink=True)
        bpy.data.objects.remove(builder.result.root, do_unlink=True)
        if retarget_guard: tracks.update(retarget_guard_tracks(source, profile))
        for name, (duration, matrices) in tracks.items():
            if name == "CombatReady": continue
            bpy.data.actions.remove(source.result.actions.pop(name).action)
            keys = [(frame/(len(matrices)-1), r._apply_matrices(source, value)) for frame, value in enumerate(matrices)]
            source._create_action(name, "combat", duration, name in ("CombatReady", "CombatBlock"), len(matrices)-1, m.FPS, keys)
        if signature != m.action_curve_signature(source, preserved):
            raise ValueError("Simple strike changed unrelated actions")
        for clip in data["clips"]:
            if clip["name"] in tracks:
                clip["duration_seconds"] = tracks[clip["name"]][0]
        data.update(windup_seconds=WINDUP_SECONDS, active_seconds=ACTIVE_SECONDS,
            recovery_seconds=RECOVERY_SECONDS)
        data["animation_signature"] = m.action_curve_signature(source, tuple(name for name, _, _ in m.SHARED_CLIPS))
        data["base_action_signature"] = m.action_curve_signature(source, ("CombatReady", "CombatAttack", "CombatBlock", "CombatHit"))
        source.restore_mesh_deformation()
        source.result.rig.animation_data.action = source.result.actions["CombatReady"].action
        bpy.context.scene.frame_set(0)
        m.common.export_animation_fbx(output / (bank+".fbx"), source.result)
        m.common.save_blend(source_output / (bank+".blend"))
        imported, actions = m.import_bank(output / (bank+".fbx"))
        audit = r._canonical(m, profile)
        combat_bat_continuity.install_anatomy(audit, ready, EARLY_SHOULDER_SECONDS)
        configure_weapon_frame(audit, physical=True)
        certificates = {} if retarget_guard else {name: data["simple_strike"]["clips"][name] for name in GUARD_NAMES}
        for name, (duration, _) in tracks.items():
            if name == "CombatReady":
                matrices = r._sample(imported, actions[name], 0.)
                imported.animation_data.action = None
                pose = r._apply_matrices(audit, matrices)
                certificate = metrics(audit, [(0., pose)], 200., name)
                origin, rotation = audit.weapon_frame()
                shaft = rotation @ Vector((0., 1., 0.))
                tip = origin + rotation @ Vector((0., .570, .145))
                bones = audit.result.rig.pose.bones
                elbow_height = bones["forearm.L"].head.z-bones["upper_arm.L"].head.z
                if origin.z < 1.15 or shaft.z < .70 or -tip.x < .12 or elbow_height > .08:
                    raise ValueError(f"Raised right Ready lost its natural stance: grip={origin.z}, up={shaft.z}, right={-tip.x}, elbow={elbow_height}")
                certificate.update(grip_height_m=audit.hand_weapon_frame()[0].z, shaft_up_dot=shaft.z,
                    tip_right_offset_m=-tip.x, left_elbow_above_shoulder_m=elbow_height)
                certificates[name] = certificate
                continue
            if name in ("CombatReleaseLight", "CombatReleaseHeavy"):
                certificates[name] = dict(certificates["CombatAttack"])
                continue
            imported_poses = []
            for frame in range(round(duration*200)+1):
                matrices = r._sample(imported, actions[name], frame/200.)
                imported.animation_data.action = None
                imported_poses.append((frame/200., r._apply_matrices(audit, matrices)))
            certificates[name] = guard_metrics(audit, imported_poses, name) if name in GUARD_NAMES else metrics(audit, imported_poses, 200., name)
        certificates["CombatAttack"]["maximum_active_metal_chord_error_m"] = metal_chord_error(audit, imported, actions["CombatAttack"])
        holding = data["holding"]
        holding.update(right_grip_station_m=RIGHT_GRIP_STATION, ready_grip_offset_m=LEFT_GRIP_STATION, block_grip_offset_m=m.BLOCK_GRIP,
            minimum_ready_grip_height_m=ready_height, maximum_ready_grip_height_m=ready_height,
            maximum_authored_left_contact_error_m=authored["maximum_support_error_m"],
            maximum_left_axis_error=1.-math.cos(math.radians(max(value["maximum_support_angle_degrees"] for value in certificates.values()))),
            maximum_supported_left_axis_degrees=12.,
            runtime_contract="independent left palm on the cylinder dock; contact-axis correction <=12 degrees; opposed palms apply only to guard")
        holding["breathing_grip_travel_m"]["CombatReady"] = 0.
        holding["opposed_palm_scope"] = "guard"
        holding["block_hook_clearance_m"] = m.CROWBAR_PATH[1][1]-m.BLOCK_GRIP-holding["left_hand_shaft_extent_m"]
        data["simple_strike"] = dict(anatomy_version=4, contact_sample_hz=CONTACT_SAMPLE_HZ, source_sha256=SOURCE_HASH, unchanged_action_signature=signature,
            fbx_sha256=hashlib.sha256((output/(bank+".fbx")).read_bytes()).hexdigest(),
            two_handed=True, right_grip_station_m=RIGHT_GRIP_STATION, support_grip_metres=LEFT_GRIP_STATION,
            relative_hand_spacing_m=BAT_GRIP, held_shaft_roll_degrees=HELD_SHAFT_ROLL_DEGREES,
            blended_ready_endpoints=True,
            windup_seconds=WINDUP_SECONDS, active_seconds=ACTIVE_SECONDS, recovery_seconds=RECOVERY_SECONDS,
            contact_seconds=CONTACT_SECONDS, shoulder_load_seconds=SHOULDER_LOAD_SECONDS, recoil_entry_seconds=RECOIL_ENTRY_SECONDS,
            centre_seconds=CENTRE_SECONDS, overrun_seconds=OVERRUN_SECONDS,
            brake_end_seconds=BRAKE_END_SECONDS,
            early_shoulder_seconds=EARLY_SHOULDER_SECONDS,
            authored_stops=[stop["seconds"] for stop in stops], clips=certificates)
        data["simple_strike"]["spatial_curve_stops"] = [stop["curve_seconds"] for stop in stops]
        data.pop("forehand_anatomy", None)
        print("TWO HAND STRIKE EXPORTED PROOF " + bank + " " + json.dumps(data["simple_strike"]), flush=True)
    (output / "CombatTest3D.json").write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--source-dir", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    build(args.output_dir.resolve(), args.source_dir.resolve())
