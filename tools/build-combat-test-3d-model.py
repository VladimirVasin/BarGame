"""Deterministic isolated melee test kit and bone-only HeroV2/NpcHumanV2 banks.

Run via tools/run-blender.py; --validate-only rebuilds measurements, compares
the manifest and checks the published passive FBX files through a round trip.
--actions-only publishes the banks/manifest without rewriting passive geometry.
The test arena is outside story geography. No text, injury or corpse art.
"""
from __future__ import annotations
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / filename)
    loaded = importlib.util.module_from_spec(spec)
    sys.modules[name] = loaded
    spec.loader.exec_module(loaded)
    return loaded


kit = module("combat_passive_kit", "build-city-fair-3d-model.py")
dialogue = module("combat_hero_base", "build-player-dialogue-actions-3d-model.py")
hero, common = dialogue.hero, dialogue.common
OUT = ROOT / "Assets/Resources/Combat"
SOURCE = ROOT / "ArtSource/Combat"
FPS = 100
CLIPS = (("CombatReady", 1., True), ("CombatAttack", 1.28, False),
         ("CombatBlock", 1., True), ("CombatHit", .36, False),
         ("CombatGuardImpact", .28, False), ("CombatGuardBreak", .70, False),
         ("CombatRecoil", .48, False), ("CombatDefeat", .36, False))
DEFEAT_HANDOFF_SECONDS = .16
STRAFE_CLIPS = (("CombatStrafeLeft", .80, True), ("CombatStrafeRight", .80, True))
STRAFE_CYCLE_DISTANCE = .60
STRAFE_FOOT_LIFT = .065
STEP_CLIPS = (("CombatStepForward", (0., -1., 0.), "L"),
              ("CombatStepBackward", (0., 1., 0.), "R"),
              ("CombatStepLeft", (1., 0., 0.), "L"),
              ("CombatStepRight", (-1., 0., 0.), "R"))
STEP_TRAVEL_SECONDS, STEP_SETTLE_SECONDS, STEP_DISTANCE = .30, .16, .65
STEP_DURATION = round(STEP_TRAVEL_SECONDS + STEP_SETTLE_SECONDS, 2)
REACTIONS = (("CombatGuardImpact", .06, "CombatBlock", 0., "CombatBlock", 0.),
             ("CombatGuardBreak", .12, "CombatBlock", 0., "CombatReady", 0.),
             ("CombatRecoil", .07, "CombatAttack", .56, "CombatReady", 0.))
SURFACES = {"Concrete": (.50, .51, .47), "ConcreteDark": (.34, .36, .33),
            "Patch": (.58, .56, .48), "Stripe": (.64, .54, .31),
            "Steel": (.25, .28, .27), "WornSteel": (.52, .55, .52),
            "Rubber": (.13, .15, .14)}
kit.SURFACES = SURFACES


def make_items():
    arena = kit.Item("Arena")
    arena.box("Concrete", (0, -.14, 0), (16, .28, 16), "Collision_Floor", .01)
    for sign in (-1, 1):
        arena.box("ConcreteDark", (sign * 7.82, .48, 0), (.36, .96, 16), "Collision_WallX" + str(sign), .025)
        arena.box("ConcreteDark", (0, .48, sign * 7.82), (15.28, .96, .36), "Collision_WallZ" + str(sign), .025)
        arena.box("Patch", (sign * 7.82, .975, 0), (.40, .03, 16), "Trim", .005)
        arena.box("Patch", (0, .975, sign * 7.82), (15.2, .03, .40), "Trim", .005)
    arena.box("ConcreteDark", (3, .75, 1), (.45, 1.5, 3.2), "Collision_Obstacle", .025)
    arena.box("Patch", (3, 1.515, 1), (.49, .03, 3.24), "Trim", .005)
    # A worn construction joint grid and repairs keep the floor legible without
    # turning the separate technical space into an in-fiction sporting arena.
    for index in range(-3, 4):
        arena.box("ConcreteDark", (index * 2, .002, 0), (.015, .004, 15.1), "Joints", .001)
        arena.box("ConcreteDark", (0, .002, index * 2), (15.1, .004, .015), "Joints", .001)
    for x, z, sx, sz in ((-5.2, 3.6, 1.5, .55), (5.9, -4.3, .6, 1.8), (-2.4, 6, 1.4, .4)):
        arena.box("Patch", (x, .006, z), (sx, .008, sz), "Repairs", .003)
    for x in (-.35, .35):
        arena.box("Stripe", (x, .009, -2.5), (.14, .008, .55), "Marks", .002)
    arena.anchor("HeroSpawn", (0, 0, -2.5))
    arena.anchor("OpponentSpawn", (0, 0, 1))
    arena.anchor("ObstacleCenter", (3, .75, 1))
    crowbar = kit.Item("Crowbar")
    # Grip at origin. Local +Y is the grip socket's authored forward axis;
    # Unity uses identity local rotation when attaching the unit wrapper.
    path = ((0, -.11, 0), (0, .49, 0), (0, .55, .022),
            (0, .59, .065), (0, .605, .112), (0, .59, .145))
    for a, b in zip(path, path[1:]):
        crowbar.rod("Steel", a, b, .019, "Bar", 8)
    crowbar.rod("Rubber", (0, -.075, 0), (0, .08, 0), .023, "GripWrap", 8)
    for i in range(7):
        y = -.06 + i * .02
        crowbar.rod("Steel", (0, y, 0), (0, y + .003, 0), .0234, "GripBands", 8)
    crowbar.box("WornSteel", (0, -.119, .012), (.047, .034, .045), "Chisel", .004)
    crowbar.box("WornSteel", (0, .587, .152), (.044, .024, .025), "Claw", .003)
    crowbar.anchor("Grip", (0, 0, 0))
    crowbar.anchor("StrikeBase", (0, .10, 0))
    crowbar.anchor("StrikeTip", (0, .595, .145))
    return [arena, crowbar]


