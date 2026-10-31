"""Bounded offline continuity selection for one authored two-hand bat swing.

The right arm and torso retain ordinary FK interpolation. The left palm meets
the same cylinder at every sample; a finite contact graph picks one continuous
elbow branch and returns to its exact initial pose. This has no runtime state.
"""
from __future__ import annotations

import math

from mathutils import Matrix, Quaternion, Vector

SAMPLE_FPS = 100
MAX_LAYER_NODES = 2048
# Leave room for curved bone interpolation at the 200 Hz FBX audit; the final
# physical ceiling remains six metres per second for every exported clip.
MAX_ELBOW_STEP = 5.8 / SAMPLE_FPS
SUPPORT_RADIAL_RESERVE = 24.5
MAX_EARLY_SHOULDER_TWIST = 75.


def arm_state(builder, side, wrist=None, elbow=None):
    bones = builder.result.rig.pose.bones
    upper, lower, hand = (bones[n+"."+side] for n in ("upper_arm", "forearm", "hand"))
    shoulder = upper.head.copy()
    candidate = wrist is not None
    wrist = hand.head.copy() if wrist is None else wrist
    elbow = lower.head.copy() if elbow is None else elbow
    chest = bones["chest"].matrix.to_quaternion()
    if candidate:
        direction = (elbow-shoulder).normalized()
        normal = (wrist-elbow).normalized().cross(direction).normalized()
        rest_direction = (upper.bone.tail_local-upper.bone.head_local).normalized()
        rest_normal = rest_direction.cross(Vector((0., .85, -.15)).normalized()).normalized()
        def basis(axis, normal):
            normal = (normal-axis*normal.dot(axis)).normalized()
            return Matrix((axis, normal.cross(axis), normal)).transposed()
        rotation = (basis(direction, normal) @ basis(rest_direction, rest_normal).transposed() @ upper.bone.matrix_local.to_3x3()).to_quaternion()
    else:
        rotation = upper.matrix.to_quaternion()
    local = chest.inverted() @ rotation
    reference = builder.bat_ready_shoulders[side]
    delta = local @ reference.inverted()
    axis = reference @ Vector((0., 1., 0.))
    vector = Vector((delta.x, delta.y, delta.z))
    projected = axis*vector.dot(axis)
    twist = Quaternion((delta.w, projected.x, projected.y, projected.z)).normalized()
    twist_degrees = math.degrees(2.*math.acos(min(1., abs(twist.w))))
    shoulder_line = (bones["upper_arm.L"].head-bones["upper_arm.R"].head).normalized()
    forward = shoulder_line.cross(Vector((0., 0., 1.))).normalized()
    return local, twist_degrees, elbow.z-shoulder.z, (elbow-shoulder).dot(forward)


def install_anatomy(builder, ready, early_seconds=.15):
    builder._reset_pose(); builder._apply_pose(ready)
    bones = builder.result.rig.pose.bones
    chest = bones["chest"].matrix.to_quaternion()
    builder.bat_ready_shoulders = {side: chest.inverted() @ bones["upper_arm."+side].matrix.to_quaternion() for side in ("L", "R")}
    builder.bat_ready_body = {name: bones[name].matrix.to_quaternion() for name in ("pelvis", "chest")}
    builder.bat_ready_locations = {name: bones[name].head.copy() for name in ("pelvis", "foot.L", "foot.R")}
    builder.bat_ready_shoulder_line = bones["upper_arm.L"].head-bones["upper_arm.R"].head
    builder.bat_ready_shoulder_line.z = 0.
    builder.bat_ready_shoulder_line.normalize()
    builder.bat_seconds = 0.
    builder.bat_early_seconds = early_seconds
    original = builder.grip_elbow
    def anatomical(side, wrist, rotation, preferred=None, **kwargs):
        requested = kwargs.get("candidate_step_degrees") is not None
        kwargs["candidate_step_degrees"] = kwargs.get("candidate_step_degrees") or 10.
        choices = original(side, wrist, rotation, preferred, **kwargs) or []
        # A cross-body upper-arm swing changes the Ready-relative twist; only
        # the early lift is bounded absolutely. Every phase keeps its elbow
        # plane and the graph separately bounds shoulder angular velocity.
        limit = (MAX_EARLY_SHOULDER_TWIST if side == "L" else 100.) if builder.bat_seconds <= builder.bat_early_seconds else math.inf
        accepted = [choice for choice in choices if
            (lambda state: state[1] <= limit and state[2] <= (.02 if side == "L" else -.10 if builder.bat_seconds >= .30 else .02) and state[3] >= (.01 if side == "L" else -.10))(arm_state(builder, side, wrist, choice[2]))]
        return accepted if requested else min(accepted, key=lambda choice: choice[0], default=None)
    builder.grip_elbow = anatomical


