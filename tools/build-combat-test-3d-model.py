"""Deterministic isolated melee test kit and bone-only HeroV2/NpcHumanV2 banks.

Run via tools/run-blender.py; --validate-only rebuilds measurements, compares
the manifest and checks the published passive FBX files through a round trip.
--actions-only publishes the banks/manifest without rewriting passive geometry.
Two swing families share both banks: the forehand (right to left) and the
backhand (left to right), each with its charge, heavy overlay and wall recoil.
Both banks carry four grounded combat shuffles and the same broad ready base;
the hero's raised shoulders, guarded head and late settling distinguish his
frightened, untrained profile from the sparring opponent without changing clocks.
The test arena is outside story geography. No text, injury or corpse art.
"""
from __future__ import annotations
import argparse
from dataclasses import replace
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

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
CLIPS = (("CombatReady", 4., True), ("CombatRest", 4., True), ("CombatAttack", 1.28, False),
         ("CombatBlock", 4., True), ("CombatHit", .36, False),
         ("CombatGuardImpact", .28, False), ("CombatGuardBreak", .70, False),
         ("CombatRecoil", .48, False), ("CombatDefeat", .36, False))
CHARGE_CLIPS = (("CombatCharge", 1., False), ("CombatReleaseLight", 1.28, False),
                ("CombatReleaseHeavy", 1.28, False))
# The backhand family: the same right-hand-on-top two-hand hold loaded over the
# other shoulder and swept left to right. Its attack is also its light release;
# no copy is authored. Both banks carry every family.
BACKHAND_CLIPS = (("CombatBackhand", 1.28, False), ("CombatBackhandRecoil", .48, False),
                  ("CombatBackhandCharge", 1., False), ("CombatBackhandHeavy", 1.28, False))
SHARED_CLIPS = CLIPS + CHARGE_CLIPS + BACKHAND_CLIPS
ATTACK_STOPS = ((0, "ready"), (.10, "anticipation"), (.33, "loaded"), (.45, "windup"),
                (.56, "contact"), (.63, "follow"), (.73, "overrun"), (.96, "recover"), (1.28, "ready"))
HEAVY_STOPS = ((0., "windup"), (.10, "windup"), (.33, "heavy_windup"), (.45, "heavy_windup"),
               (.56, "heavy_contact"), (.63, "heavy_follow"), (.73, "heavy_overrun"),
               (.96, "heavy_recover"), (1.28, "ready"))
RECOIL_STOPS = ((0, "contact"), (.07, "recoil"), (.13, "recoil"), (.48, "ready"))
SWING_POSES = ("anticipation", "loaded", "windup", "contact", "follow", "overrun", "recover", "recoil",
               "heavy_windup", "heavy_contact", "heavy_follow", "heavy_overrun", "heavy_recover")
SWING_FAMILIES = (
    dict(name="forehand", prefix="", attack="CombatAttack", light="CombatReleaseLight",
         heavy="CombatReleaseHeavy", charge="CombatCharge", recoil="CombatRecoil"),
    dict(name="backhand", prefix="backhand_", attack="CombatBackhand", light=None,
         heavy="CombatBackhandHeavy", charge="CombatBackhandCharge", recoil="CombatBackhandRecoil"))
CHARGE_CLIP_NAMES = {family["charge"] for family in SWING_FAMILIES}


def family_stops(family, stops):
    """The shared stop timeline over one family's pose names; Ready is common."""
    return tuple((t, p if p.startswith("ready") else family["prefix"] + p) for t, p in stops)


DEFEAT_HANDOFF_SECONDS = .16
LOCOMOTION_CLIPS = (("CombatAdvance", (0., -1., 0.), "L"),
                    ("CombatRetreat", (0., 1., 0.), "R"),
                    ("CombatStrafeLeft", (1., 0., 0.), "L"),
                    ("CombatStrafeRight", (-1., 0., 0.), "R"))
LOCOMOTION_DURATION, LOCOMOTION_CYCLE_DISTANCE, LOCOMOTION_FOOT_LIFT = .80, .60, .065
STEP_CLIPS = (("CombatStepForward", (0., -1., 0.), "L"),
              ("CombatStepBackward", (0., 1., 0.), "R"),
              ("CombatStepLeft", (1., 0., 0.), "L"),
              ("CombatStepRight", (-1., 0., 0.), "R"))
# The .80 m step travels for .36 s, then settles for .21 s. Its wider base needs extra
# knee flexion during its opening/closing shuffle, never a longer planted leg.
STEP_TRAVEL_SECONDS, STEP_SETTLE_SECONDS, STEP_DISTANCE = .36, .21, .80
STEP_DURATION = round(STEP_TRAVEL_SECONDS + STEP_SETTLE_SECONDS, 2)
SUPPORT_OFFSETS = {"L": (.115, -.130, 0.), "R": (-.115, .110, 0.)}
SUPPORT_YAW_DEGREES = {"L": 8., "R": -14.}
RECOVERY_DURATIONS = {"CombatRiseProne": 2.40, "CombatRiseSupine": 3.20}
RECOVERY_CLIPS = ("CombatRiseProne", "CombatRiseSupine")
RELEASED_SUPPORT_CLIPS = ("CombatHit", "CombatGuardBreak", "CombatDefeat")
RELEASED_SUPPORT_POSES = ("hit", "hit_settle", "guard_break", "defeat")
RECOVERY_MARKERS = {
    "CombatRiseProne": dict(floor_hand_start=0., floor_hand_release=.28, knee_hand_start=.48,
                            both_feet=.72, regrip_start=.66, regrip_contact=.90, ready=1.),
    "CombatRiseSupine": dict(floor_hand_start=.11, floor_hand_release=.15, free_balance_start=.15,
                             both_feet=0., regrip_start=.66, regrip_contact=.90, ready=1.),
}
PELVIS_LOWERING = .075
SUPPORT_GRIP = .16
BLOCK_GRIP = .42
LEFT_GRIP_AXIS = 1
GUARD_HEIGHTS = {"block": 1.490, "block_breath": 1.497, "guard_impact": 1.430}
GUARD_PALM_ROLL_DEGREES = -90.
GUARD_LEFT_ELBOW_POLE = (.40, .078, -1.)
# A raised two-hand load: the bar axis (grip toward tip), the finger hint that
# fixes the palm about it, the right elbow pole and the forearm pronation. The
# backhand loads the mirror image over the other shoulder.
RAISED_GRIP = dict(axis=(.8, .4, .4), fingers=(0, 0, 1), elbow_pole=(-.2, -.5, -1.), forearm_roll=-math.pi / 2)
BACKHAND_RAISED_GRIP = dict(axis=(-.9, .3, .3), fingers=(0, 0, 1), elbow_pole=(-.2, -.5, -1.), forearm_roll=-math.pi / 2)
RAISED_WRISTS = {"loaded": ((-.12, -.16, 1.59), RAISED_GRIP), "windup": ((-.12, -.16, 1.62), RAISED_GRIP),
                 "heavy_windup": ((-.08, -.18, 1.60), RAISED_GRIP),
                 "backhand_loaded": ((.16, -.45, 1.38), BACKHAND_RAISED_GRIP),
                 "backhand_windup": ((.16, -.45, 1.38), BACKHAND_RAISED_GRIP),
                 # The heavy load stays within a couple of degrees of the light one: a
                 # wider lean flips the hand quaternions' sign and the power blend
                 # spins the bar a full turn at intermediate weights.
                 "backhand_heavy_windup": ((.16, -.45, 1.38), BACKHAND_RAISED_GRIP)}
# The support elbow's pole for the cross-body load: on the mirrored bar the
# left hand is the far hand, and the ordinary outward pole leaves its wrist
# bent 70 degrees; pointing the elbow down and forward halves that. Blends
# between stops carry the pole with them; kinds without an entry keep the
# ordinary solve, so the forehand's output is untouched.
DEFAULT_LEFT_ELBOW_POLE = (.9, .18, -.65)
CROSS_LEFT_ELBOW_POLE = (.2, -.3, -.93)
LEFT_ELBOW_POLES = {"backhand_loaded": CROSS_LEFT_ELBOW_POLE, "backhand_windup": CROSS_LEFT_ELBOW_POLE,
                    "backhand_heavy_windup": CROSS_LEFT_ELBOW_POLE}


def blended_left_pole(start, end, weight):
    """The pole between two stops, or None where neither stop overrides it."""
    a, b = LEFT_ELBOW_POLES.get(start), LEFT_ELBOW_POLES.get(end)
    if a is None and b is None: return None
    return Vector(a or DEFAULT_LEFT_ELBOW_POLE).lerp(Vector(b or DEFAULT_LEFT_ELBOW_POLE), weight)
REACTIONS = (("CombatGuardImpact", .06, "CombatBlock", 0., "CombatBlock", 0.),
             ("CombatGuardBreak", .12, "CombatBlock", 0., "CombatReady", 0.),
             ("CombatRecoil", .07, "CombatAttack", .56, "CombatReady", 0.),
             ("CombatBackhandRecoil", .12, "CombatBackhand", .56, "CombatReady", 0.))
SURFACES = {"Concrete": (.50, .51, .47), "ConcreteDark": (.34, .36, .33),
            "Patch": (.58, .56, .48), "Stripe": (.64, .54, .31),
            "Steel": (.25, .28, .27), "WornSteel": (.52, .55, .52),
            "Rubber": (.13, .15, .14)}
kit.SURFACES = SURFACES
CROWBAR_PATH=((0.,-.11,0.),(0.,.49,0.),(0.,.55,.022),(0.,.59,.065),(0.,.605,.112),(0.,.59,.145))


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
    path = CROWBAR_PATH
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


# Source coordinates: X left, -Y forward, Z up. Arms are solved in armature
# space; the pelvis loads the legs over pinned foot contacts. Each entry is
# (elbow, wrist, grip axis, chest euler); the shift is the pelvis load.
COMBAT_POSES = {
    "ready": ((-.33, .015, 1.10), (-.25, -.22, .99), (.62, -.43, .65), (4, 1, -9)),
    "ready_breath": ((-.333, .010, 1.104), (-.247, -.227, 1.001), (.62, -.43, .65), (3.3, 1.3, -8.5)),
    "rest": ((-.285, .015, 1.08), (-.28, -.045, .82), (-.10, -.98, -.18), (2, 0, -3)),
    "rest_breath": ((-.282, .013, 1.085), (-.277, -.05, .829), (-.10, -.98, -.18), (1.4, 0, -2.5)),
    "anticipation": ((-.41, -.08, 1.14), (-.35, -.25, 1.17), (0, -.60, .80), (-1, 0, -10)),
    "loaded": ((-.50, .02, 1.44), (-.45, .16, 1.70), (-.10, .38, .92), (-4, 0, -16)),
    "windup": ((-.51, .03, 1.51), (-.43, .16, 1.79), (-.05, .48, .88), (-6, 0, -18)),
    "contact": ((-.28, -.27, 1.29), (-.08, -.49, 1.18), (.08, -.80, .60), (12, 0, 13)),
    "follow": ((-.04, -.28, 1.20), (.22, -.35, 1.07), (.45, -.35, .82), (12, 0, 26)),
    "overrun": ((.01, -.26, 1.15), (.25, -.32, 1.00), (.55, -.25, .80), (11, 0, 24)),
    "recover": ((-.22, -.18, 1.08), (-.20, -.30, 1.11), (.24, -.66, .71), (5, 0, 9)),
    "block": ((-.36, -.16, 1.18), (-.28, -.37, 1.34), (.996, -.015, .075), (5, 0, -4)),
    "block_breath": ((-.362, -.163, 1.184), (-.28, -.375, 1.348), (.996, -.015, .075), (4.3, .2, -3.5)),
    "hit": ((-.44, .01, 1.13), (-.38, -.13, 1.22), (.05, -.45, .89), (-19, 0, -12)),
    "hit_settle": ((-.42, -.02, 1.12), (-.34, -.25, 1.19), (.05, -.60, .80), (-7, 0, -6)),
    "guard_impact": ((-.37, -.05, 1.19), (-.29, -.23, 1.35), (.98, .10, .14), (-5, 0, -9)),
    "guard_break": ((-.50, -.08, 1.30), (-.40, -.20, 1.06), (-.72, -.50, -.40), (-21, 0, -12)),
    "recoil": ((-.40, -.10, 1.23), (-.32, -.30, 1.42), (-.35, -.30, .88), (-10, 0, -7)),
    "defeat": ((-.40, -.05, 1.14), (-.28, -.20, .90), (-.15, -.90, -.40), (-23, 0, 17)),
    "heavy_windup": ((-.53, .045, 1.55), (-.42, .20, 1.81), (-.07, .54, .84), (-8, 0, -26)),
    "heavy_contact": ((-.27, -.29, 1.25), (-.055, -.55, 1.14), (.10, -.80, .60), (16, 0, 19)),
    "heavy_follow": ((-.015, -.30, 1.17), (.27, -.35, 1.03), (.45, -.35, .82), (16, 0, 36)),
    "heavy_overrun": ((.045, -.25, 1.11), (.30, -.28, .97), (.55, -.25, .80), (16, 0, 38)),
    "heavy_recover": ((-.18, -.17, 1.05), (-.14, -.30, 1.08), (.29, -.62, .72), (8, 0, 15)),
    # Backhand: the same right hand on the grip crosses to load over the
    # other shoulder, meets the target square, and follows through to the
    # right. Chest twist and pelvis shift are the forehand's mirror.
    # The chest's third angle is a side lean: the cross-body load leans left
    # like the forehand's so the right shoulder can reach across; the stroke
    # and follow-through lean the other way from the forehand's.
    "backhand_anticipation": ((-.30, -.16, 1.15), (-.02, -.27, 1.14), (.30, -.55, .78), (-1, 0, -6)),
    "backhand_loaded": ((-.22, -.18, 1.36), (.16, -.20, 1.63), (-.9, .3, .3), (-4, 0, -14)),
    "backhand_windup": ((-.20, -.20, 1.40), (.16, -.20, 1.66), (-.9, .3, .3), (-6, 0, -16)),
    "backhand_contact": ((-.06, -.27, 1.29), (.02, -.50, 1.17), (-.08, -.80, .60), (12, 0, -13)),
    "backhand_follow": ((-.30, -.22, 1.20), (-.32, -.42, 1.06), (-.50, -.80, -.30), (12, 0, -26)),
    "backhand_overrun": ((-.34, -.18, 1.16), (-.38, -.36, 1.00), (-.55, -.75, -.30), (11, 0, -24)),
    "backhand_recover": ((-.30, -.16, 1.10), (-.22, -.30, 1.08), (.10, -.70, .70), (5, 0, -6)),
    "backhand_recoil": ((-.12, -.22, 1.30), (.15, -.24, 1.45), (.55, .25, .80), (-10, 0, 7)),
    "backhand_heavy_windup": ((-.19, -.22, 1.42), (.16, -.20, 1.65), (-.9, .3, .3), (-8, 0, -18)),
    "backhand_heavy_contact": ((-.05, -.29, 1.26), (.04, -.55, 1.14), (-.10, -.80, .60), (16, 0, -19)),
    "backhand_heavy_follow": ((-.32, -.24, 1.17), (-.36, -.44, 1.03), (-.55, -.78, -.30), (16, 0, -36)),
    "backhand_heavy_overrun": ((-.37, -.19, 1.12), (-.42, -.36, .97), (-.60, -.74, -.30), (16, 0, -38)),
    "backhand_heavy_recover": ((-.27, -.15, 1.06), (-.18, -.30, 1.06), (.15, -.68, .72), (8, 0, -12)),
}
COMBAT_SHIFTS = {
    "ready": (-.008, .004, -.016), "ready_breath": (-.005, .002, -.012),
    "rest": (-.008, .005, -.010), "rest_breath": (-.005, .004, -.006),
    "anticipation": (-.012, .016, -.026), "loaded": (-.022, .024, -.043),
    "windup": (-.024, .020, -.040), "contact": (.013, -.069, -.020),
    "follow": (.022, -.075, -.038), "overrun": (.023, -.060, -.039),
    "recover": (.006, -.020, -.031), "block": (0, .008, -.027),
    "block_breath": (0, .008, -.025), "hit": (-.014, .040, -.042),
    "hit_settle": (-.006, .016, -.029), "guard_impact": (-.009, .027, -.037),
    "guard_break": (-.024, .045, -.062), "recoil": (-.015, .015, -.035),
    "defeat": (.026, .056, -.088),
    "heavy_windup": (-.024, .020, -.040), "heavy_contact": (.013, -.069, -.020),
    "heavy_follow": (.022, -.075, -.038), "heavy_overrun": (.023, -.060, -.039),
    "heavy_recover": (.006, -.020, -.031),
    "backhand_anticipation": (.012, .016, -.026), "backhand_loaded": (.022, .024, -.043),
    "backhand_windup": (.024, .020, -.040), "backhand_contact": (-.013, -.069, -.020),
    "backhand_follow": (-.022, -.075, -.038), "backhand_overrun": (-.023, -.060, -.039),
    "backhand_recover": (-.006, -.020, -.031), "backhand_recoil": (.015, .015, -.035),
    "backhand_heavy_windup": (.024, .020, -.040), "backhand_heavy_contact": (-.013, -.069, -.020),
    "backhand_heavy_follow": (-.022, -.075, -.038), "backhand_heavy_overrun": (-.023, -.060, -.039),
    "backhand_heavy_recover": (-.006, -.020, -.031),
}