class CombatBuilder(dialogue.DialogueBuilder):
    def snapshot_pose(self):
        return {bone.name: common.BonePose(
                rotation_degrees=tuple(math.degrees(v) for v in bone.rotation_quaternion.to_euler("XYZ")),
                location_m=tuple(bone.location / self.scale), scale=tuple(bone.scale))
                for bone in self.result.rig.pose.bones}

    def pin_supports(self, pose, foot_targets=None):
        """Two-bone knee solve for pinned soles or explicit stepping targets.

        A pelvis shift is real body weight, not a model-root translation. Solve
        again after interpolation so the knees flex without sliding the boots.
        """
        self._reset_pose(); self._apply_pose(pose)
        rig = self.result.rig
        for side in ("L", "R"):
            thigh, shin, foot = (rig.pose.bones[name + "." + side] for name in ("thigh", "shin", "foot"))
            hip = thigh.head.copy()
            ankle = foot.bone.head_local.copy() if foot_targets is None else foot_targets[side]
            delta = ankle - hip
            distance = delta.length
            a, b = thigh.bone.length, shin.bone.length
            if distance >= a + b or distance <= abs(a - b):
                raise ValueError(f"Unreachable planted leg {side}: {distance:.6f}/{a+b:.6f}; hip={tuple(hip)}, ankle={tuple(ankle)}")
            direction = delta.normalized()
            pole = Vector((0, -1, 0))
            pole = (pole - direction * pole.dot(direction)).normalized()
            along = (a*a - b*b + distance*distance) / (2*distance)
            knee = hip + direction*along + pole*math.sqrt(max(0., a*a-along*along))
            for bone, start, end in ((thigh, hip, knee), (shin, knee, ankle)):
                rest = bone.bone
                rotation = (rest.tail_local-rest.head_local).normalized().rotation_difference((end-start).normalized()) @ rest.matrix_local.to_quaternion()
                bone.matrix = Matrix.Translation(start) @ rotation.to_matrix().to_4x4()
                bpy.context.view_layer.update()
            if foot_targets is None:
                foot.matrix = foot.bone.matrix_local.copy()
            else:
                planted = foot.bone.matrix_local.copy()
                planted.translation = ankle
                foot.matrix = planted
            bpy.context.view_layer.update()
        return self.snapshot_pose()

    def combat_pose(self, kind):
        B = common.BonePose
        # Source coordinates: X left, -Y forward, Z up. Arms are solved in
        # armature space; the pelvis loads the legs over pinned foot contacts.
        poses = {
            "ready": ((-.39, -.10, 1.16), (-.32, -.36, 1.28), (0, -.78, .63), (-2, 0, -5)),
            "ready_breath": ((-.39, -.105, 1.163), (-.32, -.365, 1.286), (0, -.78, .63), (-1.5, 0, -4.6)),
            "anticipation": ((-.41, -.08, 1.14), (-.35, -.25, 1.17), (0, -.60, .80), (-1, 0, -10)),
            "loaded": ((-.50, .02, 1.44), (-.45, .16, 1.70), (-.10, .38, .92), (-4, 0, -16)),
            "windup": ((-.51, .03, 1.51), (-.43, .16, 1.79), (-.05, .48, .88), (-6, 0, -18)),
            "contact": ((-.28, -.27, 1.29), (-.08, -.49, 1.18), (.08, -.99, -.08), (7, 0, 13)),
            "follow": ((-.04, -.28, 1.20), (.22, -.35, 1.07), (.80, -.50, -.32), (12, 0, 26)),
            "overrun": ((.01, -.26, 1.15), (.25, -.32, 1.00), (.85, -.43, -.30), (11, 0, 24)),
            "recover": ((-.22, -.18, 1.08), (-.20, -.30, 1.11), (.24, -.66, .71), (5, 0, 9)),
            "block": ((-.40, -.15, 1.38), (-.29, -.43, 1.49), (.12, -.10, .99), (-4, 0, -7)),
            "block_breath": ((-.40, -.152, 1.383), (-.29, -.433, 1.494), (.12, -.10, .99), (-3.5, 0, -7)),
            "hit": ((-.44, .01, 1.13), (-.38, -.13, 1.22), (.05, -.45, .89), (-19, 0, -12)),
            "hit_settle": ((-.42, -.02, 1.12), (-.34, -.25, 1.19), (.05, -.60, .80), (-7, 0, -6)),
            "guard_impact": ((-.42, -.03, 1.38), (-.31, -.27, 1.50), (.20, .22, .96), (-11, 0, -11)),
            "guard_break": ((-.53, .07, 1.26), (-.58, .19, 1.01), (-.80, .25, -.54), (-21, 0, -12)),
            "recoil": ((-.49, -.05, 1.30), (-.53, -.14, 1.58), (-.43, .32, .84), (-10, 0, -7)),
            "defeat": ((-.43, .08, 1.18), (-.52, .13, .91), (-.30, .15, -.94), (-23, 0, 17)),
        }
        shifts = {
            "ready": (0, .006, -.016), "ready_breath": (0, .006, -.014),
            "anticipation": (-.012, .016, -.026), "loaded": (-.022, .024, -.043),
            "windup": (-.024, .020, -.040), "contact": (.013, -.019, -.020),
            "follow": (.022, -.035, -.038), "overrun": (.023, -.030, -.039),
            "recover": (.006, -.005, -.031), "block": (0, .008, -.027),
            "block_breath": (0, .008, -.025), "hit": (-.014, .040, -.042),
            "hit_settle": (-.006, .016, -.029), "guard_impact": (-.009, .027, -.037),
            "guard_break": (-.024, .045, -.062), "recoil": (-.015, .015, -.035),
            "defeat": (.026, .056, -.088),
        }
        elbow, wrist, axis, chest = poses[kind]
        shoulder = self.points["shoulder.R"]
        pose = self.merge_pose(self.relaxed_pose(), {
            "pelvis": B(armature_location_m=shifts[kind]),
            "spine": B(rotation_degrees=(-2, 0, chest[2] * .24)),
            "chest": B(rotation_degrees=chest),
            "head": B(rotation_degrees=(3, 0, -chest[2] * .4)),
            "upper_arm.R": B(armature_direction=tuple(Vector(elbow) - shoulder)),
            "forearm.R": B(armature_direction=tuple(Vector(wrist) - Vector(elbow))),
            "upper_arm.L": B(armature_direction=(.16, -.13, -.26)),
            "forearm.L": B(armature_direction=(-.06, -.22, .19)),
            "hand.L": B(rotation_degrees=(0, 0, 8)),
        })
        # Each impact has a different silhouette at the game's low resolution:
        # held guard compresses, broken guard opens and drops, wall recoil folds
        # the weapon elbow high while the free arm counterbalances.
        if kind == "guard_impact":
            pose.update({"upper_arm.L": B(armature_direction=(.14, -.08, -.27)),
                         "forearm.L": B(armature_direction=(-.10, -.12, .26)),
                         "head": B(rotation_degrees=(9, 0, 6))})
        elif kind == "guard_break":
            pose.update({"upper_arm.L": B(armature_direction=(.26, .08, -.14)),
                         "forearm.L": B(armature_direction=(.13, .12, -.22)),
                         "head": B(rotation_degrees=(12, 0, 8))})
        elif kind == "recoil":
            pose.update({"upper_arm.L": B(armature_direction=(.24, 0, -.18)),
                         "forearm.L": B(armature_direction=(-.12, -.14, .22)),
                         "head": B(rotation_degrees=(6, 0, -7))})
        elif kind in ("windup", "loaded"):
            pose.update({"upper_arm.L": B(armature_direction=(.17, -.18, -.20)),
                         "forearm.L": B(armature_direction=(-.12, -.10, .28))})
        elif kind in ("follow", "overrun"):
            pose.update({"upper_arm.L": B(armature_direction=(.25, .05, -.13)),
                         "forearm.L": B(armature_direction=(-.03, -.16, .23))})
        elif kind == "defeat":
            pose.update({"upper_arm.L": B(armature_direction=(.23, .05, -.23)),
                         "forearm.L": B(armature_direction=(.10, .10, -.25)),
                         "head": B(rotation_degrees=(18, 0, -8))})
        self._reset_pose(); self._apply_pose(pose)
        hand = self.result.rig.pose.bones["hand.R"]
        # Specify the crowbar's grip-axis explicitly, preserving anatomical
        # hand orientation instead of guessing exported Euler angles.
        delta = Vector((0, -1, 0)).rotation_difference(Vector(axis).normalized())
        rotation = delta @ hand.bone.matrix_local.to_quaternion()
        hand.matrix = Matrix.Translation(hand.head.copy()) @ rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()
        return self.pin_supports(self.snapshot_pose())

    def build_actions(self):
        poses = {n: self.combat_pose(n) for n in ("ready", "ready_breath", "anticipation", "loaded", "windup",
                    "contact", "follow", "overrun", "recover", "block", "block_breath", "hit", "hit_settle",
                    "guard_impact", "guard_break", "recoil", "defeat")}
        for name, duration, loop in CLIPS:
            if name == "CombatAttack":
                stops = ((0, "ready"), (.10, "anticipation"), (.33, "loaded"), (.45, "windup"),
                         (.56, "contact"), (.63, "follow"), (.73, "overrun"), (.96, "recover"), (1.28, "ready"))
            elif name == "CombatHit":
                stops = ((0, "ready"), (.07, "hit"), (.18, "hit_settle"), (.36, "ready"))
            elif name == "CombatGuardImpact":
                stops = ((0, "block"), (.06, "guard_impact"), (.10, "guard_impact"), (.28, "block"))
            elif name == "CombatGuardBreak":
                stops = ((0, "block"), (.12, "guard_break"), (.30, "guard_break"), (.70, "ready"))
            elif name == "CombatRecoil":
                stops = ((0, "contact"), (.07, "recoil"), (.13, "recoil"), (.48, "ready"))
            elif name == "CombatDefeat":
                stops = ((0, "ready"), (.06, "hit"), (DEFEAT_HANDOFF_SECONDS, "defeat"), (.36, "defeat"))
            else:
                stance = "block" if name == "CombatBlock" else "ready"
                stops = ((0, stance), (.5, stance + "_breath"), (duration, stance))
            keys = []
            for frame in range(round(duration * FPS) + 1):
                second = frame / FPS
                for (a, p), (b, q) in zip(stops, stops[1:]):
                    if second <= b + .00001:
                        blended = self.blend(poses[p], poses[q], dialogue.smooth((second - a) / (b - a)))
                        keys.append((frame / round(duration * FPS), self.pin_supports(blended)))
                        break
            self._create_action(name, "combat", duration, loop, round(duration * FPS), FPS, keys)
            print("Authored " + name, flush=True)

    def build_strafe_actions(self):
        """A lead-foot opening step followed by the trailing foot closing.

        Both loops start/end in Ready. The motor owns lateral translation;
        subtract its virtual .60 m/cycle here so each loaded sole stays fixed
        in world space. Feet never cross and the upper-body guard is reused.
        """
        ready = self.combat_pose("ready")
        rig = self.result.rig
        for name, duration, loop in STRAFE_CLIPS:
            sign = 1. if name == "CombatStrafeLeft" else -1.
            leading = "L" if sign > 0. else "R"
            keys = []
            count = round(duration * FPS)
            for frame in range(count + 1):
                t = frame / count
                self._reset_pose(); self._apply_pose(ready)
                pelvis = rig.pose.bones["pelvis"]
                weighted = pelvis.matrix.copy()
                # Lower between the separated soles and shift over whichever
                # leg currently bears weight. There is no root translation.
                weighted.translation += Vector((-sign*.018*math.sin(t*math.tau), 0., -.065*math.sin(t*math.pi)**2))
                pelvis.matrix = weighted
                bpy.context.view_layer.update()
                feet = {}
                for side in ("L", "R"):
                    progress = min(1., max(0., 2.*t - (0. if side == leading else 1.)))
                    lift = math.sin(progress*math.pi)**2
                    ankle = rig.data.bones["foot."+side].head_local.copy()
                    ankle += Vector((sign*STRAFE_CYCLE_DISTANCE*(dialogue.smooth(progress)-t),
                                     -.012*lift, STRAFE_FOOT_LIFT*lift))
                    feet[side] = ankle
                keys.append((t, self.pin_supports(self.snapshot_pose(), feet)))
            self._create_action(name, "combat_locomotion", duration, loop, count, FPS, keys)
            print("Authored hero-only " + name, flush=True)

    def build_step_actions(self):
        """A grounded opening/closing shuffle over the motor's smoothstep travel.

        One sole remains loaded throughout. The upper-body guard follows the
        pelvis lean, then the knees settle after the root has stopped moving.
        """
        ready = self.combat_pose("ready")
        rig = self.result.rig
        count = round(STEP_DURATION * FPS)
        for name, axis, leading in STEP_CLIPS:
            direction = Vector(axis)
            keys = []
            for frame in range(count + 1):
                second = frame / FPS
                travel = min(1., second / STEP_TRAVEL_SECONDS)
                root_distance = STEP_DISTANCE * dialogue.smooth(travel)
                load = math.sin(travel * math.pi)**2
                settle = math.sin(second / STEP_DURATION * math.pi)**2
                self._reset_pose(); self._apply_pose(ready)
                pelvis = rig.pose.bones["pelvis"]
                weighted = pelvis.matrix.copy()
                position = weighted.translation + direction * (.02 * load) + Vector((0., 0., -.11*load - .02*settle))
                lean = Vector((0., 0., 1.)).rotation_difference((Vector((0., 0., 1.)) + direction*(.12*load)).normalized())
                pelvis.matrix = Matrix.Translation(position) @ lean.to_matrix().to_4x4() @ weighted.to_3x3().to_4x4()
                bpy.context.view_layer.update()
                feet = {}
                for side in ("L", "R"):
                    progress = min(1., max(0., 2.*travel - (0. if side == leading else 1.)))
                    lift = math.sin(progress * math.pi)**2
                    ankle = rig.data.bones["foot."+side].head_local.copy()
                    ankle += direction * (STEP_DISTANCE * dialogue.smooth(progress) - root_distance)
                    ankle.z += .06 * lift
                    feet[side] = ankle
                try:
                    stepped = self.pin_supports(self.snapshot_pose(), feet)
                except ValueError as error:
                    raise ValueError(f"{name} at {second:.2f}s: {error}") from error
                keys.append((frame/count, stepped))
            self._create_action(name, "combat_step", STEP_DURATION, False, count, FPS, keys)
            print("Authored hero-only " + name, flush=True)


