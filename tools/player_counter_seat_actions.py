"""Hero V2's measured side access to the bar's compact counter stools."""
from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector


ROOT = Path(__file__).resolve().parents[1]
TRANSFER_NAMES = tuple(f"CounterSeat{phase}{side}" for side in ("Right", "Left") for phase in ("Enter", "Exit"))
NAMES = (*TRANSFER_NAMES, "CounterSeatLoop")
DRINK_NAMES = ("BarDrinkPickupEnter", "BarDrinkSipLoop", "BarDrinkReturnExit")
LEG_BONES = tuple(f"{bone}.{side}" for side in ("L", "R") for bone in ("thigh", "shin", "foot"))
TIMES = (0., .18, .40, .58, .68, .82, 1.)
SEAT_TOP = .8175
SEAT_CLEARANCE = .105
RAISED_CLEARANCE = .150
SIDE_OFFSET = .45
BAR_THIGH_LOWER_DEGREES = 6.
BAR_THIGH_SPLAY_DEGREES = 20.
BAR_SHIN_TUCK_DEGREES = 5.
RAISED_EXTRA_KNEE_DOWN_DEGREES = 8.


def smooth(value):
    value = min(1., max(0., value))
    return value * value * (3. - 2. * value)


def interval(progress, start, end):
    return smooth((progress-start)/(end-start))


def pelvis_path(progress, sign):
    points = ((0., .875, .27), (sign*SIDE_OFFSET, .875, .36),
        (sign*SIDE_OFFSET, .875, .91), (sign*SIDE_OFFSET, SEAT_TOP+RAISED_CLEARANCE, .91),
        (sign*SIDE_OFFSET, SEAT_TOP+RAISED_CLEARANCE, .91), (0., SEAT_TOP+RAISED_CLEARANCE, .91),
        (0., SEAT_TOP+SEAT_CLEARANCE, .91))
    if progress >= 1.: return Vector(points[-1])
    for index in range(1, len(TIMES)):
        if progress <= TIMES[index]:
            return Vector(points[index-1]).lerp(Vector(points[index]), interval(progress, TIMES[index-1], TIMES[index]))
    return Vector(points[-1])


def to_source(value):
    return Vector((-value.x, -value.z, value.y))


def to_unity(value):
    return Vector((-value.x, value.z, -value.y))


def turn(progress, sign):
    return sign * math.pi*.5 * interval(progress, 0., .18) * (1.-interval(progress, .40, .58))


def snapshot(result):
    return {bone.name: (bone.location.copy(), bone.rotation_quaternion.copy(), bone.scale.copy()) for bone in result.rig.pose.bones}


def restore_mix(result, first, last, weight):
    for bone in result.rig.pose.bones:
        a, b = first[bone.name], last[bone.name]
        bone.location = a[0].lerp(b[0], weight)
        bone.rotation_quaternion = a[1].slerp(b[1], weight)
        bone.scale = a[2].lerp(b[2], weight)
    bpy.context.view_layer.update()


def aim(bone, direction, child_position=None):
    current = (bone.tail if child_position is None else child_position)-bone.head
    rotation = current.rotation_difference(direction) @ bone.matrix.to_quaternion()
    bone.matrix = Matrix.Translation(bone.head.copy()) @ rotation.to_matrix().to_4x4()
    bpy.context.view_layer.update()


def solve_chain(upper, lower, end, target, rotation, hint):
    origin = upper.head.copy()
    a, b = (lower.head-origin).length, (end.head-lower.head).length
    delta = target-origin
    distance = min(a+b-.00001, max(abs(a-b)+.00001, delta.length))
    axis = delta.normalized()
    pole = hint-origin
    bend = pole-axis*pole.dot(axis)
    if bend.length < 1e-7: bend = axis.cross(Vector((1., 0., 0.)))
    bend.normalize()
    along = (a*a-b*b+distance*distance)/(2.*distance)
    knee = origin + axis*along + bend*math.sqrt(max(0., a*a-along*along))
    aim(upper, knee-origin, lower.head.copy())
    aim(lower, target-lower.head, end.head.copy())
    end.matrix = Matrix.Translation(end.head.copy()) @ rotation.to_matrix().to_4x4()
    bpy.context.view_layer.update()


def lower_bar_thighs(result, pitch=BAR_THIGH_LOWER_DEGREES, splay=BAR_THIGH_SPLAY_DEGREES):
    """The bar's lip is lower than a bus backrest: keep both knees beneath it."""
    for side in ("L", "R"):
        rotation = Quaternion(Vector((0., 0., 1.)), math.radians(splay)*(1 if side == "L" else -1)) @ Quaternion(
            Vector((1., 0., 0.)), math.radians(pitch))
        thigh = result.rig.pose.bones["thigh."+side]
        thigh.matrix = Matrix.Translation(thigh.head.copy()) @ (
            rotation @ thigh.matrix.to_quaternion()).to_matrix().to_4x4()
        bpy.context.view_layer.update()
        shin = result.rig.pose.bones["shin."+side]
        axis = Quaternion(Vector((0., 0., 1.)), math.radians(splay)*(1 if side == "L" else -1)) @ Vector((1., 0., 0.))
        shin.matrix = Matrix.Translation(shin.head.copy()) @ (Quaternion(axis, math.radians(BAR_SHIN_TUCK_DEGREES)) @
            shin.matrix.to_quaternion()).to_matrix().to_4x4()
        bpy.context.view_layer.update()