class CombatBuilder(dialogue.DialogueBuilder):
    hero_profile = False

    def suspend_mesh_deformation(self):
        # Authoring/validation reads the original skeleton and stored grip
        # vertices, never evaluated skinned meshes. Avoid reskinning the whole
        # dressed hero for every dense bone-only sample; restore for the source.
        if hasattr(self, "suspended_deformation"): return
        self.suspended_deformation = []
        for part in self.result.parts:
            for modifier in part.obj.modifiers:
                if modifier.type == "ARMATURE":
                    self.suspended_deformation.append((modifier, modifier.show_viewport))
                    modifier.show_viewport = False

    def restore_mesh_deformation(self):
        for modifier, visible in getattr(self, "suspended_deformation", ()):
            modifier.show_viewport = visible

    def profile_stops(self, stops):
        # The novice takes longer to arrest his follow-through, then gathers
        # the weapon close again. Only the recovery shape changes: windup,
        # active contact, total duration and every endpoint stay identical.
        if not self.hero_profile: return stops
        return tuple((.78 if second == .73 else 1.02 if second == .96 else second, pose)
                     for second, pose in stops)

    def locomotion_torso(self, direction, load, settle):
        """The ribcage follows the travelling hips, then absorbs the stop."""
        for name, weight in (("spine", .45), ("chest", .55), ("head", -.55)):
            bone = self.result.rig.pose.bones[name]
            pitch = -direction.y * (3.5*load - 2.0*settle)*weight
            roll = direction.x * (3.5*load - 2.0*settle)*weight
            bone.rotation_quaternion = bone.rotation_quaternion @ Euler(
                (math.radians(pitch), math.radians(roll), 0.), "XYZ").to_quaternion()
        bpy.context.view_layer.update()

    def track_pose(self, stops, poses, second):
        """C1 local-pose curves carry momentum through authored landmarks.

        Quaternion Bezier controls use a shared angular velocity at each knot;
        translations use monotone Hermite tangents. Real reversals/held poses
        stop, but passing a contact/settling key no longer stops every bone.
        The endpoints remain exact and the foot/grip solvers run afterwards.
        """
        if second <= stops[0][0]: return poses[stops[0][1]]
        if second >= stops[-1][0]: return poses[stops[-1][1]]
        index = next(i for i in range(len(stops)-1) if second <= stops[i+1][0] + 1.e-8)
        a, b = stops[index][0], stops[index+1][0]
        t, duration = (second-a)/(b-a), b-a
        source = [poses[key] for _, key in stops]

        def vector_tangent(values, knot):
            if knot in (0, len(stops)-1): return Vector((0., 0., 0.))
            left = (values[knot]-values[knot-1])/(stops[knot][0]-stops[knot-1][0])
            right = (values[knot+1]-values[knot])/(stops[knot+1][0]-stops[knot][0])
            return Vector(tuple(2*x*y/(x+y) if x*y > 0. else 0. for x,y in zip(left, right)))

        def angular_tangent(values, knot):
            if knot in (0, len(stops)-1): return Vector((0., 0., 0.))
            def delta(other):
                q = values[knot].inverted() @ other
                if q.w < 0.: q.negate()
                return q.to_exponential_map()
            incoming = -delta(values[knot-1])/(stops[knot][0]-stops[knot-1][0])
            outgoing = delta(values[knot+1])/(stops[knot+1][0]-stops[knot][0])
            if incoming.dot(outgoing) <= 0.: return Vector((0., 0., 0.))
            tangent = (incoming+outgoing)*.5
            maximum = 1.5*min(incoming.length, outgoing.length)
            return tangent.normalized()*min(maximum, tangent.length)

        result = {}
        for name in source[0]:
            rotations = [Euler(tuple(math.radians(v) for v in pose[name].rotation_degrees), "XYZ").to_quaternion() for pose in source]
            q0, q1 = rotations[index:index+2]
            c0 = q0 @ Quaternion(angular_tangent(rotations, index)*(duration/3.))
            c1 = q1 @ Quaternion(angular_tangent(rotations, index+1)*(-duration/3.))
            p0, p1, p2 = q0.slerp(c0, t), c0.slerp(c1, t), c1.slerp(q1, t)
            rotation = p0.slerp(p1, t).slerp(p1.slerp(p2, t), t)
            def interpolate(field):
                values = [Vector(getattr(pose[name], field)) for pose in source]
                return tuple((2*t**3-3*t*t+1)*values[index] + (t**3-2*t*t+t)*duration*vector_tangent(values, index) +
                             (-2*t**3+3*t*t)*values[index+1] + (t**3-t*t)*duration*vector_tangent(values, index+1))
            result[name] = common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in rotation.to_euler("XYZ")),
                                          location_m=interpolate("location_m"), scale=interpolate("scale"))
        return self.limit_weapon_wrist(result)

    def limit_weapon_wrist(self,pose):
        """Keep cubic wrist swing inside the radial limit without changing its arm branch."""
        self._reset_pose(); self._apply_pose(pose)
        rig=self.result.rig; hand=rig.pose.bones["hand.R"]; lower=rig.pose.bones["forearm.R"]
        direction=(hand.head-lower.head).normalized()
        delta=hand.matrix.to_quaternion() @ hand.bone.matrix_local.to_quaternion().inverted()
        across=(delta @ self.hand_frame("R")[1]).normalized()
        value=direction.dot(across); bound=math.sin(math.radians(24.8))
        if abs(value)<=bound: return pose
        palm=(delta @ self.hand_frame("R")[2]).normalized()
        target=math.copysign(bound,value); angle=0.
        for _ in range(6):
            axis=Quaternion(palm,angle) @ across
            derivative=direction.dot(palm.cross(axis))
            if abs(derivative)<.1: raise ValueError("Cubic wrist correction has no nearby neutral swing")
            angle-=(direction.dot(axis)-target)/derivative
        if abs(angle)>math.radians(8.): raise ValueError(f"Cubic wrist needs a new dock, not a large correction: {math.degrees(angle):.2f}")
        wrist=hand.head.copy(); rotation=Quaternion(palm,angle) @ hand.matrix.to_quaternion()
        hand.matrix=Matrix.Translation(wrist) @ rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()
        # Pronation stays on the lower arm; the corrective wrist bend must not
        # become an axial corkscrew when neighbouring powers are blended.
        saved=hand.matrix.copy(); forearm_axis=(wrist-lower.head).normalized()
        relative=lower.bone.matrix_local.to_quaternion().inverted() @ hand.bone.matrix_local.to_quaternion()
        q=rotation @ relative.inverted()
        q=(q @ Vector((0.,1.,0.))).rotation_difference(forearm_axis) @ q
        lower.matrix=Matrix.Translation(lower.head.copy()) @ q.to_matrix().to_4x4()
        bpy.context.view_layer.update(); hand.matrix=saved; bpy.context.view_layer.update()
        return self.snapshot_pose()

    def support(self, side):
        return self.result.rig.data.bones["foot." + side].head_local + Vector(SUPPORT_OFFSETS[side])

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
            ankle = self.support(side) if foot_targets is None else foot_targets[side]
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
            yaw = Quaternion(Vector((0., 0., 1.)), math.radians(SUPPORT_YAW_DEGREES[side]))
            planted = Matrix.Translation(ankle) @ (yaw @ foot.bone.matrix_local.to_quaternion()).to_matrix().to_4x4()
            foot.matrix = planted
            bpy.context.view_layer.update()
        return self.snapshot_pose()

    def hand_frame(self, side):
        bone = self.result.rig.data.bones["hand." + side]
        wrist = bone.head_local
        fingers = (bone.tail_local - wrist).normalized()
        across = (Vector((0, 1, 0)) - fingers * fingers.y).normalized()
        palm = fingers.cross(across).normalized() * (-1 if side == "L" else 1)
        centre = wrist + fingers * (.060 * self.scale) + palm * (.030 * self.scale)
        return centre, -across, palm

    def weapon_frame(self):
        rig = self.result.rig
        hand = rig.pose.bones["hand.R"]
        delta = hand.matrix @ hand.bone.matrix_local.inverted()
        centre, axis, _ = self.hand_frame("R")
        axis = (delta.to_3x3() @ axis).normalized()
        socket = rig.pose.bones["SOCKET_Grip.R"].matrix.to_quaternion()
        rotation = (socket @ Vector((0, 1, 0))).rotation_difference(axis) @ socket
        return delta @ centre, rotation

    def solve_arm(self, side, wrist, rotation, elbow_pole=None):
        rig = self.result.rig
        upper, forearm, hand = (rig.pose.bones[name + "." + side] for name in ("upper_arm", "forearm", "hand"))
        shoulder = upper.head.copy()
        delta = wrist - shoulder
        distance = delta.length
        a, b = upper.bone.length, forearm.bone.length
        if distance >= a + b - .0001 or distance <= abs(a - b) + .0001:
            raise ValueError(f"Unreachable {side} weapon wrist: {distance:.4f}/{a+b:.4f}")
        direction = delta.normalized()
        pole = Vector(elbow_pole or (.9 if side == "L" else -.9, .18, -.65))
        pole = (pole - direction * pole.dot(direction)).normalized()
        along = (a*a - b*b + distance*distance) / (2*distance)
        elbow = shoulder + direction*along + pole*math.sqrt(max(0., a*a-along*along))
        for bone, start, end in ((upper, shoulder, elbow), (forearm, elbow, wrist)):
            rest = bone.bone
            q = (rest.tail_local-rest.head_local).normalized().rotation_difference((end-start).normalized()) @ rest.matrix_local.to_quaternion()
            bone.matrix = Matrix.Translation(start) @ q.to_matrix().to_4x4()
            bpy.context.view_layer.update()
        hand.matrix = Matrix.Translation(wrist) @ rotation.to_4x4()
        bpy.context.view_layer.update()
        reference=upper.bone.matrix_local.to_quaternion().inverted() @ Vector((0.,.85,-.15)).normalized()
        self.recovery_hinge_frames("upper_arm."+side,"forearm."+side,"hand."+side,reference)
        hand_matrix=hand.matrix.copy(); axis=(hand.head-forearm.head).normalized()
        rest_relative=forearm.bone.matrix_local.to_quaternion().inverted() @ hand.bone.matrix_local.to_quaternion()
        lower_rotation=hand.matrix.to_quaternion() @ rest_relative.inverted()
        lower_rotation=(lower_rotation @ Vector((0.,1.,0.))).rotation_difference(axis) @ lower_rotation
        forearm.matrix=Matrix.Translation(forearm.head.copy()) @ lower_rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update(); hand.matrix=hand_matrix; bpy.context.view_layer.update()

    def left_contact_frame(self, offset=SUPPORT_GRIP):
        rig = self.result.rig
        origin, weapon_rotation = self.weapon_frame()
        target = origin + weapon_rotation @ Vector((0, offset, 0))
        centre, axis, palm = self.hand_frame("L")
        right_delta = rig.pose.bones["hand.R"].matrix @ rig.data.bones["hand.R"].matrix_local.inverted()
        # The supporting palm wraps the opposite side of the straight shaft.
        target_axis = weapon_rotation @ Vector((0, LEFT_GRIP_AXIS, 0))
        target_palm = -(right_delta.to_3x3() @ self.hand_frame("R")[2])
        def frame(forward, up):
            z = forward.normalized()
            x = up.cross(z).normalized()
            return Matrix((x, z.cross(x), z)).transposed()
        rotation = frame(target_axis, target_palm) @ frame(axis, palm).transposed()
        hand = rig.pose.bones["hand.L"]
        wrist = target - rotation @ (centre - hand.bone.head_local)
        return wrist, rotation @ hand.bone.matrix_local.to_3x3()

    def pin_left_grip(self, pose, offset=SUPPORT_GRIP, pole=None, move_right=True, optimize_pole=True, radial_limit=None, runtime_support=False, continuation=None):
        """Bake the opposing palm on the actual shaft, not the neutral socket."""
        self._reset_pose(); self._apply_pose(pose)
        rig = self.result.rig
        wrist, rotation = self.left_contact_frame(offset)
        current=self.result.rig.pose.bones["hand.L"]
        held_right=rig.pose.bones["hand.R"].matrix.copy()
        if continuation is None and not runtime_support and (current.head-wrist).length<.00001 and current.matrix.to_quaternion().rotation_difference(rotation.to_quaternion()).angle<.0001:
            return pose
        upper, forearm = rig.pose.bones["upper_arm.L"], rig.pose.bones["forearm.L"]
        shoulder = upper.head.copy()
        source_axis=(current.head-shoulder).normalized()
        source_pole=forearm.head-shoulder
        original_hint=source_pole.copy()
        source_pole=source_pole-source_axis*source_pole.dot(source_axis)
        delta = wrist - shoulder
        distance = delta.length
        a, b = upper.bone.length, forearm.bone.length
        reach_margin = .030 if self.hero_profile else .015
        if move_right and distance > a + b - reach_margin:
            # The former one-hand backswing can extend beyond the other arm.
            # Bring its original grip inward just enough to leave a bent left
            # elbow, preserving the bar's axis and the authored swing timing.
            correction = delta.normalized() * (distance - (a + b - reach_margin))
            right = rig.pose.bones["hand.R"]
            right_forearm=rig.pose.bones["forearm.R"]
            right_pole=(right_forearm.head-rig.pose.bones["upper_arm.R"].head).normalized()
            self.solve_arm("R", right.head - correction, right.matrix.to_3x3(),right_pole)
            wrist -= correction
            self.maximum_grip_adjustment = max(getattr(self, "maximum_grip_adjustment", 0.), correction.length)
        if pole is None:
            guard_weight = max(0., min(1., (offset-SUPPORT_GRIP)/(BLOCK_GRIP-SUPPORT_GRIP)))
            pole = Vector(DEFAULT_LEFT_ELBOW_POLE).lerp(Vector(GUARD_LEFT_ELBOW_POLE), guard_weight)
        if source_pole.length>.001:
            pole=source_axis.rotation_difference((wrist-shoulder).normalized()) @ source_pole.normalized()
        if continuation is not None:
            old_axis,old_pole=continuation
            pole=old_axis.rotation_difference((wrist-shoulder).normalized()) @ old_pole
            elbow=self.grip_elbow("L",wrist,rotation,pole,nearest=True,radial_limit=radial_limit,
                                  support_head=True,maximum_turn=12.,target_pole=original_hint)
            if elbow is None: raise ValueError("No continuous supporting elbow on the previous branch")
            pole=elbow[3]
        elif runtime_support:
            if move_right: raise ValueError("Runtime support cannot move the held weapon")
            # CombatSupportGrip.TryContactHint projects the live authored
            # elbow directly onto the new wrist axis, then searches at most
            # 65 degrees around that same branch. It never transports the
            # old chain axis or modifies the right hand.
            elbow=self.grip_elbow("L",wrist,rotation,original_hint,nearest=True,
                                  radial_limit=25.,support_head=True)
            if elbow is None: raise ValueError("Runtime support has no anatomical elbow hint")
            pole=elbow[3]
        elif optimize_pole:
            elbow=self.grip_elbow("L",wrist,rotation,Vector(pole),nearest=True,radial_limit=radial_limit)
            if elbow is not None: pole=elbow[3]
        else:
            # Runtime's hint is the actual authored elbow point, with no
            # transported pole search and no correction of the held weapon.
            pole=original_hint
        self.solve_arm("L", wrist, rotation, Vector(pole))
        if not move_right and max(abs(held_right[i][j]-rig.pose.bones["hand.R"].matrix[i][j]) for i in range(4) for j in range(4))>1.e-5:
            raise ValueError("Support-only solve changed the held right hand")
        return self.snapshot_pose()

    def support_branch(self,pose,side="L"):
        self._reset_pose(); self._apply_pose(pose)
        upper,lower,hand=(self.result.rig.pose.bones[n+"."+side] for n in ("upper_arm","forearm","hand"))
        axis=(hand.head-upper.head).normalized(); pole=lower.head-upper.head
        return axis.copy(),(pole-axis*pole.dot(axis)).normalized()

    def weapon_arm_state(self,pose,seconds,raw_pose=None):
        axis,pole=self.support_branch(pose,"R")
        bones=self.result.rig.pose.bones
        upper=bones["upper_arm.R"]
        correction=Quaternion()
        if raw_pose is not None:
            rest=upper.parent.bone.matrix_local.to_quaternion().inverted() @ upper.bone.matrix_local.to_quaternion()
            raw=Euler(tuple(math.radians(v) for v in raw_pose[upper.name].rotation_degrees),"XYZ").to_quaternion()
            correction=(rest @ upper.rotation_quaternion @ raw.inverted() @ rest.inverted()).normalized()
        return dict(axis=axis,pole=pole,seconds=seconds,pose=dict(pose),elbow=bones["forearm.R"].head.copy(),
                    shoulder_correction=correction,
                    left_elbow=bones["forearm.L"].head.copy(),
                    rotations={name:(bones[name].matrix.to_quaternion().copy(),bones[name].rotation_quaternion.copy())
                               for name in ("upper_arm.R","forearm.R")})

    def check_weapon_arm_edge(self,result,continuation,seconds,context,restore_pose,authoring_reserve=False):
        """Validate the actual local FBX interpolation and final support solve."""
        rig=self.result.rig;name=context.split("@")[0]
        held=name not in RELEASED_SUPPORT_CLIPS and name!="CombatRest"
        offset=BLOCK_GRIP if name in ("CombatBlock","CombatGuardImpact") else SUPPORT_GRIP
        animation=rig.animation_data
        action=animation.action if animation is not None else None
        slot=animation.action_slot if animation is not None else None
        elapsed=seconds-continuation["seconds"] if continuation is not None else 0.
        previous={"R":continuation["elbow"],"L":continuation["left_elbow"]} if continuation is not None else None
        try:
            if animation is not None:animation.action=None
            for weight in ((.25,.5,.75,1.) if continuation is not None else (1.,)):
                blended=self.linear_key_pose(continuation["pose"],result,weight) if continuation is not None else result
                if held:self.pin_left_grip(blended,offset,move_right=False,runtime_support=True)
                else:self._reset_pose();self._apply_pose(blended)
                at=seconds-elapsed*(1.-weight)
                self.weapon_anatomy(f"{name}@{at:.6f}",30. if name in ("CombatReady","CombatRest") else 55. if name in CHARGE_CLIP_NAMES else 75.)
                if authoring_reserve:
                    sides=("R","L") if held else ("R",)
                    clearance=min(self.arm_core_clearance(side) for side in sides)
                    if clearance<.006:raise ValueError(f"Authored edge core reserve {name}@{at:.6f}: {clearance:.9f} m")
                elbows={side:rig.pose.bones["forearm."+side].head.copy() for side in ("R","L")}
                if previous is not None:
                    speed=max((elbows[side]-previous[side]).length for side in elbows)/(elapsed*.25)
                    if speed>6.:raise ValueError(f"Discontinuous elbow {name}@{at:.6f}: {speed:.6f} m/s")
                previous=elbows
        finally:
            if animation is not None:
                animation.action=action
                if action is not None and slot is not None:animation.action_slot=slot
            self._reset_pose();self._apply_pose(restore_pose)

    def clear_whole_weapon_arm(self,pose,continuation,seconds,context,*,limit_degrees=45.):
        """Move the rigid held chain at its shoulder; never bend the wrist to clear a body."""
        self.last_whole_edge_failure=None
        name=context.split("@")[0]
        rig=self.result.rig
        upper,lower,hand=(rig.pose.bones[n+".R"] for n in ("upper_arm","forearm","hand"))
        shoulder=upper.head.copy();elbow=lower.head.copy();wrist=hand.head.copy()
        upper_matrix=upper.matrix.copy();hand_matrix=hand.matrix.copy()
        original_local={bone.name:bone.rotation_quaternion.copy() for bone in (lower,hand)}
        direction=(wrist-elbow).normalized();fingers=(hand.tail-hand.head).normalized()
        delta=hand_matrix.to_3x3() @ hand.bone.matrix_local.to_3x3().inverted()
        across=(delta @ self.hand_frame("R")[1]).normalized();palm=(delta @ self.hand_frame("R")[2]).normalized()
        if (abs(math.degrees(math.asin(max(-1.,min(1.,direction.dot(across))))))>25.+1.e-5 or
            abs(math.degrees(math.atan2(direction.dot(palm),direction.dot(fingers))))>55.+1.e-5 or
            math.degrees(direction.angle(fingers))>65.+1.e-5):return None
        held=name not in RELEASED_SUPPORT_CLIPS and name!="CombatRest"
        left_upper,left_lower=(rig.pose.bones[n+".L"] for n in ("upper_arm","forearm"))
        left_shoulder=left_upper.head.copy();left_hint=left_lower.head-left_shoulder
        offset=BLOCK_GRIP if name in ("CombatBlock","CombatGuardImpact") else SUPPORT_GRIP
        origin,weapon=self.weapon_frame();boxes=self.body_boxes()
        head=rig.pose.bones["head"];head_delta=head.matrix.to_quaternion() @ head.bone.matrix_local.to_quaternion().inverted()
        head_a=head.head+head_delta @ Vector((0.,0.,.10));head_b=head.head+head_delta @ Vector((0.,0.,.20))
        elapsed=seconds-continuation["seconds"] if continuation is not None else 0.
        def rotation_degrees(rotation):
            q=rotation.normalized()
            return math.degrees(2.*math.atan2(math.sqrt(q.x*q.x+q.y*q.y+q.z*q.z),abs(q.w)))
        def safe_edge(result):
            if continuation is None:return True
            try:
                self.check_weapon_arm_edge(result,continuation,seconds,context,pose,authoring_reserve=True)
                return True
            except ValueError as error:
                self.last_whole_edge_failure=str(error)
                return False
        def candidate(rotation):
            rotation=rotation.normalized()
            if rotation_degrees(rotation)>limit_degrees+1.e-7:return None
            moved_elbow=shoulder+rotation @ (elbow-shoulder);moved_wrist=shoulder+rotation @ (wrist-shoulder)
            segments=((shoulder.lerp(moved_elbow,.65),moved_elbow),(moved_elbow,moved_wrist))
            # Keep interpolation inside the unchanged 5 mm presented-pose gate.
            if any(self.segment_box_distance(a,b,c,q,h)<.007 for a,b in segments for _,c,q,h in boxes):return None
            if any(self.segment_distance(a,b,head_a,head_b)<.127 for a,b in segments):return None
            if continuation is not None and (moved_elbow-continuation["elbow"]).length>6.*elapsed+1.e-7:return None
            pivot=Matrix.Translation(shoulder) @ rotation.to_matrix().to_4x4() @ Matrix.Translation(-shoulder)
            moved_hand=pivot @ hand_matrix
            moved_origin=(moved_hand @ hand.bone.matrix_local.inverted()) @ self.hand_frame("R")[0]
            moved_weapon=rotation @ weapon
            if name in tuple(f["attack"] for f in SWING_FAMILIES) and abs(seconds-.56)<1.e-6 and -(moved_origin+moved_weapon @ Vector((0.,.595,.145))).y<.95:return None
            arms={"R":(shoulder,moved_elbow,moved_wrist)};left=None
            if held:
                left_wrist,left_rotation=self.left_frame_for_weapon(moved_origin,moved_weapon,moved_hand,offset)
                if (left_wrist-left_shoulder).length>left_upper.bone.length+left_lower.bone.length-.010:return None
                left=self.grip_elbow("L",left_wrist,left_rotation,left_hint,nearest=True,radial_limit=25.,support_head=True)
                if left is None:return None
                if continuation is not None and (left[2]-continuation["left_elbow"]).length>6.*elapsed+1.e-7:return None
                arms["L"]=(left_shoulder,left[2],left_wrist)
            clearance=self.weapon_clearance(moved_origin,moved_weapon,arms)
            if clearance["body_gap_m"]<.0005 or clearance["floor_gap_m"]<.025:return None
            moved_upper=pivot @ upper_matrix
            parent=upper.parent
            basis=upper.bone.matrix_local.inverted() @ parent.bone.matrix_local @ parent.matrix.inverted() @ moved_upper
            result=dict(pose)
            result[upper.name]=common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in basis.to_quaternion().to_euler("XYZ")),
                location_m=pose[upper.name].location_m,scale=pose[upper.name].scale)
            if held:result,_=self.right_candidate_pose(result,Matrix.Translation(left_wrist) @ left_rotation.to_4x4(),left[2],"L")
            if not safe_edge(result):return None
            cost=(moved_elbow-elbow).length_squared+(moved_wrist-wrist).length_squared
            return cost,result,moved_hand,moved_origin,moved_weapon
        axes=[Vector(v).normalized() for v in ((0,0,1),(1,0,0),(0,1,0),(1,0,1),(-1,0,1))]
        identity=Quaternion();warm=identity
        if continuation is not None and name!="CombatReady":
            parent=upper.parent.matrix.to_quaternion().normalized()
            warm=(parent @ continuation.get("shoulder_correction",identity) @ parent.inverted()).normalized()
        best_rotation=warm;best=candidate(warm)
        if best is not None:
            # Keep the accepted shoulder correction on the current authored
            # elbow/wrist, then approach raw only through a connected safe arc.
            for step in (range(1,13) if rotation_degrees(warm)>1.e-5 else ()):
                rotation=warm.slerp(identity,step/12.)
                trial=candidate(rotation)
                if trial is None:break
                best,best_rotation=trial,rotation
        elif name=="CombatReady":return None
        else:
            # Try small perturbations around the carried correction first.
            # Every resulting rotation still obeys the total envelope to raw.
            bases=[warm,identity] if rotation_degrees(warm)>1.e-5 else [identity]
            for base in bases:
                trial=candidate(base)
                if trial is not None:
                    best,best_rotation=trial,base
                    break
                best_axis=None;best_degrees=0.
                for ring in range(1,math.ceil(limit_degrees)+1):
                    for axis in axes:
                        for sign in (-1.,1.):
                            rotation=Quaternion(axis,math.radians(ring*sign)) @ base
                            trial=candidate(rotation)
                            if trial is not None and (best is None or trial[0]<best[0]):
                                best,best_axis,best_degrees,best_rotation=trial,axis,ring*sign,rotation
                    if best is not None:break
                if best is None:continue
                lo=math.copysign(max(0.,abs(best_degrees)-1.),best_degrees);hi=best_degrees
                for _ in range(10):
                    middle=(lo+hi)*.5;rotation=Quaternion(best_axis,math.radians(middle)) @ base
                    trial=candidate(rotation)
                    if trial is None:lo=middle
                    else:hi=middle;best,best_rotation=trial,rotation
                break
        if best is None:return None
        self._reset_pose();self._apply_pose(best[1])
        for bone in (lower,hand):
            difference=original_local[bone.name].normalized().rotation_difference(bone.rotation_quaternion.normalized())
            angle=2.*math.atan2(math.sqrt(difference.x**2+difference.y**2+difference.z**2),abs(difference.w))
            if angle>2.*math.acos(1.-1.e-7):
                raise ValueError("Shoulder clearance changed local elbow/wrist: "+context)
        actual_origin,actual_weapon=self.weapon_frame()
        hand_error=max(abs(hand.matrix[i][j]-best[2][i][j]) for i in range(4) for j in range(4))
        origin_error=(actual_origin-best[3]).length
        weapon_angle=rotation_degrees(best[4].normalized().rotation_difference(actual_weapon.normalized()))
        if hand_error>1.e-5 or origin_error>1.e-5 or weapon_angle>math.degrees(2.*math.acos(1.-1.e-6)):
            raise ValueError(f"Shoulder clearance lost rigid palm/weapon frame: {context}; matrix={hand_error}, origin={origin_error}, rotation={weapon_angle}")
        return best[1],rotation_degrees(best_rotation)

    def project_weapon_arm(self,pose,continuation=None,context=""):
        """Preserve a nominal held chain; repair its elbow only for a non-nominal wrist."""
        self._reset_pose(); self._apply_pose(pose)
        rig=self.result.rig
        upper,lower,hand=(rig.pose.bones[n+".R"] for n in ("upper_arm","forearm","hand"))
        held=hand.matrix.copy(); origin,weapon=self.weapon_frame()
        axis=(hand.head-upper.head).normalized(); raw_hint=lower.head-upper.head
        pole=(raw_hint-axis*raw_hint.dot(axis)).normalized()
        seconds=float(context.rsplit("@",1)[1]) if "@" in context else 0.
        if continuation is not None:
            pole=continuation["axis"].rotation_difference(axis) @ continuation["pole"]
        elapsed=seconds-continuation["seconds"] if continuation is not None else 0.
        args=dict(nearest=True,radial_limit=25.,support_head=True,
                  maximum_turn=65.,target_pole=raw_hint,weapon_check=True,
                  arm_clearance=.007,
                  previous_elbow=continuation["elbow"] if continuation is not None else None,
                  maximum_elbow_travel=6.*elapsed if continuation is not None else None)
        direction=(hand.head-lower.head).normalized();fingers=(hand.tail-hand.head).normalized()
        delta=held.to_3x3() @ hand.bone.matrix_local.to_3x3().inverted()
        across=(delta @ self.hand_frame("R")[1]).normalized();palm=(delta @ self.hand_frame("R")[2]).normalized()
        nominal=(abs(math.degrees(math.asin(max(-1.,min(1.,direction.dot(across))))))<=25.+1.e-5 and
                 abs(math.degrees(math.atan2(direction.dot(palm),direction.dot(fingers))))<=55.+1.e-5 and
                 math.degrees(direction.angle(fingers))<=65.+1.e-5)
        solved=None if nominal else self.grip_elbow("R",held.translation,held.to_3x3(),pole,**args)
        whole=self.clear_whole_weapon_arm(pose,continuation,seconds,context) if nominal else None
        if solved is None and whole is None:
            trace=[]
            nearest=self.grip_elbow("R",held.translation,held.to_3x3(),pole,
                **dict(args,maximum_turn=180.,target_pole=pole,trace=trace,maximum_elbow_travel=None))
            a,b=upper.bone.length,lower.bone.length;d=(held.translation-upper.head).length
            along=(a*a-b*b+d*d)/(2*d)
            detail=dict(context=context,shoulder=list(upper.head),wrist=list(held.translation),
                        radius=math.sqrt(max(0.,a*a-along*along)),trace=trace,
                        previous_elbow=list(continuation["elbow"]) if continuation is not None else None,
                        maximum_travel_m=6.*elapsed,
                        nearest_angle=None if nearest is None else math.degrees(math.atan2(axis.dot(pole.cross(nearest[3])),pole.dot(nearest[3]))),
                        edge_failure=getattr(self,"last_whole_edge_failure",None))
            target=ROOT/"Captures/CombatTest/recovery-authoring/right-arm-projection-failure.json"
            target.write_text(json.dumps(detail),encoding="utf8")
            raise ValueError(f"No continuous right elbow at fixed weapon frame {context}: nearest={detail['nearest_angle']}, radius={detail['radius']}, edge={detail['edge_failure']}; {target}")
        changed=whole is not None or (solved[2]-lower.head).length>=1.e-5
        if whole is None and changed:self.solve_arm("R",held.translation,held.to_3x3(),solved[3])
        after_origin,after_weapon=self.weapon_frame()
        if whole is None and (max(abs(held[i][j]-hand.matrix[i][j]) for i in range(4) for j in range(4))>1.e-5 or (after_origin-origin).length>1.e-5 or 1.-abs(weapon.dot(after_weapon))>1.e-6):
            raise ValueError("Right elbow projection changed the held weapon: "+context)
        if continuation is not None and elapsed>0.:
            metrics=getattr(self,"right_projection_metrics",{})
            key=("hero:" if self.hero_profile else "npc:")+context.split("@")[0]
            row=metrics.setdefault(key,{})
            values=dict(elbow_speed_m_s=(lower.head-continuation["elbow"]).length/elapsed)
            if whole is not None:values["shoulder_clearance_degrees"]=whole[1]
            for name in ("upper_arm.R","forearm.R"):
                bone=rig.pose.bones[name]
                for label,before,after in zip(("world","local"),continuation["rotations"][name],(bone.matrix.to_quaternion(),bone.rotation_quaternion)):
                    degrees=math.degrees(2.*math.acos(min(1.,abs(before.normalized().dot(after.normalized())))))
                    values[name+"_"+label+"_degrees_per_10ms"]=degrees*.01/elapsed
            for name,value in values.items():
                if value>row.get(name,0.): row[name]=value;row[name+"_time"]=seconds
            self.right_projection_metrics=metrics
        return self.snapshot_pose() if changed else pose

    @staticmethod
    def backhand_return_time(seconds):
        if seconds<=.56: return seconds
        if seconds<=.63:
            t=(seconds-.56)/.07
            # Continue the contact velocity, brake over three authored
            # hundredths of the path, then reverse without a held plateau.
            return (2*t**3-3*t*t+1)*.56+(t**3-2*t*t+t)*.07+(-2*t**3+3*t*t)*.59
        return .59*(1.-dialogue.smooth((seconds-.63)/.65))

    @staticmethod
    def power_source_time(seconds,power=1.):
        # Charge advances the same anatomical preparation arc. The tracks
        # converge with equal velocity before Active, then share the strike
        # and its braking/return; combat rules retain the charged impulse.
        return seconds+power*.18*(1.-dialogue.smooth(seconds/.45)) if seconds<.45 else seconds

    @staticmethod
    def linear_key_pose(a,b,weight):
        """The actual component-linear, sign-compatible FBX quaternion channels."""
        result={}
        for name,value in a.items():
            other=b[name]
            qa=Euler(tuple(math.radians(v) for v in value.rotation_degrees),"XYZ").to_quaternion()
            qb=Euler(tuple(math.radians(v) for v in other.rotation_degrees),"XYZ").to_quaternion()
            if qa.dot(qb)<0.:qb.negate()
            q=Quaternion(tuple(x*(1.-weight)+y*weight for x,y in zip(qa,qb))).normalized()
            result[name]=common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in q.to_euler("XYZ")),
                location_m=tuple(Vector(value.location_m).lerp(Vector(other.location_m),weight)),
                scale=tuple(Vector(value.scale).lerp(Vector(other.scale),weight)))
        return result

    def right_candidate_pose(self,pose,held,elbow,side="R"):
        """Pure FK equivalent of solve_arm, with the same hinge/neutral wrist frames."""
        rig=self.result.rig;upper,lower,hand=(rig.pose.bones[n+"."+side] for n in ("upper_arm","forearm","hand"))
        shoulder=upper.head.copy();wrist=held.translation
        first=(elbow-shoulder).normalized();second=(wrist-elbow).normalized();normal=second.cross(first)
        rest_direction=(upper.bone.tail_local-upper.bone.head_local).normalized()
        if normal.length<.01:
            upper_q=rest_direction.rotation_difference(first) @ upper.bone.matrix_local.to_quaternion()
        else:
            normal.normalize();rest_normal=rest_direction.cross(Vector((0.,.85,-.15)).normalized()).normalized()
            def basis(direction,n):
                n=(n-direction*n.dot(direction)).normalized()
                return Matrix((direction,n.cross(direction),n)).transposed()
            upper_q=(basis(first,normal) @ basis(rest_direction,rest_normal).transposed() @ upper.bone.matrix_local.to_3x3()).to_quaternion()
        relative=lower.bone.matrix_local.to_quaternion().inverted() @ hand.bone.matrix_local.to_quaternion()
        lower_q=held.to_quaternion() @ relative.inverted()
        lower_q=(lower_q @ Vector((0.,1.,0.))).rotation_difference(second) @ lower_q
        matrices={"upper_arm."+side:Matrix.Translation(shoulder) @ upper_q.to_matrix().to_4x4(),
                  "forearm."+side:Matrix.Translation(elbow) @ lower_q.to_matrix().to_4x4(),"hand."+side:held.copy()}
        result=dict(pose)
        for bone in (upper,lower,hand):
            parent=bone.parent
            parent_world=matrices.get(parent.name,parent.matrix)
            basis=bone.bone.matrix_local.inverted() @ parent.bone.matrix_local @ parent_world.inverted() @ matrices[bone.name]
            result[bone.name]=common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in basis.to_quaternion().to_euler("XYZ")),
                location_m=pose[bone.name].location_m,scale=pose[bone.name].scale)
        return result,matrices

    def left_frame_for_weapon(self,origin,weapon,right_hand,offset=SUPPORT_GRIP):
        centre,axis,palm=self.hand_frame("L")
        rig=self.result.rig;left=rig.data.bones["hand.L"];right=rig.data.bones["hand.R"]
        right_delta=right_hand.to_3x3() @ right.matrix_local.to_3x3().inverted()
        target_axis=weapon @ Vector((0.,LEFT_GRIP_AXIS,0.))
        target_palm=-(right_delta @ self.hand_frame("R")[2])
        def frame(forward,up):
            z=forward.normalized();x=up.cross(z).normalized()
            return Matrix((x,z.cross(x),z)).transposed()
        rotation=frame(target_axis,target_palm) @ frame(axis,palm).transposed()
        wrist=origin+weapon @ Vector((0.,offset,0.))-rotation @ (centre-left.head_local)
        return wrist,rotation @ left.matrix_local.to_3x3()

    def solve_backhand_arm_path(self,keys):
        """Choose one coupled arm/grip path; Ready is exact, contact stays physical."""
        rig=self.result.rig;layers=[];costs=[];links=[];step_limit=6./FPS;edge_cache={};parity_checked=False
        edge_rejections={};edge_examples={}
        def quaternion(pose,name):
            return Euler(tuple(math.radians(v) for v in pose[name].rotation_degrees),"XYZ").to_quaternion()
        def rotation_angle(a,b):
            return 2.*math.acos(min(1.,abs(a.normalized().dot(b.normalized()))))
        def check_edge(frame,left_index,right_index,left,right):
            cache_key=(frame,left_index,right_index)
            if cache_key in edge_cache:return edge_cache[cache_key]
            a,b=left["poses"][left_index],right["poses"][right_index]
            previous_matrices=left["matrices"][left_index]
            previous_pose=a;record=dict(valid=True,metrics={})
            for weight in (.25,.5,.75,1.):
                seconds=(frame-1+weight)/FPS
                if weight==1.:
                    posed=b;matrices=right["matrices"][right_index]
                else:
                    posed=self.linear_key_pose(a,b,weight)
                    try:
                        # This call applies the supplied pose itself and
                        # asserts the right hand is untouched by the left solve.
                        self.pin_left_grip(posed,move_right=False,runtime_support=True)
                        matrices={name+"."+side:rig.pose.bones[name+"."+side].matrix.copy() for side in ("R","L") for name in ("upper_arm","forearm","hand")}
                        if weight==.5:record["midpoint"]={name:value.copy() for name,value in matrices.items() if name.endswith(".R")}
                        self.weapon_anatomy(f"CombatBackhand@edge{seconds:.5f}")
                        posed=self.snapshot_pose()
                    except ValueError as error:
                        record.update(valid=False,error=str(error),seconds=seconds);break
                speed=max((matrices["forearm."+side].translation-previous_matrices["forearm."+side].translation).length*FPS*4. for side in ("R","L"))
                if speed>6.+1.e-4:
                    record.update(valid=False,error=f"elbow speed {speed:.6f}",seconds=seconds);break
                values=dict(elbow_speed_m_s=speed)
                for name in ("upper_arm.R","forearm.R","upper_arm.L","forearm.L","hand.R","hand.L"):
                    values[name+"_world_degrees_per_10ms"]=math.degrees(rotation_angle(previous_matrices[name].to_quaternion(),matrices[name].to_quaternion()))*4.
                    values[name+"_local_degrees_per_10ms"]=math.degrees(rotation_angle(quaternion(previous_pose,name),quaternion(posed,name)))*4.
                for name,value in values.items():
                    if value>record["metrics"].get(name,0.):record["metrics"][name]=value;record["metrics"][name+"_time"]=seconds
                previous_matrices=matrices;previous_pose=posed
            if not record["valid"]:
                reason=record["error"].split(":",1)[0];edge_rejections[reason]=edge_rejections.get(reason,0)+1
                edge_examples.setdefault(reason,dict(seconds=record["seconds"],error=record["error"]))
            edge_cache[cache_key]=record
            return record
        for frame,(_,pose) in enumerate(keys):
            self._reset_pose();self._apply_pose(pose)
            if frame==0:self.weapon_anatomy("CombatBackhand@exact-ready",65.)
            upper,lower,hand=(rig.pose.bones[n+".R"] for n in ("upper_arm","forearm","hand"))
            raw=lower.head.copy();held=hand.matrix.copy();axis=(hand.head-upper.head).normalized()
            hint=raw-upper.head;hint=(hint-axis*hint.dot(axis)).normalized()
            circles=self.grip_elbow("R",held.translation,held.to_3x3(),hint,nearest=True,
                radial_limit=25.,support_head=True,maximum_turn=180.,candidate_step_degrees=2.5,ignore_flexion=True) or []
            if frame==0:circles=[c for c in circles if (c[2]-raw).length<1.e-5]
            origin,weapon=self.weapon_frame();shaft=(weapon @ Vector((0.,1.,0.))).normalized()
            delta=held.to_3x3() @ hand.bone.matrix_local.to_3x3().inverted()
            fingers=held.to_quaternion() @ Vector((0.,1.,0.));palm=delta @ self.hand_frame("R")[2]
            left_upper,left_lower=(rig.pose.bones[n+".L"] for n in ("upper_arm","forearm"))
            left_shoulder=left_upper.head.copy();left_hint=left_lower.head-left_shoulder;raw_left=left_lower.head.copy()
            candidates=[];candidate_poses=[];candidate_matrices=[];rolls=[];candidate_hands=[]
            for candidate in circles:
                direction=(held.translation-candidate[2]).normalized()
                flex=math.degrees(math.atan2(direction.dot(palm),direction.dot(fingers)))
                minimum=flex-max(-55.,min(55.,flex))
                # The envelope follows the necessary anatomical correction;
                # zero and neighbouring rolls permit anticipating it in time.
                roll_options=(0.,) if frame==0 else (0.,-5.,5.) if abs(minimum)<1.e-6 else (minimum,minimum+math.copysign(5.,minimum),minimum+math.copysign(10.,minimum))
                for degrees in roll_options:
                    if abs(flex-degrees)>55.+1.e-5:continue
                    roll=Quaternion(shaft,math.radians(degrees))
                    changed=Matrix.Translation(held.translation) @ (roll @ held.to_quaternion()).to_matrix().to_4x4()
                    changed_origin=held.translation+roll @ (origin-held.translation);changed_weapon=roll @ weapon
                    if frame==56 and -(changed_origin+changed_weapon @ Vector((0.,.595,.145))).y<.95:continue
                    left_wrist,left_rotation=self.left_frame_for_weapon(changed_origin,changed_weapon,changed)
                    if (left_wrist-left_shoulder).length>left_upper.bone.length+left_lower.bone.length-.010:continue
                    left=self.grip_elbow("L",left_wrist,left_rotation,left_hint,nearest=True,radial_limit=25.,support_head=True)
                    if left is None:continue
                    clearance=self.weapon_clearance(changed_origin,changed_weapon,arms={"R":(upper.head,candidate[2],held.translation),"L":(left_shoulder,left[2],left_wrist)})
                    if clearance["body_gap_m"]<.0005 or clearance["floor_gap_m"]<.025:continue
                    local,matrices=self.right_candidate_pose(pose,changed,candidate[2])
                    local,left_matrices=self.right_candidate_pose(local,Matrix.Translation(left_wrist) @ left_rotation.to_4x4(),left[2],"L")
                    matrices.update(left_matrices)
                    if frame==0:
                        local=pose;matrices={name+"."+side:rig.pose.bones[name+"."+side].matrix.copy() for side in ("R","L") for name in ("upper_arm","forearm","hand")}
                    candidates.append(candidate);candidate_poses.append(local);candidate_matrices.append(matrices);rolls.append(degrees);candidate_hands.append(changed)
            if candidates and not parity_checked and frame>0:
                self._reset_pose();self._apply_pose(candidate_poses[0])
                error=max(abs(candidate_matrices[0][name][i][j]-rig.pose.bones[name].matrix[i][j]) for name in candidate_matrices[0] for i in range(4) for j in range(4))
                if error>1.e-5:raise ValueError(f"Right candidate pure FK differs from Blender: {error}")
                print(f"Right candidate pure FK/Blender parity {error:.9f}",flush=True);parity_checked=True
            layer=dict(pose=pose,held=held,raw=raw,candidates=candidates,poses=candidate_poses,matrices=candidate_matrices,rolls=rolls,hands=candidate_hands)
            current=[];parents=[]
            for candidate_index,candidate in enumerate(candidates):
                correction=(candidate[2]-raw).length_squared+.10*(candidate_matrices[candidate_index]["forearm.L"].translation-raw_left).length_squared+.03*math.radians(rolls[candidate_index])**2
                if frame==0:
                    current.append(correction);parents.append(-1);continue
                choices=[(cost+.20*(candidate[2]-previous[2]).length_squared+.02*sum(rotation_angle(quaternion(layers[-1]["poses"][index],name),quaternion(candidate_poses[candidate_index],name))**2 for name in ("upper_arm.R","forearm.R","upper_arm.L","forearm.L","hand.R")),index)
                         for index,(previous,cost) in enumerate(zip(layers[-1]["candidates"],costs[-1]))
                         if math.isfinite(cost) and (candidate[2]-previous[2]).length<=step_limit+1.e-7 and (candidate_matrices[candidate_index]["forearm.L"].translation-layers[-1]["matrices"][index]["forearm.L"].translation).length<=step_limit+1.e-7]
                best=(math.inf,-1)
                for choice in sorted(choices):
                    if check_edge(frame,choice[1],candidate_index,layers[-1],layer)["valid"]:best=choice;break
                current.append(best[0]+correction);parents.append(best[1])
            layers.append(layer);costs.append(current);links.append(parents)
            if not any(math.isfinite(value) for value in current):
                previous=layers[-2]["candidates"] if frame else []
                reachable=[i for i,cost in enumerate(costs[-2]) if math.isfinite(cost)] if frame else []
                detail=dict(profile="hero" if self.hero_profile else "npc",seconds=frame/FPS,
                    pinned_endpoint=frame==0,step_limit_m=step_limit,
                    candidates=[list(c[2]) for c in candidates],
                    reachable_previous=[list(previous[i][2]) for i in reachable],
                    minimum_transition_m=min(((c[2]-previous[i][2]).length for c in candidates for i in reachable),default=None),
                    edge_rejections=edge_rejections,edge_examples=edge_examples,roll_degrees=rolls,
                    layers=[dict(seconds=i/FPS,count=len(row["candidates"]),reachable=sum(math.isfinite(v) for v in costs[i])) for i,row in enumerate(layers)])
                target=ROOT/"Captures/CombatTest/recovery-authoring/backhand-arm-path-failure.json"
                target.write_text(json.dumps(detail),encoding="utf8")
                raise ValueError(f"Disconnected backhand right-arm path at {frame/FPS:.3f}: {len(candidates)} candidates, nearest {detail['minimum_transition_m']}; {target}")
            if frame%10==0 or frame>=50:print(f"Backhand DAG {frame/FPS:.2f}: candidates={len(candidates)}, reachable={sum(math.isfinite(v) for v in current)}, edges={len(edge_cache)}",flush=True)
        selected=[0]*len(layers);selected[-1]=min(range(len(costs[-1])),key=lambda i:costs[-1][i])
        for frame in range(len(layers)-1,0,-1): selected[frame-1]=links[frame][selected[frame]]
        solved=[];previous=None;edge_speeds=[0.];metrics=dict(maximum_elbow_speed_m_s=0.,maximum_correction_m=0.)
        for frame,(layer,index) in enumerate(zip(layers,selected)):
            pose=layer["poses"][index]
            self._reset_pose();self._apply_pose(pose)
            candidate=layer["candidates"][index];held=layer["hands"][index];hand=rig.pose.bones["hand.R"]
            if max(abs(held[i][j]-hand.matrix[i][j]) for i in range(4) for j in range(4))>1.e-5 or (hand.head-layer["held"].translation).length>1.e-5:
                raise ValueError(f"Coupled arm solve moved the wrist or lost its hand-derived frame at {frame/FPS}")
            if abs(layer["rolls"][index])>metrics.get("maximum_hand_roll_degrees",0.):metrics["maximum_hand_roll_degrees"]=abs(layer["rolls"][index]);metrics["maximum_hand_roll_time"]=frame/FPS
            if frame==56:
                origin,rotation=self.weapon_frame()
                metrics.update(contact_reach_m=-(origin+rotation @ Vector((0.,.595,.145))).y,contact_hand_roll_degrees=layer["rolls"][index])
            state=self.weapon_arm_state(pose,frame/FPS)
            metrics["maximum_correction_m"]=max(metrics["maximum_correction_m"],(state["elbow"]-layer["raw"]).length)
            if previous is not None:
                metrics["maximum_elbow_speed_m_s"]=max(metrics["maximum_elbow_speed_m_s"],(state["elbow"]-previous["elbow"]).length*FPS)
                for bone in state["rotations"]:
                    for label,a,b in zip(("world","local"),previous["rotations"][bone],state["rotations"][bone]):
                        angle=math.degrees(2.*math.acos(min(1.,abs(a.normalized().dot(b.normalized())))))
                        key=bone+"_"+label+"_degrees_per_10ms"
                        if angle>metrics.get(key,0.):metrics[key]=angle;metrics[key+"_time"]=frame/FPS
                edge=edge_cache[(frame,selected[frame-1],index)]
                edge_speeds.append(edge["metrics"]["elbow_speed_m_s"])
                for key,value in edge["metrics"].items():
                    if key.endswith("_time"):continue
                    if value>metrics.get(key,0.):metrics[key]=value;metrics[key+"_time"]=edge["metrics"][key+"_time"]
                if frame==48:self.backhand_edge_sample=((frame-.5)/FPS,edge["midpoint"])
            previous=state;solved.append((keys[frame][0],pose))
        print("Backhand whole-curve right arm "+json.dumps(metrics),flush=True)
        self.backhand_edge_speeds=edge_speeds
        return solved

    def sample_forward_keys(self,keys,seconds):
        frame=max(0.,min(len(keys)-1.,seconds*FPS))
        left=math.floor(frame); right=min(left+1,len(keys)-1)
        if right==left or frame-left<1.e-10:return dict(keys[left][1])
        return self.linear_key_pose(keys[left][1],keys[right][1],frame-left)

    def measure_recoil_speeds(self,reference):
        """Measure the final presented elbows along the actual linear key edges."""
        rig=self.result.rig;rig.animation_data.action=None
        self.pin_left_grip(reference[0][1],move_right=False,runtime_support=True)
        previous={side:rig.pose.bones["forearm."+side].head.copy() for side in ("R","L")}
        speeds=[0.]
        for (_,left),(_,right) in zip(reference,reference[1:]):
            maximum=0.
            for weight in (.25,.5,.75,1.):
                pose=self.linear_key_pose(left,right,weight) if weight<1. else right
                self.pin_left_grip(pose,move_right=False,runtime_support=True)
                current={side:rig.pose.bones["forearm."+side].head.copy() for side in ("R","L")}
                maximum=max(maximum,max((current[side]-previous[side]).length*FPS*4. for side in current))
                previous=current
            speeds.append(maximum)
        return speeds

    def recoil_keys(self,duration,reference,edge_speeds,name):
        """Reverse the proven full-body prefix with a continuous measured clock."""
        # Each edge already measured both actual elbows at quarter-frame FK
        # samples. Give fast edges enough time, then stretch to the clip's
        # existing duration. The C1 clock removes the old held recoil plateau.
        speeds=list(reversed(edge_speeds[1:57]))
        intervals=[max(1.e-5,speed/FPS/6.) for speed in speeds]
        def derivatives(spans):
            chords=[1./FPS/value for value in spans]
            return [0.]+[min(a,b) for a,b in zip(chords,chords[1:])]+[0.]
        def maximum_rate(span,start,end):
            chord=1./FPS/span
            a=-6.*chord+3.*(start+end);b=6.*chord-4.*start-2.*end
            values=[start,end]
            if abs(a)>1.e-12:
                t=-b/(2.*a)
                if 0.<t<1.:values.append(a*t*t+b*t+start)
            return max(values)
        for _ in range(24):
            tangent=derivatives(intervals)
            rates=[maximum_rate(span,a,b) for span,a,b in zip(intervals,tangent,tangent[1:])]
            factors=[max(1.,speed*rate/6.) for speed,rate in zip(speeds,rates)]
            if max(factors)<=1.+1.e-7:break
            intervals=[span*factor for span,factor in zip(intervals,factors)]
        required=sum(intervals)
        if required>duration+1.e-6:
            raise ValueError(f"{name} needs {required:.6f}s for the measured continuous path; clip is {duration:.3f}s")
        intervals=[span*duration/required for span in intervals]
        tangent=derivatives(intervals);times=[0.]
        for span in intervals:times.append(times[-1]+span)
        count=round(duration*FPS);keys=[]
        for frame in range(count+1):
            second=frame/FPS
            if frame==0:source=.56
            elif frame==count:source=0.
            else:
                index=next(i for i in range(len(intervals)) if second<=times[i+1])
                u=(second-times[index])/intervals[index]
                travel=(-2*u**3+3*u*u)/FPS+(u**3-2*u*u+u)*intervals[index]*tangent[index]+(u**3-u*u)*intervals[index]*tangent[index+1]
                source=(56-index)/FPS-travel
            keys.append((frame/count,self.sample_forward_keys(reference,source)))
        print(name+" measured clock "+json.dumps(dict(duration_seconds=duration,minimum_measured_seconds=required,
            maximum_bound_m_s=max(speed*maximum_rate(span,a,b) for speed,span,a,b in zip(speeds,intervals,tangent,tangent[1:])))),flush=True)
        return keys

    def clear_retimed_keys(self,keys,duration,name,start=0):
        """Repair only the written chords of a time-remapped proven path."""
        corrected=list(keys[:start]);maximum=0.
        previous=self.weapon_arm_state(keys[start-1][1],keys[start-1][0]*duration) if start else None
        for index in range(start,len(keys)):
            time,raw=keys[index];seconds=time*duration;context=f"{name}@{seconds:.6f}"
            self._reset_pose();self._apply_pose(raw)
            if index in (0,len(keys)-1):
                self.check_weapon_arm_edge(raw,previous,seconds,context,raw)
                posed=raw
            else:
                cleared=self.clear_whole_weapon_arm(raw,previous,seconds,context)
                if cleared is None:raise ValueError(f"No safe resampling edge {context}: {self.last_whole_edge_failure}")
                posed,angle=cleared;maximum=max(maximum,angle)
            corrected.append((time,posed))
            previous=self.weapon_arm_state(posed,seconds,raw)
        print(f"{name} resampling shoulder correction {maximum:.6f} degrees",flush=True)
        return corrected

    def author_support_grip(self, pose, offset=SUPPORT_GRIP, pole=None, context="", continuation=None):
        """Bake the smallest palm swing that keeps both supporting wrists real.

        This is authoring only. The mixed-release oracle uses an unchanged
        right chain and the runtime's actual left elbow hint instead.
        """
        continuous=True
        try: posed=self.pin_left_grip(pose,offset,pole,radial_limit=24.,continuation=continuation)
        except ValueError:
            if continuation is None: raise
            continuous=False
            posed=self.pin_left_grip(pose,offset,pole,radial_limit=24.)
        if continuous and posed is pose: return posed  # Preserve exact named contact frames.
        metric=self.guard_metrics(posed)
        if continuous and abs(metric["wrist_components"][0]["deviation"])<=25.01 and abs(metric["wrist_components"][1]["deviation"])<=24.1 and max(abs(c["flexion"]) for c in metric["wrist_components"])<=55.:
            return posed
        rig=self.result.rig; right=rig.pose.bones["hand.R"]; lower=rig.pose.bones["forearm.R"]
        hand_matrix=right.matrix.copy(); elbow=lower.head.copy(); wrist=right.head.copy()
        palm=(hand_matrix.to_3x3() @ right.bone.matrix_local.to_3x3().inverted() @ self.hand_frame("R")[2]).normalized()
        rest_relative=lower.bone.matrix_local.to_quaternion().inverted() @ right.bone.matrix_local.to_quaternion()
        fore_axis=(wrist-elbow).normalized()
        sign=-math.copysign(1.,metric["wrist_components"][1]["deviation"])
        def candidate(degrees):
            self._reset_pose(); self._apply_pose(posed)
            q=Quaternion(palm,math.radians(degrees)) @ hand_matrix.to_quaternion()
            held=Matrix.Translation(wrist) @ q.to_matrix().to_4x4()
            neutral=q @ rest_relative.inverted()
            neutral=(neutral @ Vector((0.,1.,0.))).rotation_difference(fore_axis) @ neutral
            lower.matrix=Matrix.Translation(elbow) @ neutral.to_matrix().to_4x4()
            bpy.context.view_layer.update(); right.matrix=held; bpy.context.view_layer.update()
            target,_=self.left_contact_frame(offset)
            left,fore=(rig.pose.bones[n+".L"] for n in ("upper_arm","forearm"))
            if (target-left.head).length>left.bone.length+fore.bone.length-.010: return None
            try: result=self.pin_left_grip(self.snapshot_pose(),offset,pole,move_right=False,radial_limit=24.,continuation=continuation)
            except ValueError: return None
            measured=self.guard_metrics(result); clearance=self.weapon_clearance()
            if max(measured["wrist_angles"])>65. or abs(measured["wrist_components"][0]["deviation"])>24.8 or abs(measured["wrist_components"][1]["deviation"])>24.1 or max(abs(c["flexion"]) for c in measured["wrist_components"])>55. or clearance["body_gap_m"]<.0005 or clearance["floor_gap_m"]<.025:
                return None
            return result
        for magnitude in range(1,31):
            direction=sign
            result=candidate(direction*magnitude)
            if result is None and not continuous:
                direction=-sign
                result=candidate(direction*magnitude)
            if result is None: continue
            lo,hi=magnitude-1.,float(magnitude)
            for _ in range(9):
                mid=(lo+hi)*.5; trial=candidate(direction*mid)
                if trial is None: lo=mid
                else: hi=mid; result=trial
            if hi>getattr(self,"maximum_baked_palm_swing",0.):
                self.maximum_baked_palm_swing=hi; self.maximum_baked_palm_swing_context=context
            self._reset_pose(); self._apply_pose(result)
            return result
        raise ValueError(f"No continuous anatomical supporting grip {context}: previous_branch={continuous}, {metric}")

    def place_guard_grip(self, pose, height, roll_degrees):
        """Raise the physical grip and roll the palms without tilting the bar."""
        self._reset_pose(); self._apply_pose(pose)
        rig = self.result.rig
        hand = rig.pose.bones["hand.R"]
        origin, weapon_rotation = self.weapon_frame()
        origin.z = height * self.scale
        axis = weapon_rotation @ Vector((0, 1, 0))
        roll = Quaternion(axis, math.radians(roll_degrees)).to_matrix()
        rotation = roll @ hand.matrix.to_3x3()
        delta = rotation @ hand.bone.matrix_local.to_3x3().inverted()
        wrist = origin - delta @ (self.hand_frame("R")[0] - hand.bone.head_local)
        self.solve_arm("R", wrist, rotation, (-.20, .10, -1.))
        left_wrist, left_rotation = self.left_contact_frame(BLOCK_GRIP)
        self.solve_arm("L", left_wrist, left_rotation, GUARD_LEFT_ELBOW_POLE)
        actual, actual_rotation = self.weapon_frame()
        if (actual-origin).length > .00001 or (actual_rotation @ Vector((0, 1, 0))).dot(axis) < .99999:
            raise ValueError("Guard wrist roll changed the physical grip or shaft direction")
        for side in ("R", "L"):
            upper, forearm, hand = (rig.pose.bones[name + "." + side] for name in ("upper_arm", "forearm", "hand"))
            margin = upper.bone.length + forearm.bone.length - (hand.head-upper.head).length
            if margin < .010:
                raise ValueError(f"High guard {side} wrist leaves less than10mm arm margin: {margin:.4f}")
        return self.snapshot_pose()

    def guard_metrics(self, pose):
        self._reset_pose(); self._apply_pose(pose)
        rig = self.result.rig
        angles = []; components=[]
        for side in ("R", "L"):
            forearm, hand = (rig.pose.bones[name + "." + side] for name in ("forearm", "hand"))
            angles.append(math.degrees((hand.head-forearm.head).angle(hand.tail-hand.head)))
            direction=(hand.head-forearm.head).normalized(); fingers=(hand.tail-hand.head).normalized()
            delta=hand.matrix.to_3x3() @ hand.bone.matrix_local.to_3x3().inverted()
            across=(delta @ self.hand_frame(side)[1]).normalized(); palm=(delta @ self.hand_frame(side)[2]).normalized()
            rest_relative=forearm.bone.matrix_local.to_quaternion().inverted() @ hand.bone.matrix_local.to_quaternion()
            relative=forearm.matrix.to_quaternion().inverted() @ hand.matrix.to_quaternion() @ rest_relative.inverted()
            twist=math.degrees(2.*math.atan2(relative.y,relative.w)); twist=(twist+180.)%360.-180.
            components.append(dict(flexion=math.degrees(math.atan2(direction.dot(palm),direction.dot(fingers))),
                deviation=math.degrees(math.asin(max(-1.,min(1.,direction.dot(across))))),axial_twist=twist))
        origin, _ = self.weapon_frame()
        return dict(height=round(origin.z/self.scale, 4), wrist_angles=[round(value, 2) for value in angles],wrist_components=components)

    def align_right_grip(self, pose, support=True, left_pole=None, frame=None, right_pole=None, support_offset=SUPPORT_GRIP,wrist_limit=65.,preserve_roll=False):
        """Keep the cylinder contact/axis while the hand follows its forearm."""
        self._reset_pose(); self._apply_pose(pose)
        rig=self.result.rig
        upper,forearm,hand=(rig.pose.bones[n+".R"] for n in ("upper_arm","forearm","hand"))
        original_origin,original_weapon=self.weapon_frame()
        origin,weapon=frame if frame is not None else (original_origin,original_weapon)
        axis=(weapon @ Vector((0.,1.,0.))).normalized()
        pole=Vector(right_pole) if right_pole is not None else (forearm.head-upper.head).normalized()
        rest=hand.bone; centre,rest_axis,_=self.hand_frame("R")
        def frame(shaft,fingers):
            y=(fingers-shaft*fingers.dot(shaft)).normalized()
            x=y.cross(shaft).normalized()
            return Matrix((x,shaft.cross(x),shaft)).transposed()
        rest_frame=frame(rest_axis,(rest.tail_local-rest.head_local).normalized())
        neutral_relative=forearm.bone.matrix_local.to_quaternion().inverted() @ rest.matrix_local.to_quaternion()
        initial=weapon @ original_weapon.inverted() @ hand.matrix.to_quaternion()
        left_rest=rig.data.bones["hand.L"]
        left_centre,left_axis,left_palm=self.hand_frame("L")
        def palm_frame(forward,up):
            z=forward.normalized(); x=up.cross(z).normalized()
            return Matrix((x,z.cross(x),z)).transposed()
        def elbow_at(shoulder,wrist,a,b,bend_pole):
            delta=wrist-shoulder; distance=delta.length
            if not abs(a-b)+.001 < distance < a+b-.001: return None
            direction=delta.normalized()
            bend=bend_pole-direction*bend_pole.dot(direction)
            if bend.length<.01: return None
            along=(a*a-b*b+distance*distance)/(2*distance)
            return shoulder+direction*along+bend.normalized()*math.sqrt(max(0.,a*a-along*along))
        best=None; rejected={"right":0,"left_reach":0,"left":0,"collision":0}; closest=None
        for degrees in ((0,) if preserve_roll else range(-180,181,6)):
            rotation=(Quaternion(axis,math.radians(degrees)) @ initial).to_matrix()
            delta=rotation @ rest.matrix_local.to_3x3().inverted()
            wrist=origin-delta @ (centre-rest.head_local)
            right_choice=self.grip_elbow("R",wrist,rotation,pole,total_limit=wrist_limit,support_head=getattr(self,"grip_backhand",False))
            if right_choice is None: rejected["right"]+=1; continue
            angle=right_choice[1]
            candidate_arms={"R":(upper.head,right_choice[2],wrist)}
            left_angle=0.
            if support:
                target_palm=-(delta @ self.hand_frame("R")[2])
                left_delta=palm_frame(axis*LEFT_GRIP_AXIS,target_palm) @ palm_frame(left_axis,left_palm).transposed()
                left_wrist=origin+axis*support_offset-left_delta @ (left_centre-left_rest.head_local)
                left_upper,left_forearm=(rig.pose.bones[n+".L"] for n in ("upper_arm","forearm"))
                if (left_wrist-left_upper.head).length > left_upper.bone.length+left_forearm.bone.length-(.030 if self.hero_profile else .015): rejected["left_reach"]+=1; continue
                left_choice=self.grip_elbow("L",left_wrist,left_delta @ left_rest.matrix_local.to_3x3(),Vector(left_pole or DEFAULT_LEFT_ELBOW_POLE),total_limit=wrist_limit,support_head=getattr(self,"grip_backhand",False))
                if left_choice is None: rejected["left"]+=1; continue
                left_angle=left_choice[1]
                candidate_arms["L"]=(left_upper.head,left_choice[2],left_wrist)
            clearance=self.weapon_clearance(origin,Quaternion(axis,math.radians(degrees)) @ weapon,candidate_arms)
            if closest is None or clearance["body_gap_m"]>closest["body_gap_m"]: closest=clearance
            if clearance["body_gap_m"] < .005 or clearance["floor_gap_m"] < .025: rejected["collision"]+=1; continue
            branch_cost=(right_choice[0]-angle)+(left_choice[0]-left_angle if support else 0.)
            score=max(angle,left_angle)+.15*(angle+left_angle)+branch_cost+abs(degrees)*.006
            candidate=(score,abs(degrees),wrist,rotation,right_choice[3],left_choice[3] if support else None)
            if best is None or candidate[:2]<best[:2]: best=candidate
        if best is None:
            # Preserve the actual failed circles for diagnosis; this never
            # changes the chosen pose or retries another dock.
            failure=dict(profile="hero" if self.hero_profile else "npc",origin=list(origin),axis=list(axis),rejected=rejected,rolls=[])
            for degrees in ((0,) if preserve_roll else range(-180,181,6)):
                rotation=(Quaternion(axis,math.radians(degrees)) @ initial).to_matrix()
                delta=rotation @ rest.matrix_local.to_3x3().inverted()
                wrist=origin-delta @ (centre-rest.head_local)
                trace=[]
                choice=self.grip_elbow("R",wrist,rotation,pole,total_limit=wrist_limit,support_head=getattr(self,"grip_backhand",False),trace=trace)
                record=dict(roll=degrees,right_wrist=list(wrist),right=trace)
                if choice is not None and support:
                    target_palm=-(delta @ self.hand_frame("R")[2])
                    left_delta=palm_frame(axis*LEFT_GRIP_AXIS,target_palm) @ palm_frame(left_axis,left_palm).transposed()
                    left_wrist=origin+axis*support_offset-left_delta @ (left_centre-left_rest.head_local)
                    left_trace=[]
                    self.grip_elbow("L",left_wrist,left_delta @ left_rest.matrix_local.to_3x3(),Vector(left_pole or DEFAULT_LEFT_ELBOW_POLE),total_limit=wrist_limit,support_head=getattr(self,"grip_backhand",False),trace=left_trace)
                    record.update(left_wrist=list(left_wrist),left=left_trace)
                failure["rolls"].append(record)
            diagnostic=ROOT/"Captures/CombatTest/recovery-authoring/grip-failure.json"
            diagnostic.parent.mkdir(parents=True,exist_ok=True)
            diagnostic.write_text(json.dumps(failure),encoding="utf8")
            raise ValueError(f"No reachable anatomical grip roll: {rejected}, {closest}, O={tuple(origin)}; full circles: {diagnostic}")
        _,_,wrist,rotation,pole,left_pole=best
        self.solve_arm("R",wrist,rotation,pole)
        # A single local hinge frame must be shared by light/heavy variants.
        # Interpolating different lower-arm axial frames moves the wrist out
        # of the other hand's reach at intermediate charge powers.
        if support:
            left_wrist,left_rotation=self.left_contact_frame(support_offset)
            self.solve_arm("L",left_wrist,left_rotation,left_pole)
        actual_origin,actual_weapon=self.weapon_frame()
        if (actual_origin-origin).length>.0001 or (actual_weapon @ Vector((0.,1.,0.))-axis).length>.0001:
            raise ValueError("Anatomical wrist alignment changed the intended cylinder contact")
        return self.snapshot_pose()

    @staticmethod
    def segment_distance(a,b,c,d):
        u,v,r=b-a,d-c,a-c
        aa,ee=u.dot(u),v.dot(v)
        if aa<1.e-12: return (a-(c+v*max(0.,min(1.,v.dot(r)/max(ee,1.e-12))))).length
        if ee<1.e-12: return (c-(a+u*max(0.,min(1.,-u.dot(r)/aa)))).length
        bb,cc,ff=u.dot(v),u.dot(r),v.dot(r)
        denominator=aa*ee-bb*bb
        s=max(0.,min(1.,(bb*ff-cc*ee)/denominator)) if denominator>1.e-12 else 0.
        t=(bb*s+ff)/ee
        if t<0.: t=0.; s=max(0.,min(1.,-cc/aa))
        elif t>1.: t=1.; s=max(0.,min(1.,(bb-cc)/aa))
        return (a+u*s-c-v*t).length

    def grip_elbow(self,side,wrist,rotation,preferred=None,nearest=False,total_limit=65.,radial_limit=None,support_head=False,maximum_turn=65.,target_pole=None,trace=None,weapon_check=False,previous_elbow=None,maximum_elbow_travel=None,candidate_step_degrees=None,ignore_flexion=False,arm_clearance=.005):
        """Nearest anatomical elbow on its length sphere, outside the torso."""
        rig=self.result.rig
        upper,lower=(rig.pose.bones[n+"."+side] for n in ("upper_arm","forearm"))
        shoulder=upper.head.copy(); a,b=upper.bone.length,lower.bone.length
        delta=wrist-shoulder; distance=delta.length
        if not abs(a-b)+.001<distance<a+b-.001: return None
        axis=delta.normalized(); along=(a*a-b*b+distance*distance)/(2*distance)
        centre=shoulder+axis*along; radius=math.sqrt(max(0.,a*a-along*along))
        pole=Vector(preferred) if preferred is not None else lower.head-shoulder
        seed=getattr(self,"grip_seed",{}).get(side) if not nearest else None
        if seed is not None: pole=seed[0].rotation_difference(axis) @ seed[1]
        pole=(pole-axis*pole.dot(axis)).normalized()
        fingers=rotation @ Vector((0.,1.,0.))
        desired=(-fingers+axis*fingers.dot(axis)).normalized()
        optimal=math.atan2(axis.dot(pole.cross(desired)),pole.dot(desired))
        target_angle=0.
        if target_pole is not None:
            target=(target_pole-axis*target_pole.dot(axis)).normalized()
            target_angle=math.atan2(axis.dot(pole.cross(target)),pole.dot(target))
        torso=self.body_boxes()
        left_shoulder=rig.pose.bones["upper_arm.L"].head
        right_shoulder=rig.pose.bones["upper_arm.R"].head
        shoulder_mid=(left_shoulder+right_shoulder)*.5
        lateral=(left_shoulder-right_shoulder).normalized()
        forward=lateral.cross(Vector((0.,0.,1.))).normalized()
        best=None
        hand_rest=rig.data.bones["hand."+side]
        hand_delta=rotation @ hand_rest.matrix_local.to_3x3().inverted()
        across=(hand_delta @ self.hand_frame(side)[1]).normalized()
        palm=(hand_delta @ self.hand_frame(side)[2]).normalized()
        if support_head:
            head=rig.pose.bones["head"]
            head_rotation=head.matrix.to_quaternion() @ head.bone.matrix_local.to_quaternion().inverted()
            head_a=head.head+head_rotation @ Vector((0.,0.,.10))
            head_b=head.head+head_rotation @ Vector((0.,0.,.20))
        def evaluate(angle):
            if nearest and abs(angle)>math.radians(maximum_turn)+1.e-10: return None
            bend=Quaternion(axis,angle) @ pole; elbow=centre+bend*radius
            if trace is not None:
                direction=(wrist-elbow).normalized()
                trace.append(dict(angle=math.degrees(angle),shoulder=list(shoulder),elbow=list(elbow),
                    radial=math.degrees(math.asin(max(-1.,min(1.,direction.dot(across))))),
                    flexion=math.degrees(math.atan2(direction.dot(palm),direction.dot(fingers))),
                    total=math.degrees(direction.angle(fingers)),
                    travel_m=(elbow-previous_elbow).length if previous_elbow is not None else None,
                    core=min(min(self.segment_box_distance(shoulder.lerp(elbow,.65),elbow,c,q,h),self.segment_box_distance(elbow,wrist,c,q,h)) for _,c,q,h in torso),
                    head=min(self.segment_distance(shoulder.lerp(elbow,.65),elbow,head_a,head_b),self.segment_distance(elbow,wrist,head_a,head_b))-.12 if support_head else None))
            if maximum_elbow_travel is not None and (elbow-previous_elbow).length>maximum_elbow_travel+1.e-7: return None
            if getattr(self,"grip_ready_pose",False) and not nearest:
                if elbow.z>shoulder.z-.12*self.scale or (elbow-shoulder).dot(forward)<.015*self.scale or (elbow-shoulder_mid).dot(lateral)*(1. if side=="L" else -1.)<.02*self.scale:
                    return None
            if getattr(self,"grip_forward_elbows",False) and not nearest and elbow.y>shoulder.y-.03: return None
            if getattr(self,"grip_forward_elbows",False) and not nearest and side=="R" and elbow.z>shoulder.z-.08: return None
            # The upper arm joins the jacket at the armpit, and an elbow can
            # rest against it. Reject a bone centreline inside the torso;
            # the held object's separate oracle retains its full radii.
            if any(self.segment_box_distance(shoulder.lerp(elbow,.65),elbow,c,q,h)<arm_clearance or
                   self.segment_box_distance(elbow,wrist,c,q,h)<arm_clearance for _,c,q,h in torso): return None
            if support_head and (self.segment_distance(shoulder.lerp(elbow,.65),elbow,head_a,head_b)<.12+arm_clearance or
                                 self.segment_distance(elbow,wrist,head_a,head_b)<.12+arm_clearance): return None
            wrist_angle=math.degrees((wrist-elbow).angle(fingers))
            lower_axis=(wrist-elbow).normalized()
            wrist_deviation=math.degrees(math.asin(max(-1.,min(1.,lower_axis.dot(across)))))
            flexion=math.degrees(math.atan2(lower_axis.dot(palm),lower_axis.dot(fingers)))
            deviation_limit=radial_limit if radial_limit is not None else 24.8 if nearest else getattr(self,"grip_deviation_limit",25.)
            if abs(wrist_deviation)>deviation_limit or (not ignore_flexion and (abs(flexion)>55. or wrist_angle>total_limit)): return None
            if weapon_check and self.weapon_clearance(arms={side:(shoulder,elbow,wrist)})["body_gap_m"]<.0005: return None
            deviation=abs(math.degrees(math.atan2(math.sin(angle),math.cos(angle))))
            distance=abs(math.degrees(math.atan2(math.sin(angle-target_angle),math.cos(angle-target_angle))))
            score=distance+.002*wrist_angle if nearest else wrist_angle+.35*deviation
            return score,wrist_angle,elbow,bend
        if candidate_step_degrees is not None:
            count=round(360./candidate_step_degrees)
            grid=[(-math.pi+2.*math.pi*i/count) for i in range(count+1)]
            values=[evaluate(angle) for angle in grid]
            result=[value for value in values if value is not None]
            result.extend(value for angle in (0.,optimal,target_angle) if (value:=evaluate(angle)) is not None)
            for a,b,left,right in zip(grid,grid[1:],values,values[1:]):
                if (left is None)==(right is None): continue
                invalid,valid=(a,b) if left is None else (b,a)
                accepted=right if left is None else left
                for _ in range(9):
                    middle=(invalid+valid)*.5;value=evaluate(middle)
                    if value is None: invalid=middle
                    else: valid=middle;accepted=value
                result.append(accepted)
            unique={tuple(round(v,6) for v in candidate[2]):candidate for candidate in result}
            return list(unique.values())
        if nearest:
            best=evaluate(target_angle)
            if best is not None: return best
        bounded_target=max(-math.radians(maximum_turn),min(math.radians(maximum_turn),target_angle))
        for angle in (bounded_target,optimal,-math.radians(maximum_turn),math.radians(maximum_turn),
                      *(math.radians(offset) for offset in range(-180,181,5))):
            trial=evaluate(angle)
            if trial is not None and (best is None or trial[0]<best[0]): best=trial
        refinement_step=math.radians(5.)
        if nearest and best is None:
            # Two rejected coarse endpoints can enclose a valid interval:
            # one violates wrist deviation, the other torso clearance.
            # Match CombatSupportGrip's bounded nested midpoint search.
            for level in range(1,5):
                step=5./(2**level)
                for index in range(math.ceil(-maximum_turn/step),math.floor(maximum_turn/step)+1):
                    if index%2==0:continue
                    trial=evaluate(math.radians(index*step))
                    if trial is not None and (best is None or trial[0]<best[0]):best=trial
                if best is not None:
                    refinement_step=math.radians(step)
                    break
        if nearest and best is not None:
            hi=math.atan2(axis.dot(pole.cross(best[3])),pole.dot(best[3]))
            target_delta=math.atan2(math.sin(target_angle-hi),math.cos(target_angle-hi))
            lo=hi+math.copysign(min(abs(target_delta),refinement_step),target_delta)
            for _ in range(12):
                mid=(lo+hi)*.5; trial=evaluate(mid)
                if trial is None: lo=mid
                else: hi=mid; best=trial
        return best

    def body_boxes(self):
        rig=self.result.rig; result=[]
        for name,start,end,width,depth in (("lower torso","spine","chest",.32,.19),("chest","chest","neck",.38,.20)):
            bone=rig.pose.bones[start]; rest=bone.bone
            rest_axis=rig.data.bones[end].head_local-rest.head_local
            orientation=bone.matrix.to_quaternion() @ rest.matrix_local.to_quaternion().inverted() @ Vector((0.,0.,1.)).rotation_difference(rest_axis.normalized())
            result.append((name,(bone.head+rig.pose.bones[end].head)*.5,orientation,Vector((width*.5,depth*.5,rest_axis.length*.5))))
        bone=rig.pose.bones["pelvis"]; orientation=bone.matrix.to_quaternion() @ bone.bone.matrix_local.to_quaternion().inverted()
        result.append(("pelvis",bone.head+orientation @ Vector((0.,0.,.06)),orientation,Vector((.14,.10,.10))))
        return result

    @staticmethod
    def segment_box_distance(start,end,centre,rotation,half):
        inverse=rotation.inverted(); a=inverse @ (start-centre); d=inverse @ (end-start)
        breaks=[0.,1.]
        for i in range(3):
            if abs(d[i])>1.e-9:
                breaks.extend(t for bound in (-half[i],half[i]) if 0.<(t:=(bound-a[i])/d[i])<1.)
        breaks=sorted(breaks); best=100.
        for lo,hi in zip(breaks,breaks[1:]):
            mid=(lo+hi)*.5; aa=bb=0.
            for i in range(3):
                value=a[i]+d[i]*mid
                if abs(value)>half[i]:
                    c=a[i]-math.copysign(half[i],value); aa+=d[i]*d[i]; bb+=c*d[i]
            t=max(lo,min(hi,-bb/aa)) if aa>1.e-12 else mid
            distance=sum(max(0.,abs(a[i]+d[i]*t)-half[i])**2 for i in range(3))
            best=min(best,distance)
        return math.sqrt(best)

    def weapon_clearance(self, origin=None, rotation=None, arms=None):
        """Full bent steel path, radii and end boxes against the visible body's capsules."""
        rig=self.result.rig
        if origin is None: origin,rotation=self.weapon_frame()
        arms=arms or {}
        points=[origin+rotation @ Vector(p) for p in CROWBAR_PATH]
        bars=[(a,b,.019) for a,b in zip(points,points[1:])]
        bars.append((origin+rotation@Vector((0.,-.075,0.)),origin+rotation@Vector((0.,.08,0.)),.024))
        for centre,radius in (((0.,-.119,.012),.03671),((0.,.587,.152),.02802)):
            point=origin+rotation @ Vector(centre); bars.append((point,point,radius))
        capsules=[]
        head=rig.pose.bones["head"]
        head_rotation=head.matrix.to_quaternion() @ head.bone.matrix_local.to_quaternion().inverted()
        capsules.append(("head",head.head+head_rotation@Vector((0.,0.,-.02)),head.head+head_rotation@Vector((0.,0.,.32)),.12))
        for side in ("L","R"):
            for a,b,radius in (("upper_arm","forearm",.06),("forearm","hand",.05),("thigh","shin",.085),("shin","foot",.07)):
                start,end=rig.pose.bones[a+"."+side].head.copy(),rig.pose.bones[b+"."+side].head.copy()
                if side in arms and a in ("upper_arm","forearm"):
                    index=0 if a=="upper_arm" else 1
                    start,end=(value.copy() for value in arms[side][index:index+2])
                capsules.append((a+"."+side,start,end,radius))
        # Runtime capsules use total height equal to the bone segment, so
        # hemisphere centres are inset by the radius at both ends.
        inner=[]
        for name,start,end,radius in capsules:
            delta=end-start
            inset=min(radius,delta.length*.5)
            direction=delta.normalized()
            inner.append((name,start+direction*inset,end-direction*inset,radius))
        capsules=inner
        clearance,part=10.,""
        for a,b,radius in bars:
            for name,c,d,body_radius in capsules:
                gap=self.segment_distance(a,b,c,d)-radius-body_radius
                if gap<clearance: clearance,part=gap,name
            for name,centre,orientation,half in self.body_boxes():
                gap=self.segment_box_distance(a,b,centre,orientation,half)-radius
                if gap<clearance: clearance,part=gap,name
        floor=min(min(a.z,b.z)-radius for a,b,radius in bars)
        return dict(body_gap_m=clearance,body_part=part,floor_gap_m=floor)

    def arm_core_clearance(self,side):
        rig=self.result.rig
        upper,lower,hand=(rig.pose.bones[n+"."+side] for n in ("upper_arm","forearm","hand"))
        segments=((upper.head.lerp(lower.head,.65),lower.head),(lower.head,hand.head))
        head=rig.pose.bones["head"]
        rotation=head.matrix.to_quaternion() @ head.bone.matrix_local.to_quaternion().inverted()
        head_a=head.head+rotation @ Vector((0.,0.,.10)); head_b=head.head+rotation @ Vector((0.,0.,.20))
        return min(*(self.segment_box_distance(a,b,c,q,h) for a,b in segments for _,c,q,h in self.body_boxes()),
                   *(self.segment_distance(a,b,head_a,head_b)-.12 for a,b in segments))

    def weapon_anatomy(self,context,wrist_limit=75.):
        """Measure the presented rigid object and both actual wrist directions."""
        rig=self.result.rig
        angles=[]; components=[]
        name=context.split("@")[0]
        sides=("R",) if name in RELEASED_SUPPORT_CLIPS or name=="CombatRest" else ("R","L")
        for side in sides:
            lower,hand=(rig.pose.bones[n+"."+side] for n in ("forearm","hand"))
            angles.append(math.degrees((hand.head-lower.head).angle(hand.tail-hand.head)))
            direction=(hand.head-lower.head).normalized(); fingers=(hand.tail-hand.head).normalized()
            delta=hand.matrix.to_3x3() @ hand.bone.matrix_local.to_3x3().inverted()
            across=(delta @ self.hand_frame(side)[1]).normalized(); palm=(delta @ self.hand_frame(side)[2]).normalized()
            components.append((math.degrees(math.asin(max(-1.,min(1.,direction.dot(across))))),
                math.degrees(math.atan2(direction.dot(palm),direction.dot(fingers)))))
        clearance=self.weapon_clearance()
        arm_clearance={side:self.arm_core_clearance(side) for side in sides}
        if max(angles)>wrist_limit+.01 or max(abs(c[0]) for c in components)>25.01 or max(abs(c[1]) for c in components)>55.01 or clearance["body_gap_m"]<-.0001 or clearance["floor_gap_m"]<.025 or min(arm_clearance.values())<.0049:
            raise ValueError(f"Held weapon anatomy {context}: wrist={angles}, deviation/flexion={components}, clearance={clearance}, arm_core={arm_clearance}")
        return dict(maximum_wrist_degrees=max(angles),**clearance)

    def place_raised_grip(self, pose, wrist, grip):
        """Lift a reachable two-hand grip with a forward elbow and forearm roll."""
        self._reset_pose(); self._apply_pose(pose)
        rig = self.result.rig
        hand = rig.pose.bones["hand.R"]
        rest = hand.bone
        def frame(axis, fingers):
            z = axis.normalized()
            y = (fingers - z * fingers.dot(z)).normalized()
            x = y.cross(z).normalized()
            return Matrix((x, z.cross(x), z)).transposed()
        rotation = (frame(Vector(grip["axis"]), Vector(grip["fingers"])) @
                    frame(self.hand_frame("R")[1], rest.tail_local-rest.head_local).transposed() @
                    rest.matrix_local.to_3x3())
        self.solve_arm("R", Vector(wrist) * self.scale, rotation, grip["elbow_pole"])
        # Pronation belongs to the forearm. Leaving its independent shortest-axis
        # roll in place sends local quaternion interpolation through a folded
        # elbow and a backward wrist while the hand still meets the weapon.
        forearm = rig.pose.bones["forearm.R"]
        hand_matrix, forearm_matrix = hand.matrix.copy(), forearm.matrix.copy()
        roll = Quaternion((hand.head-forearm.head).normalized(), grip["forearm_roll"])
        forearm.matrix = (Matrix.Translation(forearm_matrix.translation) @
                          (roll @ forearm_matrix.to_quaternion()).to_matrix().to_4x4())
        bpy.context.view_layer.update()
        hand.matrix = hand_matrix
        bpy.context.view_layer.update()
        return self.pin_left_grip(self.snapshot_pose())

    def combat_pose(self, kind):
        cache=getattr(self,"combat_pose_cache",{})
        key=(self.hero_profile,kind)
        if key in cache: return dict(cache[key])
        parents={"ready_breath":"ready","anticipation":"ready","loaded":"anticipation","windup":"loaded",
            "heavy_windup":"windup","contact":"windup","heavy_contact":"contact","follow":"contact",
            "overrun":"follow","recover":"overrun","heavy_follow":"follow","heavy_overrun":"overrun",
            "heavy_recover":"recover","recoil":"contact","hit":"ready","hit_settle":"hit",
            "defeat":"hit","guard_break":"block","backhand_anticipation":"ready"}
        for name in ("loaded","windup","heavy_windup","contact","heavy_contact","follow","overrun","recover",
                     "heavy_follow","heavy_overrun","heavy_recover","recoil"):
            parents["backhand_"+name]="backhand_"+parents[name]
        parents["backhand_contact"]="backhand_windup"
        parents["backhand_recover"]="ready"
        previous=getattr(self,"grip_seed",{})
        seed={}
        if kind in parents:
            source=self.combat_pose(parents[kind]); self._reset_pose(); self._apply_pose(source)
            seed_frame=self.weapon_frame()
            for side in ("R","L"):
                upper,lower,hand=(self.result.rig.pose.bones[n+"."+side] for n in ("upper_arm","forearm","hand"))
                axis=(hand.head-upper.head).normalized(); pole=lower.head-upper.head
                seed[side]=(axis.copy(),(pole-axis*pole.dot(axis)).normalized())
        self.grip_seed=seed
        self.grip_ready_pose=kind.startswith("ready")
        self.grip_backhand=kind.startswith("backhand_")
        self.grip_forward_elbows=kind.startswith("backhand_") and kind.endswith(("loaded","windup"))
        self.grip_deviation_limit=24.8 if kind.startswith("backhand_") and kind.endswith(("loaded","windup")) else 22. if kind.startswith("backhand_") and kind.endswith("contact") else 25.
        self.grip_seed_frame=seed_frame if kind in parents else None
        self.grip_seed_pose=source if kind in parents else None
        try: pose=self._combat_pose(kind)
        finally: self.grip_seed=previous
        pose=self.limit_weapon_wrist(pose)
        if kind not in RELEASED_SUPPORT_POSES and not kind.startswith("rest"):
            pose=self.pin_left_grip(pose,BLOCK_GRIP if kind in GUARD_HEIGHTS else SUPPORT_GRIP)
        cache=getattr(self,"combat_pose_cache",cache)
        cache[key]=pose; self.combat_pose_cache=cache
        return dict(pose)

    def _combat_pose(self, kind):
        B = common.BonePose
        elbow, wrist, axis, chest = COMBAT_POSES[kind]
        if not self.hero_profile and kind.startswith("backhand_") and kind.endswith(("follow","overrun")):
            chest=(chest[0]*.6,chest[1],chest[2]*.45)
        shoulder = self.points["shoulder.R"]
        # Keep the carefully solved charge/guard docks; the hips unwind into
        # contact and continue through recovery, instead of moving those grips.
        body_weight = 1. if kind.removeprefix("backhand_") in ("contact", "follow", "overrun", "recover", "hit", "hit_settle",
            "guard_impact", "guard_break", "recoil", "defeat", "heavy_contact", "heavy_follow", "heavy_overrun", "heavy_recover") else 0.
        pose = self.merge_pose(self.relaxed_pose(), {
            "pelvis": B(rotation_degrees=(chest[0]*.12*body_weight, 0, chest[2]*.18*body_weight),
                        armature_location_m=tuple(Vector(COMBAT_SHIFTS[kind]) + Vector((0, 0, -PELVIS_LOWERING)))),
            "spine": B(rotation_degrees=(-2 + chest[0]*.22*body_weight, 0, chest[2] * (.24+.06*body_weight))),
            "chest": B(rotation_degrees=chest),
            "head": B(rotation_degrees=(3-chest[0]*.10, 0, -chest[2] * .52)),
            "upper_arm.R": B(armature_direction=tuple(Vector(elbow) - shoulder)),
            "forearm.R": B(armature_direction=tuple(Vector(wrist) - Vector(elbow))),
            "upper_arm.L": B(armature_direction=(.16, -.13, -.26)),
            "forearm.L": B(armature_direction=(-.06, -.22, .19)),
            "hand.L": B(rotation_degrees=(0, 0, 8)),
        })
        # Each impact has a different silhouette at the game's low resolution:
        # held guard compresses, broken guard opens and drops, wall recoil folds
        # the weapon elbow high. The support palm is solved onto the shaft below.
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
        elif kind == "backhand_recoil":
            pose.update({"upper_arm.L": B(armature_direction=(.20, -.06, -.22)),
                         "forearm.L": B(armature_direction=(-.16, -.10, .22)),
                         "head": B(rotation_degrees=(6, 0, 7))})
        elif kind in ("windup", "loaded", "heavy_windup", "backhand_windup", "backhand_loaded", "backhand_heavy_windup"):
            pose.update({"upper_arm.L": B(armature_direction=(.17, -.18, -.20)),
                         "forearm.L": B(armature_direction=(-.12, -.10, .28))})
        elif kind in ("follow", "overrun", "heavy_follow", "heavy_overrun",
                      "backhand_follow", "backhand_overrun", "backhand_heavy_follow", "backhand_heavy_overrun"):
            pose.update({"upper_arm.L": B(armature_direction=(.25, .05, -.13)),
                         "forearm.L": B(armature_direction=(-.03, -.16, .23)),
                         # Eyes stay on the opponent as the chest passes, then
                         # the head catches the remaining rotation during braking.
                         "head": B(rotation_degrees=(3-chest[0]*.10, 0,
                            -chest[2]*(.68 if kind.endswith("follow") else .40)))})
        elif kind == "defeat":
            pose.update({"upper_arm.L": B(armature_direction=(.23, .05, -.23)),
                         "forearm.L": B(armature_direction=(.10, .10, -.25)),
                         "head": B(rotation_degrees=(18, 0, -8))})
        elif kind.startswith("rest"):
            pose.update({"upper_arm.L": B(armature_direction=(.07, .025, -.31)),
                         "forearm.L": B(armature_direction=(-.02, -.04, -.28)),
                         "hand.L": B(rotation_degrees=(0, 0, 0))})
        if self.hero_profile and not kind.startswith("rest") and kind != "defeat":
            # A frightened novice keeps his chin behind the hands and his neck
            # rigid instead of presenting a confident fencing silhouette. The
            # weapon/hips retain their authored landmarks and both palm solves;
            # fear cannot delay a contact or impersonate the damage reactions.
            guarded = kind in ("ready", "ready_breath", "block", "block_breath") or kind.endswith(("loaded", "windup"))
            effort = kind.endswith(("overrun", "recover"))
            amount = 1. if guarded else .7 if effort else .3
            for side in ("L", "R"):
                clavicle = self.result.rig.data.bones["clavicle." + side]
                pose["clavicle." + side] = B(armature_direction=tuple(
                    clavicle.tail_local-clavicle.head_local + Vector((0., -.018*amount, .024*amount))))
            pose["neck"] = B(rotation_degrees=(-3.8 * amount, 0., 1.1 * amount))
            head = pose["head"].rotation_degrees
            pose["head"] = B(rotation_degrees=(head[0] + 5.8 * amount, head[1], head[2] - 1.8 * amount))
        if kind in RELEASED_SUPPORT_POSES:
            # Release begins from the live supporting chain. Its local pose
            # follows the recoiling chest; resetting it to a relaxed arm would
            # whip the elbow across the body before the palm has let go.
            for name in ("upper_arm.L","forearm.L","hand.L"):
                pose[name]=self.grip_seed_pose[name]
        if not self.hero_profile and kind.startswith("backhand_") and kind.endswith(("follow","overrun")):
            # Brake the arm chains toward the same stance while the trunk
            # unloads. The former forward-pointing low dock could only hold
            # the supporting elbow above the shoulder, then flipped it down.
            source=self.combat_pose_cache[(self.hero_profile,"backhand_contact")]
            ready=self.combat_pose_cache[(self.hero_profile,"ready")]
            arms=self.blend(source,ready,.35 if kind.endswith("follow") else .90)
            for side in ("R","L"):
                for name in ("clavicle","upper_arm","forearm","hand"):
                    pose[name+"."+side]=arms[name+"."+side]
            result=self.limit_weapon_wrist(self.pin_supports(pose))
            rig=self.result.rig
            left_pole=rig.pose.bones["forearm.L"].head-rig.pose.bones["upper_arm.L"].head
            return self.align_right_grip(result,True,left_pole,frame=self.weapon_frame())
        if kind == "backhand_anticipation" or (self.hero_profile and kind in ("backhand_recover", "backhand_heavy_recover")):
            # The preload comes from the chest and legs. Retain the ready arm
            # frames here so partial charge follows the same upward arc as the
            # held windup instead of unwinding the shaft through the floor.
            # Hero recovery similarly closes relative to its own torso;
            # forcing Ready's world grip under a turned chest is unreachable.
            for side in ("R", "L"):
                for name in ("clavicle", "upper_arm", "forearm", "hand"):
                    pose[name+"."+side]=self.grip_seed_pose[name+"."+side]
            return self.pin_supports(pose)
        self._reset_pose(); self._apply_pose(pose)
        hand = self.result.rig.pose.bones["hand.R"]
        # Specify the crowbar's grip-axis explicitly, preserving anatomical
        # hand orientation instead of guessing exported Euler angles.
        delta = Vector((0, -1, 0)).rotation_difference(Vector(axis).normalized())
        rotation = delta @ hand.bone.matrix_local.to_quaternion()
        hand.matrix = Matrix.Translation(hand.head.copy()) @ rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()
        result = self.pin_supports(self.snapshot_pose())
        if kind.startswith("rest"): return self.align_right_grip(result,False)
        if kind in RAISED_WRISTS:
            wrist, grip = RAISED_WRISTS[kind]
            # These docks were measured with the old .040 m body lowering.
            # Carry them down with the deeper stance so the local arm blend
            # keeps its reachable quaternion branch at every charge power.
            lowered_wrist = Vector(wrist) - Vector((0., 0., PELVIS_LOWERING-.040))
            result=self.place_raised_grip(result, lowered_wrist, grip)
            result=self.align_right_grip(result)
            return self.pin_left_grip(result)
        if kind in GUARD_HEIGHTS:
            return self.place_guard_grip(result, GUARD_HEIGHTS[kind]-(PELVIS_LOWERING-.040), GUARD_PALM_ROLL_DEGREES)
        try:
            # A broken guard and the crossed-body follow-through must remain in
            # front of the jacket. These actor-space docks keep the entire bar
            # outside it instead of inheriting a backwards torso rotation.
            docks={
                "ready":((-.15,-.40,1.08),(.90,-.10,.42)),
                "ready_breath":((-.15,-.40,1.087),(.90,-.10,.42)),
                "contact":((0,0,0),(.08,-.90,.45)),
                "heavy_contact":((0,0,0),(.10,-.90,.45)),
                "backhand_contact":((0,0,0),(-.08,-.92,.39)),
                "backhand_heavy_contact":((0,0,0),(-.10,-.92,.39)),
                "guard_break":((-.08,-.20,1.15),(.60,-.60,.53)),
                "defeat":((-.06,-.24,1.04),(.65,-.70,.30)),
                "backhand_anticipation":((0,0,0),(-.55,-.20,.80)),
                "backhand_follow":((.08,-.39,1.20),(-.15,-.80,.58)),
                "backhand_overrun":((.04,-.36,1.17),(-.20,-.75,.63)),
                "backhand_heavy_follow":((.08,-.39,1.20),(-.15,-.80,.58)),
                "backhand_heavy_overrun":((.04,-.36,1.17),(-.20,-.75,.63)),
            }
            desired_frame=None
            if kind in ("hit","hit_settle","guard_break","recoil","backhand_recoil","anticipation","backhand_anticipation"):
                origin,rotation=self.grip_seed_frame
                offset=Vector((.03,.26 if kind=="recoil" else .18,.025)) if kind.endswith("recoil") else Vector((0.,.12,-.10)) if kind=="guard_break" else Vector((0.,.04,.025))
                if kind=="hit_settle": offset=Vector((0.,-.025,-.015))
                if kind.endswith("anticipation"): offset=Vector((.08 if kind.startswith("backhand_") else -.02,.03,.08))
                docks[kind]=(origin+offset,rotation @ Vector((0.,1.,0.)))
            if kind in ("backhand_heavy_contact", "backhand_recover", "backhand_heavy_recover"):
                origin,rotation=self.grip_seed_frame
                docks[kind]=(origin,rotation @ Vector((0.,1.,0.)))
            if kind in docks:
                origin,dock_axis=(Vector(v) for v in docks[kind])
                if self.hero_profile and kind.startswith("ready"):
                    origin+=Vector((.03,-.02,0.))
                if kind=="defeat" or (kind.endswith("contact") and kind!="backhand_heavy_contact"):
                    centre=(self.result.rig.pose.bones["upper_arm.R"].head+self.result.rig.pose.bones["upper_arm.L"].head)*.5
                    offset=(0.,-.33,-.28) if kind.startswith("backhand_") and kind.endswith("contact") else (0.,-.40,-.22) if kind.endswith("contact") else (.10,-.20,.08) if kind.endswith("anticipation") else (-.08,-.30,-.24)
                    origin=centre+Vector(offset)
                    if kind=="backhand_contact" and not self.hero_profile:
                        origin.y-=.003  # Preserve the actual .95 m baked strike reach.
                elif kind.startswith("backhand_") and kind.endswith(("follow","overrun")) and not self.hero_profile:
                    origin.z-=.20
                old_origin,old_bar=self.weapon_frame()
                rotate=(old_bar @ Vector((0.,1.,0.))).rotation_difference(dock_axis.normalized())
                desired_frame=(origin,rotate @ old_bar)
                if kind in ("backhand_recover", "backhand_heavy_recover"):
                    desired_frame=self.grip_seed_frame
                result=self.snapshot_pose()
            else:
                result=self.pin_left_grip(result,SUPPORT_GRIP,LEFT_ELBOW_POLES.get(kind))
            support=kind not in RELEASED_SUPPORT_POSES
            result=self.align_right_grip(result,support,LEFT_ELBOW_POLES.get(kind),frame=desired_frame,wrist_limit=30. if kind.startswith("ready") else 65.,preserve_roll=kind in ("backhand_recover", "backhand_heavy_recover"))
            return self.pin_left_grip(result,SUPPORT_GRIP,LEFT_ELBOW_POLES.get(kind)) if support else result
        except ValueError as error:
            raise ValueError(kind + ": " + str(error)) from error

    def recovery_leg(self, side, ankle, pole, foot_rotation):
        """Bake a length-preserving knee hinge against an explicit support trajectory."""
        rig = self.result.rig
        thigh, shin, foot = (rig.pose.bones[n + "." + side] for n in ("thigh", "shin", "foot"))
        hip = thigh.head.copy()
        a, b = thigh.bone.length, shin.bone.length
        delta = ankle - hip
        distance = delta.length
        if not abs(a-b)+.005 < distance < a+b-.005:
            raise ValueError(f"Recovery {side} leg cannot reach {distance:.4f}/{a+b:.4f}: hip={tuple(hip)} ankle={tuple(ankle)}")
        axis = delta.normalized()
        bend = Vector(pole) - axis * Vector(pole).dot(axis)
        if bend.length < .01: raise ValueError("Recovery knee pole parallel to chain")
        along = (a*a-b*b+distance*distance)/(2*distance)
        knee = hip + axis*along + bend.normalized()*math.sqrt(max(0., a*a-along*along))
        for bone, start, end in ((thigh, hip, knee), (shin, knee, ankle)):
            rest = bone.bone
            rotation = (rest.tail_local-rest.head_local).normalized().rotation_difference((end-start).normalized()) @ rest.matrix_local.to_quaternion()
            bone.matrix = Matrix.Translation(start) @ rotation.to_matrix().to_4x4()
            bpy.context.view_layer.update()
        foot.matrix = Matrix.Translation(ankle) @ foot_rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()

    def recovery_hinge_frames(self, root_name, hinge_name, tip_name, reference):
        """Both segments share one hinge plane, including the root bone's axial roll."""
        rig=self.result.rig
        root,hinge,tip=(rig.pose.bones[n] for n in (root_name,hinge_name,tip_name))
        points=[b.head.copy() for b in (root,hinge,tip)]
        tip_rotation=tip.matrix.to_quaternion()
        upper=(points[1]-points[0]).normalized(); lower=(points[2]-points[1]).normalized()
        normal=lower.cross(upper)
        if normal.length < .01: return
        normal.normalize()
        rest_direction=(root.bone.tail_local-root.bone.head_local).normalized()
        rest_reference=root.bone.matrix_local.to_quaternion() @ reference
        rest_normal=rest_direction.cross(rest_reference).normalized()
        def basis(direction,normal):
            normal=(normal-direction*normal.dot(direction)).normalized()
            return Matrix((direction,normal.cross(direction),normal)).transposed()
        for bone,start,end in ((root,points[0],points[1]),(hinge,points[1],points[2])):
            rest=bone.bone
            delta=basis((end-start).normalized(),normal) @ basis((rest.tail_local-rest.head_local).normalized(),rest_normal).transposed()
            bone.matrix=Matrix.Translation(start) @ (delta @ rest.matrix_local.to_3x3()).to_4x4()
            bpy.context.view_layer.update()
        tip.matrix=Matrix.Translation(points[2]) @ tip_rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()

    def recovery_palm(self, point, normal, fingers):
        """Place the real palm surface, not a wrist or the weapon socket, on its support."""
        bone = self.result.rig.data.bones["hand.L"]
        centre, _, rest_palm = self.hand_frame("L")
        rest_fingers = (bone.tail_local-bone.head_local).normalized()
        def frame(forward, up):
            z = forward.normalized()
            x = up.cross(z).normalized()
            return Matrix((x, z.cross(x), z)).transposed()
        delta = frame(Vector(fingers), Vector(normal)) @ frame(rest_fingers, rest_palm).transposed()
        wrist = Vector(point) - delta @ (centre-bone.head_local)
        return wrist, (delta @ bone.matrix_local.to_3x3()).to_quaternion()

    def recovery_ready_roll(self, chain, ready_rotations, weight):
        """Join the authored Ready axial frames without moving any joint or support."""
        if weight <= 0: return
        bones=[self.result.rig.pose.bones[n] for n in chain]
        points=[b.head.copy() for b in bones]
        rotations=[b.matrix.to_quaternion() for b in bones]
        for i,bone in enumerate(bones[:-1]):
            axis=(points[i+1]-points[i]).normalized()
            reference=Vector((1.,0.,0.))
            current=rotations[i] @ reference
            target=ready_rotations[bone.name] @ reference
            current=(current-axis*current.dot(axis)).normalized()
            target=(target-axis*target.dot(axis)).normalized()
            angle=math.atan2(axis.dot(current.cross(target)),current.dot(target))
            rotation=Quaternion(axis,angle*weight) @ rotations[i]
            bone.matrix=Matrix.Translation(points[i]) @ rotation.to_matrix().to_4x4()
            bpy.context.view_layer.update()
        bones[-1].matrix=Matrix.Translation(points[-1]) @ rotations[-1].to_matrix().to_4x4()
        bpy.context.view_layer.update()

    def build_recovery_actions(self):
        """One-hand combat recovery; no borrowed seated split or runtime torso rescue.

        Each dense sample solves the intended feet and arms on the original rig.
        Supine rocks over planted boots; prone gathers one lead boot beside
        the resting knee. Both routes continue into the same compact stance. The last key is the exact profile's two-hand Ready.
        """
        rig = self.result.rig
        if getattr(self,"recovery_probe",False):
            profile=self.hero_profile
            for self.hero_profile in (False,True):
                print("Recovery wrist baseline "+json.dumps(dict(profile="hero" if self.hero_profile else "npc",
                    poses={n:self.guard_metrics(self.combat_pose(n)) for n in ("ready","ready_breath","block","contact","recover","rest")})),flush=True)
            self.hero_profile=profile
        ready = self.combat_pose("ready")
        self._reset_pose(); self._apply_pose(ready)
        ready_pelvis = rig.pose.bones["pelvis"].head.copy()
        ready_feet = {s: rig.pose.bones["foot."+s].head.copy() for s in ("L", "R")}
        ready_rotations = {s: rig.pose.bones["foot."+s].matrix.to_quaternion() for s in ("L", "R")}
        ready_wrist = rig.pose.bones["hand.R"].head.copy()
        ready_shoulder = rig.pose.bones["upper_arm.R"].head.copy()
        ready_hand_rotation = rig.pose.bones["hand.R"].matrix.to_quaternion()
        ready_wrist_relative = rig.pose.bones["forearm.R"].matrix.to_quaternion().inverted() @ ready_hand_rotation
        ready_elbow=rig.pose.bones["forearm.R"].head.copy()
        ready_elbow_pole=(ready_elbow-rig.pose.bones["upper_arm.R"].head).normalized()
        ready_left_elbow_pole=(rig.pose.bones["forearm.L"].head-rig.pose.bones["upper_arm.L"].head).normalized()
        ready_rotations_all={b.name:b.matrix.to_quaternion() for b in rig.pose.bones}
        rest_pelvis = rig.data.bones["pelvis"].head_local.copy()
        self._reset_pose(); self._apply_pose(self.relaxed_pose())
        references = common.calibrate_hinge_references(rig)
        # Blender: front -Y, left +X, up +Z. No lateral sweep of the trailing
        # thigh: the right ankle stays in its own narrow sagittal corridor.
        common_states = (
            (.40, (0., .04, .40), (70., 0., -8.), (.20, -.30, .115), (-.16, .57, .115), (0., -1., 0.), (0., -1., 0.)),
            (.54, (0., .02, .44), (37., 0., -3.), tuple(ready_feet["L"]), (-.19, .43, .115), (0., -1., 0.), (0., -1., 0.)),
            (.72, tuple(ready_pelvis + Vector((0., 0., -.17))), (26., 0., -2.), tuple(ready_feet["L"]), tuple(ready_feet["R"]), (0., -1., 0.), (0., -1., 0.)),
            (1., tuple(ready_pelvis), (0., 0., 0.), tuple(ready_feet["L"]), tuple(ready_feet["R"]), (0., -1., 0.), (0., -1., 0.)),
        )
        self.recovery_measurements = []
        for name in RECOVERY_CLIPS:
            selected=getattr(self,"recovery_probe",False)
            if selected in ("prone","supine") and not name.lower().endswith(selected): continue
            supine = name.endswith("Supine")
            duration=RECOVERY_DURATIONS[name]
            if supine:
                # Keep both legs in front. The seat/palm give way to a compact squat;
                # no ankle crosses through the body to manufacture an all-fours pose.
                planted_left,planted_right=tuple(ready_feet["L"]),tuple(ready_feet["R"])
                states = (
                    (0., (0., .46, .17), (-78., 6., -3.), planted_left, planted_right, (0., 0., 1.), (0., 0., 1.)),
                    (.14, (0., .48, .115), (-40., 10., -2.), planted_left, planted_right, (0., 0., 1.), (0., 0., 1.)),
                    (.34, (0., .49, .17), (75., 8., 0.), planted_left, planted_right, (0., -.4, 1.), (0., -.4, 1.)),
                    (.46, (0., .40, .35), (70., 8., 0.), planted_left, planted_right, (0., -.7, .6), (0., -.7, .6)),
                    (.56, (0., .24, .46), (38., 5., -2.), planted_left, planted_right, (0., -.9, .2), (0., -.9, .2)),
                    (.64, (0., .15, .51), (26., 5., -3.), planted_left, planted_right, (0., -.8, .3), (0., -.8, .3)),
                ) + common_states[2:]
            else:
                states = (
                    (0., (0., .02, .18), (82., 0., -4.), (.15, .715, .115), (-.15, .715, .115), (0., -1., 0.), (0., -1., 0.)),
                    (.14, (0., .14, .49), (82., 0., -8.), (.19, .60, .115), (-.16, .64, .115), (0., -1., 0.), (0., -1., 0.)),
                ) + common_states
            def state_at(t):
                for a,b in zip(states, states[1:]):
                    if t <= b[0] + 1.e-8:
                        q = dialogue.smooth((t-a[0])/(b[0]-a[0]))
                        return [Vector(x).lerp(Vector(y),q) for x,y in zip(a[1:],b[1:])]
                return [Vector(v) for v in states[-1][1:]]
            keys = []
            min_hinge, max_hinge, max_span, min_knee, min_separation = 180., -180., 0., 10., 10.
            previous = None
            max_frame_travel = 0.
            max_rotation=0.; previous_rotation=None
            max_length_error=max_contact_error=0.
            min_weapon_height=10.; min_weapon_body=10.; max_right_wrist=0.; max_right_deviation=0.
            unsupported_seconds=max_unsupported_seconds=0.
            diagnostic = dict(hinges={}, maximum_delta=None, frames=[])
            count = round(duration*(30 if getattr(self,"recovery_probe",False) else FPS))
            for frame in range(count+1):
                t = frame/count
                if frame % 24 == 0: print(f"Recovery landmark {name} t={t:.2f}",flush=True)
                if frame == count:
                    self._reset_pose(); self._apply_pose(ready)
                    pose = ready
                else:
                    hip, rotation, left_ankle, right_ankle, left_pole, right_pole = state_at(t)
                    B = common.BonePose
                    body = self.merge_pose(self.relaxed_pose(), {
                        "pelvis": B(rotation_degrees=tuple(rotation), armature_location_m=tuple(hip-rest_pelvis)),
                        "spine": B(rotation_degrees=(7.*(1-dialogue.smooth((t-.72)/.28)), 0., 0.)),
                        "chest": B(rotation_degrees=(-4.*(1-dialogue.smooth((t-.72)/.28)), 0., 0.)),
                        "neck": B(rotation_degrees=(-8.*(1-dialogue.smooth((t-.72)/.28)), 0., 0.)),
                        "head": B(rotation_degrees=(6.*(1-dialogue.smooth((t-.72)/.28)), 0., 0.)),
                    })
                    # The final quarter joins all Ready joints, including clavicles,
                    # rather than combining a relaxed torso with a foreign weapon arm.
                    terminal = dialogue.smooth((t-.72)/.28)
                    self._reset_pose(); self._apply_pose(body)
                    pose = self.snapshot_pose()
                    if terminal > 0:
                        pose = self.track_pose(((0., "a"),(1., "b")), {"a": pose,"b": ready}, terminal)
                        self._reset_pose(); self._apply_pose(pose)
                    for side, ankle, pole in (("L",left_ankle,left_pole),("R",right_ankle,right_pole)):
                        # Toe-down early feet turn onto the sole before loading; they
                        # keep their authored ankle position instead of being extended.
                        toe = 0. if supine else max(0.,1.-dialogue.smooth((t-.32)/.4)) * 28.
                        foot_rotation = Quaternion(Vector((1.,0.,0.)), math.radians(-toe)) @ ready_rotations[side]
                        self.recovery_leg(side, ankle, pole, foot_rotation)
                        self.recovery_hinge_frames("thigh."+side,"shin."+side,"foot."+side,references[("left" if side=="L" else "right")+" knee"])
                    # The carry dock lives beside/in front of the actual shoulder in
                    # actor axes. It cannot rotate behind the back with the torso.
                    shoulder=rig.pose.bones["upper_arm.R"].head.copy()
                    wrist=shoulder+Vector((-.23,-.18,-.20)); wrist.z=max(.33,wrist.z)
                    carry_blend=dialogue.smooth((t-.54)/.26)
                    wrist=wrist.lerp(shoulder+ready_wrist-ready_shoulder,carry_blend)
                    carry_arc=math.sin(math.pi*max(0.,min(1.,(t-.54)/.26)))**2
                    wrist+=Vector((0.,-.045,-.04))*carry_arc
                    pole=Vector((-1.,.6,0.)).lerp(ready_elbow_pole,carry_blend)
                    self.solve_arm("R",wrist,ready_hand_rotation.to_matrix(),pole)
                    self.recovery_hinge_frames("upper_arm.R","forearm.R","hand.R",references["right elbow"])
                    forearm,hand=rig.pose.bones["forearm.R"],rig.pose.bones["hand.R"]
                    fingers=(wrist-forearm.head).normalized()
                    shaft=Vector((-.45,-.20,.87))
                    shaft=(shaft-fingers*shaft.dot(fingers)).normalized()
                    def grip_frame(axis,finger_direction):
                        y=(finger_direction-axis*axis.dot(finger_direction)).normalized()
                        x=y.cross(axis).normalized()
                        return Matrix((x,axis.cross(x),axis)).transposed()
                    rest=hand.bone
                    neutral=(grip_frame(shaft,fingers) @ grip_frame(self.hand_frame("R")[1],(rest.tail_local-rest.head_local).normalized()).transposed() @ rest.matrix_local.to_3x3()).to_quaternion()
                    # Pronation rotates the forearm around its own segment; the wrist
                    # continues it. No local-quaternion blend may move solved endpoints.
                    neutral_relative=forearm.bone.matrix_local.to_quaternion().inverted() @ rest.matrix_local.to_quaternion()
                    lower_rotation=neutral @ neutral_relative.inverted()
                    lower_rotation=(lower_rotation @ Vector((0.,1.,0.))).rotation_difference(fingers) @ lower_rotation
                    forearm.matrix=Matrix.Translation(forearm.head.copy()) @ lower_rotation.to_matrix().to_4x4()
                    bpy.context.view_layer.update()
                    hand_rotation=neutral.slerp(ready_hand_rotation,carry_blend)
                    hand_fingers=hand_rotation @ Vector((0.,1.,0.))
                    wrist_bend=fingers.angle(hand_fingers)
                    if wrist_bend>math.radians(28.):
                        desired=fingers.slerp(hand_fingers,math.radians(28.)/wrist_bend)
                        hand_rotation=hand_fingers.rotation_difference(desired) @ hand_rotation
                    hand.matrix=Matrix.Translation(wrist) @ hand_rotation.to_matrix().to_4x4()
                    bpy.context.view_layer.update()
                    floor_palm = Vector((.20 if supine else .30,.64 if supine else -.25,.035))
                    knee_palm = rig.pose.bones["shin.L"].head + Vector((0.,-.015,.04))
                    transfer = dialogue.smooth((t-(.18 if supine else .28))/(.16 if supine else .20))
                    palm = floor_palm.lerp(knee_palm, transfer)
                    wrist_left, rotation_left = self.recovery_palm(palm, (0.,0.,-1.), (0.,-1.,0.))
                    if supine and t > .15:
                        # The planted boots accept the forward rock while the free arm
                        # travels beside the torso; it does not pretend to remain loaded
                        # on an unreachable point behind the seat.
                        free=Vector((.40,.20,.45)).lerp(rig.pose.bones["upper_arm.L"].head+Vector((.35,.05,-.24)),dialogue.smooth((t-.38)/.18))
                        floor_wrist,floor_rotation=self.recovery_palm(floor_palm,(0.,0.,-1.),(0.,-1.,0.))
                        wrist_left=floor_wrist.lerp(free,dialogue.smooth((t-.15)/.19))
                        rotation_left=floor_rotation
                    grip_wrist, grip_rotation = self.left_contact_frame()
                    regrip = dialogue.smooth((t-.66)/.24)
                    wrist_left = wrist_left.lerp(grip_wrist,regrip)
                    rotation_left = rotation_left.slerp(grip_rotation.to_quaternion(),regrip)
                    # Supine first folds the free arm beside its chest, then sets the
                    # palm visibly onto the floor as the body gathers over it.
                    if supine and t < .11:
                        gather = dialogue.smooth(t/.11)
                        free = rig.pose.bones["upper_arm.L"].head + Vector((.18,-.31,-.16))
                        wrist_left = free.lerp(wrist_left,gather)
                    if (wrist_left-rig.pose.bones["upper_arm.L"].head).length > .579:
                        raise ValueError(f"Left support {name} t={t:.4f} shoulder={tuple(rig.pose.bones['upper_arm.L'].head)} wrist={tuple(wrist_left)}")
                    left_pole=Vector((.7,.15,-.6))
                    if supine:
                        left_pole=left_pole.lerp(Vector((.8,.6,-.2)),dialogue.smooth((t-.15)/.19))
                    self.solve_arm("L", wrist_left, rotation_left.to_matrix(), left_pole.lerp(ready_left_elbow_pole,regrip))
                    self.recovery_hinge_frames("upper_arm.L","forearm.L","hand.L",references["left elbow"])
                    roll_blend=dialogue.smooth((t-.80)/.20)
                    for chain in (("thigh.L","shin.L","foot.L"),("thigh.R","shin.R","foot.R"),("upper_arm.L","forearm.L","hand.L"),("upper_arm.R","forearm.R","hand.R")):
                        self.recovery_ready_roll(chain,ready_rotations_all,roll_blend)
                    pose = self.snapshot_pose()
                for joint,value in common.measure_hinges(rig,references):
                    old=diagnostic["hinges"].get(joint,(0.,0.))
                    if abs(value)>abs(old[0]): diagnostic["hinges"][joint]=(value,t)
                hinge_values = [value for _,value in common.measure_hinges(rig,references)]
                min_hinge = min(min_hinge,*hinge_values); max_hinge = max(max_hinge,*hinge_values)
                min_knee = min(min_knee,rig.pose.bones["shin.L"].head.z,rig.pose.bones["shin.R"].head.z)
                max_span = max(max_span,abs(rig.pose.bones["shin.L"].head.x-rig.pose.bones["shin.R"].head.x))
                min_separation=min(min_separation,rig.pose.bones["shin.L"].head.x-rig.pose.bones["shin.R"].head.x)
                if supine:
                    # A seated rise first carries its mass over the boots. This
                    # proxy rejects the old levitating seat-to-squat transfer;
                    # it is a conservative contact check, not a dynamic solver.
                    point=lambda n:rig.pose.bones[n].head
                    centre=(point("pelvis")+point("neck"))*.25+point("head")*.08
                    for side in ("L","R"):
                        for a,b,weight in (("upper_arm","forearm",.03),("forearm","hand",.02),("thigh","shin",.10),("shin","foot",.04)):
                            centre+=(point(a+"."+side)+point(b+"."+side))*(weight*.5)
                        centre+=point("hand."+side)*.01+point("foot."+side)*.01
                    palm=rig.pose.bones["hand.L"].matrix @ rig.data.bones["hand.L"].matrix_local.inverted() @ self.hand_frame("L")[0]
                    behind=centre.y-max(point("foot.L").y,point("foot.R").y)-.08
                    unloaded=point("pelvis").z>.18 and palm.z>.06 and behind>0.
                    unsupported_seconds=unsupported_seconds+duration/count if unloaded else 0.
                    max_unsupported_seconds=max(max_unsupported_seconds,unsupported_seconds)
                for root,hinge,tip in (("thigh.L","shin.L","foot.L"),("thigh.R","shin.R","foot.R"),("upper_arm.L","forearm.L","hand.L"),("upper_arm.R","forearm.R","hand.R")):
                    for a,b in ((root,hinge),(hinge,tip)):
                        max_length_error=max(max_length_error,abs((rig.pose.bones[b].head-rig.pose.bones[a].head).length-rig.data.bones[a].length))
                if frame < count:
                    for side,target in (("L",left_ankle),("R",right_ankle)):
                        max_contact_error=max(max_contact_error,(rig.pose.bones["foot."+side].head-target).length)
                    if ((.11 <= t <= .15) if supine else (0. <= t <= .28)):
                        rest_hand=rig.data.bones["hand.L"]
                        centre=self.hand_frame("L")[0]
                        actual=rig.pose.bones["hand.L"].matrix @ rest_hand.matrix_local.inverted() @ centre
                        max_contact_error=max(max_contact_error,(actual-floor_palm).length)
                origin,bar=self.weapon_frame()
                clearance=self.weapon_clearance()
                min_weapon_height=min(min_weapon_height,clearance["floor_gap_m"])
                if clearance["body_gap_m"]<min_weapon_body:
                    min_weapon_body=clearance["body_gap_m"]; diagnostic["minimum_body_clearance"]=(clearance["body_part"],min_weapon_body,t)
                hand,lower=rig.pose.bones["hand.R"],rig.pose.bones["forearm.R"]
                max_right_wrist=max(max_right_wrist,math.degrees((hand.head-lower.head).angle(hand.tail-hand.head)))
                right_deviation=abs(math.degrees(math.asin(max(-1.,min(1.,(hand.head-lower.head).normalized().dot(bar @ Vector((0.,1.,0.))))))))
                max_right_deviation=max(max_right_deviation,right_deviation)
                rotations={n:rig.pose.bones[n].matrix.to_quaternion() for n in
                    ("pelvis","spine","chest","shin.L","shin.R","foot.L","foot.R","upper_arm.L","upper_arm.R","forearm.L","forearm.R","hand.L","hand.R")}
                if previous_rotation is not None:
                    for n,q in rotations.items():
                        angle=math.degrees(q.rotation_difference(previous_rotation[n]).angle)
                        angle=min(angle,360.-angle)
                        if angle>max_rotation: max_rotation=angle; diagnostic["maximum_rotation"]=(n,angle,t)
                previous_rotation=rotations
                current = {n: rig.pose.bones[n].head.copy() for n in ("pelvis","shin.L","shin.R","hand.L","hand.R")}
                if previous is not None:
                    joint=max(current,key=lambda n:(current[n]-previous[n]).length)
                    error=(current[joint]-previous[joint]).length
                    if error>max_frame_travel: max_frame_travel=error; diagnostic["maximum_delta"]=(joint,error,t)
                diagnostic["frames"].append(dict(t=t,bones={b.name:list(b.head) for b in rig.pose.bones if b.name in
                    ("pelvis","spine","chest","neck","head","thigh.L","thigh.R","shin.L","shin.R","foot.L","foot.R","upper_arm.L","upper_arm.R","forearm.L","forearm.R","hand.L","hand.R")},
                    weapon=[list(self.weapon_frame()[0]),list(self.weapon_frame()[0]+self.weapon_frame()[1]@Vector((0.,.60,.145)))],
                    right_wrist_deviation_degrees=right_deviation,
                    right_wrist_angle_degrees=math.degrees((rig.pose.bones["hand.R"].head-rig.pose.bones["forearm.R"].head).angle(rig.pose.bones["hand.R"].tail-rig.pose.bones["hand.R"].head))))
                previous=current
                keys.append((t,pose))
            preview=ROOT/"Captures/CombatTest/recovery-authoring"
            preview.mkdir(parents=True,exist_ok=True)
            (preview/(name+".json")).write_text(json.dumps(diagnostic),encoding="utf8")
            print("Recovery diagnostics " + json.dumps({k:v for k,v in diagnostic.items() if k!="frames"}),flush=True)
            if min_weapon_body<0. or max_right_wrist>30. or max_right_deviation>25.01:
                raise ValueError(f"Recovery weapon anatomy {name}: body={min_weapon_body}, wrist={max_right_wrist}, radial={max_right_deviation}, {diagnostic.get('minimum_body_clearance')}")
            if max_rotation*(count/duration)/60. > 15. or max_length_error > .0001 or max_contact_error > .001 or min_weapon_height < .025:
                raise ValueError(f"Recovery contact {name}: rotation={max_rotation}, lengths={max_length_error}, contacts={max_contact_error}, weapon={min_weapon_height}")
            if min_hinge < -8. or max_hinge > 130. or min_knee < .025 or max_span > .60 or min_separation < .06 or max_frame_travel*(count/duration)/60. > .045:
                raise ValueError(f"Recovery anatomy {name}: hinges={min_hinge:.1f}..{max_hinge:.1f}, kneeZ={min_knee:.3f}, span={max_span:.3f}, separation={min_separation:.3f}, frame={max_frame_travel:.3f}")
            if max_unsupported_seconds>.15:
                raise ValueError(f"Recovery raises seat before mass reaches feet: {name} {max_unsupported_seconds:.3f}s")
            self._create_action(name,"combat",duration,False,count,count/duration,keys)
            self.recovery_measurements.append(dict(name=name,duration_seconds=duration,
                minimum_hinge_degrees=min_hinge,maximum_hinge_degrees=max_hinge,minimum_knee_height_m=min_knee,
                maximum_knee_span_m=max_span,minimum_knee_separation_m=min_separation,maximum_sample_travel_m=max_frame_travel, maximum_frame_travel_60hz_m=max_frame_travel*(count/duration)/60.,
                maximum_rotation_60hz_degrees=max_rotation*(count/duration)/60.,maximum_length_error_m=max_length_error,maximum_contact_error_m=max_contact_error,minimum_weapon_height_m=min_weapon_height))
            self.recovery_measurements[-1]["maximum_unsupported_transfer_seconds"]=max_unsupported_seconds
            self.recovery_measurements[-1].update(minimum_weapon_body_clearance_m=min_weapon_body,maximum_right_wrist_degrees=max_right_wrist,maximum_right_wrist_deviation_degrees=max_right_deviation)
            print("Authored combat recovery " + json.dumps(self.recovery_measurements[-1]),flush=True)

    def build_actions(self):
        self.suspend_mesh_deformation()
        resume=getattr(self,"resume_npc_bank",None)
        if resume is not None and not self.hero_profile:
            state=json.loads(resume.with_suffix(".json").read_text(encoding="utf8"))
            names=tuple(state["actions"])
            ordinary={name for name,_,_ in CLIPS+CHARGE_CLIPS}
            allowed={name for name,_,_ in SHARED_CLIPS+STEP_CLIPS+LOCOMOTION_CLIPS}|set(RECOVERY_CLIPS)
            if state["profile"]!="npc" or not ordinary.issubset(names) or not set(names).issubset(allowed):
                raise ValueError("Resume requires a known NPC bank with complete ordinary Actions")
            with bpy.data.libraries.load(str(resume),link=False) as (available,loaded):
                primary=tuple(name for name in names if name in available.actions)
                missing=tuple(name for name in names if name not in available.actions)
                if not set(missing).issubset(ordinary):raise ValueError("Incomplete checkpoint Actions: "+str(missing))
                loaded.actions=list(primary)
            imported=dict(zip(primary,loaded.actions))
            if missing:
                # Older resumed checkpoints lacked fake users on appended
                # Actions; their verified ordinary source remains alongside.
                fallback=resume.with_name("npc-ordinary-before-dense.blend")
                if fallback==resume:raise ValueError("Incomplete ordinary checkpoint")
                with bpy.data.libraries.load(str(fallback),link=False) as (available,loaded):
                    if not set(missing).issubset(available.actions):raise ValueError("Missing verified ordinary Actions")
                    loaded.actions=list(missing)
                imported.update(zip(missing,loaded.actions))
            for name in names:
                action=imported[name];action.name=name;action.use_fake_user=True
                self.result.actions[name]=common.ActionRecord(action=action,**state["actions"][name])
            self.result.rig.animation_data_create()
            for key,value in state["measurements"].items():setattr(self,key,value)
            self.resume_npc_bank=None
            print("Resumed NPC Actions from "+str(resume),flush=True)
        if getattr(self,"pose_probe",False):
            rig=self.result.rig; records=[]
            tracks=[]
            for self.hero_profile in (False,True):
                poses={}
                for name in COMBAT_POSES:
                    try: pose=self.combat_pose(name)
                    except ValueError as error:
                        records.append(dict(profile="hero" if self.hero_profile else "npc",name=name,error=str(error)))
                        print("Wrist pose failure "+json.dumps(records[-1]),flush=True)
                        continue
                    poses[name]=pose
                    metrics=self.guard_metrics(pose)
                    metrics.update(self.weapon_clearance())
                    origin,bar=self.weapon_frame()
                    metrics.update(profile="hero" if self.hero_profile else "npc",name=name,
                        arm_frames={side:{n:dict(world=list(rig.pose.bones[n+"."+side].matrix.to_quaternion()),rest=list(rig.data.bones[n+"."+side].matrix_local.to_quaternion())) for n in ("upper_arm","forearm","hand")} for side in ("R","L")},
                        tip=list(origin+bar @ Vector((0.,.595,.145))),
                        bones={b.name:list(b.head) for b in rig.pose.bones if b.name in ("pelvis","chest","neck","head","upper_arm.R","forearm.R","hand.R","upper_arm.L","forearm.L","hand.L")},
                        weapon=[list(origin-bar @ Vector((0.,.13,0.))),list(origin+bar @ Vector((0.,.595,.145)))])
                    records.append(metrics)
                    print("Wrist pose "+json.dumps({k:v for k,v in metrics.items() if k not in ("bones","weapon","arm_frames")}),flush=True)
                if len(poses)==len(COMBAT_POSES):
                    definitions=[("Ready",((0,"ready"),(.6,"ready_breath"),(1.2,"ready"))),
                        ("Rest",((0,"rest"),(.6,"rest_breath"),(1.2,"rest"))),
                        ("GuardBreak",((0,"block"),(.12,"guard_break"),(.30,"guard_break"),(.70,"ready"))),
                        ("Defeat",((0,"ready"),(.06,"hit"),(.16,"defeat"),(.36,"defeat")))]
                    for family in SWING_FAMILIES:
                        for label,stops in (("Attack",ATTACK_STOPS),("Heavy",HEAVY_STOPS),("Recoil",RECOIL_STOPS)):
                            definitions.append((family["name"]+label,self.profile_stops(family_stops(family,stops))))
                    for label,stops in definitions:
                        worst=dict(profile="hero" if self.hero_profile else "npc",name=label,wrist=0.,body_gap_m=10.,floor_gap_m=10.)
                        count=round(stops[-1][0]*30); previous=None
                        for frame in range(count+1):
                            second=frame/30.
                            try: pose=self.pin_supports(self.track_pose(stops,poses,second))
                            except ValueError as error:
                                worst["error"]=str(error); break
                            if label not in ("Rest","GuardBreak","Defeat"):
                                grip=BLOCK_GRIP+(SUPPORT_GRIP-BLOCK_GRIP)*dialogue.smooth(second/.12) if label=="GuardBreak" else SUPPORT_GRIP
                                pose=self.pin_left_grip(pose,grip)
                            metrics=self.guard_metrics(pose); clearance=self.weapon_clearance()
                            current={n:self.result.rig.pose.bones[n].head.copy() for n in ("forearm.R","forearm.L","hand.R","hand.L")}
                            if previous is not None:
                                travel=max((current[n]-previous[n]).length for n in current)
                                if travel>worst.get("joint_delta",0.): worst.update(joint_delta=travel,joint_time=second)
                            previous=current
                            held_sides=1 if label in ("Rest","GuardBreak","Defeat") else 2
                            angle=max(metrics["wrist_angles"][:held_sides])
                            if angle>worst["wrist"]: worst.update(wrist=angle,wrist_time=second,wrist_sides=metrics["wrist_angles"],wrist_components=metrics["wrist_components"])
                            for component in ("deviation","flexion","axial_twist"):
                                value=max(abs(c[component]) for c in metrics["wrist_components"][:held_sides])
                                if value>worst.get(component,0.): worst.update({component:value,component+"_time":second})
                            if clearance["body_gap_m"]<worst["body_gap_m"]: worst.update(body_gap_m=clearance["body_gap_m"],body_time=second,body_part=clearance["body_part"])
                            worst["floor_gap_m"]=min(worst["floor_gap_m"],clearance["floor_gap_m"])
                        tracks.append(worst)
                        print("Wrist track "+json.dumps(worst),flush=True)
            output=ROOT/"Captures/CombatTest/recovery-authoring/wrist-pose-probe.json"
            output.write_text(json.dumps(records),encoding="utf8")
            output.with_name("wrist-track-probe.json").write_text(json.dumps(tracks),encoding="utf8")
            if any("error" in r for r in records): raise ValueError("Pose probe found unreachable grip poses; inspect wrist-pose-probe.json")
            for track in tracks:
                limit=30. if track["name"] in ("Ready","Rest") else 75.
                if "error" in track or track["wrist"]>limit or track.get("deviation",0.)>25.01 or track["body_gap_m"]<0. or track["floor_gap_m"]<.025:
                    raise ValueError("Pose track anatomy: "+str(track))
            return
        if getattr(self, "recovery_probe", False):
            self.build_recovery_actions()
            return
        poses = {n: self.combat_pose(n) for n in ("ready", "ready_breath", "rest", "rest_breath", "block", "block_breath",
                                                  "hit", "hit_settle", "guard_impact", "guard_break", "defeat")}
        # Each family's strike poses, with the inward grip correction that the
        # opposing palm forced on that family recorded by name.
        self.grip_reposition = {}
        for family in SWING_FAMILIES:
            before = getattr(self, "maximum_grip_adjustment", 0.)
            self.maximum_grip_adjustment = 0.
            poses.update({family["prefix"] + n: self.combat_pose(family["prefix"] + n) for n in SWING_POSES})
            self.grip_reposition[family["name"]] = self.maximum_grip_adjustment
            self.maximum_grip_adjustment = max(before, self.maximum_grip_adjustment)
        if getattr(self,"probe_only",False):
            for name in ("ready","rest","loaded","windup","contact","follow","recover","backhand_anticipation","backhand_loaded","backhand_contact","backhand_follow"):
                print("Wrist components "+json.dumps(dict(profile="hero" if self.hero_profile else "npc",name=name,**self.guard_metrics(poses[name]))),flush=True)
            pose=self.track_pose(self.profile_stops(family_stops(SWING_FAMILIES[1],ATTACK_STOPS)),poses,.20)
            print("Wrist components backhand .20 "+json.dumps(self.guard_metrics(pose)),flush=True)
        centre, axis, _ = self.hand_frame("L")
        far_extent = 0.
        for part in self.result.parts:
            obj = part.obj
            if not obj.name.endswith(".L") or not obj.name.startswith(("GEO_Hand", "GEO_Finger", "GEO_Thumb")): continue
            matrix = self.result.rig.matrix_world.inverted() @ obj.matrix_world
            shape = obj.data.shape_keys.key_blocks["CylindricalGrip"]
            far_extent = max(far_extent, max((matrix @ vertex.co-centre).dot(axis * LEFT_GRIP_AXIS) for vertex in shape.data))
        if BLOCK_GRIP + far_extent > .485:
            raise ValueError(f"Left grip reaches the hook: offset={BLOCK_GRIP}, closed-hand extent={far_extent:.4f}")
        self.closed_left_hand_shaft_extent = far_extent
        for family in SWING_FAMILIES:
            for kind in ("contact", "heavy_contact"):
                name = family["prefix"] + kind
                self._reset_pose(); self._apply_pose(poses[name])
                origin, rotation = self.weapon_frame()
                tip = origin + rotation @ Vector((0, .595, .145))
                print(f"Two-hand landmark {name}: physical reach={-tip.y:.4f}m; hook clearance={.49-BLOCK_GRIP-far_extent:.4f}m", flush=True)
                if -tip.y < .95:
                    raise ValueError(f"Two-handed {name} needs physical reach >=0.95m, got {-tip.y:.4f}m")
        for kind in ("ready", "block", "block_breath", "guard_impact"):
            metrics = self.guard_metrics(poses[kind])
            print(f"Guard wrist landmark {kind}: {metrics}", flush=True)
            if kind in GUARD_HEIGHTS and max(metrics["wrist_angles"]) > 35.:
                raise ValueError(f"Guard wrist must continue the forearm without a sharp kink: {kind} {metrics}")
        if getattr(self, "probe_only", False): return
        # Creation order stays a prefix of the old bank: the shared clips, the
        # forehand charge trio, then the backhand family appended.
        backhand = SWING_FAMILIES[1]
        upper_names={bone.name for bone in self.result.rig.pose.bones
                     if bone.name=="spine" or any(parent.name=="spine" for parent in bone.parent_recursive)}
        authored = CLIPS + tuple(clip for clip in BACKHAND_CLIPS if clip[0] in (backhand["attack"], backhand["recoil"]))
        if getattr(self, "dense_charge_probe", False):
            authored = tuple(clip for clip in authored if clip[0] in ("CombatReady", "CombatAttack", "CombatBackhand", "CombatBackhandRecoil"))
        recoil_paths={}
        for name, duration, loop in authored:
            if name in self.result.actions:
                if name==backhand["attack"] and name not in getattr(self,"retimed_cleared_clips",[]):
                    rig=self.result.rig;record=self.result.actions[name];keys=[]
                    rig.animation_data.action=record.action
                    for frame in range(129):
                        bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
                        keys.append((frame/128,self.snapshot_pose()))
                    rig.animation_data.action=None
                    keys=self.clear_retimed_keys(keys,duration,name,start=57)
                    self.retimed_cleared_clips=sorted(set(getattr(self,"retimed_cleared_clips",[]))|{name})
                    if backhand["recoil"] not in self.result.actions:
                        recoil_paths[backhand["name"]]=(keys[:57],self.measure_recoil_speeds(keys[:57]))
                    del self.result.actions[name];bpy.data.actions.remove(record.action)
                    self._create_action(name,"combat",duration,False,128,FPS,keys)
                    for derived in (backhand["charge"],backhand["heavy"]):
                        if derived in self.result.actions:
                            previous=self.result.actions.pop(derived);bpy.data.actions.remove(previous.action)
                    save_bank_checkpoint(self,"backhand-return-repaired")
                continue
            family = next((f for f in SWING_FAMILIES if name in (f["attack"], f["recoil"])), None)
            if family is not None and name==family["recoil"]:
                count=round(duration*FPS)
                reference,speeds=recoil_paths[family["name"]]
                keys=self.recoil_keys(duration,reference,speeds,name)
                keys=self.clear_retimed_keys(keys,duration,name)
                self._create_action(name,"combat",duration,False,count,FPS,keys)
                print("Authored "+name+" from solved full-body "+family["name"]+" prefix",flush=True)
                continue
            if family is not None:
                stops = self.profile_stops(family_stops(family, ATTACK_STOPS if name == family["attack"] else RECOIL_STOPS))
            elif name == "CombatHit":
                stops = ((0, "ready"), (.07, "hit"), (.18, "hit_settle"), (.36, "ready"))
            elif name == "CombatGuardImpact":
                stops = ((0, "block"), (.06, "guard_impact"), (.10, "guard_impact"), (.28, "block"))
            elif name == "CombatGuardBreak":
                stops = ((0, "block"), (.12, "guard_break"), (.30, "guard_break"), (.70, "ready"))
            elif name == "CombatDefeat":
                stops = ((0, "ready"), (.06, "hit"), (DEFEAT_HANDOFF_SECONDS, "defeat"), (.36, "defeat"))
            else:
                stance = "block" if name == "CombatBlock" else "rest" if name == "CombatRest" else "ready"
                stops = ((0, stance), (duration * .42, stance + "_breath"), (duration, stance))
            keys = []
            sample_fps = 25 if loop else FPS
            continuation=None
            right_continuation=None
            count = round(duration * sample_fps)
            for frame in range(count + 1):
                second = frame / sample_fps
                for (a, p), (b, q) in zip(stops, stops[1:]):
                    if second <= b + .00001:
                        weight = dialogue.smooth((second - a) / (b - a))
                        blended = self.track_pose(stops, poses, second)
                        posed = self.pin_supports(blended)
                        reverse_return=name==backhand["attack"]
                        if name != "CombatRest" and name not in RELEASED_SUPPORT_CLIPS and not (reverse_return and frame>=60):
                            grip = BLOCK_GRIP if name in ("CombatBlock", "CombatGuardImpact") else SUPPORT_GRIP
                            if name == "CombatGuardBreak":
                                grip = BLOCK_GRIP + (SUPPORT_GRIP-BLOCK_GRIP) * dialogue.smooth(second / .12)
                            posed = self.author_support_grip(posed, grip, blended_left_pole(p, q, weight),context=f"{name}@{second}",continuation=continuation)
                        if not reverse_return:
                            raw_arm_pose=posed
                            posed=self.project_weapon_arm(posed,right_continuation,f"{name}@{second}")
                            right_continuation=self.weapon_arm_state(posed,second,raw_arm_pose)
                        if name != "CombatRest" and name not in RELEASED_SUPPORT_CLIPS and not (reverse_return and frame>=60):
                            continuation=self.support_branch(posed)
                        keys.append((frame / count, posed))
                        break
            if name==backhand["attack"]:
                reference=self.solve_backhand_arm_path(keys[:60])
                self.backhand_reference=reference
                keys[:60]=reference
                # Recoil must start from the physical contact this path chose,
                # including its neutral wrist roll and continuous elbow plane.
                contact=reference[56][1]
                cache=self.combat_pose_cache
                for kind in ("backhand_contact","backhand_heavy_contact"):
                    poses[kind]=dict(contact);cache[(self.hero_profile,kind)]=dict(contact)
                cache.pop((self.hero_profile,"backhand_recoil"),None)
                poses.pop("backhand_recoil",None)
                for frame in range(57,count+1):
                    returned=self.sample_forward_keys(reference,self.backhand_return_time(frame/FPS))
                    base=keys[frame][1]
                    keys[frame]=(frame/count,{bone:returned[bone] if bone in upper_names else value for bone,value in base.items()})
                keys=self.clear_retimed_keys(keys,duration,name,start=57)
                self.retimed_cleared_clips=sorted(set(getattr(self,"retimed_cleared_clips",[]))|{name})
            if family is not None and name==family["attack"]:
                reference=keys[:57]
                speeds=self.backhand_edge_speeds if family["name"]=="backhand" else self.measure_recoil_speeds(reference)
                recoil_paths[family["name"]]=(reference,speeds)
            self._create_action(name, "combat", duration, loop, count, sample_fps, keys)
            if name==backhand["attack"] and hasattr(self,"backhand_edge_sample"):
                seconds,expected=self.backhand_edge_sample
                self.result.rig.animation_data.action=self.result.actions[name].action
                sample=seconds*FPS
                bpy.context.scene.frame_set(math.floor(sample),subframe=sample%1.);bpy.context.view_layer.update()
                error=max(abs(expected[bone][i][j]-self.result.rig.pose.bones[bone].matrix[i][j]) for bone in expected for i in range(4) for j in range(4))
                self.result.rig.animation_data.action=None
                if error>1.e-5:raise ValueError(f"Backhand edge FK differs from actual linear Action at {seconds}: {error}")
                print(f"Backhand edge/Action parity at {seconds:.3f}: {error:.9f}",flush=True)
            print("Authored " + name, flush=True)
            if family is not None and name==family["attack"]:
                save_bank_checkpoint(self,family["name"]+"-attack-before-derived")
            metric_key=("hero:" if self.hero_profile else "npc:")+name
            if metric_key in getattr(self,"right_projection_metrics",{}):
                print("Right arm continuity "+metric_key+" "+json.dumps(self.right_projection_metrics[metric_key]),flush=True)
            if name == CLIPS[-1][0] or (getattr(self, "dense_charge_probe", False) and name == "CombatAttack"):
                self.build_charge_actions(SWING_FAMILIES[0], poses)
                save_bank_checkpoint(self,"ordinary-before-dense")
                for ordinary,duration,_ in CLIPS+CHARGE_CLIPS:
                    if ordinary not in self.result.actions:continue
                    print("Checking ordinary presented track before Backhand " + ordinary,flush=True)
                    for _ in presented_weapon_samples(self,ordinary,duration):pass
                self.result.rig.animation_data.action=None
                print("Ordinary presented weapon/arm/speed tracks passed before Backhand",flush=True)
                charge_payload(self,SWING_FAMILIES[0])
                print("Forehand actual source-time powers passed before Backhand",flush=True)
        if not (backhand["heavy"] in getattr(self,"retimed_cleared_clips",[]) and
                all(backhand[key] in self.result.actions for key in ("charge","heavy"))):
            self.build_charge_actions(backhand, poses)

    def build_charge_actions(self, family, poses):
        """Power changes only the upper-body track; both release feet stay identical."""
        rig = self.result.rig
        rig.animation_data.action=None
        for name in (family["light"],family["heavy"],family["charge"]):
            if name and name in self.result.actions:
                previous=self.result.actions.pop(name);bpy.data.actions.remove(previous.action)
        upper = {bone.name for bone in rig.pose.bones
                 if bone.name == "spine" or any(parent.name == "spine" for parent in bone.parent_recursive)}
        attack = self.result.actions[family["attack"]]
        if family["light"]:
            light = attack.action.copy()
            light.name = family["light"]
            light.use_fake_user = True
            self.result.actions[light.name] = replace(attack, action=light)
        light_poses = []
        rig.animation_data.action = attack.action
        for frame in range(129):
            bpy.context.scene.frame_set(frame); bpy.context.view_layer.update()
            light_poses.append(self.snapshot_pose())
        # Both families share one physical arm curve across powers. Advancing
        # its preparation avoids interpolating unrelated elbow/weapon paths.
        keys=[]
        for frame in range(129):
            second=frame/FPS
            if second<.45:
                rig.animation_data.action=attack.action
                source_frame=self.power_source_time(second)*FPS
                bpy.context.scene.frame_set(math.floor(source_frame),subframe=source_frame%1.)
                bpy.context.view_layer.update()
                advanced=self.snapshot_pose()
            else:
                advanced=light_poses[frame]
            keys.append((frame/128,{name:advanced[name] if name in upper else value
                                   for name,value in light_poses[frame].items()}))
        # This standalone table retimes and re-samples the source. Repair only
        # its new windup chords; gameplay samples the unchanged source curve.
        rig.animation_data.action=None
        keys=self.clear_retimed_keys(keys[:46],1.28,family["heavy"])+keys[46:]
        self._create_action(family["heavy"],"combat",1.28,False,128,FPS,keys)
        charge_keys=[]
        for frame in range(FPS+1):
            rig.animation_data.action=attack.action
            source_frame=.18*frame
            bpy.context.scene.frame_set(math.floor(source_frame),subframe=source_frame%1.)
            bpy.context.view_layer.update();advanced=self.snapshot_pose()
            charge_keys.append((frame/FPS,{name:advanced[name] if name in upper else value for name,value in light_poses[0].items()}))
        self._create_action(family["charge"],"combat",1.,False,FPS,FPS,charge_keys)
        self.retimed_cleared_clips=sorted(set(getattr(self,"retimed_cleared_clips",[]))|{family["heavy"]})
        print("Authored "+family["charge"]+"/"+family["heavy"]+" from shared "+family["name"]+" arc",flush=True)

    def build_locomotion_actions(self):
        """A lead-foot opening step followed by the trailing foot closing.

        All four loops start/end in Ready. The motor owns planar translation;
        subtract its virtual .60 m/cycle here so each loaded sole stays fixed
        in world space. Feet never cross and the upper-body guard is reused.
        """
        ready = self.combat_pose("ready")
        rig = self.result.rig
        duration = LOCOMOTION_DURATION
        for name, axis, leading in LOCOMOTION_CLIPS:
            direction = Vector(axis)
            keys = []
            count = round(duration * FPS)
            for frame in range(count + 1):
                t = frame / count
                self._reset_pose(); self._apply_pose(ready)
                pelvis = rig.pose.bones["pelvis"]
                weighted = pelvis.matrix.copy()
                # Lower between the separated soles and shift over whichever
                # leg currently bears weight. There is no root translation.
                weighted.translation += -direction * (.018*math.sin(t*math.tau)) + Vector((0., 0., -.105*math.sin(t*math.pi)**2))
                pelvis.matrix = weighted
                bpy.context.view_layer.update()
                self.locomotion_torso(direction, math.sin(t*math.pi)**2,
                                      math.sin(t*math.pi)**2*dialogue.smooth(t))
                feet = {}
                for side in ("L", "R"):
                    progress = min(1., max(0., 2.*t - (0. if side == leading else 1.)))
                    lift = math.sin(progress*math.pi)**2
                    ankle = self.support(side)
                    ankle += direction * (LOCOMOTION_CYCLE_DISTANCE*(dialogue.smooth(progress)-t))
                    ankle.z += LOCOMOTION_FOOT_LIFT*lift
                    feet[side] = ankle
                keys.append((t, self.pin_supports(self.snapshot_pose(), feet)))
            self._create_action(name, "combat_locomotion", duration, True, count, FPS, keys)
            print("Authored shared " + name, flush=True)

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
                position = weighted.translation + direction * (.02 * load) + Vector((0., 0., -.26*load - .02*settle))
                lean = Vector((0., 0., 1.)).rotation_difference((Vector((0., 0., 1.)) + direction*(.12*load)).normalized())
                pelvis.matrix = Matrix.Translation(position) @ lean.to_matrix().to_4x4() @ weighted.to_3x3().to_4x4()
                bpy.context.view_layer.update()
                landing = dialogue.smooth(travel) * math.sin(second/STEP_DURATION*math.pi)**2
                self.locomotion_torso(direction, load, landing)
                feet = {}
                for side in ("L", "R"):
                    progress = min(1., max(0., 2.*travel - (0. if side == leading else 1.)))
                    lift = math.sin(progress * math.pi)**2
                    ankle = self.support(side)
                    ankle += direction * (STEP_DISTANCE * dialogue.smooth(progress) - root_distance)
                    ankle.z += .06 * lift
                    feet[side] = ankle
                try:
                    stepped = self.pin_supports(self.snapshot_pose(), feet)
                except ValueError as error:
                    raise ValueError(f"{name} at {second:.2f}s: {error}") from error
                keys.append((frame/count, stepped))
            self._create_action(name, "combat_step", STEP_DURATION, False, count, FPS, keys)
            print("Authored shared " + name, flush=True)


