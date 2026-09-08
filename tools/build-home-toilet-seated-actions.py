"""Author the toilet's contact and seated actions on the unchanged production rig.

The bank contains only bones. The normal hero generator supplies the rig and
geometry for measurement; its prefab, textures and action library are not written.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
sys.path.insert(0, str(TOOLS))
spec = importlib.util.spec_from_file_location("toilet_seated_hero", TOOLS / "build-player-3d-model-v2.py")
hero = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = hero
spec.loader.exec_module(hero)
common = hero.common
OUT = ROOT / "Assets/Resources/Player/HomeToiletSeatedActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/HomeToiletSeatedActions.blend"
FPS = 24
ROOT_HEIGHT = .04
DOCK = (3.32, 0., 1.40)
SEAT_PELVIS = (4.05, .705, 1.40)
LID_HINGE = (4.32, .655, 1.40)
LID_GRIP = (0., .0125, -.49)
REACH_END = .25
RELEASE_START = .75
FLUSH_HANDLE = (4.49, 1.14, 1.60)
FLUSH_TOP = .025
FLUSH_PRESS_DEPTH = .03
FLUSH_CUE = 1.5
FLUSH_REACH = .8
FLUSH_RELEASE = 1.65
BOWL_LOOK = (4.05, .4373, 1.40)
DURATIONS = {"ToiletOpenLid": 2., "ToiletPrepare": 2., "ToiletSit": 2.,
             "ToiletSeated": 3., "ToiletRise": 2., "ToiletDress": 2., "ToiletCloseLid": 2.,
             "ToiletInspect": 2., "ToiletFlush": 2.5}


def smooth(t):
    t = max(0., min(1., t))
    return t * t * (3. - 2. * t)


def lid_amount(t):
    return smooth((t - REACH_END) / (RELEASE_START - REACH_END))


def clothing_amount(t):
    return smooth((t - .2) / .6)


def flush_press(seconds):
    return smooth((seconds - 1.25) / .25) if seconds <= FLUSH_CUE else 1. - smooth((seconds - FLUSH_CUE) / .15)


def facing_right_source(world):
    return Vector((world[2] - DOCK[2], -(world[0] - DOCK[0]), world[1] - ROOT_HEIGHT))


def seated_progress(name, t):
    return smooth(t) if name == "ToiletSit" else 1. - smooth(t) if name == "ToiletRise" else 1. if name == "ToiletSeated" else 0.


def source_pelvis(p):
    return Vector((0., .008 + .722 * p, .835 - .170 * p - .075 * math.sin(math.pi * p)))


def triangle_intersects_box(points, center, extents):
    a, b, c = (point - center for point in points)
    edges = (b - a, c - b, a - c)
    basis = (Vector((1., 0., 0.)), Vector((0., 1., 0.)), Vector((0., 0., 1.)))
    axes = (*basis, edges[0].cross(edges[1]), *(edge.cross(axis) for edge in edges for axis in basis))
    for axis in axes:
        if axis.length_squared < 1e-14: continue
        radius = sum(abs(axis[i]) * extents[i] for i in range(3))
        values = (a.dot(axis), b.dot(axis), c.dot(axis))
        if min(values) > radius or max(values) < -radius: return False
    return True


def validate_flush_lid_parts(parts, sample_time):
    # Conservative box around the actual raised leaf, including 2 mm margin.
    center = Vector((0., -1.0125, .860))
    extents = Vector((.257, .0145, .247))
    graph = bpy.context.evaluated_depsgraph_get()
    for part in parts:
        if part.bone not in ("upper_arm.R", "forearm.R", "hand.R"): continue
        obj = part.obj.evaluated_get(graph)
        mesh = obj.to_mesh()
        try:
            mesh.calc_loop_triangles()
            vertices = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
            for triangle in mesh.loop_triangles:
                if triangle_intersects_box([vertices[index] for index in triangle.vertices], center, extents):
                    raise ValueError(f"Flush arm intersects raised lid: {part.obj.name}@{sample_time}")
        finally:
            obj.to_mesh_clear()


class ToiletBuilder(hero.HeroV2Builder):
    def snapshot_pose(self, pose):
        self._reset_pose()
        self._apply_pose(pose)
        return {b.name: (b.rotation_quaternion.copy(), b.location.copy()) for b in self.result.rig.pose.bones}

    def blend(self, first, second, t):
        first = self.snapshot_pose(first)
        second = self.snapshot_pose(second)
        return {name: common.BonePose(
            rotation_degrees=tuple(math.degrees(v) for v in first[name][0].slerp(second[name][0], t).to_euler("XYZ")),
            location_m=tuple(first[name][1].lerp(second[name][1], t))) for name in first}

    def solve_chain(self, pose, first_name, second_name, target, hint):
        self._reset_pose()
        self._apply_pose(pose)
        rig = self.result.rig
        start = rig.pose.bones[first_name].head.copy()
        upper = rig.data.bones[first_name].length
        lower = rig.data.bones[second_name].length
        delta = Vector(target) - start
        distance = delta.length
        if distance > upper + lower - .0001 or distance < abs(upper - lower) + .0001:
            raise ValueError(f"Unreachable {first_name}: {distance:.6f} / {upper + lower:.6f}; target={tuple(target)} start={tuple(start)}")
        axis = delta.normalized()
        hint = Vector(hint)
        across = (hint - axis * hint.dot(axis)).normalized()
        along = (upper * upper - lower * lower + distance * distance) / (2. * distance)
        elbow = start + axis * along + across * math.sqrt(max(0., upper * upper - along * along))
        pose[first_name] = common.BonePose(armature_direction=tuple(elbow - start))
        pose[second_name] = common.BonePose(armature_direction=tuple(Vector(target) - elbow))

    def hand(self, pose, side, target, direction=(0., -.4, -.9165), elbow_hint=None):
        rig = self.result.rig
        direction = Vector(direction).normalized()
        offset = (rig.data.bones[f"SOCKET_Grip.{side}"].head_local - rig.data.bones[f"hand.{side}"].head_local).length
        wrist = Vector(target) - direction * offset
        self.solve_chain(pose, f"upper_arm.{side}", f"forearm.{side}", wrist,
                         elbow_hint if elbow_hint is not None else (1. if side == "L" else -1., -.3, -.2))
        pose[f"hand.{side}"] = common.BonePose(armature_direction=tuple(direction))

    def feet(self, pose, progress=0.):
        rig = self.result.rig
        for side, sign, start in (("L", 1., 0.), ("R", -1., .30)):
            step = smooth((progress - start) / (.58 if side == "L" else .70))
            original = rig.data.bones[f"foot.{side}"].head_local.copy()
            target = original + Vector((sign * .049 * step, .40 * step, .065 * math.sin(math.pi * step)))
            self.solve_chain(pose, f"thigh.{side}", f"shin.{side}", target, (sign * .05, -1., .05))
            foot = rig.data.bones[f"foot.{side}"]
            pose[f"foot.{side}"] = common.BonePose(armature_direction=tuple(foot.tail_local - foot.head_local))

    def lid_target(self, amount):
        angle = math.pi * .5 * amount
        y = LID_GRIP[1] * math.cos(angle) - LID_GRIP[2] * math.sin(angle)
        z = LID_GRIP[1] * math.sin(angle) + LID_GRIP[2] * math.cos(angle)
        # Source (x,y,z) becomes world (y reversed,z,x) with the actor facing +X.
        return Vector((0., -(LID_HINGE[0] + z - DOCK[0]), LID_HINGE[1] + y - ROOT_HEIGHT))

    def lid_contact(self, amount):
        B = common.BonePose
        pose = self.merge_pose(self.relaxed_pose(), {
            "pelvis": B(armature_location_m=(0., -.12, -.06)),
            "spine": B(armature_direction=(0., -.84, .54)),
            "chest": B(armature_direction=(0., -.88, .475)),
            "neck": B(armature_direction=(0., -.65, .76)),
            "head": B(rotation_degrees=(8., 0., 0.)),
        })
        self.feet(pose)
        target = self.lid_target(amount)
        self.hand(pose, "R", target, (0., -.65, -.76))
        return pose

    def opening(self, t):
        contact = self.lid_contact(lid_amount(t))
        if t < REACH_END:
            contact = self.blend(self.relaxed_pose(), contact, smooth(t / REACH_END))
        elif t > RELEASE_START:
            contact = self.blend(contact, self.relaxed_pose(), smooth((t - RELEASE_START) / (1. - RELEASE_START)))
        self.feet(contact)
        return contact

    def prepare_contact(self, amount, lean_degrees=55.):
        B = common.BonePose
        lean = math.radians(lean_degrees * amount)
        pose = self.merge_pose(self.relaxed_pose(), {
            "pelvis": B(armature_location_m=(0., 0., -(.20 if lean_degrees < 55. else .16) * amount)),
            "spine": B(armature_direction=(0., -math.sin(lean), math.cos(lean))),
            "chest": B(armature_direction=(0., -math.sin(lean), math.cos(lean))),
            "neck": B(rotation_degrees=(2., 0., 0.)),
            "head": B(rotation_degrees=(8. * amount, 0., 0.)),
        })
        self.feet(pose)
        for side, sign in (("L", 1.), ("R", -1.)):
            self.hand(pose, side, (sign * .19, -.13 - .08 * amount, .90 - .34 * amount))
        return pose

    def preparing(self, t, lean_degrees=55.):
        contact = self.prepare_contact(clothing_amount(t), lean_degrees)
        if t < .2:
            contact = self.blend(self.relaxed_pose(), contact, smooth(t / .2))
        elif t > .8:
            contact = self.blend(contact, self.relaxed_pose(), smooth((t - .8) / .2))
        self.feet(contact)
        return contact

    def sitting(self, t):
        if t <= 0.:
            return self.relaxed_pose()
        p = smooth(t)
        B = common.BonePose
        pelvis = source_pelvis(p)
        lean = math.radians(18. * p + 15. * math.sin(math.pi * p))
        pose = self.merge_pose(self.relaxed_pose(), {
            "pelvis": B(armature_location_m=tuple(pelvis - Vector((0., .008, .835)))),
            "spine": B(armature_direction=(0., -math.sin(lean), math.cos(lean))),
            "chest": B(armature_direction=(0., -math.sin(lean), math.cos(lean))),
            "head": B(rotation_degrees=(3. * p, 0., 0.)),
        })
        self.feet(pose, p)
        self._reset_pose()
        self._apply_pose(self.relaxed_pose())
        neutral_grips = {s: self.result.rig.pose.bones[f"SOCKET_Grip.{s}"].head.copy() for s in ("L", "R")}
        neutral_directions = {s: (self.result.rig.pose.bones[f"hand.{s}"].tail -
                                 self.result.rig.pose.bones[f"hand.{s}"].head).normalized() for s in ("L", "R")}
        for side, sign in (("L", 1.), ("R", -1.)):
            target = neutral_grips[side].lerp(Vector((sign * .20, .33, .80)), p)
            direction = neutral_directions[side].lerp(Vector((0., -.4, -.9165)), p).normalized()
            self.hand(pose, side, target, direction)
        # Resolve the different IK elbow plane over the first part of the step,
        # starting from the production Idle rather than switching arm axes.
        if t < .2:
            settle = smooth(t / .2)
            pose = self.blend(self.relaxed_pose(), pose, settle)
            self.feet(pose, p * settle)
        return pose

    def seated(self, t):
        pose = self.sitting(1.)
        # Quiet effort in the torso; the supporting pelvis and feet remain still.
        lean = math.radians(18. + 2. * math.sin(math.pi * t) ** 2)
        for bone in ("spine", "chest"):
            pose[bone] = common.BonePose(armature_direction=(0., -math.sin(lean), math.cos(lean)))
        for side, sign in (("L", 1.), ("R", -1.)):
            self.hand(pose, side, (sign * .20, .33, .80))
        return pose

    def look_at(self, pose, world_target):
        target = facing_right_source(world_target)
        rig = self.result.rig
        for _ in range(4):
            self._reset_pose()
            self._apply_pose(pose)
            eyes = (rig.pose.bones["face.eye.L"].head + rig.pose.bones["face.eye.R"].head) * .5
            forward = (target - eyes).normalized()
            # Keep the actor's lateral axis continuous when looking beyond
            # straight down; a world-up look-at would flip his head 180 degrees.
            right = (Vector((1., 0., 0.)) - forward * forward.x).normalized()
            up = forward.cross(right).normalized()
            delta = Matrix((right, -forward, up)).transposed().to_quaternion()
            head = rig.pose.bones["head"]
            target_rotation = delta @ head.bone.matrix_local.to_quaternion()
            parent_relative = head.parent.matrix.to_quaternion().inverted() @ target_rotation
            rest_relative = head.parent.bone.matrix_local.to_quaternion().inverted() @ head.bone.matrix_local.to_quaternion()
            local = rest_relative.inverted() @ parent_relative
            pose["head"] = common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in local.to_euler("XYZ")))

    def inspecting_hold(self):
        if hasattr(self, "_inspect_hold"): return dict(self._inspect_hold)
        B = common.BonePose
        lean = math.radians(55.)
        pose = self.merge_pose(self.relaxed_pose(), {
            "pelvis": B(armature_location_m=(0., -.12, -.06)),
            "spine": B(armature_direction=(0., -math.sin(lean), math.cos(lean))),
            "chest": B(armature_direction=(0., -math.sin(lean), math.cos(lean))),
            "neck": B(armature_direction=(0., -math.sin(math.radians(65.)), math.cos(math.radians(65.)))),
        })
        self.feet(pose)
        for side, sign in (("L", 1.), ("R", -1.)):
            self.hand(pose, side, (sign * .19, -.22, .70))
        self.look_at(pose, BOWL_LOOK)
        self._inspect_hold = dict(pose)
        return pose

    def inspecting(self, t):
        if t <= 0.: return self.relaxed_pose()
        pose = self.blend(self.relaxed_pose(), self.inspecting_hold(), smooth(t / .75))
        self.feet(pose)
        return pose

    def flush_target(self, seconds):
        return facing_right_source((FLUSH_HANDLE[0], FLUSH_HANDLE[1] + FLUSH_TOP -
                                    FLUSH_PRESS_DEPTH * flush_press(seconds), FLUSH_HANDLE[2]))

    def flushing_contact(self, seconds):
        key = round(flush_press(seconds), 8)
        if not hasattr(self, "_flush_contacts"): self._flush_contacts = {}
        if key in self._flush_contacts: return dict(self._flush_contacts[key])
        B = common.BonePose
        lean = math.radians(55.)
        pose = self.merge_pose(self.relaxed_pose(), {
            "pelvis": B(armature_location_m=(.12, -.32, -.12)),
            "spine": B(armature_direction=(0., -math.sin(lean), math.cos(lean))),
            "chest": B(armature_direction=(0., -math.sin(lean), math.cos(lean))),
            "neck": B(armature_direction=(0., -math.sin(math.radians(65.)), math.cos(math.radians(65.)))),
        })
        self.feet(pose)
        self.hand(pose, "L", (.19, -.70, .71))
        self.hand(pose, "R", self.flush_target(seconds), (0., -.85, -.527), (.5, -.1, 1.))
        self.look_at(pose, BOWL_LOOK)
        self._flush_contacts[key] = dict(pose)
        return pose

    def flushing(self, t):
        seconds = t * DURATIONS["ToiletFlush"]
        if t >= 1.: return self.relaxed_pose()
        pose = self.flushing_contact(seconds)
        if seconds < FLUSH_REACH:
            p = smooth(seconds / FLUSH_REACH)
            start_pose = self.inspecting_hold()
            self._reset_pose()
            self._apply_pose(start_pose)
            start = self.result.rig.pose.bones["SOCKET_Grip.R"].head.copy()
            pose = self.blend(start_pose, pose, p)
            target = start.lerp(self.flush_target(0.), p) + Vector((0., 0., .30 * math.sin(math.pi * p)))
            base = dict(pose)
            direction = Vector((0., -.4, -.9165)).lerp(Vector((0., -.85, -.527)), p).normalized()
            self.hand(pose, "R", target, direction, (.5, -.1, 1.))
            if seconds < .2: pose = self.blend(base, pose, smooth(seconds / .2))
        elif seconds > FLUSH_RELEASE:
            p = smooth((seconds - FLUSH_RELEASE) / (DURATIONS["ToiletFlush"] - FLUSH_RELEASE))
            self._reset_pose()
            self._apply_pose(self.relaxed_pose())
            end = self.result.rig.pose.bones["SOCKET_Grip.R"].head.copy()
            hand = self.result.rig.pose.bones["hand.R"]
            end_direction = (hand.tail - hand.head).normalized()
            pose = self.blend(pose, self.relaxed_pose(), p)
            target = self.flush_target(FLUSH_RELEASE).lerp(end, p) + Vector((0., 0., .30 * math.sin(math.pi * p)))
            base = dict(pose)
            direction = Vector((0., -.85, -.527)).lerp(end_direction, p).normalized()
            self.hand(pose, "R", target, direction, (.5, -.1, 1.))
            if p > .8: pose = self.blend(pose, base, smooth((p - .8) / .2))
        self.feet(pose)
        return pose

    def build_actions(self):
        self.samples = {}
        self._reset_pose()
        self._apply_pose(self.relaxed_pose())
        self.neutral = {b.name: b.matrix.copy() for b in self.result.rig.pose.bones}
        for name, duration in DURATIONS.items():
            samples, keys = [], []
            for frame in range(round(duration * FPS) + 1):
                t = frame / round(duration * FPS)
                if name in ("ToiletOpenLid", "ToiletCloseLid"):
                    sample_t = t if name == "ToiletOpenLid" else 1. - t
                    pose = self.opening(sample_t)
                elif name in ("ToiletPrepare", "ToiletDress"):
                    sample_t = t if name == "ToiletPrepare" else 1. - t
                    # Dress now accompanies the camera's return while facing the
                    # bowl. Keep the head behind that column without changing the
                    # hands' waistband/knee targets or the neutral endpoints.
                    pose = self.preparing(sample_t, 30. if name == "ToiletDress" else 55.)
                elif name in ("ToiletInspect", "ToiletFlush"):
                    sample_t = t
                    pose = self.inspecting(t) if name == "ToiletInspect" else self.flushing(t)
                else:
                    sample_t = t if name == "ToiletSit" else 1. - t if name == "ToiletRise" else 1.
                    pose = self.seated(t) if name == "ToiletSeated" else self.sitting(sample_t)
                if name not in ("ToiletSeated", "ToiletFlush") and t in (0., 1.) and sample_t == 0.:
                    pose = self.relaxed_pose()
                if name in ("ToiletOpenLid", "ToiletCloseLid", "ToiletPrepare", "ToiletDress") and t in (0., 1.):
                    pose = self.relaxed_pose()
                self._reset_pose()
                self._apply_pose(pose)
                samples.append({"time": t, "source_pelvis": tuple(self.result.rig.pose.bones["pelvis"].head),
                                "lid_amount": lid_amount(sample_t) if "Lid" in name else None,
                                "hand_contact": "Lid" in name and REACH_END <= sample_t <= RELEASE_START,
                                "flush_contact": name == "ToiletFlush" and FLUSH_REACH <= t * duration <= FLUSH_RELEASE})
                keys.append((t, pose))
            self._create_action(name, "home_toilet", duration, name == "ToiletSeated", round(duration * FPS), FPS, keys)
            self.samples[name] = samples
            print(f"Authored {name}", flush=True)


def validate(builder):
    rig = builder.result.rig
    grip_error = flush_error = endpoint_error = root_error = 0.
    minimum_gaze_alignment = 1.
    minimum_foot_height = 10.
    dress_column_clearance = 10.
    endpoint_poses = {}
    for name, samples in builder.samples.items():
        action = builder.result.actions[name].action
        rig.animation_data.action = action
        for sample in samples:
            frame = action.frame_end * sample["time"]
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
            bpy.context.view_layer.update()
            if sample["hand_contact"]:
                error = (rig.pose.bones["SOCKET_Grip.R"].head - builder.lid_target(sample["lid_amount"])).length
                grip_error = max(grip_error, error)
                if error > .0003:
                    raise ValueError(f"Lid grip error {name}@{sample['time']}: {error}")
            if sample["flush_contact"]:
                error = (rig.pose.bones["SOCKET_Grip.R"].head - builder.flush_target(sample["time"] * DURATIONS[name])).length
                flush_error = max(flush_error, error)
                if error > .0003: raise ValueError(f"Flush grip error {sample['time']}: {error}")
            if name == "ToiletFlush": validate_flush_lid_parts(builder.result.parts, sample["time"])
            if name == "ToiletInspect" and sample["time"] >= .75:
                head = rig.pose.bones["head"]
                forward = (head.matrix.to_quaternion() @ head.bone.matrix_local.to_quaternion().inverted()) @ Vector((0., -1., 0.))
                eyes = (rig.pose.bones["face.eye.L"].head + rig.pose.bones["face.eye.R"].head) * .5
                alignment = forward.normalized().dot((facing_right_source(BOWL_LOOK) - eyes).normalized())
                minimum_gaze_alignment = min(minimum_gaze_alignment, alignment)
            root_error = max(root_error, rig.pose.bones["root"].location.length,
                             rig.pose.bones["root"].rotation_quaternion.angle)
            for side in ("L", "R"):
                minimum_foot_height = min(minimum_foot_height, rig.pose.bones[f"foot.{side}"].head.z)
            if name == "ToiletDress":
                graph = bpy.context.evaluated_depsgraph_get()
                for part in builder.result.parts:
                    obj = part.obj.evaluated_get(graph)
                    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
                    # Facing +X: source -Y is world +X. Test the same bounded
                    # vertical exit column used by the runtime clearance gate.
                    if max(p.z for p in points) + ROOT_HEIGHT < .575 or min(p.x for p in points) > .15 or max(p.x for p in points) < -.15:
                        continue
                    clearance = 3.88 - (DOCK[0] - min(p.y for p in points))
                    dress_column_clearance = min(dress_column_clearance, clearance)
                    if clearance < 0.: raise ValueError(f"Dress blocks camera column: {part.obj.name}@{sample['time']} clearance={clearance}")
            if sample["time"] in (0., 1.):
                endpoint_poses[(name, sample["time"])] = {b.name: b.matrix.copy() for b in rig.pose.bones}
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['):
                raise ValueError("Toilet bank contains non-bone motion")
    for before, after in (("ToiletPrepare", "ToiletSit"), ("ToiletSit", "ToiletSeated"),
                          ("ToiletSeated", "ToiletRise"), ("ToiletRise", "ToiletDress"),
                          ("ToiletDress", "ToiletCloseLid"), ("ToiletOpenLid", "ToiletPrepare"),
                          ("ToiletRise", "ToiletInspect"), ("ToiletInspect", "ToiletFlush"), ("ToiletFlush", "ToiletDress")):
        for bone in rig.pose.bones:
            first, second = endpoint_poses[(before, 1.)][bone.name], endpoint_poses[(after, 0.)][bone.name]
            endpoint_error = max(endpoint_error, max(abs(first[r][c] - second[r][c]) for r in range(4) for c in range(4)))
    if endpoint_error > .00002 or root_error > .00002 or minimum_foot_height < .091 or minimum_gaze_alignment < .999:
        raise ValueError(f"Toilet endpoints/root/feet/gaze: {endpoint_error}, {root_error}, {minimum_foot_height}, {minimum_gaze_alignment}")
    rig.animation_data.action = None
    builder._reset_pose()
    return {"maximum_grip_error": grip_error, "maximum_flush_grip_error": flush_error,
            "minimum_inspect_gaze_alignment": minimum_gaze_alignment, "maximum_endpoint_error": endpoint_error,
            "minimum_dress_camera_clearance": dress_column_clearance,
            "flush_lid_clearance_samples": len(builder.samples["ToiletFlush"]),
            "maximum_root_motion": root_error, "minimum_foot_anchor_height": minimum_foot_height}


def main():
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20260908, "apose")
    builder = ToiletBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    checks = validate(builder)
    signature = hashlib.sha256()
    clips = []
    for name, record in sorted(builder.result.actions.items()):
        clips.append({"name": name, "duration": record.duration_seconds, "loop": record.loop,
                      "samples": builder.samples[name]})
        for curve in common.iter_action_fcurves(record.action):
            signature.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 6) for v in point.co] for point in curve.keyframe_points]], separators=(",", ":")).encode())
    payload = {"generator": "home_toilet_seated_actions_v2", "rig": "HeroV2",
               "bone_count": len(builder.result.rig.data.bones), "root_motion": False,
               "animation_events": 0, "fps": FPS, "grounded_root_offset": ROOT_HEIGHT,
               "entry_ground": DOCK, "exit_ground": DOCK,
               "lid_entry_facing": [1., 0., 0.], "lid_exit_facing": [1., 0., 0.],
               "seat_entry_facing": [-1., 0., 0.], "seat_exit_facing": [-1., 0., 0.],
               "seated_pelvis_world": SEAT_PELVIS, "lid_grip_local": LID_GRIP,
               "flush_handle_world": FLUSH_HANDLE, "flush_handle_top": FLUSH_TOP,
               "flush_cue_seconds": FLUSH_CUE, "flush_contact_start": FLUSH_REACH,
               "flush_contact_end": FLUSH_RELEASE, "flush_press_depth": FLUSH_PRESS_DEPTH,
               "bowl_look_world": BOWL_LOOK,
               "reach_end": REACH_END, "release_start": RELEASE_START,
               **checks, "clips": clips, "signature": signature.hexdigest()}
    if "--validate-only" in sys.argv:
        if json.loads(MANIFEST.read_text(encoding="utf-8")) != json.loads(json.dumps(payload)):
            raise ValueError("Toilet action bank changed on deterministic rebuild")
        print("Toilet action bank determinism and contact checks passed.", flush=True)
        return
    common.export_animation_fbx(OUT, builder.result)
    common.save_blend(BLEND)
    MANIFEST.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"clips": len(clips), **checks, "signature": payload["signature"]}), flush=True)


if __name__ == "__main__":
    main()