def step_payload(builder):
    rig = builder.result.rig
    checksum = hashlib.sha256()
    records = []
    rig.animation_data.action = builder.result.actions["CombatReady"].action
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    ready = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    excluded = {"pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R"}
    for name, axis, leading in STEP_CLIPS:
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Defensive step has object motion")
            checksum.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode())
        direction = Vector(axis)
        support_error = upper_error = seam_error = 0.
        lifts = {"L": 0., "R": 0.}
        separation = 100.
        rig.animation_data.action = action
        count = round(STEP_DURATION*FPS)*2
        for sample in range(count + 1):
            frame = sample*.5
            second = frame / FPS
            travel = min(1., second / STEP_TRAVEL_SECONDS)
            virtual_root = direction * (STEP_DISTANCE * dialogue.smooth(travel))
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.); bpy.context.view_layer.update()
            for side in ("L", "R"):
                foot = rig.pose.bones["foot."+side]
                rest = foot.bone.head_local
                lifts[side] = max(lifts[side], foot.head.z-rest.z)
                planted = travel >= 1. or (side == leading and travel >= .5) or (side != leading and travel <= .5)
                if planted:
                    landed = travel >= 1. or side == leading
                    expected = rest + direction * (STEP_DISTANCE if landed else 0.)
                    support_error = max(support_error, (foot.head + virtual_root - expected).length)
            separation = min(separation, rig.pose.bones["foot.L"].head.x-rig.pose.bones["foot.R"].head.x)
            for bone in rig.pose.bones:
                error = max(abs(bone.matrix_basis[i][j]-ready[bone.name][i][j]) for i in range(4) for j in range(4))
                if bone.name not in excluded: upper_error = max(upper_error, error)
                if sample in (0, count): seam_error = max(seam_error, error)
        if support_error > .002 or upper_error > .00001 or seam_error > .00001 or separation < .18 or min(lifts.values()) < .05:
            raise ValueError(f"Defensive step support/guard/seam/spacing failed: {name}: {support_error}, {upper_error}, {seam_error}, {separation}, {lifts}")
        records.append(dict(name=name, leading_foot=leading, direction_unity=[-direction.x, direction.z, -direction.y],
                            maximum_world_support_error_m=support_error, maximum_upper_body_error=upper_error,
                            maximum_ready_seam_error=seam_error, minimum_foot_separation_m=separation,
                            left_foot_lift_m=lifts["L"], right_foot_lift_m=lifts["R"]))
    return dict(duration_seconds=STEP_DURATION, travel_seconds=STEP_TRAVEL_SECONDS,
                settle_seconds=STEP_SETTLE_SECONDS, distance_m=STEP_DISTANCE, travel_curve="smoothstep",
                validation_hz=200, signature=checksum.hexdigest(), clips=records)