def step_payload(builder):
    rig = builder.result.rig
    checksum = hashlib.sha256()
    records = []
    rig.animation_data.action = builder.result.actions["CombatReady"].action
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    ready = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    excluded = {"pelvis", "spine", "chest", "head", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R"}
    for name, axis, leading in STEP_CLIPS:
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Defensive step has object motion")
            checksum.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode())
        direction = Vector(axis)
        support_error = upper_error = seam_error = torso_travel = 0.
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
                rest = builder.support(side)
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
                if bone.name in ("spine", "chest"):
                    torso_travel = max(torso_travel, math.degrees(ready[bone.name].to_quaternion().rotation_difference(bone.rotation_quaternion).angle))
                if sample in (0, count): seam_error = max(seam_error, error)
        if support_error > .002 or upper_error > .00001 or seam_error > .00001 or separation < .18 or min(lifts.values()) < .05 or torso_travel < 1.:
            raise ValueError(f"Defensive step support/guard/seam/spacing failed: {name}: {support_error}, {upper_error}, {seam_error}, {separation}, {lifts}")
        records.append(dict(name=name, leading_foot=leading, direction_unity=[-direction.x, direction.z, -direction.y],
                            maximum_world_support_error_m=support_error, maximum_arm_local_error=upper_error,
                            maximum_torso_counterlean_degrees=torso_travel,
                            maximum_ready_seam_error=seam_error, minimum_foot_separation_m=separation,
                            left_foot_lift_m=lifts["L"], right_foot_lift_m=lifts["R"]))
    return dict(duration_seconds=STEP_DURATION, travel_seconds=STEP_TRAVEL_SECONDS,
                settle_seconds=STEP_SETTLE_SECONDS, distance_m=STEP_DISTANCE, travel_curve="smoothstep",
                validation_hz=200, signature=checksum.hexdigest(), clips=records)