def _left_candidates(builder, pose, grip_offset, frame, elbow_step=10.):
    builder._reset_pose(); builder._apply_pose(pose)
    rig = builder.result.rig
    hand = rig.pose.bones["hand.L"]
    rest = hand.bone
    centre, rest_axis, _ = builder.hand_frame("L")
    origin, bar = builder.weapon_frame()
    axis = bar @ Vector((0., 1., 0.))
    initial = hand.matrix.to_quaternion()
    delta = initial.to_matrix() @ rest.matrix_local.to_3x3().inverted()
    initial = (delta @ rest_axis).rotation_difference(axis) @ initial
    shoulder = rig.pose.bones["upper_arm.L"].head.copy()
    pole = Vector((-.6, -.5, .3))
    found = {}
    for degrees in range(-180, 181, 6):
        base = (Quaternion(axis, math.radians(degrees)) @ initial).to_matrix()
        base_delta = base @ rest.matrix_local.to_3x3().inverted()
        orientations = [(base, 0.)]
        relief_start, relief_end = builder.bat_axis_relief_seconds
        if relief_start <= frame/SAMPLE_FPS <= relief_end:
            # The shared runtime cylinder contact accepts up to twelve degrees
            # of palm-axis correction. Use only eight through the moving arc,
            # while keeping the palm centre exactly on its 85 mm authored dock.
            for transverse in (base @ Vector((0.,1.,0.)), base_delta @ builder.hand_frame("L")[2]):
                for tilt in (-8., 8.):
                    orientations.append(((Quaternion(transverse.normalized(), math.radians(tilt)).to_matrix() @ base), abs(tilt)))
        for rotation, tilt in orientations:
            delta = rotation @ rest.matrix_local.to_3x3().inverted()
            wrist = origin + axis * grip_offset - delta @ (centre-rest.head_local)
            choices = builder.grip_elbow("L", wrist, rotation, pole,
                total_limit=65., radial_limit=SUPPORT_RADIAL_RESERVE,
                support_head=True, weapon_check=True, candidate_step_degrees=elbow_step,
                # Dense source keys still curve between samples after FBX import.
                # Keep a reserve above the unchanged 4.9 mm imported core gate.
                arm_clearance=.008)
            for choice in choices or ():
                direction = (wrist-choice[2]).normalized()
                fingers = rotation @ Vector((0., 1., 0.))
                palm = (delta @ builder.hand_frame("L")[2]).normalized()
                flexion = abs(math.degrees(math.atan2(direction.dot(palm), direction.dot(fingers))))
                if flexion > 54.5: continue
                clearance = builder.weapon_clearance(arms={"L": (shoulder, choice[2], wrist)})
                if clearance["body_gap_m"] < .005: continue
                # The support forearm curves between exported 100 Hz keys.
                # Preserve the four-millimetre imported metal/skin clearance.
                if clearance["body_part"].endswith(".L") and clearance["body_gap_m"] < .006: continue
                key = tuple(round(v, 3) for v in choice[2]) + tuple(round(v, 2) for v in wrist)
                # Prefer a relaxed elbow below the shoulder without removing
                # the anatomically valid branches needed by the broad swing.
                height_cost = max(0., choice[2].z-shoulder.z+.08)
                upper_rotation, twist, _, _ = arm_state(builder, "L", wrist, choice[2])
                node = (wrist.copy(), rotation.copy(), choice[3].copy(), choice[2].copy(), choice[1], height_cost, tilt, rotation.to_quaternion(), upper_rotation, twist)
                if key not in found or node[4]+tilt*.05 < found[key][4]+found[key][6]*.05: found[key] = node
    if len(found) > MAX_LAYER_NODES:
        raise ValueError(f"Bat contact layer exceeds its bounded authoring population: {len(found)}")
    return list(found.values())