def replace_leg_tracks(action, pose, common, bones=LEG_BONES):
    values = {}
    for name in bones:
        location, rotation, scale = pose[name]
        for field, data in (("location", location), ("rotation_quaternion", rotation), ("scale", scale)):
            values[f'pose.bones["{name}"].{field}'] = data
    for curve in common.iter_action_fcurves(action):
        if curve.data_path not in values: continue
        value = values[curve.data_path][curve.array_index]
        for key in curve.keyframe_points:
            key.co.y = key.handle_left.y = key.handle_right.y = value
        curve.update()


def _pose(builder, common, neutral, seated, progress, sign, feet, hand_frames):
    result, rig = builder.result, builder.result.rig
    fold = interval(progress, .48, .58)
    restore_mix(result, neutral, seated, fold)
    leg_rotations = {side: rig.pose.bones["thigh."+side].matrix.to_quaternion().copy() for side in ("L", "R")}
    pelvis = rig.pose.bones["pelvis"]
    yaw = turn(progress, sign)
    source_yaw = Quaternion(Vector((0., 0., 1.)), -yaw)
    pelvis.matrix = Matrix.Translation(pelvis.head.copy()) @ (source_yaw @ pelvis.matrix.to_quaternion()).to_matrix().to_4x4()
    bpy.context.view_layer.update()
    leg_turn = 1.-interval(progress, .40, .48)
    for side in ("L", "R"):
        thigh = rig.pose.bones["thigh."+side]
        rotated = Quaternion(Vector((0., 0., 1.)), -yaw*leg_turn) @ leg_rotations[side]
        # While the pelvis is above its resting height, keep the knees lower
        # than the counter's underside; unfold only while lowering onto wood.
        tuck = math.radians(RAISED_EXTRA_KNEE_DOWN_DEGREES)*fold*(1.-interval(progress, .82, 1.))
        rotated = Quaternion(Vector((1., 0., 0.)), tuck) @ rotated
        thigh.matrix = Matrix.Translation(thigh.head.copy()) @ rotated.to_matrix().to_4x4()
        bpy.context.view_layer.update()
    hip_world = pelvis_path(progress, sign)
    alignment = to_source(hip_world)-pelvis.head
    if 0. < progress < .48:
        # Small alternating side steps keep a supporting boot rather than
        # sliding both soles with the pelvis. Narrow-space turns use the toes.
        for side in ("L", "R"):
            foot = rig.pose.bones["foot."+side]
            folded_ankle = to_unity(foot.head+alignment)
            folded_rotation = foot.matrix.to_quaternion()
            leading = (side == "R") == (sign > 0)
            intervals = ((0., .05), (.13, .21), (.29, .37)) if leading else ((.05, .13), (.21, .29), (.37, .45))
            previous = 0.
            move_start = move_end = None
            for start, end in intervals:
                if progress < start: break
                if progress <= end:
                    move_start, move_end = start, end
                    break
                previous = end
            def landing(at):
                at = min(at+.04, .40) if at > 0. else 0.
                angle = turn(at, sign)
                yaw_q = Quaternion(Vector((0., 1., 0.)), angle)
                offset = yaw_q @ feet[side]["offset"]
                point = pelvis_path(at, sign)+offset
                return point, angle
            if move_start is None:
                ankle, foot_yaw = landing(previous)
                lift = 0.
            else:
                start, start_yaw = landing(previous)
                end, end_yaw = landing(move_end)
                amount = interval(progress, move_start, move_end)
                ankle = start.lerp(end, amount)
                foot_yaw = start_yaw+(end_yaw-start_yaw)*amount
                lift = math.sin(math.pi*amount)*.085
            toe = math.radians(20.) * interval(progress, 0., .04)
            rotation = Quaternion(Vector((0., 0., 1.)), -foot_yaw) @ Quaternion(Vector((1., 0., 0.)), toe) @ feet[side]["rotation"]
            sole_offset = min((rotation @ point).z for point in feet[side]["points"])
            ankle.y = feet[side]["floor"]-sole_offset+lift
            release = interval(progress, .40, .48)
            ankle = ankle.lerp(folded_ankle, release)
            rotation = rotation.slerp(folded_rotation, release)
            target = to_source(ankle)-alignment
            leg_yaw = Quaternion(Vector((0., 0., 1.)), -yaw*leg_turn)
            hint = rig.pose.bones["thigh."+side].head + leg_yaw @ Vector((0., -.5, -.20))
            solve_chain(rig.pose.bones["thigh."+side], rig.pose.bones["shin."+side], foot,
                        target, rotation, hint)

    if .42 <= progress < 1.:
        # The seated shin folds beneath the real counter lip. Interpolating
        # only its rotations swings a long boot toe through the cabinet first;
        # author the same ankle arc with its complete boot shell behind it.
        for side in ("L", "R"):
            foot = rig.pose.bones["foot."+side]
            rotation = foot.matrix.to_quaternion()
            ankle = to_unity(foot.head+alignment)
            front = max(to_unity(rotation @ point).z for point in feet[side]["points"])
            correction = max(0., ankle.z+front-1.385)*(1.-interval(progress, .82, 1.))
            lift = math.sin(math.pi*interval(progress, .42, .56))*.12 if progress < .56 else 0.
            if correction > 0. or lift > 0.:
                ankle.z -= correction
                ankle.y += lift
                upper, lower = (rig.pose.bones[name+"."+side] for name in ("thigh", "shin"))
                hint = lower.head.copy()
                solve_chain(upper, lower, foot, to_source(ankle)-alignment, rotation, hint)

    # The near arm braces the actual counter lip during the lift and sideways
    # transfer. Both feet can then fold clear before the butt crosses the seat.
    brace = interval(progress, .30, .40)*(1.-interval(progress, .88, 1.))
    if brace > 0.:
        far_side = "R" if sign > 0 else "L"
        upper, lower, hand = (rig.pose.bones[name+"."+far_side] for name in ("upper_arm", "forearm", "hand"))
        tucked = to_source(hip_world+Vector((sign*.22, .40, .16)))-alignment
        tucked = hand.head.lerp(tucked, brace)
        hint = upper.head+Vector((-sign*.30, -.10, -.20))
        solve_chain(upper, lower, hand, tucked, hand.matrix.to_quaternion(), hint)
        side = "L" if sign > 0 else "R"
        upper, lower, hand = (rig.pose.bones[name+"."+side] for name in ("upper_arm", "forearm", "hand"))
        finger, _, palm = hand_frames[side]["frame"]
        distal, normal = Vector((0., -1., 0.)), Vector((0., 0., -1.))
        delta = finger.rotation_difference(distal)
        turned_palm = delta @ palm
        roll = math.atan2(distal.dot(turned_palm.cross(normal)), turned_palm.dot(normal))
        rotation = Quaternion(distal, roll) @ delta @ hand.bone.matrix_local.to_quaternion()
        underside = min((rotation @ point).z for point in hand_frames[side]["points"])
        # Wrist/cuff stays outside the guest lip; the palm and fingers extend
        # onto the top. Putting the wrist itself over the wood embeds its cuff.
        target = to_source(Vector((hip_world.x-sign*.14, 1.028-underside, 1.245)))-alignment
        raised = 1.-(1.-brace)**3
        original = hand.head.copy()
        target = original.lerp(target, brace*brace)
        target.z = original.z+(1.028-underside-alignment.z-original.z)*raised+math.sin(math.pi*brace)*.05
        rotation = hand.matrix.to_quaternion().slerp(rotation, raised)
        shoulder = rig.pose.bones["clavicle."+side]
        # One bounded clavicle aim spends the real shoulder segment when the
        # side-facing arm cannot quite reach the front lip with elbow alone.
        length = (lower.head-upper.head).length+(hand.head-lower.head).length
        shoulder_weight = smooth(((target-upper.head).length/length-.80)/.30)*.40
        shoulder_rotation = shoulder.matrix.to_quaternion()
        aimed_rotation = (shoulder.tail-shoulder.head).rotation_difference(target-shoulder.head) @ shoulder_rotation
        shoulder.matrix = Matrix.Translation(shoulder.head.copy()) @ shoulder_rotation.slerp(
            aimed_rotation, shoulder_weight).to_matrix().to_4x4()
        bpy.context.view_layer.update()
        hint = upper.head + Vector((sign*.15, .1, -.35))
        solve_chain(upper, lower, hand, target, rotation, hint)

    return {name: common.BonePose(location_m=tuple(bone.location/builder.scale),
        rotation_degrees=tuple(math.degrees(v) for v in bone.rotation_quaternion.to_euler("XYZ")),
        scale=tuple(bone.scale)) for name, bone in rig.pose.bones.items()
        if name in (*common.REQUIRED_BONES, *common.REQUIRED_FACE_BONES)}


