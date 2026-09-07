"""Author the shower's two in-place curtain actions on the production Hero V2.

The outside action opens the curtain; the inside action closes it. Reversing
either clip gives its exit counterpart, with identical neutral endpoints.
The existing production builder supplies every bone, mesh and neutral sample.
Only the new bone-only action bank is exported; the hero prefab is untouched.
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
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
sys.path.insert(0, str(TOOLS))
spec = importlib.util.spec_from_file_location("shower_curtain_hero", TOOLS / "build-player-3d-model-v2.py")
hero = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = hero
spec.loader.exec_module(hero)
common = hero.common
OUT = ROOT / "Assets/Resources/Player/HomeShowerCurtainActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/HomeShowerCurtainActions.blend"
FPS = 24
DURATION = 1.5
REACH_END = .25
RELEASE_START = .75
OUTSIDE_DOCK = (4.20, 0., 2.18)
INSIDE_DOCK = (4.20, .18, 2.74)
CLOSED_EDGE_X = 4.515
OPEN_EDGE_X = 3.846
EDGE_Z = 2.396
GRIP_ABOVE_ROOT = 1.38


def smooth(t):
    t = max(0., min(1., t))
    return t * t * (3. - 2. * t)


def pull_progress(t):
    return smooth((t - REACH_END) / (RELEASE_START - REACH_END))


class CurtainBuilder(hero.HeroV2Builder):
    def grip_target(self, inside, opening):
        dock = INSIDE_DOCK if inside else OUTSIDE_DOCK
        dx = CLOSED_EDGE_X + (OPEN_EDGE_X - CLOSED_EDGE_X) * opening - dock[0]
        dz = EDGE_Z - dock[2]
        # Production prefab rotates the imported model 180 degrees: its
        # source +X is anatomical left, source -Y is the actor's forward.
        sign = 1. if inside else -1.
        return Vector((sign * dx, sign * dz, GRIP_ABOVE_ROOT))

    def contact_pose(self, inside, opening):
        B = common.BonePose
        side = "L" if inside else "R"
        sign = 1. if inside else -1.
        target = self.grip_target(inside, opening)
        # The shoulder follows the hand slightly, but the pelvis, legs and
        # gameplay root remain fixed throughout the authored action.
        pose = self.merge_pose(self.relaxed_pose(), {
            "spine": B(armature_direction=(-sign * .30 * opening, -.025, 1.)),
            "chest": B(armature_direction=(-sign * .30 * opening, -.035, 1.)),
            "head": B(rotation_degrees=(4., 0., sign * (4. - 8. * opening))),
            f"upper_arm.{'R' if inside else 'L'}": B(armature_direction=(-sign * .11, -.025, -1.)),
            f"forearm.{'R' if inside else 'L'}": B(armature_direction=(-sign * .08, -.045, -1.)),
        })
        self._reset_pose()
        self._apply_pose(pose)
        rig = self.result.rig
        # Fingers extend up the leading hem. The measured socket, not the
        # hand-bone origin, stays at the curtain throughout the pull.
        hand_direction = Vector((0., -.10, .995)).normalized()
        grip_bone = rig.data.bones[f"SOCKET_Grip.{side}"]
        hand_bone = rig.data.bones[f"hand.{side}"]
        grip_distance = (grip_bone.head_local - hand_bone.head_local).length
        wrist = target - hand_direction * grip_distance
        shoulder = rig.pose.bones[f"upper_arm.{side}"].head.copy()
        upper_length = rig.data.bones[f"upper_arm.{side}"].length
        fore_length = rig.data.bones[f"forearm.{side}"].length
        delta = wrist - shoulder
        distance = delta.length
        if distance >= upper_length + fore_length - .005:
            raise ValueError(f"{side} unreachable curtain at {opening}: {distance:.4f} / {upper_length + fore_length:.4f}")
        axis = delta.normalized()
        hint = Vector((sign * .35, -.95, -.35))
        lateral_axis = (hint - axis * hint.dot(axis)).normalized()
        along = (upper_length**2 - fore_length**2 + distance**2) / (2. * distance)
        lateral = math.sqrt(max(0., upper_length**2 - along**2))
        elbow = shoulder + axis * along + lateral_axis * lateral
        pose[f"upper_arm.{side}"] = B(armature_direction=tuple(elbow - shoulder))
        pose[f"forearm.{side}"] = B(armature_direction=tuple(wrist - elbow))
        pose[f"hand.{side}"] = B(armature_direction=tuple(hand_direction))
        return pose

    def blend_pose(self, source, target, t):
        self._reset_pose()
        self._apply_pose(source)
        start = {b.name: b.rotation_quaternion.copy() for b in self.result.rig.pose.bones}
        self._reset_pose()
        self._apply_pose(target)
        return {b.name: common.BonePose(rotation_degrees=tuple(
            math.degrees(v) for v in start[b.name].slerp(b.rotation_quaternion, t).to_euler("XYZ")))
            for b in self.result.rig.pose.bones}

    def build_actions(self):
        relaxed = self.relaxed_pose()
        self.samples = {}
        self._reset_pose()
        self._apply_pose(relaxed)
        self.neutral = {b.name: b.matrix.copy() for b in self.result.rig.pose.bones}
        for inside, name in ((False, "ShowerCurtainOpenOutside"), (True, "ShowerCurtainCloseInside")):
            keys, samples = [], []
            for frame in range(round(DURATION * FPS) + 1):
                t = frame / (DURATION * FPS)
                opening = 1. - pull_progress(t) if inside else pull_progress(t)
                contact = self.contact_pose(inside, opening)
                if t < REACH_END:
                    pose = self.blend_pose(relaxed, contact, smooth(t / REACH_END))
                elif t > RELEASE_START:
                    pose = self.blend_pose(contact, relaxed, smooth((t - RELEASE_START) / (1. - RELEASE_START)))
                else:
                    pose = contact
                if frame in (0, round(DURATION * FPS)):
                    pose = relaxed
                keys.append((t, pose))
                samples.append({"time": t, "opening": opening,
                                "contact": REACH_END <= t <= RELEASE_START,
                                "target": tuple(self.grip_target(inside, opening)),
                                "hand": "L" if inside else "R"})
            self._create_action(name, "home_shower", DURATION, False, round(DURATION * FPS), FPS, keys)
            self.samples[name] = samples


def validate(builder):
    result = builder.result
    maximum_error = 0.
    endpoint_error = 0.
    fixed_bones = ("root", "pelvis", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R")
    for name, samples in builder.samples.items():
        record = result.actions[name]
        result.rig.animation_data.action = record.action
        for sample in samples:
            frame = sample["time"] * record.action.frame_end
            bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
            bpy.context.view_layer.update()
            if sample["contact"]:
                grip = result.rig.pose.bones[f'SOCKET_Grip.{sample["hand"]}'].head
                error = (grip - Vector(sample["target"])).length
                maximum_error = max(maximum_error, error)
                if error > .0002:
                    raise ValueError(f"{name}@{frame}: curtain contact error {error}")
            for bone in result.rig.pose.bones:
                error = max(abs(bone.matrix[r][c] - builder.neutral[bone.name][r][c])
                            for r in range(4) for c in range(4))
                if sample["time"] in (0., 1.):
                    endpoint_error = max(endpoint_error, error)
                    if error > 1e-5:
                        raise ValueError(f"Neutral endpoint mismatch: {name}, {bone.name}")
                if bone.name in fixed_bones and error > 1e-5:
                    raise ValueError(f"Grounded bone moved: {name}, {bone.name}")
        for curve in common.iter_action_fcurves(record.action):
            if not curve.data_path.startswith('pose.bones['):
                raise ValueError("Curtain clip contains a non-bone animation curve")
    result.rig.animation_data.action = None
    builder._reset_pose()
    return maximum_error, endpoint_error


def validate_body_clearance(builder):
    """Check the actual deformed, potentially non-convex clothing at 60 Hz.

    Bone lines alone cannot see a hand buried in a jacket. BVH triangle
    intersections detect crossing surfaces, while signed nearest-surface
    distances also detect a small hand wholly enclosed by a larger volume.
    Adjacent parts of the same arm intentionally share their elbow/wrist seam.
    """
    body = ("GEO_Torso", "CLO_JacketBody", "GEO_Pelvis", "GEO_Neck",
            "GEO_Thigh.L", "GEO_Thigh.R")
    arm = {side: (f"GEO_UpperArm.{side}", f"CLO_JacketSleeve.{side}",
                  f"GEO_Forearm.{side}", f"GEO_Hand.{side}", f"GEO_Thumb.{side}",
                  f"CLO_JacketForearm.{side}")
           for side in ("L", "R")}
    distal = {side: arm[side][2:] for side in arm}
    names = set(body + arm["L"] + arm["R"])
    objects = {name: bpy.data.objects[name] for name in names}
    pairs = {(first, second) for side in arm for first in distal[side]
             for second in body + arm["R" if side == "L" else "L"]}

    def volume(obj, graph):
        evaluated = obj.evaluated_get(graph)
        mesh = evaluated.to_mesh()
        try:
            mesh.calc_loop_triangles()
            vertices = [evaluated.matrix_world @ v.co for v in mesh.vertices]
            triangles = [tuple(t.vertices) for t in mesh.loop_triangles]
            tree = BVHTree.FromPolygons(vertices, triangles, all_triangles=True)
            bounds = (Vector(tuple(min(v[i] for v in vertices) for i in range(3))),
                      Vector(tuple(max(v[i] for v in vertices) for i in range(3))))
            return vertices, tree, bounds
        finally:
            evaluated.to_mesh_clear()

    def contains(point, surface, bounds):
        if any(point[i] <= bounds[0][i] or point[i] >= bounds[1][i] for i in range(3)):
            return False
        # Ray parity is independent of face winding and works for the real
        # articulated (non-convex) torso, including enclosed hand volumes.
        direction = Vector((.783, .361, .506)).normalized()
        origin = point.copy()
        crossings = 0
        for _ in range(64):
            hit, _, _, _ = surface.ray_cast(origin, direction)
            if hit is None:
                return crossings % 2 == 1
            crossings += 1
            origin = hit + direction * 1e-6
        raise ValueError("Mesh containment ray did not leave the body")

    result = builder.result
    previous_action = result.rig.animation_data.action
    report = {"method": "evaluated mesh BVH intersections and ray-parity containment", "sample_fps": 60,
              "tolerance_m": .001, "pairs_per_sample": len(pairs), "actions": {}}
    for name, record in result.actions.items():
        result.rig.animation_data.action = record.action
        intersections = []
        closest = {"clearance_m": float("inf")}
        for sample_index in range(round(DURATION * 60) + 1):
            t = sample_index / (DURATION * 60)
            frame = record.action.frame_end * t
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
            graph = bpy.context.evaluated_depsgraph_get()
            volumes = {part: volume(obj, graph) for part, obj in objects.items()}
            for first, second in sorted(pairs):
                vertices, tree, bounds = volumes[first]
                other_vertices, other_tree, other_bounds = volumes[second]
                depths = []
                for points, surface, target_bounds in ((vertices, other_tree, other_bounds), (other_vertices, tree, bounds)):
                    for point in points:
                        hit, normal, _, distance = surface.find_nearest(point)
                        if hit is not None:
                            depths.append(-distance if contains(point, surface, target_bounds) else distance)
                gap = min(depths)
                sample = {"clearance_m": gap, "time": t, "first": first, "second": second}
                if gap < closest["clearance_m"]:
                    closest = sample
                if tree.overlap(other_tree) or gap < -.001:
                    intersections.append(sample)
        report["actions"][name] = {"sample_count": round(DURATION * 60) + 1,
                                     "intersecting_pairs": len(intersections), "closest": closest,
                                     "first_intersections": intersections[:12]}
        print(f"{name}: mesh clearance {closest}, intersecting pair samples {len(intersections)}", flush=True)
    result.rig.animation_data.action = previous_action
    builder._reset_pose()
    if any(action["intersecting_pairs"] for action in report["actions"].values()):
        print(json.dumps(report), flush=True)
        raise ValueError("Curtain action passes an arm through the body")
    return report


def main():
    config = common.BuildConfig(BLEND, None, None, MANIFEST, None, None, OUT, 1.75, 20260907, "apose")
    builder = CurtainBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    grip_error, endpoint_error = validate(builder)
    clearance = validate_body_clearance(builder)
    signature = hashlib.sha256()
    clips = []
    source_pelvis = builder.neutral["pelvis"].translation
    pelvis_relative = [-source_pelvis.x, source_pelvis.z, -source_pelvis.y]
    for name, record in sorted(builder.result.actions.items()):
        clips.append({"name": name, "duration": record.duration_seconds, "loop": record.loop,
                      "entry_pelvis_relative_to_root": list(pelvis_relative),
                      "action_pelvis_relative_to_root": list(pelvis_relative),
                      "exit_pelvis_relative_to_root": list(pelvis_relative)})
        for curve in common.iter_action_fcurves(record.action):
            signature.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 6) for v in point.co] for point in curve.keyframe_points]], separators=(",", ":")).encode())
    payload = {"generator": "home_shower_curtain_v1", "rig": "HeroV2",
               "bone_count": len(builder.result.rig.data.bones), "root_motion": False,
               "animation_events": 0, "fps": FPS, "duration": DURATION,
               "outside_entry_ground": OUTSIDE_DOCK, "outside_exit_ground": OUTSIDE_DOCK,
               "inside_entry_ground": INSIDE_DOCK, "inside_exit_ground": INSIDE_DOCK,
               "outside_entry_facing": [0., 0., 1.], "outside_exit_facing": [0., 0., 1.],
               "inside_entry_facing": [0., 0., -1.], "inside_exit_facing": [0., 0., -1.],
               "closed_edge_x": CLOSED_EDGE_X, "open_edge_x": OPEN_EDGE_X,
               "edge_z": EDGE_Z, "grip_above_root": GRIP_ABOVE_ROOT,
               "reach_end": REACH_END, "release_start": RELEASE_START,
               "maximum_grip_error": grip_error, "maximum_endpoint_error": endpoint_error,
               "body_clearance": clearance,
               "clips": clips, "signature": signature.hexdigest()}
    if "--validate-only" in sys.argv:
        previous = json.loads(MANIFEST.read_text(encoding="utf-8"))
        if previous != json.loads(json.dumps(payload)):
            raise ValueError("Curtain action manifest changed on deterministic rebuild")
        print("Curtain action determinism, fixed feet/root, neutral seams and measured hand contact passed.")
        return
    common.export_animation_fbx(OUT, builder.result)
    common.save_blend(BLEND)
    MANIFEST.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(payload))
    if "--preview" in sys.argv:
        scene = bpy.context.scene
        scene.render.resolution_x = 700
        scene.render.resolution_y = 850
        scene.render.resolution_percentage = 100
        for name, record in builder.result.actions.items():
            builder.result.rig.animation_data.action = record.action
            nearest = clearance["actions"][name]["closest"]["time"]
            for t, label in ((.25, "Reach"), (.5, "Pulling"), (.75, "Pulled"), (nearest, "ClosestClearance")):
                frame = record.action.frame_end * t
                scene.frame_set(math.floor(frame), subframe=frame % 1.)
                scene.render.filepath = str(BLEND.parent / f"{name}-{label}.png")
                bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