def _path(layers, start=None, anchors=None, early_frame=15):
    _path.last_gap = None
    costs = [node[4]*.001 + node[5]*5. + node[6]*.01 if start is None or index == start else math.inf
             for index, node in enumerate(layers[0])]
    parents = [None]
    for frame in range(1, len(layers)):
        before, after = layers[frame-1], layers[frame]
        # Exact six-centimetre neighbours; spatial bins avoid comparing every
        # physical contact with distant branches which cannot form an edge.
        bins = {}
        for index, prior in enumerate(before):
            if math.isfinite(costs[index]):
                cell = tuple(math.floor(value/MAX_ELBOW_STEP) for value in prior[3])
                bins.setdefault(cell, []).append(index)
        current, links = [], []
        for node_index, node in enumerate(after):
            if anchors and frame in anchors and node_index != anchors[frame]:
                current.append(math.inf); links.append(None); continue
            best = None
            rotation = node[7]
            cell = tuple(math.floor(value/MAX_ELBOW_STEP) for value in node[3])
            neighbours = (index for x in (-1,0,1) for y in (-1,0,1) for z in (-1,0,1)
                for index in bins.get((cell[0]+x,cell[1]+y,cell[2]+z), ()))
            for index in neighbours:
                prior = before[index]
                squared_travel = (node[3]-prior[3]).length_squared
                if squared_travel > MAX_ELBOW_STEP*MAX_ELBOW_STEP: continue
                travel = math.sqrt(squared_travel)
                wrist_travel = (node[0]-prior[0]).length
                dot = min(1., abs(rotation.dot(prior[7])))
                angle = math.degrees(2.*math.acos(dot))
                shoulder_angle = math.degrees(2.*math.acos(min(1., abs(node[8].dot(prior[8])))))
                if shoulder_angle > (8.5 if frame <= early_frame else 11.5): continue
                value = (costs[index] + travel*10. + wrist_travel*5. +
                         angle*.0002 + shoulder_angle*.02 + node[9]*.001 + node[4]*.001 + node[5]*5. + node[6]*.01, index)
                if best is None or value < best: best = value
            current.append(math.inf if best is None else best[0])
            links.append(None if best is None else best[1])
        if not any(math.isfinite(cost) for cost in current):
            reachable = [before[index][3] for index, cost in enumerate(costs) if math.isfinite(cost)]
            nearest = min(((node[3]-point).length for node in after for point in reachable), default=math.inf)
            print(f"BAT CONTINUITY GAP {frame}/{len(layers)-1}: closest elbow step {nearest:.6f}m, closest shoulder step {min((math.degrees(2.*math.acos(min(1.,abs(node[8].dot(before[index][8]))))) for node in after for index,cost in enumerate(costs) if math.isfinite(cost)), default=math.inf):.3f}deg", flush=True)
            _path.last_gap = frame
            return None
        costs, parents = current, parents+[links]
    end = min(range(len(costs)), key=costs.__getitem__) if start is None else start
    if not math.isfinite(costs[end]): return None
    selected = []
    index = end
    for frame in range(len(layers)-1, -1, -1):
        selected.append(index)
        index = parents[frame][index] if frame else None
    selected.reverse()
    return selected