def build(builder, common):
    import player_hand_frames
    result = builder.result
    for name in NAMES:
        result.actions.pop(name, None)
        old = bpy.data.actions.get(name)
        if old is not None: bpy.data.actions.remove(old)
    sample(result, "Relaxed", 0.)
    neutral = snapshot(result)
    neutral_pelvis = result.rig.pose.bones["pelvis"].head.copy()
    feet = {}
    frames = {}
    for side in ("L", "R"):
        bone = result.rig.pose.bones["foot."+side]
        points = [point-bone.head for part, points in vertices(result) if part.bone == "foot."+side for point in points]
        local = [bone.matrix.to_quaternion().inverted() @ point for point in points]
        feet[side] = {"rotation": bone.matrix.to_quaternion().copy(), "points": local,
            "offset": to_unity(bone.head-neutral_pelvis),
            "floor": min(p.z+bone.head.z for p in points)+(.875-neutral_pelvis.z)}
        hand = result.rig.pose.bones["hand."+side]
        hand_inverse = hand.matrix.to_quaternion().inverted()
        frames[side] = {"frame": player_hand_frames.bind_frame(result, side),
            "points": [hand_inverse @ (point-hand.head) for part, values in vertices(result)
                       if part.bone == "hand."+side for point in values]}
    sample(result, "BusRideLoop", 0.)
    builder.counter_seat_feet = feet
    result.rig.animation_data.action = None
    lower_bar_thighs(result)
    seated = snapshot(result)
    loop_source = result.actions["BusRideLoop"]
    loop = loop_source.action.copy()
    loop.name = "CounterSeatLoop"
    loop["bp_category"] = "counter_seat"
    loop["bp_generator_version"] = "hero_v2_counter_seat_v1"
    # A bar stool supports the hips. Preserve the bus's spine/head/arm breath,
    # while its vehicle sway must not roll the pelvis through the seat rim.
    replace_leg_tracks(loop, seated, common, (*LEG_BONES, "pelvis"))
    result.actions["CounterSeatLoop"] = common.ActionRecord(loop, "counter_seat", 2., True, 16, 8.)
    for name in DRINK_NAMES:
        replace_leg_tracks(result.actions[name].action, seated, common)
    result.rig.animation_data.action = None
    modifiers = [(modifier, modifier.show_viewport) for part in result.parts for modifier in part.obj.modifiers
                 if modifier.type == "ARMATURE"]
    try:
        for modifier, _ in modifiers: modifier.show_viewport = False
        for side, sign in (("Right", 1.), ("Left", -1.)):
            result.rig.animation_data.action = None
            keys = []
            for frame in range(73):
                progress = frame/72
                if frame in (0, 72):
                    restore_mix(result, neutral, seated, float(frame == 72))
                    pose = {name: common.BonePose(location_m=tuple(bone.location/builder.scale),
                        rotation_degrees=tuple(math.degrees(v) for v in bone.rotation_quaternion.to_euler("XYZ")), scale=tuple(bone.scale))
                        for name, bone in result.rig.pose.bones.items() if name in (*common.REQUIRED_BONES, *common.REQUIRED_FACE_BONES)}
                else: pose = _pose(builder, common, neutral, seated, progress, sign, feet, frames)
                keys.append((progress, pose))
            for phase, posed_keys in (("Enter", keys), ("Exit", [(1.-t, pose) for t, pose in reversed(keys)])):
                name = "CounterSeat"+phase+side
                builder._create_action(name, "counter_seat", 3., False, 36, 12., posed_keys)
                result.actions[name].action["bp_generator_version"] = "hero_v2_counter_seat_v1"
                result.actions[name].action["bp_event_count"] = 0
            print("Counter seat authored " + side, flush=True)
    finally:
        for modifier, enabled in modifiers: modifier.show_viewport = enabled