def strafe_payload(builder):
    rig = builder.result.rig
    checksum = hashlib.sha256()
    records = []
    rig.animation_data.action = builder.result.actions["CombatReady"].action
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    ready = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    excluded = {"pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R"}
    for name, duration, _ in STRAFE_CLIPS:
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Strafe has object motion")
            checksum.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode())
        sign = 1. if name == "CombatStrafeLeft" else -1.
        leading = "L" if sign > 0. else "R"
        support_error = upper_error = seam_error = 0.
        lifts = {"L": 0., "R": 0.}
        separation = 100.
        rig.animation_data.action = action
        for sample in range(round(duration*FPS)*2 + 1):
            frame = sample * .5
            t = frame / (duration*FPS)
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.); bpy.context.view_layer.update()
            for side in ("L", "R"):
                foot = rig.pose.bones["foot."+side]
                rest = foot.bone.head_local
                lifts[side] = max(lifts[side], foot.head.z-rest.z)
                planted = (side == leading and t >= .5) or (side != leading and t <= .5)
                if planted:
                    expected = rest + Vector((sign*STRAFE_CYCLE_DISTANCE*(1. if side == leading else 0.), 0., 0.))
                    virtual_world = foot.head + Vector((sign*STRAFE_CYCLE_DISTANCE*t, 0., 0.))
                    support_error = max(support_error, (virtual_world-expected).length)
            separation = min(separation, rig.pose.bones["foot.L"].head.x-rig.pose.bones["foot.R"].head.x)
            for bone in rig.pose.bones:
                error = max(abs(bone.matrix_basis[i][j]-ready[bone.name][i][j]) for i in range(4) for j in range(4))
                if bone.name not in excluded: upper_error = max(upper_error, error)
                if sample == 0 or sample == round(duration*FPS)*2: seam_error = max(seam_error, error)
        if support_error > .001 or upper_error > .00001 or seam_error > .00001 or separation < .18 or min(lifts.values()) < .05:
            raise ValueError(f"Strafe support/guard/seam/foot spacing failed: {name}: {support_error}, {upper_error}, {seam_error}, {separation}, {lifts}")
        records.append(dict(name=name, leading_foot=leading, direction_unity=[-sign,0.,0.],
                            maximum_world_support_error_m=support_error, maximum_upper_body_error=upper_error,
                            maximum_ready_seam_error=seam_error, minimum_foot_separation_m=separation,
                            left_foot_lift_m=lifts["L"], right_foot_lift_m=lifts["R"]))
    return dict(duration_seconds=.80, cycle_distance_m=STRAFE_CYCLE_DISTANCE,
                nominal_speed_m_s=round(STRAFE_CYCLE_DISTANCE/.80, 6), validation_hz=200,
                support_contract="virtual lateral root travel cancels each planted sole; leading foot opens, trailing closes",
                signature=checksum.hexdigest(), clips=records)