def locomotion_payload(builder, clips=LOCOMOTION_CLIPS):
    rig = builder.result.rig
    checksum = hashlib.sha256()
    records = []
    rig.animation_data.action = builder.result.actions["CombatReady"].action
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    ready = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    excluded = {"pelvis", "spine", "chest", "head", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R"}
    duration = LOCOMOTION_DURATION
    for name, axis, leading in clips:
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Combat locomotion has object motion")
            checksum.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode())
        direction = Vector(axis)
        support_error = upper_error = seam_error = torso_travel = 0.
        lifts = {"L": 0., "R": 0.}
        separation = 100.
        rig.animation_data.action = action
        for sample in range(round(duration*FPS)*2 + 1):
            frame = sample * .5
            t = frame / (duration*FPS)
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.); bpy.context.view_layer.update()
            for side in ("L", "R"):
                foot = rig.pose.bones["foot."+side]
                rest = builder.support(side)
                lifts[side] = max(lifts[side], foot.head.z-rest.z)
                planted = (side == leading and t >= .5) or (side != leading and t <= .5)
                if planted:
                    expected = rest + direction*(LOCOMOTION_CYCLE_DISTANCE*(1. if side == leading else 0.))
                    virtual_world = foot.head + direction*(LOCOMOTION_CYCLE_DISTANCE*t)
                    support_error = max(support_error, (virtual_world-expected).length)
            separation = min(separation, rig.pose.bones["foot.L"].head.x-rig.pose.bones["foot.R"].head.x)
            for bone in rig.pose.bones:
                error = max(abs(bone.matrix_basis[i][j]-ready[bone.name][i][j]) for i in range(4) for j in range(4))
                if bone.name not in excluded: upper_error = max(upper_error, error)
                if bone.name in ("spine", "chest"):
                    torso_travel = max(torso_travel, math.degrees(ready[bone.name].to_quaternion().rotation_difference(bone.rotation_quaternion).angle))
                if sample == 0 or sample == round(duration*FPS)*2: seam_error = max(seam_error, error)
        if support_error > .001 or upper_error > .00001 or seam_error > .00001 or separation < .18 or min(lifts.values()) < .05 or torso_travel < 1.:
            raise ValueError(f"Combat locomotion support/guard/seam/foot spacing failed: {name}: {support_error}, {upper_error}, {seam_error}, {separation}, {lifts}")
        records.append(dict(name=name, leading_foot=leading, direction_unity=[-direction.x,direction.z,-direction.y],
                            maximum_world_support_error_m=support_error, maximum_arm_local_error=upper_error,
                            maximum_torso_counterlean_degrees=torso_travel,
                            maximum_ready_seam_error=seam_error, minimum_foot_separation_m=separation,
                            left_foot_lift_m=lifts["L"], right_foot_lift_m=lifts["R"]))
    return dict(duration_seconds=duration, cycle_distance_m=LOCOMOTION_CYCLE_DISTANCE,
                nominal_speed_m_s=round(LOCOMOTION_CYCLE_DISTANCE/duration, 6), validation_hz=200,
                support_contract="linear virtual root travel cancels each planted sole; leading foot opens during [0,.5], trailing closes during [.5,1]",
                signature=checksum.hexdigest(), clips=records)