def load(module, config):
    payload = json.loads(module.DEFAULT_MANIFEST.read_text(encoding="utf-8"))
    bpy.ops.wm.open_mainfile(filepath=str(module.DEFAULT_OUTPUT))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    rig.animation_data_create().action = None
    parts = [module.common.PartRecord(bpy.data.objects[row["name"]], row["role"], row["bone"], row["sprite_part"], row["side"])
             for row in payload["parts"]]
    ordered = [name for item in payload["wardrobe"]["items"] for name in item["renderers"]]
    ordered += [name for chain in payload["hair"]["chains"] for name in chain["renderers"]]
    order = {name: index for index, name in enumerate(ordered)}
    parts.sort(key=lambda part: order.get(part.obj.name, len(order)))
    actions = {row["name"]: module.common.ActionRecord(bpy.data.actions[row["name"]], row["category"], row["duration_seconds"],
               row["loop"], row["source_frame_count"], row["source_fps"]) for row in payload["actions"]}
    result = module.common.BuildResult(rig.parent, rig, {}, {}, parts=parts, actions=actions)
    if payload.get("joint_surfaces"):
        result.joint_surfaces = copy.deepcopy(payload["joint_surfaces"])
        result.joint_surfaces.pop("preserved_action_bank", None)
    result.authored_anchors = [obj for obj in bpy.data.objects if obj.name.startswith(
        ("ANCHOR_HandGrip", "ANCHOR_WineBowlGrip", "ANCHOR_SnifterBowlGrip"))]
    builder = module.HeroV2Builder(config, module.DEFAULT_FACE_ATLAS, module.DEFAULT_CLOTHING_ATLAS)
    builder.result = result
    return payload, builder