def build_poses(builder, stops, grip_offset, *, contact_seconds, recoil_seconds):
    if stops[0]["pose"] != stops[-1]["pose"]:
        raise ValueError("The bat swing must share its exact FK Ready endpoints")
    poses, layers = [], []
    # The former fixed 20..45 frame window cut a shortened return in half.
    # Exact Ready endpoints remain singleton zero-tilt contacts.
    builder.bat_axis_relief_seconds = (stops[2]["seconds"], stops[-1]["seconds"])
    sample_count = round(stops[-1]["seconds"]*SAMPLE_FPS)+1
    clock = tuple((stop["seconds"], stop.get("curve_seconds", stop["seconds"])) for stop in stops)
    knots = []
    for index, (_, curve_seconds) in enumerate(clock):
        if not knots or curve_seconds > knots[-1][0]: knots.append((curve_seconds, index))
    keyed = {index: stop["pose"] for index, stop in enumerate(stops)}
    for frame in range(sample_count):
        seconds = frame/SAMPLE_FPS
        builder.bat_seconds = seconds
        if frame == sample_count-1: pose = dict(stops[-1]["pose"])
        else:
            # Shared C1 authoring keeps velocity through the strike landmarks;
            # independent smoothsteps made the hand stop at side and contact.
            for (start, source_start), (end, source_end) in zip(clock, clock[1:]):
                if seconds <= end+1.e-8:
                    curve_seconds = source_start+(source_end-source_start)*(seconds-start)/(end-start)
                    break
            pose = builder.pin_supports(builder.track_pose(knots, keyed, curve_seconds))
        if frame in (0, sample_count-1):
            # Keep the published Ready wrist/elbow branch, rather than letting
            # a different full-swing optimum change the resting stance.
            builder._reset_pose(); builder._apply_pose(pose)
            bones = builder.result.rig.pose.bones
            hand, elbow, shoulder = (bones[n+".L"] for n in ("hand", "forearm", "upper_arm"))
            rotation = hand.matrix.to_3x3()
            wrist_angle = math.degrees((hand.head-elbow.head).angle(hand.tail-hand.head))
            upper_rotation, twist, _, _ = arm_state(builder, "L")
            candidates = [(hand.head.copy(), rotation.copy(), elbow.head-shoulder.head,
                elbow.head.copy(), wrist_angle, max(0.,elbow.head.z-shoulder.head.z+.08), 0., rotation.to_quaternion(), upper_rotation, twist)]
        else:
            candidates = _left_candidates(builder, pose, grip_offset, frame)
        if not candidates:
            raise ValueError(f"No complete anatomical bat contact at {seconds:.3f}s")
        poses.append((seconds, pose)); layers.append(candidates)
        if frame % 10 == 0: print(f"BAT CONTINUITY CANDIDATES {frame}/{sample_count-1} ({len(candidates)})", flush=True)
    if len(layers[0]) != len(layers[-1]):
        raise ValueError("Deterministic Ready contact layers differ at their shared endpoint")
    early_frame = round(builder.bat_early_seconds*SAMPLE_FPS)
    unrestricted = _path(layers, early_frame=early_frame)
    if unrestricted is None and _path.last_gap is not None:
        gap = _path.last_gap
        for frame in range(max(0, gap-3), min(sample_count-1, gap+2)):
            builder.bat_seconds = frame/SAMPLE_FPS
            layers[frame] = _left_candidates(builder, poses[frame][1], grip_offset, frame, elbow_step=5.)
            print(f"BAT CONTINUITY LOCAL REFINE {frame}/{sample_count-1} ({len(layers[frame])})", flush=True)
        unrestricted = _path(layers, early_frame=early_frame)
    if unrestricted is None: raise ValueError("The authored bat arc has no continuous physical left-hand path")
    contact_frame, recoil_frame = round(contact_seconds*SAMPLE_FPS), round(recoil_seconds*SAMPLE_FPS)
    contact = unrestricted[contact_frame]
    contact_node = layers[contact_frame][contact]
    return_contact = min(range(len(layers[recoil_frame])), key=lambda index:
        (layers[recoil_frame][index][3]-contact_node[3]).length_squared +
        (layers[recoil_frame][index][0]-contact_node[0]).length_squared +
        (1.-abs(layers[recoil_frame][index][7].dot(contact_node[7]))))
    terminal = layers[recoil_frame][return_contact]
    if (terminal[3]-contact_node[3]).length > .0001 or \
            math.degrees(terminal[7].rotation_difference(contact_node[7]).angle) > .06:
        raise ValueError("Impact and recoil entry do not share the same physical left grasp")
    anchors = {contact_frame:contact, recoil_frame:return_contact}
    starts = [unrestricted[0]]
    starts.extend(index for index in sorted(range(len(layers[0])), key=lambda i: layers[0][i][4])
                  if index not in starts)
    selected = None
    # One natural initial branch usually closes; the bounded fallback considers
    # only the four best Ready grips, never an unbounded search or relaxed gate.
    for start in starts[:4]:
        selected = _path(layers, start, anchors, early_frame=early_frame)
        if selected is not None: break
    if selected is None: raise ValueError("No physical bat path returns to its exact initial Ready grasp")
    for frame, (seconds, pose) in enumerate(poses):
        builder._reset_pose(); builder._apply_pose(pose)
        wrist, rotation, pole = layers[frame][selected[frame]][:3]
        builder.solve_arm("L", wrist, rotation, pole)
        poses[frame] = seconds, builder.snapshot_pose()
    poses[0] = poses[0][0], dict(stops[0]["pose"])
    poses[-1] = poses[-1][0], dict(stops[-1]["pose"])
    builder._reset_pose(); builder._apply_pose(poses[0][1])
    first = {bone.name: bone.matrix.copy() for bone in builder.result.rig.pose.bones}
    builder._reset_pose(); builder._apply_pose(poses[-1][1])
    for bone in builder.result.rig.pose.bones:
        if (bone.matrix.translation-first[bone.name].translation).length > .0001 or \
            math.degrees(bone.matrix.to_quaternion().rotation_difference(first[bone.name].to_quaternion()).angle) > .06:
            raise ValueError(f"Bat Ready endpoint does not match its initial posed bone: {bone.name}")
    # Serialize and sample both endpoints from one identical BonePose mapping.
    # Their FK/cylinder frames already agree; this preserves exact Ready bytes.
    poses[-1] = poses[-1][0], dict(poses[0][1])
    print("BAT CONTINUITY CLOSED", flush=True)
    return poses