def charge_payload(builder, family):
    """One family's charge/release contract. The backhand's light release is its
    attack, so the legacy light-copy comparison is measured only where a copy exists."""
    print("Checking continuous charge/release " + family["name"], flush=True)
    light_name = family["light"] or family["attack"]
    rig = builder.result.rig
    upper = {bone.name for bone in rig.pose.bones
             if bone.name == "spine" or any(parent.name == "spine" for parent in bone.parent_recursive)}
    lower = [bone.name for bone in rig.pose.bones if bone.name not in upper]
    def sample(name, seconds):
        rig.animation_data.action = builder.result.actions[name].action
        frame = seconds * FPS
        bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
        bpy.context.view_layer.update()
        return builder.snapshot_pose(), {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    def difference(a, b, names):
        return max(abs(a[name][i][j] - b[name][i][j]) for name in names for i in range(4) for j in range(4))
    def apply_power(seconds,q,light,heavy):
        advanced,_=sample(light_name,builder.power_source_time(seconds,q))
        pose={name:advanced[name] if name in upper else value for name,value in light.items()}
        rig.animation_data.action=None
        builder._reset_pose();builder._apply_pose(pose)
        return {bone.name:bone.matrix.copy() for bone in rig.pose.bones}
    light_start, support = sample(light_name, 0.)
    heavy_start, _ = sample(family["heavy"], 0.)
    endpoint_error = support_error = lower_error = light_error = 0.
    minimum_left_reach_margin = 100.
    maximum_charge_wrist_angle = maximum_charge_elbow_flexion = 0.
    mixed_weapon=dict(maximum_wrist_degrees=0.,minimum_body_clearance_m=10.,minimum_floor_clearance_m=10.)
    reach = {str(q): 0. for q in (0., .5, 1.)}
    # Endpoint contact alone missed a right elbow folding almost flat during
    # the lift. Measure both joints throughout the actual authored charge.
    for frame in range(FPS + 1):
        sample(family["charge"], frame / FPS)
        for side in ("L", "R"):
            arm, forearm, hand = (rig.pose.bones[name + "." + side]
                                  for name in ("upper_arm", "forearm", "hand"))
            maximum_charge_wrist_angle = max(maximum_charge_wrist_angle,
                math.degrees((hand.head-forearm.head).angle(hand.tail-hand.head)))
            maximum_charge_elbow_flexion = max(maximum_charge_elbow_flexion,
                math.degrees((forearm.head-arm.head).angle(hand.head-forearm.head)))
    if maximum_charge_wrist_angle > 55. or maximum_charge_elbow_flexion > 150.:
        raise ValueError(f"{family['name']} charged lift bends arms beyond their envelope: wrist={maximum_charge_wrist_angle:.2f}, elbow={maximum_charge_elbow_flexion:.2f}")
    for q in (0., .5, 1.):
        _, charge = sample(family["charge"], q)
        released = apply_power(0.,q,light_start,heavy_start)
        endpoint_error = max(endpoint_error, difference(charge, released, charge))
    for frame in range(257):
        seconds = frame / (FPS * 2)
        light, light_world = sample(light_name, seconds)
        heavy, heavy_world = sample(family["heavy"], seconds)
        lower_error = max(lower_error, difference(light_world, heavy_world, lower))
        if family["light"]:
            _, attack = sample(family["attack"], seconds)
            light_error = max(light_error, difference(light_world, attack, attack))
        for q in (0., .25, .5, .75, 1.):
            pose = apply_power(seconds,q,light,heavy)
            support_error = max(support_error, difference(pose, support, ("root", "foot.L", "foot.R")))
            wrist, _ = builder.left_contact_frame()
            arm, forearm = rig.pose.bones["upper_arm.L"], rig.pose.bones["forearm.L"]
            minimum_left_reach_margin = min(minimum_left_reach_margin,
                arm.bone.length + forearm.bone.length - (wrist-arm.head).length)
            if .45 <= seconds <= .63 and str(q) in reach:
                grip = pose["SOCKET_Grip.R"]
                tip = grip @ Vector((0, .595, .145))
                reach[str(q)] = max(reach[str(q)], -tip.y)
            # Runtime's final support solve follows the mixed right grip.
            try:
                builder.pin_left_grip(builder.snapshot_pose(),move_right=False,runtime_support=True)
            except ValueError as error:
                raise ValueError(f"{family['name']} power {q}@{seconds:.4f}: {error}") from error
            metric=builder.weapon_anatomy(f"{family['name']} power {q}@{seconds:.4f}")
            mixed_weapon["maximum_wrist_degrees"]=max(mixed_weapon["maximum_wrist_degrees"],metric["maximum_wrist_degrees"])
            mixed_weapon["minimum_body_clearance_m"]=min(mixed_weapon["minimum_body_clearance_m"],metric["body_gap_m"])
            mixed_weapon["minimum_floor_clearance_m"]=min(mixed_weapon["minimum_floor_clearance_m"],metric["floor_gap_m"])
    if endpoint_error > .00001 or lower_error > .00001 or light_error > .00001:
        raise ValueError(f"{family['name']} charged release mismatched pose/feet/legacy stroke: {endpoint_error}/{lower_error}/{light_error}")
    # Quarter powers catch quaternion interpolation differences that endpoints
    # and a midpoint cannot expose; they share the same entry tolerance.
    for q in (.25, .75):
        _, charge = sample(family["charge"], q)
        released = apply_power(0.,q,light_start,heavy_start)
        if difference(charge, released, charge) > .00001:
            raise ValueError(f"{family['name']} charged quarter-power entry mismatch: {q}")
    if support_error > .001 or min(reach.values()) < .95:
        raise ValueError(f"{family['name']} charged release lost grounded supports/reach: {support_error}/{reach}")
    if minimum_left_reach_margin < .010:
        raise ValueError(f"{family['name']} mixed charge/release left wrist lacks10mm reach margin: {minimum_left_reach_margin:.6f}m")
    parameterization=dict(parameterization="shared_light_time",source_clip=family["attack"],
        preparation_advance_seconds=.18,convergence_seconds=.45,charge_source_seconds=.18)
    return dict(charge_clip=family["charge"], release_light=light_name, release_heavy=family["heavy"], weapon_anatomy=mixed_weapon,
                charge_parameter="linear", release_seconds=1.28, windup_seconds=.45, active_seconds=.18,
                recovery_seconds=.65, powers=[0., .5, 1.], validation_hz=FPS * 2,
                maximum_entry_error=endpoint_error, maximum_lower_track_error=lower_error,
                maximum_light_legacy_error=light_error, maximum_support_error=support_error,
                maximum_charge_wrist_deflection_degrees=maximum_charge_wrist_angle,
                maximum_charge_elbow_flexion_degrees=maximum_charge_elbow_flexion,
                minimum_reach_m=min(reach.values()), minimum_left_wrist_reach_margin_m=minimum_left_reach_margin,**parameterization)


def holding_payload(builder):
    """Measure the contacts players actually see on the shaft and planted feet."""
    print("Checking opposed palms, guard and wide stance", flush=True)
    rig = builder.result.rig
    gap = axis_error = palm_error = guard_elevation = 0.
    raw_gap=0.;raw_gap_at=None
    ready_heights, block_heights = [], []
    guard_wrist_angle = 0.
    loop_travel = {}
    for name, duration, loop in SHARED_CLIPS:
        # A charge samples the source stroke's upper body on the starting support pose.
        if name == "CombatRest" or name in CHARGE_CLIP_NAMES: continue
        action=builder.result.actions[name].action
        centres = []
        for sample in range(round(duration * FPS) + 1):
            second = sample / FPS
            rig.animation_data.action=action
            bpy.context.scene.frame_set(sample); bpy.context.view_layer.update()
            origin, rotation = builder.weapon_frame()
            offset = BLOCK_GRIP if name in ("CombatBlock", "CombatGuardImpact") else SUPPORT_GRIP
            if name == "CombatGuardBreak":
                offset = BLOCK_GRIP + (SUPPORT_GRIP-BLOCK_GRIP) * dialogue.smooth(second / .12)
            if name not in RELEASED_SUPPORT_CLIPS:
                hand=rig.pose.bones["hand.L"];centre,_,_=builder.hand_frame("L")
                raw=((hand.matrix @ hand.bone.matrix_local.inverted()) @ centre-origin-rotation @ Vector((0,offset,0))).length
                if raw>raw_gap:raw_gap=raw;raw_gap_at=dict(clip=name,seconds=second)
                right_matrix=rig.pose.bones["hand.R"].matrix.copy()
                posed=builder.snapshot_pose();rig.animation_data.action=None
                builder.pin_left_grip(posed,offset,move_right=False,runtime_support=True)
                if max(abs(right_matrix[i][j]-rig.pose.bones["hand.R"].matrix[i][j]) for i in range(4) for j in range(4))>1.e-5:
                    raise ValueError("Presented holding moved the owning right hand: "+name)
            hand = rig.pose.bones["hand.L"]
            delta = hand.matrix @ hand.bone.matrix_local.inverted()
            centre, axis, palm = builder.hand_frame("L")
            right = rig.pose.bones["hand.R"]
            right_delta = right.matrix @ right.bone.matrix_local.inverted()
            if name not in RELEASED_SUPPORT_CLIPS:
                gap = max(gap, (delta @ centre - origin - rotation @ Vector((0, offset, 0))).length)
                axis_error = max(axis_error, 1. - (delta.to_3x3() @ axis).normalized().dot(rotation @ Vector((0, LEFT_GRIP_AXIS, 0))))
                palm_error = max(palm_error, 1. + (delta.to_3x3() @ palm).normalized().dot(
                    (right_delta.to_3x3() @ builder.hand_frame("R")[2]).normalized()))
            if name == "CombatBlock":
                guard_elevation = max(guard_elevation, math.degrees(math.asin(abs((rotation @ Vector((0, 1, 0))).z))))
                block_heights.append(origin.z)
            if name in ("CombatBlock", "CombatGuardImpact"):
                for side in ("R", "L"):
                    forearm, hand = (rig.pose.bones[bone + "." + side] for bone in ("forearm", "hand"))
                    guard_wrist_angle = max(guard_wrist_angle, math.degrees((hand.head-forearm.head).angle(hand.tail-hand.head)))
            if name == "CombatReady": ready_heights.append(origin.z)
            if loop: centres.append(origin.copy())
        if loop: loop_travel[name] = max((a-b).length for a in centres for b in centres)
    if gap > .001 or axis_error > .0001 or palm_error > .0001 or guard_elevation > 20.:
        raise ValueError(f"Combat two-hand contact/axis/opposed palms/horizontal guard failed: {gap}/{axis_error}/{palm_error}/{guard_elevation}")
    if not .85 < min(ready_heights) <= max(ready_heights) < 1.3 or min(loop_travel.values()) < .005:
        raise ValueError(f"Combat ready is too high or the breathing is frozen: {ready_heights[0]}/{loop_travel}")
    if not 1.45 < min(block_heights) <= max(block_heights) < 1.55 or guard_wrist_angle > 35.:
        raise ValueError(f"Combat high guard height or wrist alignment failed: {min(block_heights)}/{max(block_heights)}/{guard_wrist_angle}")
    rig.animation_data.action = builder.result.actions["CombatReady"].action
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    ready_pelvis_height = rig.pose.bones["pelvis"].head.z
    ready_knee_flexion = {side: math.degrees((rig.pose.bones["shin."+side].head-rig.pose.bones["thigh."+side].head).angle(
        rig.pose.bones["foot."+side].head-rig.pose.bones["shin."+side].head)) for side in ("L", "R")}
    width = builder.support("L").x-builder.support("R").x
    stagger = builder.support("R").y-builder.support("L").y
    if not .40 <= width <= .45 or not .25 <= stagger <= .30 or min(ready_knee_flexion.values()) < 20.:
        raise ValueError(f"Ready needs a broad, soft-kneed base: {width}/{stagger}/{ready_knee_flexion}")
    print("Holding raw binding "+json.dumps(dict(maximum_error_m=raw_gap,at=raw_gap_at)),flush=True)
    return dict(ready_grip_offset_m=SUPPORT_GRIP, block_grip_offset_m=BLOCK_GRIP,
                released_support_clips=RELEASED_SUPPORT_CLIPS,
                left_grip_axis_sign=LEFT_GRIP_AXIS, maximum_authored_left_contact_error_m=raw_gap,
                maximum_authored_left_contact_error_at=raw_gap_at,maximum_presented_left_contact_error_m=gap,
                maximum_opposed_palm_error=palm_error,
                maximum_left_axis_error=axis_error, maximum_block_elevation_degrees=guard_elevation,
                minimum_ready_grip_height_m=min(ready_heights), maximum_ready_grip_height_m=max(ready_heights),
                minimum_block_grip_height_m=min(block_heights), maximum_block_grip_height_m=max(block_heights),
                maximum_guard_wrist_deflection_degrees=guard_wrist_angle,
                breathing_grip_travel_m=loop_travel,
                maximum_two_hand_grip_reposition_m=getattr(builder, "maximum_grip_adjustment", 0.),
                left_hand_shaft_extent_m=builder.closed_left_hand_shaft_extent,
                block_hook_clearance_m=.49-BLOCK_GRIP-builder.closed_left_hand_shaft_extent,
                foot_offsets_blender_m=SUPPORT_OFFSETS,
                foot_yaw_blender_degrees=SUPPORT_YAW_DEGREES,
                stance_width_m=width, stance_stagger_m=stagger,
                ready_pelvis_height_m=ready_pelvis_height, ready_knee_flexion_degrees=ready_knee_flexion,
                runtime_contract="final left cylinder solve after charge blend, injury and pose recovery; same shaft axis, opposed palms")


def presented_weapon_samples(builder,name,duration):
    """The same final two-hand pose and dense physical gates as runtime presentation."""
    rig=builder.result.rig;action=builder.result.actions[name].action
    previous_elbows=None
    for sample in range(round(duration*FPS)*2+1):
        frame=sample*.5
        rig.animation_data.action=action
        bpy.context.scene.frame_set(math.floor(frame),subframe=frame%1.);bpy.context.view_layer.update()
        if name not in RELEASED_SUPPORT_CLIPS and name!="CombatRest":
            posed=builder.snapshot_pose();rig.animation_data.action=None
            builder.pin_left_grip(posed,BLOCK_GRIP if name in ("CombatBlock","CombatGuardImpact") else SUPPORT_GRIP,
                                  move_right=False,runtime_support=True)
        metric=builder.weapon_anatomy(f"{name}@{frame/FPS:.4f}",30. if name in ("CombatReady","CombatRest") else 55. if name in CHARGE_CLIP_NAMES else 75.)
        elbows={s:rig.pose.bones["forearm."+s].head.copy() for s in ("L","R")}
        speed=0. if previous_elbows is None else max((elbows[s]-previous_elbows[s]).length*FPS*2 for s in elbows)
        if speed>6.:raise ValueError(f"Discontinuous elbow {name}@{frame/FPS:.4f}: {speed:.6f} m/s")
        previous_elbows=elbows
        metric["elbow_speed_m_s"]=speed
        yield frame,metric


def action_payload(builder):
    rig = builder.result.rig
    checksum = hashlib.sha256()
    base_checksum = hashlib.sha256()
    attack_names = [family["attack"] for family in SWING_FAMILIES]
    endpoint_clips = set(attack_names) | {"CombatHit", "CombatGuardImpact"}
    points = {name: [] for name in attack_names}
    windup_tips={}
    active_tip_travel={name:0. for name in attack_names}
    attack_pelvis = {name: [] for name in attack_names}
    attack_knee_travel = {name: 0. for name in attack_names}
    initial_knees = {}
    lower_error = 0.
    support_angle = 0.
    body_rotation = {name: {bone: 0. for bone in ("pelvis", "spine", "chest", "head")} for name in attack_names}
    world_body_samples={name:{bone:[] for bone in ("spine","chest")} for name in attack_names}
    initial_body = {}
    contact_rotations = {name: {} for name in attack_names}
    reference = None
    weapon_metrics={}
    for name, duration, loop in SHARED_CLIPS:
        print("Checking bone/contact track " + name, flush=True)
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Combat object motion")
            curve_bytes = json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode()
            checksum.update(curve_bytes)
            if name in ("CombatReady", "CombatAttack", "CombatBlock", "CombatHit"): base_checksum.update(curve_bytes)
        rig.animation_data.action = action
        weapon_metrics[name]=dict(maximum_wrist_degrees=0.,minimum_body_clearance_m=10.,minimum_floor_clearance_m=10.)
        for frame,metric in presented_weapon_samples(builder,name,duration):
            measured=weapon_metrics[name]
            measured["maximum_wrist_degrees"]=max(measured["maximum_wrist_degrees"],metric["maximum_wrist_degrees"])
            measured["minimum_body_clearance_m"]=min(measured["minimum_body_clearance_m"],metric["body_gap_m"])
            measured["minimum_floor_clearance_m"]=min(measured["minimum_floor_clearance_m"],metric["floor_gap_m"])
            measured["maximum_elbow_speed_m_s"]=max(measured.get("maximum_elbow_speed_m_s",0.),metric["elbow_speed_m_s"])
            support = {n: rig.pose.bones[n].matrix.copy() for n in ("root", "foot.L", "foot.R")}
            if reference is None: reference = support
            lower_error = max(lower_error, max((reference[n].translation - support[n].translation).length for n in reference))
            support_angle = max(support_angle, max(math.degrees(reference[n].to_quaternion().rotation_difference(
                support[n].to_quaternion()).angle) for n in reference))
            if name in attack_pelvis:
                attack_pelvis[name].append(rig.pose.bones["pelvis"].head.copy())
                if frame == 0:
                    initial_knees[name] = {s: rig.pose.bones["shin."+s].rotation_quaternion.copy() for s in ("L", "R")}
                    initial_body[name] = {bone: rig.pose.bones[bone].rotation_quaternion.copy() for bone in body_rotation[name]}
                attack_knee_travel[name] = max(attack_knee_travel[name], max(math.degrees(initial_knees[name][s].rotation_difference(
                    rig.pose.bones["shin."+s].rotation_quaternion).angle) for s in initial_knees[name]))
                for bone in body_rotation[name]:
                    body_rotation[name][bone] = max(body_rotation[name][bone], math.degrees(initial_body[name][bone].rotation_difference(
                        rig.pose.bones[bone].rotation_quaternion).angle))
                for bone in world_body_samples[name]:
                    world_body_samples[name][bone].append(rig.pose.bones[bone].matrix.to_quaternion().normalized())
                if frame in (55, 56, 57):
                    contact_rotations[name][frame] = {bone: rig.pose.bones[bone].rotation_quaternion.copy() for bone in ("spine", "chest")}
            if name in points and frame in (0, 45, 50, 55, 56, 60, 63, 128):
                grip = rig.pose.bones["SOCKET_Grip.R"].matrix
                # Socket +Y is the exported transform's +Y. Local Z uses the
                # reflected FBX basis, whose sign is checked again in Unity.
                tip = grip @ Vector((0, .595, .145))
                p = grip.translation
                points[name].append(dict(seconds=frame / FPS, grip=[-p.x, p.z + .04, -p.y],
                                         tip=[-tip.x, tip.z + .04, -tip.y]))
            if name in points and 45<=frame<=63:
                tip=rig.pose.bones["SOCKET_Grip.R"].matrix @ Vector((0,.595,.145))
                if frame==45:windup_tips[name]=tip.copy()
                active_tip_travel[name]=max(active_tip_travel[name],(tip-windup_tips[name]).length)
            if frame == 0:
                initial = {b.name: b.matrix.copy() for b in rig.pose.bones}
            if frame == round(duration * FPS) and (loop or name in endpoint_clips):
                if max(abs(initial[b.name][i][j] - b.matrix[i][j]) for b in rig.pose.bones for i in range(4) for j in range(4)) > .00001:
                    raise ValueError("Combat action endpoint mismatch: " + name)
        checkpoint=ROOT/"Captures/CombatTest/recovery-authoring/production-bank"
        checkpoint.mkdir(parents=True,exist_ok=True)
        profile="hero" if builder.hero_profile else "npc"
        (checkpoint/(profile+"-validated-tracks.json")).write_text(json.dumps(dict(
            source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            completed_through=name,curve_prefix_sha256=checksum.hexdigest(),tracks=weapon_metrics),indent=2),encoding="utf8")
    if lower_error > .001 or support_angle > .1:
        raise ValueError(f"Combat action moved foot contacts: {lower_error:.6f} m, {support_angle:.4f} deg")
    pelvis_travel = {name: max((a-b).length for a in pts for b in pts) for name, pts in attack_pelvis.items()}
    reach = {name: max(p["tip"][2] for p in points[name] if .45 <= p["seconds"] <= .63) for name in attack_names}
    for name in attack_names:
        if pelvis_travel[name] < .04 or attack_knee_travel[name] < 7.:
            raise ValueError(name + " lacks authored leg/hip weight transfer")
        if reach[name] < .95:
            raise ValueError(f"Crowbar cannot reach a target in front: {name} {reach[name]:.4f}")
        minimum_travel=1. if name=="CombatAttack" else .60
        if active_tip_travel[name]<minimum_travel:
            raise ValueError(f"{name} active arc is too short: {active_tip_travel[name]:.6f}/{minimum_travel}m")
    contact_speeds = {name: {bone: [math.degrees(contact_rotations[name][a][bone].rotation_difference(
        contact_rotations[name][b][bone]).angle)*FPS for a,b in ((55,56), (56,57))] for bone in ("spine", "chest")}
        for name in attack_names}
    world_body_range={name:{bone:max(min(angle,360.-angle)
        for index,a in enumerate(samples) for b in samples[index+1:]
        for angle in (math.degrees(a.rotation_difference(b).angle),))
        for bone,samples in bones.items()} for name,bones in world_body_samples.items()}
    if body_rotation["CombatAttack"]["pelvis"] < 4. or body_rotation["CombatAttack"]["spine"] < 9.:
        raise ValueError(f"CombatAttack lost authored hip/spine drive: {body_rotation['CombatAttack']}")
    # The compact backhand drives one linked torso chain. Measure its actual
    # world excursion, alongside the unchanged hip travel and knee bend gates.
    if world_body_range["CombatBackhand"]["spine"] < 5. or world_body_range["CombatBackhand"]["chest"] < 15.:
        raise ValueError(f"CombatBackhand lost world torso drive: {world_body_range['CombatBackhand']}")
    if min(contact_speeds["CombatAttack"]["chest"]) < 100.:
        raise ValueError(f"Forehand torso stopped at contact: {contact_speeds['CombatAttack']}")
    def body_payload(name):
        return dict(interpolation="C1 quaternion Bezier; monotone local translation; pinned feet and grip",
                    attack_local_rotation_degrees=body_rotation[name], attack_world_rotation_range_degrees=world_body_range[name],
                    contact_angular_speed_degrees_s=contact_speeds[name])
    def snapshot(name, seconds):
        rig.animation_data.action = builder.result.actions[name].action
        bpy.context.scene.frame_set(round(seconds * FPS)); bpy.context.view_layer.update()
        return {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    reactions = []
    peak_tips = []
    peak_poses=[]
    for name, peak, enter, enter_time, exit_clip, exit_time in REACTIONS:
        start = snapshot(name, 0.)
        end = snapshot(name, next(d for n,d,_ in SHARED_CLIPS if n == name))
        for measured, expected in ((start, snapshot(enter, enter_time)), (end, snapshot(exit_clip, exit_time))):
            if max(abs(measured[n][i][j] - expected[n][i][j]) for n in measured for i in range(4) for j in range(4)) > .00001:
                raise ValueError("Reaction endpoint mismatch: " + name)
        impact = snapshot(name, peak)
        travel = (impact["SOCKET_Grip.R"].translation - start["SOCKET_Grip.R"].translation).length
        head_travel = (impact["head"].translation - start["head"].translation).length
        if travel < .09: raise ValueError("Reaction is too small to read at PS1 scale: " + name)
        peak_tips.append(impact["SOCKET_Grip.R"] @ Vector((0, .595, .145)))
        peak_poses.append((name,impact))
        reactions.append(dict(name=name, peak_seconds=peak, entry_clip=enter, entry_seconds=enter_time,
                              exit_clip=exit_clip, exit_seconds=exit_time, grip_travel_m=travel,
                              head_travel_m=head_travel))
    separation = min((a - b).length for i,a in enumerate(peak_tips) for b in peak_tips[i + 1:])
    silhouettes=[]
    for i,(first,a) in enumerate(peak_poses):
        for j in range(i+1,len(peak_poses)):
            second,b=peak_poses[j]
            distances=dict(tip_separation_m=(peak_tips[i]-peak_tips[j]).length,
                head_separation_m=(a["head"].translation-b["head"].translation).length,
                left_elbow_separation_m=(a["forearm.L"].translation-b["forearm.L"].translation).length,
                right_elbow_separation_m=(a["forearm.R"].translation-b["forearm.R"].translation).length)
            if distances["tip_separation_m"]<.20 and not (distances["head_separation_m"]>=.12 and
                max(distances["left_elbow_separation_m"],distances["right_elbow_separation_m"])>=.10):
                raise ValueError(f"Combat reaction silhouettes are indistinct: {first}/{second} {distances}")
            silhouettes.append(dict(first_clip=first,second_clip=second,**distances))
    defeat_start, ready = snapshot("CombatDefeat", 0.), snapshot("CombatReady", 0.)
    defeat, defeat_end = snapshot("CombatDefeat", DEFEAT_HANDOFF_SECONDS), snapshot("CombatDefeat", .36)
    for a, b in ((defeat_start, ready), (defeat, defeat_end)):
        if max(abs(a[n][i][j] - b[n][i][j]) for n in a for i in range(4) for j in range(4)) > .00001:
            raise ValueError("Defeat entry or held physical handoff differs")
    defeat_drop = ready["pelvis"].translation.z - defeat["pelvis"].translation.z
    if not .04 <= defeat_drop <= .12: raise ValueError("Defeat must lose balance without authoring a fall")
    charging = {family["name"]: charge_payload(builder, family) for family in SWING_FAMILIES}
    # Per-side strike contracts; the forehand's values also stay under their
    # original top-level keys, which the older readers still cite.
    swings = [dict(name=family["name"], attack_clip=family["attack"], release_light=family["light"] or family["attack"],
                   release_heavy=family["heavy"], charge_clip=family["charge"], recoil_clip=family["recoil"],
                   contact_seconds=.56, windup_seconds=.45, active_seconds=.18, recovery_seconds=.65,
                   pelvis_travel_m=pelvis_travel[family["attack"]], knee_travel_degrees=attack_knee_travel[family["attack"]],
                   minimum_reach_m=reach[family["attack"]],
                   active_tip_travel_m=active_tip_travel[family["attack"]],
                   maximum_grip_reposition_m=getattr(builder, "grip_reposition", {}).get(family["name"], 0.),
                   strike_samples=points[family["attack"]], charging=charging[family["name"]],
                   body_motion=body_payload(family["attack"]))
              for family in SWING_FAMILIES]
    return dict(rig="HeroV2", npc_rig="NpcHumanV2", fps=FPS, root_motion=False, animation_events=0, weapon_anatomy=weapon_metrics,
                clips=[dict(name=n, duration_seconds=d, loop=l) for n,d,l in SHARED_CLIPS],
                windup_seconds=.45, active_seconds=.18, recovery_seconds=.65,
                maximum_support_error=lower_error, maximum_support_angle_degrees=support_angle,
                support_validation_hz=200, support_bones=["root", "foot.L", "foot.R"],
                attack_pelvis_travel_m=pelvis_travel["CombatAttack"], attack_knee_travel_degrees=attack_knee_travel["CombatAttack"],
                body_motion=body_payload("CombatAttack"),
                defeat_handoff_seconds=DEFEAT_HANDOFF_SECONDS, defeat_pelvis_drop_m=defeat_drop,
                animation_signature=checksum.hexdigest(),
                base_action_signature=base_checksum.hexdigest(), strike_samples=points["CombatAttack"],
                reactions=reactions, minimum_reaction_tip_separation_m=separation,
                reaction_silhouette_contract="tip_20cm_or_head_12cm_and_elbow_10cm",reaction_silhouette_pairs=silhouettes,
                charging=charging["forehand"], swings=swings, holding=holding_payload(builder))


def meta(path):
    target = path.with_name(path.name + ".meta")
    if not target.exists():
        target.write_text("fileFormatVersion: 2\nguid: " + hashlib.sha256(path.relative_to(ROOT).as_posix().encode()).hexdigest()[:32] + "\n", encoding="utf8")


def save_bank_checkpoint(builder,stage):
    # Retain the completed calculation outside the launcher's temporary stage:
    # a downstream contract failure must not destroy the solved action curves.
    checkpoint=ROOT/"Captures/CombatTest/recovery-authoring/production-bank"
    checkpoint.mkdir(parents=True,exist_ok=True)
    profile="hero" if builder.hero_profile else "npc"
    for record in builder.result.actions.values():record.action.use_fake_user=True
    path=checkpoint/(profile+"-"+stage+".blend")
    libraries={Path(bpy.path.abspath(library.filepath)).resolve() for library in bpy.data.libraries}
    if path.resolve() in libraries:path=path.with_stem(path.stem+"-continued")
    common.save_blend(path)
    state=dict(profile=profile,source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        root=builder.result.root.name,rig=builder.result.rig.name,
        actions={name:{key:value for key,value in vars(record).items() if key!="action"} for name,record in builder.result.actions.items()},
        measurements={name:getattr(builder,name) for name in ("maximum_grip_adjustment","grip_reposition","closed_left_hand_shaft_extent","recovery_measurements","right_projection_metrics","retimed_cleared_clips") if hasattr(builder,name)})
    path.with_suffix(".json").write_text(json.dumps(state,indent=2),encoding="utf8")


def complete_bank_payload(builder):
    if not all(name in builder.result.actions for name in RECOVERY_CLIPS):builder.build_recovery_actions()
    if not all(name in builder.result.actions for name,_,_ in STEP_CLIPS):builder.build_step_actions()
    if not all(name in builder.result.actions for name,_,_ in LOCOMOTION_CLIPS):builder.build_locomotion_actions()
    save_bank_checkpoint(builder,"before-payload")
    measured = None
    if getattr(builder, "reuse_unchanged_actions", False):
        previous=builder.previous_payload["actions"]
        if not builder.hero_profile: previous=previous["npc"]
        checksum=hashlib.sha256()
        for name,_,_ in SHARED_CLIPS:
            for curve in common.iter_action_fcurves(builder.result.actions[name].action):
                checksum.update(json.dumps([name,curve.data_path,curve.array_index,
                    [[round(v,7) for v in key.co] for key in curve.keyframe_points]],separators=(",",":")).encode())
        if checksum.hexdigest() == previous["animation_signature"]:
            measured=json.loads(json.dumps(previous))
            measured.pop("npc",None)
            print("Unchanged shared action curves match published SHA; reusing their contracts",flush=True)
        else:
            print("Shared action curves changed; checking their complete contracts",flush=True)
    if measured is None: measured = action_payload(builder)
    measured["released_support_clips"] = list(RELEASED_SUPPORT_CLIPS)
    print("Base action/contact bank passed", flush=True)
    measured["recovery"] = dict(lead_foot="Left", markers=RECOVERY_MARKERS,
        clips=builder.recovery_measurements, endpoint="CombatReady", root_motion=False,
        hand_sequence="prone left floor -> left knee -> shaft; supine left floor -> balance -> shaft; right retains weapon")
    measured["profile"] = "frightened_novice" if builder.hero_profile else "sparring_opponent"
    measured["step_clips"] = [dict(name=n, duration_seconds=STEP_DURATION, loop=False) for n,_,_ in STEP_CLIPS]
    measured["defensive_step"] = step_payload(builder)
    measured["locomotion_clips"] = [dict(name=n, duration_seconds=LOCOMOTION_DURATION, loop=True) for n,_,_ in LOCOMOTION_CLIPS]
    measured["locomotion"] = locomotion_payload(builder)
    # Retain the old lateral measurement key for readers interested only in
    # strafe contacts. These clips now belong to both banks.
    measured["strafing"] = locomotion_payload(builder, LOCOMOTION_CLIPS[2:])
    profile="hero" if builder.hero_profile else "npc"
    checkpoint=ROOT/"Captures/CombatTest/recovery-authoring/production-bank"
    (checkpoint/(profile+"-validated-payload.json")).write_text(json.dumps(dict(
        source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        curve_signature=measured["animation_signature"],payload=measured),indent=2),encoding="utf8")
    return measured


def main():
    global OUT, SOURCE
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--actions-only", action="store_true")
    parser.add_argument("--probe-only", action="store_true")
    parser.add_argument("--dense-hero-probe", action="store_true")
    parser.add_argument("--recovery-probe", nargs="?", const="all", choices=("all","prone","supine"))
    parser.add_argument("--pose-probe", action="store_true")
    parser.add_argument("--reuse-unchanged-actions", action="store_true")
    parser.add_argument("--resume-npc-bank",type=Path)
    parser.add_argument("--output-dir", type=Path, default=OUT)
    parser.add_argument("--source-dir", type=Path, default=SOURCE)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.reuse_unchanged_actions and not args.actions_only:
        parser.error("--reuse-unchanged-actions requires --actions-only")
    if args.resume_npc_bank and (not args.actions_only or args.validate_only or args.dense_hero_probe or args.recovery_probe or args.pose_probe or args.probe_only):
        parser.error("--resume-npc-bank requires the ordinary --actions-only production run")
    published_out = OUT
    OUT, SOURCE = args.output_dir.resolve(), args.source_dir.resolve()
    validate_only, actions_only = args.validate_only, args.actions_only
    items = make_items(); signature = kit.signature(items)
    if kit.signature(make_items()) != signature: raise ValueError("Passive geometry is nondeterministic")
    payload = kit.manifest(items, signature)
    next(model for model in payload["models"] if model["name"]=="Crowbar")["collision_shapes"] = dict(
        space="grip_local_unity_metres", segments=[
            dict(start=a,end=b,radius=.019) for a,b in zip(CROWBAR_PATH,CROWBAR_PATH[1:])]+
            [dict(start=(0.,-.075,0.),end=(0.,.08,0.),radius=.024),
             dict(start=(0.,-.119,.012),end=(0.,-.119,.012),radius=.03671),
             dict(start=(0.,.587,.152),end=(0.,.587,.152),radius=.02802)])
    payload.update(generator="tools/build-combat-test-3d-model.py", generator_version="2.0.0", test_only=True)
    OUT.mkdir(parents=True, exist_ok=True); SOURCE.mkdir(parents=True, exist_ok=True)
    if not validate_only and not actions_only:
        roots = kit.build_objects(items)
        for root in roots: kit.export(root, OUT / (root.name + ".fbx"))
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "CombatTest.blend"), check_existing=False)
    kit.verify_fbx(published_out if actions_only else OUT, payload)
    common.ANIMATION_FPS = FPS
    config = common.BuildConfig(SOURCE / "CombatActions.blend", None, None, OUT / "CombatTest3D.json",
                                None, None, OUT / "CombatActions.fbx", 1.75, 20260919, "apose")
    builder = CombatBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.resume_npc_bank=args.resume_npc_bank.resolve() if args.resume_npc_bank else None
    builder.recovery_probe = args.recovery_probe
    builder.pose_probe = args.pose_probe
    builder.reuse_unchanged_actions = args.reuse_unchanged_actions
    if args.reuse_unchanged_actions:
        builder.previous_payload=json.loads((published_out/"CombatTest3D.json").read_text(encoding="utf8"))
    builder.probe_only = args.probe_only
    builder.hero_profile = args.dense_hero_probe
    builder.dense_charge_probe = args.dense_hero_probe
    builder.build()
    if args.recovery_probe or args.pose_probe: return
    if args.dense_hero_probe:
        for family in SWING_FAMILIES:
            print("DENSE HERO CHARGE CONTRACT OK " + json.dumps(charge_payload(builder, family)), flush=True)
        return
    if args.probe_only:
        builder.build_step_actions()
        builder.build_locomotion_actions()
        builder.hero_profile = True
        builder.build_actions()
        print("Combat feet and both profile grips preflight passed", flush=True)
        return
    npc_payload = complete_bank_payload(builder)
    if not validate_only:
        builder.result.root.name = "ROOT_Player"
        common.export_animation_fbx(OUT / "CombatNpcActions.fbx", builder.result)
        checkpoint=ROOT/"Captures/CombatTest/recovery-authoring/production-bank"
        (checkpoint/"CombatNpcActions.fbx").write_bytes((OUT/"CombatNpcActions.fbx").read_bytes())
        print("NPC bank payload passed and FBX exported; checkpoint copy retained",flush=True)
        builder.result.root.name = "ROOT_PlayerV2"
    # The exporter scans all compatible Actions. Remove the completed NPC
    # library before authoring the distinct hero profile on the identical rig.
    builder.result.rig.animation_data.action = None
    for action in tuple(bpy.data.actions): bpy.data.actions.remove(action)
    builder.result.actions.clear()
    builder.maximum_grip_adjustment = 0.
    builder.retimed_cleared_clips=[]
    builder.hero_profile = True
    builder.build_actions()
    payload["actions"] = complete_bank_payload(builder)
    payload["actions"]["npc"] = npc_payload
    payload = json.loads(json.dumps(payload))
    if validate_only:
        if json.loads((OUT / "CombatTest3D.json").read_text(encoding="utf8")) != payload:
            raise ValueError("Combat deterministic manifest differs")
    else:
        common.export_animation_fbx(OUT / "CombatActions.fbx", builder.result)
        builder.restore_mesh_deformation()
        common.save_blend(SOURCE / "CombatActions.blend")
        (OUT / "CombatTest3D.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
        for path in OUT.iterdir():
            if path.suffix != ".meta" and path.is_relative_to(ROOT / "Assets"): meta(path)
    print("COMBAT TEST ART CONTRACT OK " + json.dumps(payload["actions"]), flush=True)


if __name__ == "__main__": main()
