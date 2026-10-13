"""Bounded offline continuity selection for one authored two-hand bat swing.

The right arm and torso retain ordinary FK interpolation. The left palm meets
the same cylinder at every sample; a finite contact graph picks one continuous
elbow branch and returns to its exact initial pose. This has no runtime state.
"""
from __future__ import annotations

import math

from mathutils import Quaternion, Vector

SAMPLE_FPS = 100
SAMPLE_COUNT = 129
MAX_LAYER_NODES = 2048
# Leave room for curved bone interpolation at the 200 Hz FBX audit; the final
# physical ceiling remains six metres per second for every exported clip.
MAX_ELBOW_STEP = 5.8 / SAMPLE_FPS
SUPPORT_RADIAL_RESERVE = 24.5


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
        if 44 <= frame <= 80:
            # The shared runtime cylinder contact accepts up to twelve degrees
            # of palm-axis correction. Use only eight, locally through the arc,
            # while keeping the palm centre exactly on its 85 mm authored dock.
            for transverse in (base @ Vector((0.,1.,0.)), base_delta @ builder.hand_frame("L")[2]):
                for tilt in (-8., 8.):
                    orientations.append(((Quaternion(transverse.normalized(), math.radians(tilt)).to_matrix() @ base), abs(tilt)))
        for rotation, tilt in orientations:
            delta = rotation @ rest.matrix_local.to_3x3().inverted()
            wrist = origin + axis * grip_offset - delta @ (centre-rest.head_local)
            choices = builder.grip_elbow("L", wrist, rotation, pole,
                total_limit=65., radial_limit=SUPPORT_RADIAL_RESERVE,
                support_head=True, weapon_check=True, candidate_step_degrees=elbow_step)
            for choice in choices or ():
                if 63 <= frame <= 80 and choice[2].z > shoulder.z + .10: continue
                direction = (wrist-choice[2]).normalized()
                fingers = rotation @ Vector((0., 1., 0.))
                palm = (delta @ builder.hand_frame("L")[2]).normalized()
                flexion = abs(math.degrees(math.atan2(direction.dot(palm), direction.dot(fingers))))
                if flexion > 54.5: continue
                clearance = builder.weapon_clearance(arms={"L": (shoulder, choice[2], wrist)})
                if clearance["body_gap_m"] < .005: continue
                key = tuple(round(v, 3) for v in choice[2]) + tuple(round(v, 2) for v in wrist)
                height_cost = max(0., choice[2].z-shoulder.z)
                node = (wrist.copy(), rotation.copy(), choice[3].copy(), choice[2].copy(), choice[1], height_cost, tilt, rotation.to_quaternion())
                if key not in found or node[4]+tilt*.05 < found[key][4]+found[key][6]*.05: found[key] = node
    if len(found) > MAX_LAYER_NODES:
        raise ValueError(f"Bat contact layer exceeds its bounded authoring population: {len(found)}")
    return list(found.values())


def _path(layers, start=None, anchors=None):
    _path.last_gap = None
    costs = [node[4]*.001 + node[5]*5. + node[6]*.01 if start is None or index == start else math.inf
             for index, node in enumerate(layers[0])]
    parents = [None]
    for frame in range(1, SAMPLE_COUNT):
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
                value = (costs[index] + travel*10. + wrist_travel*5. +
                         angle*.0002 + node[4]*.00001 + node[5]*5. + node[6]*.01, index)
                if best is None or value < best: best = value
            current.append(math.inf if best is None else best[0])
            links.append(None if best is None else best[1])
        if not any(math.isfinite(cost) for cost in current):
            reachable = [before[index][3] for index, cost in enumerate(costs) if math.isfinite(cost)]
            nearest = min(((node[3]-point).length for node in after for point in reachable), default=math.inf)
            print(f"BAT CONTINUITY GAP {frame}/128: closest elbow step {nearest:.6f}m", flush=True)
            _path.last_gap = frame
            return None
        costs, parents = current, parents+[links]
    end = min(range(len(costs)), key=costs.__getitem__) if start is None else start
    if not math.isfinite(costs[end]): return None
    selected = []
    index = end
    for frame in range(SAMPLE_COUNT-1, -1, -1):
        selected.append(index)
        index = parents[frame][index] if frame else None
    selected.reverse()
    return selected


def build_poses(builder, stops, grip_offset):
    if stops[0]["pose"] != stops[-1]["pose"]:
        raise ValueError("The bat swing must share its exact FK Ready endpoints")
    poses, layers = [], []
    for frame in range(SAMPLE_COUNT):
        seconds = frame/SAMPLE_FPS
        if frame == SAMPLE_COUNT-1: pose = dict(stops[-1]["pose"])
        else:
            a, b = next((a, b) for a, b in zip(stops, stops[1:]) if seconds <= b["seconds"])
            weight = (seconds-a["seconds"])/(b["seconds"]-a["seconds"])
            weight = weight*weight*(3.-2.*weight)
            pose = builder.pin_supports(builder.linear_key_pose(a["pose"], b["pose"], weight))
        candidates = _left_candidates(builder, pose, grip_offset, frame)
        if not candidates:
            raise ValueError(f"No complete anatomical bat contact at {seconds:.3f}s")
        poses.append((seconds, pose)); layers.append(candidates)
        if frame % 10 == 0: print(f"BAT CONTINUITY CANDIDATES {frame}/128 ({len(candidates)})", flush=True)
    if len(layers[0]) != len(layers[-1]):
        raise ValueError("Deterministic Ready contact layers differ at their shared endpoint")
    unrestricted = _path(layers)
    if unrestricted is None and _path.last_gap is not None:
        gap = _path.last_gap
        for frame in range(max(0, gap-3), min(SAMPLE_COUNT, gap+2)):
            layers[frame] = _left_candidates(builder, poses[frame][1], grip_offset, frame, elbow_step=5.)
            print(f"BAT CONTINUITY LOCAL REFINE {frame}/128 ({len(layers[frame])})", flush=True)
        unrestricted = _path(layers)
    if unrestricted is None: raise ValueError("The authored bat arc has no continuous physical left-hand path")
    contact = unrestricted[63]
    contact_node = layers[63][contact]
    return_contact = min(range(len(layers[80])), key=lambda index:
        (layers[80][index][3]-contact_node[3]).length_squared +
        (layers[80][index][0]-contact_node[0]).length_squared +
        (1.-abs(layers[80][index][7].dot(contact_node[7]))))
    terminal = layers[80][return_contact]
    if (terminal[3]-contact_node[3]).length > .0001 or \
            math.degrees(terminal[7].rotation_difference(contact_node[7]).angle) > .06:
        raise ValueError("Impact and recoil entry do not share the same physical left grasp")
    anchors = {63:contact, 80:return_contact}
    starts = [unrestricted[0]]
    starts.extend(index for index in sorted(range(len(layers[0])), key=lambda i: layers[0][i][4])
                  if index not in starts)
    selected = None
    # One natural initial branch usually closes; the bounded fallback considers
    # only the four best Ready grips, never an unbounded search or relaxed gate.
    for start in starts[:4]:
        selected = _path(layers, start, anchors)
        if selected is not None: break
    if selected is None: raise ValueError("No physical bat path returns to its exact initial Ready grasp")
    for frame, (seconds, pose) in enumerate(poses):
        builder._reset_pose(); builder._apply_pose(pose)
        wrist, rotation, pole, _, _, _, _, _ = layers[frame][selected[frame]]
        builder.solve_arm("L", wrist, rotation, pole)
        poses[frame] = seconds, builder.snapshot_pose()
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