def action_payload(builder):
    rig = builder.result.rig
    checksum = hashlib.sha256()
    base_checksum = hashlib.sha256()
    points = []
    lower_error = 0.
    support_angle = 0.
    attack_pelvis = []
    attack_knee_travel = 0.
    reference = None
    for name, duration, loop in CLIPS:
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Combat object motion")
            curve_bytes = json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode()
            checksum.update(curve_bytes)
            if name in ("CombatReady", "CombatAttack", "CombatBlock", "CombatHit"): base_checksum.update(curve_bytes)
        rig.animation_data.action = action
        for sample in range(round(duration * FPS) * 2 + 1):
            frame = sample * .5
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.); bpy.context.view_layer.update()
            support = {n: rig.pose.bones[n].matrix.copy() for n in ("root", "foot.L", "foot.R")}
            if reference is None: reference = support
            lower_error = max(lower_error, max((reference[n].translation - support[n].translation).length for n in reference))
            support_angle = max(support_angle, max(math.degrees(reference[n].to_quaternion().rotation_difference(
                support[n].to_quaternion()).angle) for n in reference))
            if name == "CombatAttack":
                attack_pelvis.append(rig.pose.bones["pelvis"].head.copy())
                if frame == 0: initial_knees = {s: rig.pose.bones["shin."+s].rotation_quaternion.copy() for s in ("L", "R")}
                attack_knee_travel = max(attack_knee_travel, max(math.degrees(initial_knees[s].rotation_difference(
                    rig.pose.bones["shin."+s].rotation_quaternion).angle) for s in initial_knees))
            if name == "CombatAttack" and frame in (0, 45, 50, 55, 56, 60, 63, 128):
                grip = rig.pose.bones["SOCKET_Grip.R"].matrix
                # Socket +Y is the exported transform's +Y. Local Z uses the
                # reflected FBX basis, whose sign is checked again in Unity.
                tip = grip @ Vector((0, .595, .145))
                p = grip.translation
                points.append(dict(seconds=frame / FPS, grip=[-p.x, p.z + .04, -p.y],
                                   tip=[-tip.x, tip.z + .04, -tip.y]))
            if frame == 0:
                initial = {b.name: b.matrix.copy() for b in rig.pose.bones}
            if frame == round(duration * FPS) and (loop or name in ("CombatAttack", "CombatHit", "CombatGuardImpact")):
                if max(abs(initial[b.name][i][j] - b.matrix[i][j]) for b in rig.pose.bones for i in range(4) for j in range(4)) > .00001:
                    raise ValueError("Combat action endpoint mismatch")
    if lower_error > .001 or support_angle > .1:
        raise ValueError(f"Combat action moved foot contacts: {lower_error:.6f} m, {support_angle:.4f} deg")
    pelvis_travel = max((a-b).length for a in attack_pelvis for b in attack_pelvis)
    if pelvis_travel < .04 or attack_knee_travel < 7.:
        raise ValueError("Attack lacks authored leg/hip weight transfer")
    if max(p["tip"][2] for p in points if .45 <= p["seconds"] <= .63) < 1.:
        raise ValueError("Crowbar cannot reach a target in front")
    def snapshot(name, seconds):
        rig.animation_data.action = builder.result.actions[name].action
        bpy.context.scene.frame_set(round(seconds * FPS)); bpy.context.view_layer.update()
        return {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    reactions = []
    peak_tips = []
    for name, peak, enter, enter_time, exit_clip, exit_time in REACTIONS:
        start = snapshot(name, 0.)
        end = snapshot(name, next(d for n,d,_ in CLIPS if n == name))
        for measured, expected in ((start, snapshot(enter, enter_time)), (end, snapshot(exit_clip, exit_time))):
            if max(abs(measured[n][i][j] - expected[n][i][j]) for n in measured for i in range(4) for j in range(4)) > .00001:
                raise ValueError("Reaction endpoint mismatch: " + name)
        impact = snapshot(name, peak)
        travel = (impact["SOCKET_Grip.R"].translation - start["SOCKET_Grip.R"].translation).length
        head_travel = (impact["head"].translation - start["head"].translation).length
        if travel < .09: raise ValueError("Reaction is too small to read at PS1 scale: " + name)
        peak_tips.append(impact["SOCKET_Grip.R"] @ Vector((0, .595, .145)))
        reactions.append(dict(name=name, peak_seconds=peak, entry_clip=enter, entry_seconds=enter_time,
                              exit_clip=exit_clip, exit_seconds=exit_time, grip_travel_m=travel,
                              head_travel_m=head_travel))
    separation = min((a - b).length for i,a in enumerate(peak_tips) for b in peak_tips[i + 1:])
    if separation < .2: raise ValueError(f"Combat reaction silhouettes are indistinct: {separation:.3f}m")
    defeat_start, ready = snapshot("CombatDefeat", 0.), snapshot("CombatReady", 0.)
    defeat, defeat_end = snapshot("CombatDefeat", DEFEAT_HANDOFF_SECONDS), snapshot("CombatDefeat", .36)
    for a, b in ((defeat_start, ready), (defeat, defeat_end)):
        if max(abs(a[n][i][j] - b[n][i][j]) for n in a for i in range(4) for j in range(4)) > .00001:
            raise ValueError("Defeat entry or held physical handoff differs")
    defeat_drop = ready["pelvis"].translation.z - defeat["pelvis"].translation.z
    if not .04 <= defeat_drop <= .12: raise ValueError("Defeat must lose balance without authoring a fall")
    return dict(rig="HeroV2", npc_rig="NpcHumanV2", fps=FPS, root_motion=False, animation_events=0,
                clips=[dict(name=n, duration_seconds=d, loop=l) for n,d,l in CLIPS],
                windup_seconds=.45, active_seconds=.18, recovery_seconds=.65,
                maximum_support_error=lower_error, maximum_support_angle_degrees=support_angle,
                support_validation_hz=200, support_bones=["root", "foot.L", "foot.R"],
                attack_pelvis_travel_m=pelvis_travel, attack_knee_travel_degrees=attack_knee_travel,
                defeat_handoff_seconds=DEFEAT_HANDOFF_SECONDS, defeat_pelvis_drop_m=defeat_drop,
                animation_signature=checksum.hexdigest(),
                base_action_signature=base_checksum.hexdigest(), strike_samples=points,
                reactions=reactions, minimum_reaction_tip_separation_m=separation)


def meta(path):
    target = path.with_name(path.name + ".meta")
    if not target.exists():
        target.write_text("fileFormatVersion: 2\nguid: " + hashlib.sha256(path.relative_to(ROOT).as_posix().encode()).hexdigest()[:32] + "\n", encoding="utf8")


def main():
    validate_only = "--validate-only" in sys.argv
    actions_only = "--actions-only" in sys.argv
    items = make_items(); signature = kit.signature(items)
    if kit.signature(make_items()) != signature: raise ValueError("Passive geometry is nondeterministic")
    payload = kit.manifest(items, signature)
    payload.update(generator="tools/build-combat-test-3d-model.py", generator_version="1.4.0", test_only=True)
    OUT.mkdir(parents=True, exist_ok=True); SOURCE.mkdir(parents=True, exist_ok=True)
    if not validate_only and not actions_only:
        roots = kit.build_objects(items)
        for root in roots: kit.export(root, OUT / (root.name + ".fbx"))
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "CombatTest.blend"), check_existing=False)
    kit.verify_fbx(OUT, payload)
    common.ANIMATION_FPS = FPS
    config = common.BuildConfig(SOURCE / "CombatActions.blend", None, None, OUT / "CombatTest3D.json",
                                None, None, OUT / "CombatActions.fbx", 1.75, 20260919, "apose")
    builder = CombatBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.build()
    payload["actions"] = action_payload(builder)
    # Export the shared NPC bank before creating hero-only locomotion Actions:
    # Blender's FBX exporter scans all compatible actions, not result.actions.
    previous = json.loads((OUT / "CombatTest3D.json").read_text(encoding="utf8")) if (OUT / "CombatTest3D.json").exists() else {}
    same_shared_bank = previous.get("actions", {}).get("animation_signature") == payload["actions"]["animation_signature"]
    if not validate_only and (not actions_only or not same_shared_bank or not (OUT / "CombatNpcActions.fbx").exists()):
        builder.result.root.name = "ROOT_Player"
        common.export_animation_fbx(OUT / "CombatNpcActions.fbx", builder.result)
        builder.result.root.name = "ROOT_PlayerV2"
    builder.build_strafe_actions()
    payload["actions"]["hero_only_clips"] = [dict(name=n, duration_seconds=d, loop=l) for n,d,l in STRAFE_CLIPS]
    payload["actions"]["strafing"] = strafe_payload(builder)
    builder.build_step_actions()
    payload["actions"]["hero_step_clips"] = [dict(name=n, duration_seconds=STEP_DURATION, loop=False) for n,_,_ in STEP_CLIPS]
    payload["actions"]["defensive_step"] = step_payload(builder)
    payload = json.loads(json.dumps(payload))
    if validate_only:
        if json.loads((OUT / "CombatTest3D.json").read_text(encoding="utf8")) != payload:
            raise ValueError("Combat deterministic manifest differs")
    else:
        common.export_animation_fbx(OUT / "CombatActions.fbx", builder.result)
        common.save_blend(SOURCE / "CombatActions.blend")
        (OUT / "CombatTest3D.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
        for path in OUT.iterdir():
            if path.suffix != ".meta": meta(path)
    print("COMBAT TEST ART CONTRACT OK " + json.dumps(payload["actions"]), flush=True)


if __name__ == "__main__": main()