def vertices(result):
    deps = bpy.context.evaluated_depsgraph_get()
    for part in result.parts:
        evaluated = part.obj.evaluated_get(deps)
        mesh = evaluated.to_mesh()
        try:
            if len(mesh.vertices) != len(part.obj.data.vertices):
                raise RuntimeError(f"Counter-seat skin topology changed: {part.obj.name}")
            yield part, [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
        finally:
            evaluated.to_mesh_clear()


def vertex_arrays(result):
    """Read evaluated skins in bulk; dense validation does not allocate Vectors."""
    deps = bpy.context.evaluated_depsgraph_get()
    for part in result.parts:
        evaluated = part.obj.evaluated_get(deps)
        mesh = evaluated.to_mesh()
        try:
            if len(mesh.vertices) != len(part.obj.data.vertices):
                raise RuntimeError(f"Counter-seat skin topology changed: {part.obj.name}")
            values = np.empty(len(mesh.vertices)*3, dtype=np.float64)
            mesh.vertices.foreach_get("co", values)
            matrix = np.array(evaluated.matrix_world)
            yield part, values.reshape((-1, 3)) @ matrix[:3, :3].T+matrix[:3, 3]
        finally:
            evaluated.to_mesh_clear()


def _solid(name, minimum, maximum, planes):
    return name, np.array(minimum), np.array(maximum), planes


def _convex(name, geometry):
    """Use the actual authored polygon faces, including their relieved rims."""
    points, faces = geometry
    world = np.array(points, dtype=float)
    centre = world.mean(axis=0)
    planes = []
    for face in faces:
        a, b, c = (world[index] for index in face[:3])
        normal = np.cross(b-a, c-a)
        length = np.linalg.norm(normal)
        if length < 1e-10: continue
        normal /= length
        distance = float(normal @ a)
        if normal @ centre > distance: normal, distance = -normal, -distance
        if not any(np.linalg.norm(normal-n) < 1e-7 and abs(distance-d-.0003) < 1e-7 for n, d in planes):
            planes.append((normal, distance-.0003))
    return _solid(name, world.min(axis=0), world.max(axis=0), planes)


def _box(name, minimum, maximum):
    planes = []
    for axis in range(3):
        direction = np.zeros(3); direction[axis] = 1.
        planes.extend(((direction, maximum[axis]-.0003), (-direction, -minimum[axis]-.0003)))
    return _solid(name, minimum, maximum, planes)


def _cylinder(name, x, z, radius, bottom, top):
    planes = [(np.array((0., -1., 0.)), -bottom-.0003), (np.array((0., 1., 0.)), top-.0003)]
    for index in range(32):
        angle = math.tau*index/32
        normal = np.array((math.cos(angle), 0., math.sin(angle)))
        planes.append((normal, normal[0]*x+normal[2]*z+radius-.0003))
    return _solid(name, (x-radius, bottom, z-radius), (x+radius, top, z+radius), planes)


def solids(seat_x):
    import bar_parts as bp
    import interior_kit as kit
    result = []
    for x in (-1.65, -.75, .15, 1.05, 1.95):
        result.extend((_convex(f"stool {x:+.2f} seat", bp.u_cylinder((x, .79, .91), (.48, .0275, .48), sides=14)),
            _convex(f"stool {x:+.2f} stem", bp.u_cylinder((x, .39, .91), (.10, .39, .10), sides=10)),
            _convex(f"stool {x:+.2f} foot", bp.u_cylinder((x, .035, .91), (.34, .035, .34), sides=12))))
    result.extend((_convex("counter top", bp.u_box((.875, .94, 1.95), (6.4, .16, 1.32), .02)),
        _convex("return top", bp.u_box((3.57, .94, 1.47), (1.36, .16, 1.96), .02)),
        _convex("return body", bp.u_box((3.57, .43, 1.55), (1.04, .86, 1.8), .026)),
        _convex("counter footrail", kit.translated(bp.u_rotated(bp.u_cylinder((0., 0., 0.),
            (.10, 2.75, .10), sides=12), (0., 0., 90.)), (.875, .1806, 1.33))),
        _convex("return footrail", kit.translated(bp.u_rotated(bp.u_cylinder((0., 0., 0.),
            (.10, .71, .10), sides=12), (90., 0., 0.)), (2.9252, .1806, 1.43)))))
    for name, position, size, cut, top in (
            ("counter plinth", (.875, .06, 1.95), (5.93, .12, .85), .012, False),
            ("counter body", (.875, .46, 1.95), (5.95, .68, 1.), .012, True),
            ("counter rim", (.875, .83, 1.95), (5.99, .06, 1.04), .0192, True)):
        x, y, z = position; sx, sy, sz = size
        points, faces = kit.chamfered_box((x, z, y), (sx, sz, sy), cut, top=top)
        result.append(_convex(name, ([(x, z, y) for x, y, z in points], faces)))
    for index in range(6):
        x = .875-5.95*.5+.33+(5.3/6)*(index+.5)
        result.append(_convex("counter front panel", bp.u_box((x, .43, 1.44), (5.3/6-.11, .66, .08), .006)))
    for z in (1.08, 1.9):
        result.append(_convex("return front panel", bp.u_box((3.0396, .43, z), (.08, .66, .72), .006)))
    return result


def _clips_triangle(triangle, planes):
    points = list(triangle)
    for normal, distance in planes:
        if not points: return False
        clipped = []
        previous = points[-1]
        previous_distance = float(np.dot(previous, normal)-distance)
        for current in points:
            current_distance = float(np.dot(current, normal)-distance)
            if (current_distance <= 0.) != (previous_distance <= 0.):
                clipped.append(previous+(current-previous)*(previous_distance/(previous_distance-current_distance)))
            if current_distance <= 0.: clipped.append(current)
            previous, previous_distance = current, current_distance
        points = clipped
    return len(points) >= 3 and any(np.linalg.norm(np.cross(points[index]-points[0], points[index+1]-points[0])) > 1e-10
        for index in range(1, len(points)-1))


def validate_seams(result):
    for side in ("Right", "Left"):
        for first, first_t, last, last_t in (
                ("CounterSeatEnter"+side, 0., "Relaxed", 0.),
                ("CounterSeatEnter"+side, 1., "CounterSeatLoop", 0.),
                ("CounterSeatExit"+side, 0., "CounterSeatLoop", 0.),
                ("CounterSeatExit"+side, 1., "Relaxed", 0.)):
            sample(result, first, first_t); expected = snapshot(result)
            sample(result, last, last_t); actual = snapshot(result)
            for bone in expected:
                if (expected[bone][0]-actual[bone][0]).length > 2e-6 or abs(expected[bone][1].normalized().dot(actual[bone][1].normalized())) < 1.-2e-6:
                    raise RuntimeError(f"Counter-seat terminal seam {first}/{last}: {bone}: "
                        f"{list(expected[bone][0])}/{list(actual[bone][0])}, {list(expected[bone][1])}/{list(actual[bone][1])}")
        for progress in (.07, .231, .453, .653, .899):
            sample(result, "CounterSeatEnter"+side, progress); expected = snapshot(result)
            sample(result, "CounterSeatExit"+side, 1.-progress); actual = snapshot(result)
            for bone in expected:
                if (expected[bone][0]-actual[bone][0]).length > 2e-6 or abs(expected[bone][1].normalized().dot(actual[bone][1].normalized())) < 1.-2e-6:
                    raise RuntimeError(f"Counter-seat reverse continuity: {side}/{bone}")


def validate_result(result, density=144, progresses=None):
    """Clip every evaluated skin triangle against the same physical furniture.

    Source meshes, including covered body surfaces, participate; bone and
    renderer AABBs only reject broadphase pairs, never prove clearance.
    """
    previous_action = result.rig.animation_data.action
    validate_seams(result)
    sample(result, "Relaxed", 0.)
    neutral_pelvis = result.rig.pose.bones["pelvis"].head.z
    neutral_sole = min(points[:, 2].min()-neutral_pelvis+.875 for part, points in vertex_arrays(result)
        if part.bone in ("foot.L", "foot.R"))
    triangle_cache = {}
    trials = [("CounterSeat"+phase+side, sign, seat_x, phase == "Exit")
        for side, sign, seat_x in (("Right", 1., -.75), ("Left", -1., .15), ("Right", 1., 1.95))
        for phase in ("Enter", "Exit")]
    trials.append(("CounterSeatLoop", 0., 1.95, False))
    for name, sign, seat_x, reverse in trials:
        obstacles = solids(seat_x)
        lows, highs = np.array([solid[1] for solid in obstacles]), np.array([solid[2] for solid in obstacles])
        for index, progress in enumerate(progresses if progresses is not None else (i/density for i in range(density+1))):
            sample(result, name, progress)
            pelvis = result.rig.matrix_world @ result.rig.pose.bones["pelvis"].head
            path_progress = 1.-progress if reverse else progress
            target = (pelvis_path(path_progress, sign) if sign else Vector((0., SEAT_TOP+SEAT_CLEARANCE, .91)))+Vector((seat_x, 0., 0.))
            soles = []
            for part, points in vertex_arrays(result):
                relative = points-np.array(pelvis)
                world = np.column_stack((-relative[:, 0], relative[:, 2], -relative[:, 1]))+np.array(target)
                minimum, maximum = world.min(axis=0), world.max(axis=0)
                if part.bone in ("foot.L", "foot.R"):
                    soles.append(minimum[1])
                pairs = np.all(maximum >= lows, axis=1)&np.all(minimum <= highs, axis=1)
                for pair in np.flatnonzero(pairs):
                    solid_name, low, high, planes = obstacles[pair]
                    # A strict inside vertex is already a physical intersection.
                    inside = np.ones(len(world), dtype=bool)
                    for normal, distance in planes:
                        inside &= world @ normal < distance
                        if not inside.any(): break
                    if inside.any():
                        vertex = world[np.flatnonzero(inside)[0]]
                        if "counter" in solid_name:
                            print("COUNTER_ARM_DIAGNOSTIC " + json.dumps({bone: list(to_unity(result.rig.pose.bones[bone].head-pelvis)+target)
                                for bone in ("pelvis", "clavicle.L", "upper_arm.L", "forearm.L", "hand.L")}), flush=True)
                        raise RuntimeError(f"{name} p={progress:.5f} {part.obj.name} vertex{vertex.tolist()} inside {solid_name}")
                    if part.obj.name not in triangle_cache:
                        part.obj.data.calc_loop_triangles()
                        triangle_cache[part.obj.name] = np.array([tuple(t.vertices) for t in part.obj.data.loop_triangles])
                    triangles = world[triangle_cache[part.obj.name]]
                    candidates = np.all(triangles.max(axis=1) >= low, axis=1)&np.all(triangles.min(axis=1) <= high, axis=1)
                    for triangle in triangles[candidates]:
                        if _clips_triangle(triangle, planes):
                            raise RuntimeError(f"{name} p={progress:.5f} {part.obj.name} triangle {triangle.tolist()} intersects {solid_name}")
            if min(soles) < -.002:
                raise RuntimeError(f"{name} p={progress:.5f} boot below the authored floor: {min(soles)}")
            if sign and path_progress < .40 and min(soles) > neutral_sole+.025:
                raise RuntimeError(f"{name} p={progress:.5f} both stepping boots lost ground support")
            if index % 24 == 0: print(f"Counter seat skins {name} x={seat_x} {index}/{density}", flush=True)
    result.rig.animation_data.action = previous_action
    return contract()


def contract():
    return {"contract": "counter_seat_side_v1", "source": "all evaluated body/clothing triangles",
        "stool_seat_top_m": SEAT_TOP, "stool_diameter_m": .48, "stool_pitch_m": .9,
        "action_hip_clearance_m": SEAT_CLEARANCE, "raised_hip_clearance_m": RAISED_CLEARANCE,
        "side_offset_m": SIDE_OFFSET, "enter_key_times": list(TIMES),
        "bar_thigh_down_degrees": BAR_THIGH_LOWER_DEGREES, "bar_thigh_splay_degrees": BAR_THIGH_SPLAY_DEGREES,
        "bar_shin_tuck_degrees": BAR_SHIN_TUCK_DEGREES,
        "raised_extra_knee_down_degrees": RAISED_EXTRA_KNEE_DOWN_DEGREES,
        "loop": "CounterSeatLoop", "unchanged_bus_cafe": True}


def refresh(module, config):
    """Publish bar-only takes without regenerating the model or other actions."""
    original, builder = load(module, config)
    result = builder.result
    hashes = (original["face_atlas"]["sha256"], original["texture_bindings"][0]["sha256"], original["bare_skin_atlas"]["sha256"])
    def bind_pose():
        result.rig.animation_data.action = None
        builder._reset_pose()
    def signature():
        # Bone-parented grip anchors expose their armature-relative transform;
        # hash the same bind state rather than whichever take was sampled last.
        bind_pose()
        return module.content_signature(config, result, *hashes)
    if signature() != original["content_signature_sha256"]:
        raise RuntimeError("Production hero source differs from its manifest; refusing a stale counter-seat refresh")
    def upper_signature():
        rows = []
        for name in DRINK_NAMES:
            rows.extend((name, curve.data_path, curve.array_index,
                [(tuple(point.co), point.interpolation) for point in curve.keyframe_points])
                for curve in module.common.iter_action_fcurves(result.actions[name].action)
                if not any(curve.data_path.startswith(f'pose.bones["{bone}"]') for bone in LEG_BONES))
        return hashlib.sha256(repr(sorted(rows)).encode("utf-8")).hexdigest()
    drink_upper = upper_signature()
    old_actions = {name: record for name, record in result.actions.items() if name not in (*NAMES, *DRINK_NAMES)}
    previous = result.actions
    result.actions = old_actions
    preserved = signature()
    result.actions = previous
    build(builder, module.common)
    proof = validate_result(result)
    first = signature()
    def action_values():
        return {name: [(curve.data_path, curve.array_index,
            [tuple(round(float(v), 6) for v in key.co) for key in curve.keyframe_points])
            for curve in sorted(module.common.iter_action_fcurves(record.action), key=lambda c: (c.data_path, c.array_index))]
            for name, record in result.actions.items()}
    first_actions = action_values()
    build(builder, module.common)
    if signature() != first:
        second_actions = action_values()
        names = [name for name in first_actions if first_actions[name] != second_actions[name]]
        for name in names:
            for first_curve, second_curve in zip(first_actions[name], second_actions[name]):
                if first_curve != second_curve:
                    print("COUNTER_SEAT_REPEAT_DIFF", name, first_curve, second_curve, flush=True)
                    break
        raise RuntimeError(f"Counter-seat bone actions are nondeterministic: {names}")
    complete = result.actions
    result.actions = old_actions
    if signature() != preserved:
        raise RuntimeError("Counter-seat refresh changed an unrelated hero mesh, rig, material, UV or action")
    result.actions = complete
    if upper_signature() != drink_upper:
        raise RuntimeError("Counter-seat refresh changed a bar-drink upper-body or hand track")
    original["actions"] = [row for row in original["actions"] if row["name"] not in NAMES]
    for name in NAMES:
        record = result.actions[name]
        original["actions"].append({"name": name, "category": record.category,
            "duration_seconds": record.duration_seconds, "loop": record.loop,
            "source_frame_count": record.source_frame_count, "source_fps": record.source_fps,
            "frame_start": record.action.frame_start, "frame_end": record.action.frame_end,
            "root_motion": False, "event_count": 0, "bone_only": True, "in_place": True})
    original["actions"].sort(key=lambda row: row["name"])
    original["action_count"] = len(result.actions)
    original["torso_skin"]["action_count"] = len(result.actions)
    original["counter_seat"] = proof
    original["counter_seat_authoring_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    original["content_signature_sha256"] = first
    result.root["bp_content_signature_sha256"] = first
    bpy.context.scene["bp_content_signature_sha256"] = first
    module.common.export_animation_fbx(config.animation_fbx, result)
    bind_pose()
    bpy.context.preferences.filepaths.save_version = 0
    module.common.save_blend(config.output)
    config.manifest.write_text(json.dumps(original, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    print("Counter seat publication: five deterministic bone-only takes; original model, bus and other actions preserved; bar-drink leg tracks shared", flush=True)


def sample(result, name, progress):
    record = result.actions[name]
    result.rig.animation_data.action = record.action
    if len(record.action.slots):
        result.rig.animation_data.action_slot = record.action.slots[0]
    frame = record.action.frame_end * progress
    bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
    bpy.context.view_layer.update()


def measure(builder):
    result = builder.result
    sample(result, "Relaxed", 0)
    print("COUNTER_SEAT_LEGS " + json.dumps({side: {name: list(result.rig.pose.bones[name + '.' + side].head)
        for name in ("thigh", "shin", "foot")} for side in ("L", "R")}), flush=True)
    sample(result, "BusRideLoop", 0)
    pelvis = result.rig.matrix_world @ result.rig.pose.bones["pelvis"].head
    print("COUNTER_SEAT_SEATED_FEET " + json.dumps({side: list(to_unity(
        result.rig.pose.bones["foot."+side].head-pelvis)) for side in ("L", "R")}), flush=True)
    details = []
    supported = []
    for part, points in vertices(result):
        over = [p.z - pelvis.z for p in points if (p.x - pelvis.x) ** 2 + (p.y - pelvis.y) ** 2 <= .24 ** 2
                and p.z - pelvis.z > -.25]
        if over:
            supported.append((min(over), part.obj.name))
        if part.obj.name in ("CLO_TrousersPelvis", "GEO_Pelvis", "CLO_JacketBody", "GEO_UpperLeg.L", "GEO_UpperLeg.R", "CLO_TrousersThigh.L", "CLO_TrousersThigh.R"):
            details.append({"name": part.obj.name, "min_relative_z": round(min(p.z - pelvis.z for p in points), 6),
                            "x": [round(min(p.x for p in points), 6), round(max(p.x for p in points), 6)],
                            "y": [round(min(p.y - pelvis.y for p in points), 6), round(max(p.y - pelvis.y for p in points), 6)]})
    print("COUNTER_SEAT_BASELINE " + json.dumps({"pelvis": list(pelvis), "parts": details,
        "seat_disk_lowest": sorted(supported)[:8]}), flush=True)
    worst = (0, "", 0)
    for index in range(49):
        progress = index / 48
        sample(result, "BusRideLoop", progress)
        pelvis = result.rig.matrix_world @ result.rig.pose.bones["pelvis"].head
        for part, points in vertices(result):
            over = [p.z - pelvis.z for p in points if (p.x-pelvis.x)**2+(p.y-pelvis.y)**2 <= .24**2 and p.z-pelvis.z > -.25]
            if over and min(over) < worst[0]: worst = (min(over), part.obj.name, progress)
    print("COUNTER_SEAT_LOOP_SUPPORT " + json.dumps(worst), flush=True)


def fit_endpoint(builder):
    result = builder.result
    sample(result, "BusRideLoop", 0.)
    original = snapshot(result)
    result.rig.animation_data.action = None
    for pitch in (4., 6., 8., 10.):
        for splay in (0., 10., 20., 30.):
            restore_mix(result, original, original, 0.)
            lower_bar_thighs(result, pitch, splay)
            pelvis = result.rig.pose.bones["pelvis"].head.copy()
            failed = []
            for part, points in vertices(result):
                world = np.array([tuple(to_unity(point-pelvis)+Vector((.15, SEAT_TOP+SEAT_CLEARANCE, .91))) for point in points])
                minimum, maximum = world.min(axis=0), world.max(axis=0)
                for name, low, high, planes in solids(.15):
                    if np.any(maximum < low) or np.any(minimum > high): continue
                    inside = np.ones(len(world), dtype=bool)
                    for normal, distance in planes:
                        inside &= world @ normal < distance
                    if inside.any(): failed.append((part.obj.name, name))
            print("COUNTER_SEAT_FIT " + json.dumps({"pitch": pitch, "splay": splay, "failed": failed}), flush=True)


def fit_raised(builder):
    result = builder.result
    sample(result, "BusRideLoop", 0.)
    result.rig.animation_data.action = None
    lower_bar_thighs(result)
    base = snapshot(result)
    obstacles = solids(.15)
    lows, highs = np.array([s[1] for s in obstacles]), np.array([s[2] for s in obstacles])
    triangles = {}
    for part in result.parts:
        part.obj.data.calc_loop_triangles()
        triangles[part.obj.name] = np.array([tuple(t.vertices) for t in part.obj.data.loop_triangles])
    for extra in (6., 7., 8., 9., 10.):
        restore_mix(result, base, base, 0.)
        for side in ("L", "R"):
            thigh = result.rig.pose.bones["thigh."+side]
            thigh.matrix = Matrix.Translation(thigh.head.copy()) @ (Quaternion(Vector((1., 0., 0.)), math.radians(extra)) @
                thigh.matrix.to_quaternion()).to_matrix().to_4x4()
            bpy.context.view_layer.update()
        pelvis = result.rig.pose.bones["pelvis"].head.copy()
        meshes = []
        for part, points in vertex_arrays(result):
            if part.bone not in (*LEG_BONES, "pelvis"): continue
            rel = points-np.array(pelvis)
            meshes.append((part.obj.name, np.column_stack((-rel[:, 0], rel[:, 2], -rel[:, 1]))))
        for clearance in (.15, .155, .16, .165, .17, .175, .18, .185, .19, .195, .20):
            failed = None
            for shift in np.linspace(-.45, .45, 57):
                for name, points in meshes:
                    world = points+np.array((.15+shift, SEAT_TOP+clearance, .91))
                    minimum, maximum = world.min(axis=0), world.max(axis=0)
                    pairs = np.all(maximum >= lows, axis=1)&np.all(minimum <= highs, axis=1)
                    for pair in np.flatnonzero(pairs):
                        solid_name, low, high, planes = obstacles[pair]
                        values = world[triangles[name]]
                        candidates = np.all(values.max(axis=1) >= low, axis=1)&np.all(values.min(axis=1) <= high, axis=1)
                        if any(_clips_triangle(t, planes) for t in values[candidates]):
                            failed = (name, solid_name, float(shift)); break
                    if failed: break
                if failed: break
            if not failed:
                print("COUNTER_SEAT_RAISED_FIT " + json.dumps({"extra_knee_down": extra, "clearance": clearance}), flush=True)
                break
        else:
            print("COUNTER_SEAT_RAISED_FIT " + json.dumps({"extra_knee_down": extra, "failed": failed}), flush=True)


def main():
    sys.path.insert(0, str(ROOT / "tools"))
    spec = importlib.util.spec_from_file_location("hero_counter_source", ROOT / "tools/build-player-3d-model-v2.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    config = module.common.BuildConfig(output=module.DEFAULT_OUTPUT, preview=None, portrait=None,
        manifest=module.DEFAULT_MANIFEST, glb=None, fbx=module.DEFAULT_FBX,
        animation_fbx=module.DEFAULT_ANIMATION_FBX, height=1.75, seed=17301, pose="apose")
    _, builder = load(module, config)
    if "--fit-raised" in sys.argv:
        fit_raised(builder)
    elif "--fit-endpoint" in sys.argv:
        fit_endpoint(builder)
    elif "--author" in sys.argv:
        build(builder, module.common)
        if "--feet-probe" in sys.argv:
            for progress in (6/72, 6.5/72, 7/72):
                sample(builder.result, "CounterSeatEnterRight", progress)
                pelvis = builder.result.rig.pose.bones["pelvis"].head.copy()
                print("FEET_PROBE", progress, "rig", list(builder.result.rig.matrix_world), flush=True)
                for side in ("L", "R"):
                    foot = builder.result.rig.pose.bones["foot."+side]
                    rigid = min((foot.matrix.to_quaternion() @ point+foot.head-pelvis).z+.875
                        for point in builder.counter_seat_feet[side]["points"])
                    print("FOOT", side, list(foot.head), "rigid", rigid, flush=True)
                print([(part.obj.name, points[:, 2].min()-pelvis.z+.875) for part, points in vertex_arrays(builder.result)
                    if part.bone in ("foot.L", "foot.R")], flush=True)
            return
        probe = next((float(arg.split("=", 1)[1]) for arg in sys.argv if arg.startswith("--probe=")), None)
        print("COUNTER_SEAT_VALIDATED " + json.dumps(validate_result(builder.result,
            progresses=None if probe is None else [probe])), flush=True)
    else:
        measure(builder)


if __name__ == "__main__":
    main()
