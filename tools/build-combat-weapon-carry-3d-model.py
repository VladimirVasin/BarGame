"""Deterministic grip-local body mounts and six in-place Hero V2 draw/stow clips.

Reads the current hero and existing combat action banks without rewriting them.
Run through tools/run-blender.py; published geometry/actions are round-tripped.
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
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("carry_pistol_helpers", ROOT / "tools/build-combat-pistol-3d-model.py")
pistol = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = pistol
spec.loader.exec_module(pistol)
combat, kit, common, hero = pistol.combat, pistol.kit, pistol.common, pistol.hero
FPS = 100
SURFACES = {"Leather": (.16, .17, .13), "Webbing": (.22, .23, .16), "Steel": (.24, .255, .23)}
DURATIONS = {"Crowbar": 1.30, "Pistol": 1.70, "Shotgun": 2.65}
TRANSFER = {"Crowbar": .38, "Pistol": .48, "Shotgun": .88}
SHOTGUN_PUSH, SHOTGUN_CATCH, SHOTGUN_GRIP = .30, .70, 1.65
# Open right palm below the lower barrels, then the left palm above the stock.
# These are grip-local metres, not new weapon geometry or a moved body dock.
SHOTGUN_PUSH_POINT = (.020, .140, .300)
SHOTGUN_CATCH_POINT = (0., -.040, -.350)
SHOTGUN_PUSHED_CENTRE = (.040, .200, 1.290)
SHOTGUN_PUSHED_UP = (.642787610, 0., -.766044443)
SHOTGUN_PUSHED_FORWARD = (-.766044443, 0., -.642787610)
CLIPS = tuple((weapon + phase, duration, False) for weapon, duration in DURATIONS.items() for phase in ("Draw", "Stow"))
NAMES = {"Crowbar": "CrowbarBeltHolder", "Pistol": "PistolHolster", "Shotgun": "ShotgunSling"}
# Anatomy is source +X left, -Y front and +Z up. Weapons use grip-local
# +Y cylinder axis and +Z barrel. The imported-rig setup measures the final
# hand-to-bone TRS rather than guessing the FBX bone-axis conversion.
SLOTS = {
    "Crowbar": dict(bone="pelvis", centre=(-.225, .055, 1.015), up=(0., 0., -1.), forward=(0., -1., 0.)),
    "Pistol": dict(bone="chest", centre=(.175, -.072, 1.34), up=(-.906307787, -.422618262, 0.), forward=(0., 0., -1.)),
    "Shotgun": dict(bone="chest", centre=(-.10, .140, 1.24), up=(.866025404, 0., -.5), forward=(-.5, 0., -.866025404))}
FLAP = (.16, -.16, 1.35)
PISTOL_LIFT = (.175, -.072, 1.54)
PISTOL_EXIT = (.175, -.205, 1.54)
PISTOL_CONTACT_POLE = (-.95, -.25, -.1)
PISTOL_EXTRACT_POLE = (-.45, -.35, -.85)
PISTOL_POCKET_LIP = .024
LOWER = ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R")
SLING_STOCK = (.036, .062, -.300)
SLING_FOREEND = (.036, .072, .310)
SLING_SEGMENTS = 80
SLING_LENGTH = 1.40
SLING_HAND_FRACTION = 18 / 40
SLING_SAG = .606702087099114
SLING_HAND_STOPS = ((.30, (-.03, -.16, 1.25)), (.52, (.18, -.16, 1.47)),
                    (.62, (.230, -.030, 1.515)), (.88, (.240, .100, 1.515)),
                    (1.08, (.240, .110, 1.520)), (1.35, (.20, -.04, 1.54)),
                    (1.50, (.10, -.19, 1.46)), (1.75, (-.27, -.14, 1.35)))


def sling_hand_at(second):
    if second <= SLING_HAND_STOPS[0][0]: return SLING_HAND_STOPS[0][1]
    if second >= SLING_HAND_STOPS[-1][0]: return SLING_HAND_STOPS[-1][1]
    index = next(i for i in range(len(SLING_HAND_STOPS)-1) if second <= SLING_HAND_STOPS[i+1][0])
    a, b = SLING_HAND_STOPS[index:index+2]
    return tuple(Vector(a[1]).lerp(Vector(b[1]), combat.dialogue.smooth((second-a[0])/(b[0]-a[0]))))


def plate(outline, y, thickness):
    """An XZ leather outline, extruded into one outward-wound solid."""
    vertices = [(x, depth, z) for depth in (y - thickness / 2., y + thickness / 2.) for x, z in outline]
    n = len(outline)
    faces = [tuple(range(n)), tuple(reversed(range(n, 2 * n)))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    if kit.bp.signed_volume((vertices, faces)) < 0.: faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def tapered_box(z0, z1, x0, x1, low0, high0, low1, high1):
    """Short formed-leather section; bounds follow the real barrel profile."""
    vertices = [(x, y, z) for z, lo, hi in ((z0, low0, high0), (z1, low1, high1))
                for x, y in ((x0, lo), (x1, lo), (x1, hi), (x0, hi))]
    faces = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    if kit.bp.signed_volume((vertices, faces)) < 0.: faces = [tuple(reversed(f)) for f in faces]
    return vertices, faces


def sling_ribbon():
    vertices = []
    a, b = Vector(SLING_STOCK), Vector(SLING_FOREEND)
    for index in range(SLING_SEGMENTS + 1):
        t = index / SLING_SEGMENTS
        centre = a.lerp(b, t) + Vector((0., -SLING_SAG * math.sin(math.pi * t), 0.))
        vertices.extend(tuple(centre + Vector((x, y, 0.))) for x, y in ((-.0125, -.001), (.0125, -.001), (.0125, .001), (-.0125, .001)))
    faces = [tuple(reversed(range(4))), tuple(range(SLING_SEGMENTS * 4, SLING_SEGMENTS * 4 + 4))]
    for row in range(SLING_SEGMENTS):
        for side in range(4):
            a, b = row * 4 + side, row * 4 + (side + 1) % 4
            faces.append((a, b, b + 4, a + 4))
    if kit.bp.signed_volume((vertices, faces)) < 0.: faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def make_items():
    kit.SURFACES = SURFACES
    pistol_mount = kit.Item("PistolHolster")
    # The grip-local barrel is centred at Y=.088m. Follow that real profile;
    # a plate at Y=0 below the trigger would form an empty, protruding box.
    # Short separately bounded sections also keep the cloth contact OBB tight.
    sections = ((.024, .050, .022, .012, .061, .111, .113),
                (.050, .100, .022, .061, .064, .113, .113),
                (.100, .160, .022, .064, .064, .113, .113),
                (.160, .218, .020, .064, .064, .113, .113))
    for i, (z0, z1, half, back0, back1, front0, front1) in enumerate(sections):
        pistol_mount.add("Leather", tapered_box(z0, z1, -half, half, back0-.003, back0, back1-.003, back1), "PocketBack"+str(i))
        pistol_mount.add("Leather", tapered_box(z0, z1, -half, half, front0, front0+.003, front1, front1+.003), "PocketFront"+str(i))
        for side in (-1, 1):
            x0, x1 = (half, half+.003) if side == 1 else (-half-.003, -half)
            pistol_mount.add("Leather", tapered_box(z0, z1, x0, x1, back0-.003, front0+.003, back1-.003, front1+.003), "PocketSide"+str(i)+str(side))
    pistol_mount.box("Leather", (0., .0885, .220), (.043, .055, .004), "PocketBottom", chamfer=.001)
    pistol_mount.box("Webbing", (0., .010, .013), (.026, .004, .030), "ChestTabs", chamfer=.001)
    pistol_mount.anchor("GripContact", (0., 0., 0.))

    bar_mount = kit.Item("CrowbarBeltHolder")
    bar_mount.box("Leather", (0., .012, -.041), (.073, .198, .015), chamfer=.004)
    # The belt is at source z=.942-.972m, below this cylinder contact. The
    # inward suspension reaches its actual side rather than ending on the coat.
    belt_contact = Vector((.078, .058, -.064))
    start = Vector((0., .058, -.041))
    direction = (belt_contact - start).normalized()
    strap = kit.bp.u_box((0., 0., 0.), ((belt_contact - start).length, .028, .006), .001)
    rotation = Vector((1., 0., 0.)).rotation_difference(direction).to_matrix()
    centre = (start + belt_contact) * .5
    bar_mount.add("Webbing", ([tuple(rotation @ Vector(point) + centre) for point in strap[0]], strap[1]), "BeltSuspension")
    bar_mount.box("Webbing", tuple(belt_contact), (.023, .033, .006), "BeltLoop", chamfer=.001)
    # Two open C retainers release sideways; a closed belt ring would force
    # the hook through its own smaller opening during extraction.
    for y in (-.057, .078):
        previous = None
        for index in range(10):
            angle = math.radians(42. + 276. * index / 9.)
            point = (.030 * math.sin(angle), y, -.030 * math.cos(angle))
            if previous is not None: bar_mount.rod("Steel", previous, point, .005, "OpenRetainers")
            previous = point
    bar_mount.anchor("GripContact", (0., 0., 0.))
    bar_mount.anchor("BeltContact", tuple(belt_contact))

    sling = kit.Item("ShotgunSling")
    sling.add("Webbing", sling_ribbon(), "Ribbon")
    sling.anchor("GripContact", (0., 0., 0.))
    sling.anchor("SlingStock", SLING_STOCK)
    sling.anchor("SlingForeend", SLING_FOREEND)
    return [bar_mount, pistol_mount, sling]


def append_action(path, name):
    with bpy.data.libraries.load(str(path), link=False) as (available, selected):
        if name not in available.actions: raise ValueError("Missing source rest action " + name)
        selected.actions = [name]
    return selected.actions[0]


def snapshot_action(builder, action):
    rig = builder.result.rig
    rig.animation_data_create().action = action
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    return builder.snapshot_pose()


def posed_hand(builder, side):
    rig = builder.result.rig
    hand = rig.pose.bones["hand." + side]
    skin = hand.matrix @ hand.bone.matrix_local.inverted()
    centre, axis, palm = builder.hand_frame(side)
    axis, palm = (skin.to_3x3() @ axis).normalized(), (skin.to_3x3() @ palm).normalized()
    forward = palm.cross(axis).normalized() if side == "R" else axis.cross(palm).normalized()
    shoulder = rig.pose.bones["upper_arm." + side].head
    elbow = rig.pose.bones["forearm." + side].head
    direction = (hand.head - shoulder).normalized()
    pole = (elbow - shoulder - direction * (elbow - shoulder).dot(direction)).normalized()
    return (skin @ centre, axis, forward, pole)


def solve_hand(builder, side, centre, up, forward, pole):
    up, forward = Vector(up).normalized(), Vector(forward).normalized()
    forward = (forward - up * forward.dot(up)).normalized()
    palm = up.cross(forward) if side == "R" else forward.cross(up)
    rotation = pistol.hand_rotation(builder, side, up, palm)
    bone = builder.result.rig.data.bones["hand." + side]
    delta = rotation @ bone.matrix_local.to_3x3().inverted()
    wrist = Vector(centre) - delta @ (builder.hand_frame(side)[0] - bone.head_local)
    builder.solve_arm(side, wrist, rotation, Vector(pole))


def carry_frame(centre, up, forward):
    up, forward = Vector(up).normalized(), Vector(forward).normalized()
    forward = (forward-up*forward.dot(up)).normalized()
    # Quaternion interpolation needs a proper right-handed rotation. Grip-local
    # weapon X is reflected by the project's Unity/Blender axis convention.
    return Vector(centre), Matrix((up.cross(forward), up, forward)).transposed().to_quaternion()


def sample_carry_frames(stops, second):
    index = next((i for i in range(len(stops)-1) if second <= stops[i+1][0]+1e-8), len(stops)-2)
    a, b = stops[index:index+2]
    amount = combat.dialogue.smooth(max(0., min(1., (second-a[0])/(b[0]-a[0]))))
    return a[1][0].lerp(b[1][0], amount), a[1][1].slerp(b[1][1], amount)


def frame_hand(frame, local_point, local_rotation):
    centre, rotation = frame
    point = Vector(local_point); point.x = -point.x
    return centre+rotation@point, rotation@local_rotation


def frame_from_contact(centre, rotation, local_point):
    point = Vector(local_point); point.x = -point.x
    return Vector(centre)-rotation@point, rotation


def solve_frame_hand(builder, side, frame, pole):
    centre, rotation = frame
    solve_hand(builder, side, centre, rotation@Vector((0., 1., 0.)), rotation@Vector((0., 0., 1.)), pole)


def shotgun_draw_frames(builder, neutral, rest):
    """Author one physical gun path with two temporary, measured hand contacts."""
    slot = SLOTS["Shotgun"]
    stored = carry_frame(*(slot[key] for key in ("centre", "up", "forward")))
    pushed = carry_frame(SHOTGUN_PUSHED_CENTRE, SHOTGUN_PUSHED_UP, SHOTGUN_PUSHED_FORWARD)
    forward = carry_frame((-.20, -.58, 1.28), (0., 0., 1.), (0., -1., 0.))
    builder._reset_pose(); builder._apply_pose(neutral)
    relaxed = {side: carry_frame(*posed_hand(builder, side)[:3]) for side in ("R", "L")}
    relaxed_poles = {side: (builder.result.rig.pose.bones["forearm."+side].head-
                           builder.result.rig.pose.bones["upper_arm."+side].head).normalized() for side in ("R", "L")}
    builder._reset_pose(); builder._apply_pose(rest)
    final = {side: carry_frame(*posed_hand(builder, side)[:3]) for side in ("R", "L")}
    # Fingers follow the barrels; the open right palm faces into their underside.
    push_rotation = carry_frame((0., 0., 0.), (1., 0., 0.), (0., 0., 1.))[1]
    # At the catch the left palm faces down. Retain that rigid contact as the
    # stock rotates, then release it only once the normal right grip is secure.
    catch_world = carry_frame((0., 0., 0.), (1., 0., 0.), (0., 1., 0.))[1]
    catch_rotation = pushed[1].inverted()@catch_world
    # Carry the stock around the OUTSIDE of the left shoulder and then in
    # front of the chest. Interpolating gun origins alone sends its upper
    # catch through the neck as the long stock rotates.
    gun_stops = ((0., stored), (SHOTGUN_PUSH, stored), (SHOTGUN_CATCH, pushed),
                 (.90, frame_from_contact((.38, -.10, 1.65), pushed[1].slerp(forward[1], .25), SHOTGUN_CATCH_POINT)),
                 (1.15, frame_from_contact((.30, -.32, 1.53), pushed[1].slerp(forward[1], .75), SHOTGUN_CATCH_POINT)),
                 (1.45, frame_from_contact((.03, -.38, 1.32), forward[1], SHOTGUN_CATCH_POINT)),
                 (SHOTGUN_GRIP, forward), (2.30, final["R"]), (2.65, final["R"]))
    push = frame_hand(stored, SHOTGUN_PUSH_POINT, push_rotation)
    push_end = frame_hand(pushed, SHOTGUN_PUSH_POINT, push_rotation)
    catch = frame_hand(pushed, SHOTGUN_CATCH_POINT, catch_rotation)
    catch_end = frame_hand(forward, SHOTGUN_CATCH_POINT, catch_rotation)
    right_stops = ((0., relaxed["R"]), (SHOTGUN_PUSH, push), (SHOTGUN_CATCH, push_end),
                   (.95, carry_frame((-.34, -.20, 1.00), (0., 0., 1.), (0., -1., 0.))),
                   (SHOTGUN_GRIP, forward), (2.30, final["R"]), (2.65, final["R"]))
    # Approach the butt vertically from above after going outside the body.
    above = catch[0]+Vector((0., 0., .15))
    left_stops = ((0., relaxed["L"]), (.25, carry_frame((.46, -.10, 1.38), (1., 0., 0.), (0., 1., 0.))),
                  (.45, carry_frame((.43, .12, 1.72), (1., 0., 0.), (0., 1., 0.))),
                  (.58, carry_frame(above, (1., 0., 0.), (0., 1., 0.))),
                  (SHOTGUN_CATCH, catch), (SHOTGUN_GRIP, catch_end),
                  (1.95, carry_frame((.14, -.44, 1.40), (1., 0., 0.), (0., -1., 0.))),
                  (2.30, final["L"]), (2.65, final["L"]))
    poses = []
    for frame in range(round(DURATIONS["Shotgun"]*FPS)+1):
        second = frame/FPS
        rest_amount = combat.dialogue.smooth(max(0., min(1., (second-SHOTGUN_GRIP)/.65)))
        base = builder.blend(neutral, rest, rest_amount)
        builder._reset_pose(); builder._apply_pose(base)
        gun = sample_carry_frames(gun_stops, second)
        right = (frame_hand(gun, SHOTGUN_PUSH_POINT, push_rotation) if SHOTGUN_PUSH <= second <= SHOTGUN_CATCH
                 else gun if second >= SHOTGUN_GRIP else sample_carry_frames(right_stops, second))
        left = (frame_hand(gun, SHOTGUN_CATCH_POINT, catch_rotation) if SHOTGUN_CATCH <= second <= SHOTGUN_GRIP
                else sample_carry_frames(left_stops, second))
        # The right elbow stays low; the left elbow opens outward for the
        # overhead catch rather than folding its forearm through the body.
        try:
            entry = combat.dialogue.smooth(max(0., min(1., second/.25)))
            right_pole = relaxed_poles["R"].lerp(Vector((-.95, -.30, -.65)).normalized(), entry)
            solve_frame_hand(builder, "R", right, right_pole)
            pole_amount = combat.dialogue.smooth(max(0., min(1., (second-.75)/.45)))
            left_pole = Vector((.95, .30, -.25)).normalized().lerp(Vector((.85, -.65, -.25)).normalized(), pole_amount)
            left_pole = relaxed_poles["L"].lerp(left_pole, entry)
            solve_frame_hand(builder, "L", left, left_pole)
        except ValueError as error:
            raise ValueError(f"Shotgun draw at {second:.2f}s: {error}; palms R {tuple(right[0])}, L {tuple(left[0])}") from error
        pose = builder.snapshot_pose()
        if second <= .10:
            # Exact relaxed endpoint; enter the Cartesian arm solve smoothly.
            pose = builder.blend(neutral, pose, combat.dialogue.smooth(second/.10))
        elif second >= 2.30:
            pose = builder.blend(pose, rest, combat.dialogue.smooth((second-2.30)/.35))
        poses.append(pose)
    builder.shotgun_draw_contract = dict(push_point=list(SHOTGUN_PUSH_POINT), catch_point=list(SHOTGUN_CATCH_POINT),
        push_rotation=push_rotation.copy(), catch_rotation=catch_rotation.copy(), gun_stops=gun_stops)
    return poses


def shotgun_draw_gun_frame(builder, second):
    """Reconstruct the weapon from its current physical parent, as Unity does."""
    slot = SLOTS["Shotgun"]
    if second < SHOTGUN_PUSH:
        return carry_frame(*(slot[key] for key in ("centre", "up", "forward")))
    side = "R" if second < SHOTGUN_CATCH or second >= SHOTGUN_GRIP else "L"
    hand = carry_frame(*posed_hand(builder, side)[:3])
    if second >= SHOTGUN_GRIP: return hand
    if side == "R":
        point = SHOTGUN_PUSH_POINT
        offset = carry_frame((0., 0., 0.), (1., 0., 0.), (0., 0., 1.))[1]
    else:
        point = SHOTGUN_CATCH_POINT
        pushed = carry_frame(SHOTGUN_PUSHED_CENTRE, SHOTGUN_PUSHED_UP, SHOTGUN_PUSHED_FORWARD)
        offset = pushed[1].inverted()@carry_frame((0., 0., 0.), (1., 0., 0.), (0., 1., 0.))[1]
    rotation = hand[1]@offset.inverted()
    local = Vector(point); local.x = -local.x
    return hand[0]-rotation@local, rotation


def validate_shotgun_draw(builder):
    rig = builder.result.rig
    rig.animation_data.action = builder.result.actions["ShotgunDraw"].action
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    # The torso, neck and head are stationary until the final right-hand grip.
    # Measure the left forearm/palm against their evaluated production meshes,
    # rather than treating a reachable wrist as proof that its arm clears them.
    surfaces = []
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.name not in ("GEO_Torso", "GEO_Pelvis", "GEO_Head", "GEO_Neck", "GEO_NeckStub"): continue
        hidden = obj.hide_get(); obj.hide_set(False); bpy.context.view_layer.update()
        evaluated = obj.evaluated_get(depsgraph); mesh = evaluated.to_mesh()
        vertices = [evaluated.matrix_world@v.co for v in mesh.vertices]
        faces = [tuple(p.vertices) for p in mesh.polygons]
        bounds = tuple(Vector(tuple(f(v[axis] for v in vertices) for axis in range(3))) for f in (min, max))
        surfaces.append((obj.name, BVHTree.FromPolygons(vertices, faces), bounds))
        evaluated.to_mesh_clear(); obj.hide_set(hidden)
    if not any(name == "GEO_Torso" for name, _, _ in surfaces): raise ValueError("Missing actual hero torso for shotgun catch clearance")
    error_position = error_angle = maximum_elbow_height = 0.
    minimum_body_clearance = float("inf")
    previous = {}
    previous_elbows = {}
    for frame in range(round(2.30*FPS)+1):
        second = frame/FPS
        bpy.context.scene.frame_set(frame); bpy.context.view_layer.update()
        for side in ("R", "L"):
            palm = posed_hand(builder, side)[0]
            elbow, shoulder = (rig.pose.bones[name+"."+side].head for name in ("forearm", "upper_arm"))
            maximum_elbow_height = max(maximum_elbow_height, elbow.z-shoulder.z)
            if side == "R" and elbow.z > shoulder.z+.04:
                raise ValueError(f"Shotgun pickup raises right elbow above shoulder at {second:.2f}s")
            if side in previous and (palm-previous[side]).length > .09:
                raise ValueError(f"Shotgun {side} palm jumps at {second:.2f}s")
            previous[side] = palm.copy()
            if side in previous_elbows and (elbow-previous_elbows[side]).length > .04:
                raise ValueError(f"Shotgun {side} elbow jumps at {second:.2f}s")
            previous_elbows[side] = elbow.copy()
        if .15 <= second <= SHOTGUN_GRIP:
            elbow, wrist = (rig.pose.bones[name+".L"].head for name in ("forearm", "hand"))
            samples = [(elbow.lerp(wrist, i/10), .035) for i in range(11)]
            samples.append((posed_hand(builder, "L")[0], .025))
            samples.extend((wrist.lerp(rig.pose.bones["hand.L"].tail, i/4), .025) for i in range(1, 5))
            for point, radius in samples:
                for name, surface, bounds in surfaces:
                    nearest, normal, _, distance = surface.find_nearest(point)
                    # Open neck/face rings have no meaningful interior beyond
                    # their finite mesh bounds (a point beside/below them is
                    # outside even when the nearest end-ring normal faces away).
                    within = all(bounds[0][axis] <= point[axis] <= bounds[1][axis] for axis in range(3))
                    signed = -distance if within and (point-nearest).dot(normal) < 0. else distance
                    clearance = signed-radius
                    minimum_body_clearance = min(minimum_body_clearance, clearance)
                    if clearance < -.004:
                        raise ValueError(f"Shotgun left arm crosses actual {name} at {second:.2f}s: {clearance:.6f}m {tuple(point)}")
        if .58 <= second <= SHOTGUN_CATCH:
            palm, up, forward, _ = posed_hand(builder, "L")
            target = frame_hand(sample_carry_frames(builder.shotgun_draw_contract["gun_stops"], SHOTGUN_CATCH),
                                SHOTGUN_CATCH_POINT, builder.shotgun_draw_contract["catch_rotation"])[0]
            if abs(palm.x-target.x) > .00005 or abs(palm.y-target.y) > .00005 or palm.z < target.z-.00005:
                raise ValueError("Shotgun left hand does not approach the stock strictly from above")
            if forward.cross(up).dot(Vector((0., 0., -1.))) < .999:
                raise ValueError("Shotgun left palm does not face down for the upper catch")
        if SHOTGUN_PUSH <= second <= 2.30:
            planned = sample_carry_frames(builder.shotgun_draw_contract["gun_stops"], second)
            actual = shotgun_draw_gun_frame(builder, second)
            error_position = max(error_position, (planned[0]-actual[0]).length)
            difference = planned[1].inverted()@actual[1]
            sine = math.sqrt(difference.x*difference.x+difference.y*difference.y+difference.z*difference.z)
            error_angle = max(error_angle, math.degrees(2.*math.atan2(sine, abs(difference.w))))
    if error_position > .00005 or error_angle > .05:
        raise ValueError(f"Shotgun physical parent differs from its continuous gun path: {error_position:.7f}m/{error_angle:.5f}deg")
    return dict(hand_transfers_s=[SHOTGUN_PUSH, SHOTGUN_CATCH, SHOTGUN_GRIP],
                right_push_grip_local_m=list(SHOTGUN_PUSH_POINT), left_catch_grip_local_m=list(SHOTGUN_CATCH_POINT),
                maximum_weapon_path_error_m=error_position, maximum_weapon_path_angle_error_degrees=error_angle,
                maximum_elbow_above_shoulder_m=maximum_elbow_height,
                minimum_left_forearm_palm_body_clearance_m=minimum_body_clearance,
                left_catch_approach="outside left shoulder, then vertical from above; palm down")


def author_actions(shotgun_draw_only=False):
    bpy.ops.wm.open_mainfile(filepath=str(hero.DEFAULT_OUTPUT))
    rig = bpy.data.objects["RIG_Player"]
    config = common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20261010, "apose")
    builder = combat.CombatBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    # Bone-only authoring never samples skin. Avoid repeatedly deforming the
    # entire production wardrobe during hundreds of arm solves; restore it
    # before export and the actual-body geometry validator.
    builder.carry_mesh_visibility = [(obj, obj.hide_get()) for obj in bpy.data.objects if obj.type == "MESH"]
    for obj, _ in builder.carry_mesh_visibility: obj.hide_set(True)
    builder.result = common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={}, parts=[])
    builder.points = builder.create_pose_points()
    neutral = snapshot_action(builder, bpy.data.actions["Relaxed"])
    actions = {
        "Crowbar": append_action(ROOT / "ArtSource/Combat/CombatActions.blend", "CombatRest"),
        "Pistol": append_action(ROOT / "ArtSource/Combat/PistolActions.blend", "PistolRest"),
        "Shotgun": append_action(ROOT / "ArtSource/Combat/ShotgunActions.blend", "ShotgunRest")}
    rests = {}
    for weapon, action in actions.items():
        pose = snapshot_action(builder, action)
        # The owned action affects the spine subtree. Lower-body stance is
        # supplied by ordinary gait and never moved by a stow/draw bank.
        rests[weapon] = {name: (pose.get(name, neutral[name]) if
                          (rig.data.bones[name].parent_recursive and any(b.name == "spine" for b in rig.data.bones[name].parent_recursive)) or name == "spine"
                          else neutral[name]) for name in neutral}
    rig.animation_data.action = None
    for action in list(bpy.data.actions): bpy.data.actions.remove(action)
    common.ANIMATION_FPS = FPS
    bpy.context.scene.render.fps = FPS
    if shotgun_draw_only:
        poses = shotgun_draw_frames(builder, neutral, rests["Shotgun"])
        duration = DURATIONS["Shotgun"]
        builder._create_action("ShotgunDraw", "combat_weapon_carry", duration, False, round(duration*FPS), FPS,
                               [(frame/round(duration*FPS), pose) for frame, pose in enumerate(poses)])
        return builder, neutral, rests, {}

    def landmark(weapon, target, up, forward, left="neutral", pole=None):
        builder._reset_pose(); builder._apply_pose(neutral)
        solve_hand(builder, "R", target, up, forward, pole or (-.95, -.25, -.1))
        if left == "flap":
            solve_hand(builder, "L", FLAP, (-1., 0., 0.), (0., 0., -1.), (.8, -.35, -.6))
        elif left == "support":
            up, forward = Vector(up).normalized(), Vector(forward).normalized()
            centre = Vector(target) + forward.cross(up) * -.012 + up * .043 + forward * .220
            solve_hand(builder, "L", centre, up.cross(forward), forward, (.8, -.35, -.65))
        elif isinstance(left, tuple):
            solve_hand(builder, "L", left, (0., 0., -1.), (0., -1., 0.), (.65, -.60, .2))
        return builder.snapshot_pose()

    source_contacts = {}
    for weapon, duration in DURATIONS.items():
        slot = SLOTS[weapon]
        centre, up, forward = slot["centre"], slot["up"], slot["forward"]
        contact = landmark(weapon, centre, up, forward, "flap" if weapon == "Pistol" else sling_hand_at(.88) if weapon == "Shotgun" else "neutral",
                           (-1., .7, .55) if weapon == "Shotgun" else None)
        if weapon == "Crowbar":
            reach = landmark(weapon, (-.275, -.040, 1.055), up, forward)
            extract = landmark(weapon, (-.315, -.125, 1.10), up, forward)
            front = landmark(weapon, (-.265, -.285, 1.12), (0., 0., 1.), (0., -1., 0.))
            stops = ((0., neutral), (.24, reach), (.38, contact), (.44, contact), (.64, extract), (.94, front), (duration, rests[weapon]))
        elif weapon == "Pistol":
            flap = landmark(weapon, (-.10, -.22, 1.24), (0., -.96, .28), (0., -.28, -.96), "flap")
            lifted = landmark(weapon, PISTOL_LIFT, up, forward, "flap", pole=PISTOL_EXTRACT_POLE)
            extract = landmark(weapon, PISTOL_EXIT, up, forward, "flap", pole=PISTOL_EXTRACT_POLE)
            front = landmark(weapon, (-.095, -.335, 1.10), (0., 0., 1.), (0., -1., 0.))
            stops = ((0., neutral), (.20, flap), (.48, contact), (.55, contact), (.82, lifted), (.99, extract), (1.18, front), (duration, rests[weapon]))
        else:
            side = landmark(weapon, (-.36, -.08, 1.15), (0., 0., -1.), (0., -1., 0.), sling_hand_at(.30))
            reach = landmark(weapon, (-.18, .125, 1.32), up, forward, sling_hand_at(.62), pole=(-1., .25, .6))
            over_shoulder = landmark(weapon, centre, up, forward, sling_hand_at(1.08), pole=(-1., .7, .55))
            lift = landmark(weapon, (-.24, .14, 1.49), up, forward, sling_hand_at(1.35), pole=(-1., .35, .7))
            swing = landmark(weapon, (-.425, -.075, 1.425), (0., .45, .893), (0., -.893, .45), (-.27, -.14, 1.35), pole=(-1., -.1, .15))
            # The existing authored rest already owns the exact left foreend
            # contact and its protracted supporting clavicle.
            front = rests[weapon]
            stops = ((0., neutral), (.30, side), (.62, reach), (.88, contact), (.94, contact), (1.08, over_shoulder),
                     (1.35, lift), (1.75, swing), (2.30, front), (duration, rests[weapon]))
        draw_poses = []
        for frame in range(round(duration * FPS) + 1):
            second = frame / FPS
            index = next((i for i in range(len(stops) - 1) if second <= stops[i + 1][0] + 1e-8), len(stops) - 2)
            a, b = stops[index:index + 2]
            weight = combat.dialogue.smooth((second - a[0]) / (b[0] - a[0]))
            pose = builder.blend(a[1], b[1], weight)
            if weapon == "Pistol" and .55 <= second <= .99:
                # Solve every sample along the vertical extraction and exit;
                # bone-angle interpolation alone sweeps the muzzle through the
                # leather pocket even when both endpoint wrists are aligned.
                t = combat.dialogue.smooth((second - .55) / .27) if second <= .82 else combat.dialogue.smooth((second - .82) / .17)
                target = Vector(SLOTS[weapon]["centre"]).lerp(Vector(PISTOL_LIFT), t) if second <= .82 else Vector(PISTOL_LIFT).lerp(Vector(PISTOL_EXIT), t)
                # Keep the exact dock/contact pose. Redirect the elbow smoothly
                # during extraction rather than snapping its plane at .55s.
                pole = Vector(PISTOL_CONTACT_POLE).lerp(Vector(PISTOL_EXTRACT_POLE), t) if second <= .82 else Vector(PISTOL_EXTRACT_POLE)
                builder._reset_pose(); builder._apply_pose(pose)
                solve_hand(builder, "R", target, up, forward, pole)
                pose = builder.snapshot_pose()
            elif weapon == "Shotgun" and .30 <= second <= 1.75:
                # The loop crosses the actual left shoulder and then travels
                # outside the front chest. Cartesian samples retain that arc;
                # interpolated arm angles would cut back through the torso.
                builder._reset_pose(); builder._apply_pose(pose)
                solve_hand(builder, "L", sling_hand_at(second), (0., 0., -1.), (0., -1., 0.), (.65, -.60, .2))
                solved = builder.snapshot_pose()
                # Only this arm is re-authored. Keep all other sampled values
                # rather than round-tripping their Euler angles through a
                # quaternion when the Cartesian solve is applied.
                pose.update({name: solved[name] for name in ("clavicle.L", "upper_arm.L", "forearm.L", "hand.L")})
            draw_poses.append(pose)
        # Preserve the established stow exactly; the new pickup is deliberately
        # not the reverse of putting the gun over the shoulder.
        stow_poses = list(reversed(draw_poses))
        if weapon == "Shotgun": draw_poses = shotgun_draw_frames(builder, neutral, rests[weapon])
        for phase, poses in (("Draw", draw_poses), ("Stow", stow_poses)):
            builder._create_action(weapon + phase, "combat_weapon_carry", duration, False, round(duration * FPS), FPS,
                                   [(frame / round(duration * FPS), pose) for frame, pose in enumerate(poses)])
        builder._reset_pose(); builder._apply_pose(contact)
        actual, axis, barrel, _ = posed_hand(builder, "R")
        source_contacts[weapon] = dict(centre=list(actual), up=list(axis), forward=list(barrel))
    builder._reset_pose(); builder._apply_pose(neutral)
    return builder, neutral, rests, source_contacts


def measure_sling_route(builder):
    rig = builder.result.rig
    # The unchanged stow still uses the left hand to guide the webbing. New
    # Draw uses that hand for the weapon, so it does not pin the sling to it.
    rig.animation_data.action = builder.result.actions["ShotgunStow"].action
    maximum = {"stock_to_hand": (0., 0.), "hand_to_foreend": (0., 0.), "stock_via_shoulder_to_hand": (0., 0.)}
    rest_route = None
    anchors = {}
    for frame in range(round(DURATIONS["Shotgun"]*FPS)+1):
        second = frame/FPS
        bpy.context.scene.frame_set(round(DURATIONS["Shotgun"]*FPS)-frame); bpy.context.view_layer.update()
        grip, up, forward, _ = posed_hand(builder, "R")
        if second < TRANSFER["Shotgun"]:
            grip, up, forward = (Vector(SLOTS["Shotgun"][key]) for key in ("centre", "up", "forward"))
        basis = Matrix((forward.cross(up), up, forward)).transposed()
        stock, foreend = (grip+basis@Vector(point) for point in (SLING_STOCK, SLING_FOREEND))
        left, right = (rig.pose.bones["upper_arm."+side].head for side in ("L", "R"))
        chest, pelvis, head = (rig.pose.bones[name].head for name in ("chest", "pelvis", "head"))
        body_right, body_up = (right-left).normalized(), (head-chest).normalized()
        body_forward = body_up.cross(body_right).normalized()
        shoulder = left+body_up*.07-body_right*.012+body_forward*.04
        hand = posed_hand(builder, "L")[0]
        if frame in (0, round(DURATIONS["Shotgun"]*FPS)):
            anchors["neutral" if frame == 0 else "held_rest"] = dict(chest=list(chest), pelvis=list(pelvis),
                left_shoulder=list(left), right_shoulder=list(right), body_up=list(body_up), body_forward=list(body_forward))
        if frame == 0:
            route = (stock, shoulder, chest+body_forward*.15-body_right*.06,
                     pelvis+body_up*.18+body_forward*.14+body_right*.13,
                     pelvis+body_up*.13+body_right*.22-body_forward*.02, foreend)
            rest_route = dict(points=[list(point) for point in route], length_m=sum((b-a).length for a,b in zip(route, route[1:])))
        if .30 <= second <= 1.75:
            values = {"stock_to_hand": (stock-hand).length, "hand_to_foreend": (foreend-hand).length}
            if second <= .62: values["stock_via_shoulder_to_hand"] = (stock-shoulder).length+(shoulder-hand).length
            for name, value in values.items():
                if value > maximum[name][0]: maximum[name] = (value, second)
    return dict(anchors=anchors, rest_route=rest_route, maximum_grasp_spans={key: dict(length_m=value[0], draw_second=value[1]) for key,value in maximum.items()})


def validate_sling_route(builder):
    report = measure_sling_route(builder)
    stock, foreend = SLING_LENGTH*SLING_HAND_FRACTION, SLING_LENGTH*(1.-SLING_HAND_FRACTION)
    for name, record in report["maximum_grasp_spans"].items():
        available = foreend if name == "hand_to_foreend" else stock
        if record["length_m"] > available-.035:
            raise ValueError(f"Left-shoulder sling grasp lacks body-wrap room {name}: {record['length_m']:.6f}/{available:.6f}m")
        record["material_length_m"] = available
        record["minimum_geometric_reserve_m"] = available-record["length_m"]
    if report["rest_route"]["length_m"] >= SLING_LENGTH:
        raise ValueError("The left-shoulder front route exceeds its authored webbing")
    a, b = Vector(SLING_STOCK), Vector(SLING_FOREEND)
    points = [a.lerp(b, i/SLING_SEGMENTS)+Vector((0., -SLING_SAG*math.sin(math.pi*i/SLING_SEGMENTS), 0.)) for i in range(SLING_SEGMENTS+1)]
    actual = sum((y-x).length for x,y in zip(points, points[1:]))
    if abs(actual-SLING_LENGTH) > .000005: raise ValueError("Sling ribbon material length differs from runtime rest length")
    report["authored_centerline_length_m"] = actual
    return report


def validate_unchanged_carry_curves(builder):
    """Only ShotgunDraw is re-authored; all other five clips remain exact."""
    source = ROOT / "ArtSource/Combat/WeaponCarryActions.blend"
    for name, _, _ in CLIPS:
        if name == "ShotgunDraw": continue
        previous = append_action(source, name)
        def values(action):
            return sorted((curve.data_path, curve.array_index, [(float(key.co.x), float(key.co.y)) for key in curve.keyframe_points])
                          for curve in common.iter_action_fcurves(action))
        old, new = values(previous), values(builder.result.actions[name].action)
        if old != new:
            difference = next(((a, b) for a, b in zip(old, new) if a != b), (len(old), len(new)))
            raise ValueError("Shotgun pickup changed an unrelated carry curve " + name + ": " + str(difference))
        bpy.data.actions.remove(previous)


def validate_actions(builder, neutral, rests):
    rig = builder.result.rig
    lower_reference = {}
    builder._reset_pose(); builder._apply_pose(neutral)
    for name in LOWER: lower_reference[name] = rig.pose.bones[name].matrix_basis.copy()
    maximum_lower = maximum_contact = maximum_endpoint = maximum_jump = 0.
    maximum_pistol_elbow = maximum_pistol_elbow_step = 0.
    minimum_pistol_elbow_head_gap = float("inf")
    reports = {}
    for weapon, duration in DURATIONS.items():
        for phase in ("Draw", "Stow"):
            name = weapon + phase
            record = builder.result.actions[name]
            for curve in common.iter_action_fcurves(record.action):
                if not curve.data_path.startswith('pose.bones['): raise ValueError("Carry action contains object motion " + name)
            rig.animation_data.action = record.action
            previous = None
            previous_elbow = None
            path_length = 0.
            for frame in range(round(duration * FPS) + 1):
                bpy.context.scene.frame_set(frame); bpy.context.view_layer.update()
                for bone in LOWER:
                    error = max(abs(v) for row in rig.pose.bones[bone].matrix_basis - lower_reference[bone] for v in row)
                    maximum_lower = max(maximum_lower, error)
                    if error > .00002: raise ValueError("Carry lower body moved " + name + "/" + bone)
                point, axis, forward, _ = posed_hand(builder, "R")
                if previous is not None:
                    distance = (point - previous).length
                    maximum_jump = max(maximum_jump, distance)
                    if distance > .09:
                        raise ValueError(f"Carry hand discontinuity {name}/{frame}: {distance:.6f}m from {tuple(previous)} to {tuple(point)}")
                    path_length += distance
                previous = point.copy()
                draw_second = frame / FPS if phase == "Draw" else duration - frame / FPS
                if weapon == "Pistol":
                    elbow = rig.pose.bones["forearm.R"].head.copy()
                    if previous_elbow is not None:
                        elbow_step = (elbow-previous_elbow).length
                        maximum_pistol_elbow_step = max(maximum_pistol_elbow_step, elbow_step)
                        if elbow_step > .04: raise ValueError(f"Pistol elbow discontinuity {name}/{frame}: {elbow_step:.6f}m")
                    previous_elbow = elbow
                    if .55-1e-6 <= draw_second <= .99+1e-6:
                        maximum_pistol_elbow = max(maximum_pistol_elbow, elbow.z)
                        gap = rig.pose.bones["head"].head.z-elbow.z
                        minimum_pistol_elbow_head_gap = min(minimum_pistol_elbow_head_gap, gap)
                        if gap < .05: raise ValueError(f"Pistol extraction raises elbow beside head {name}/{frame}: gap {gap:.6f}m")
                if weapon == "Pistol" and .55 - 1e-6 <= draw_second <= .82 + 1e-6:
                    if abs(point.x - SLOTS[weapon]["centre"][0]) > .00005 or abs(point.y - SLOTS[weapon]["centre"][1]) > .00005:
                        raise ValueError("Pistol extraction sweeps through its leather pocket " + name)
                    left_centre = posed_hand(builder, "L")[0]
                    if (left_centre - Vector(FLAP)).length > .00005:
                        raise ValueError("Pistol extraction released the jacket before clearing its pocket " + name)
                transfer = TRANSFER[weapon] if phase == "Draw" else duration - TRANSFER[weapon]
                if frame == round(transfer * FPS) and name != "ShotgunDraw":
                    error = (point - Vector(SLOTS[weapon]["centre"])).length
                    maximum_contact = max(maximum_contact, error)
                    if error > .00002 or axis.dot(Vector(SLOTS[weapon]["up"])) < .9999:
                        raise ValueError(f"Carry transfer contact differs {name}: {error}")
                if frame in (0, round(duration * FPS)):
                    expected = neutral if (phase == "Draw") == (frame == 0) else rests[weapon]
                    current = builder.snapshot_pose()
                    for bone in current:
                        error = max(abs(a - b) for a, b in zip(current[bone].rotation_degrees, expected[bone].rotation_degrees))
                        maximum_endpoint = max(maximum_endpoint, error)
                        if error > .005: raise ValueError("Carry neutral/rest endpoint differs " + name + "/" + bone)
            if path_length < .20: raise ValueError("Carry action has no real hand travel " + name)
            reports[name] = dict(duration_s=duration, transfer_s=transfer, right_hand_travel_m=round(path_length, 6))
            if name == "ShotgunDraw":
                reports[name].pop("transfer_s")
                reports[name]["hand_transfers_s"] = [SHOTGUN_PUSH, SHOTGUN_CATCH, SHOTGUN_GRIP]
    if maximum_jump > .09: raise ValueError("Carry right-hand path jumps more than 9 cm per 10 ms")
    return dict(actions=reports, maximum_fixed_lower_error=maximum_lower,
                maximum_transfer_error_m=maximum_contact, maximum_endpoint_error_degrees=maximum_endpoint,
                maximum_hand_step_m=maximum_jump, pistol_maximum_extraction_elbow_height_m=maximum_pistol_elbow,
                pistol_minimum_elbow_to_head_base_gap_m=minimum_pistol_elbow_head_gap,
                pistol_maximum_elbow_step_m=maximum_pistol_elbow_step)


def geometry_signature(items):
    return hashlib.sha256(json.dumps([(i.name, sorted((list(k), v) for k, v in i.parts.items()), i.anchors)
                                      for i in items], sort_keys=True).encode()).hexdigest()


def payload(items, contacts, report):
    checksum = geometry_signature(items)
    data = kit.manifest(items, checksum)
    data.update(generator="tools/build-combat-weapon-carry-3d-model.py", test_only=True)
    data["slots"] = [dict(weapon=weapon, mount=NAMES[weapon], bone=slot["bone"],
                          source_contact=contacts[weapon]) for weapon, slot in SLOTS.items()]
    data["actions"] = dict(source="ArtSource/Combat/WeaponCarryActions.blend", bone_only=True,
                           root_motion=False, animation_events=0, fps=FPS, validation=report)
    data["shotgun_sling"] = dict(model="ShotgunSling", continuous_ribbon=True, segments=SLING_SEGMENTS,
                                 width_m=.025, thickness_m=.002, parameter_uv="UVMap.x=t, UVMap.y=width sign, Thickness.x=thickness sign",
                                 length_m=SLING_LENGTH, stock=SLING_STOCK, foreend=SLING_FOREEND,
                                 hand_material_fraction=SLING_HAND_FRACTION,
                                 throw_over="stow: left shoulder controlled by left hand; draw: open right palm lifts barrels, left upper catch rotates gun, then firing grip; diagonal front body route")
    return data


def verify_bank(path, reference):
    rig, actions = combat.import_bank(path)
    if set(actions) != {name for name, _, _ in CLIPS}: raise ValueError("Carry bank contains missing or foreign clips")
    maximum_position = maximum_angle = 0.
    for name, duration, _ in CLIPS:
        rig.animation_data.action = actions[name]
        first, last = actions[name].frame_range
        for index, expected in enumerate(reference[name]):
            frame = first + (last - first) * index / (len(reference[name]) - 1)
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.); bpy.context.view_layer.update()
            for bone, matrix_values in expected.items():
                target = Matrix(matrix_values)
                matrix = rig.matrix_world @ rig.pose.bones[bone].matrix
                maximum_position = max(maximum_position, (matrix.translation - target.translation).length)
                cosine = min(1., abs(matrix.to_quaternion().normalized().dot(target.to_quaternion().normalized())))
                maximum_angle = max(maximum_angle, math.degrees(2. * math.acos(cosine)))
    # Blender's FBX importer retains centimetres; combat's helper rebases the
    # imported hierarchy into authored metres before returning the rig.
    if maximum_position > .0007 or maximum_angle > .10:
        raise ValueError(f"Carry FBX motion differs: {maximum_position:.7f} m / {maximum_angle:.5f} degrees")
    return dict(maximum_position_error_m=maximum_position, maximum_angle_error_degrees=maximum_angle)


def verify_sling_parameters(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    ribbon = next(obj for obj in bpy.data.objects if obj.type == "MESH" and obj.name.startswith("Ribbon__"))
    uv, thickness = ribbon.data.uv_layers.get("UVMap"), ribbon.data.uv_layers.get("Thickness")
    if uv is None or thickness is None: raise ValueError("Sling FBX lost its authored ribbon parameter channels")
    samples = {round(loop.uv.x, 6) for loop in uv.data}
    if len(samples) != SLING_SEGMENTS + 1 or min(samples) != 0. or max(samples) != 1.:
        raise ValueError("Sling FBX changed its continuous ribbon parameter")
    if {round(loop.uv.y) for loop in uv.data} != {-1, 1} or {round(loop.uv.x) for loop in thickness.data} != {-1, 1}:
        raise ValueError("Sling FBX lost its width/thickness signs")


def metric_model_vertices(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    points = []
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.name.startswith("MuzzleFlash"): continue
        for vertex in obj.data.vertices:
            p = obj.matrix_world @ vertex.co
            points.append(Vector((p.x, p.z, p.y)))
    return points


def validate_pistol_body_fit(model_dir, source_dir):
    """Measure real meshes; wardrobe owns the outward drape around this gear."""
    models = {"Pistol": metric_model_vertices(ROOT / "Assets/Resources/CombatPistol/Pistol.fbx"),
              "PistolHolster": metric_model_vertices(model_dir / "PistolHolster.fbx")}
    bpy.ops.wm.open_mainfile(filepath=str(source_dir / "WeaponCarryActions.blend"))
    rig = bpy.data.objects["RIG_Player"]
    rig.animation_data.action = bpy.data.actions["CrowbarDraw"]
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    chest_reference = rig.pose.bones["chest"].matrix.copy()
    bpy.context.scene.frame_set(round(DURATIONS["Crowbar"] * FPS)); bpy.context.view_layer.update()
    chest_delta = rig.pose.bones["chest"].matrix @ chest_reference.inverted()
    depsgraph = bpy.context.evaluated_depsgraph_get()

    def surface(predicate):
        vertices, faces = [], []
        for obj in bpy.data.objects:
            if obj.type != "MESH" or not predicate(obj.name): continue
            evaluated = obj.evaluated_get(depsgraph); mesh = evaluated.to_mesh(); base = len(vertices)
            vertices.extend(evaluated.matrix_world @ v.co for v in mesh.vertices)
            faces.extend(tuple(base + i for i in p.vertices) for p in mesh.polygons)
            evaluated.to_mesh_clear()
        return BVHTree.FromPolygons(vertices, faces)

    shell = surface(lambda name: name.startswith("CLO_Jacket"))
    body = surface(lambda name: name in ("GEO_Torso", "GEO_Pelvis", "CLO_ShirtBody", "CLO_TrousersPelvis", "GEO_UpperArm.L", "GEO_UpperArm.R"))
    centre, up, forward = (Vector(SLOTS["Pistol"][key]) for key in ("centre", "up", "forward"))
    right = forward.cross(up)
    records = {}
    for name, vertices in models.items():
        penetration = outside = side = 0.
        minimum_clearance = float("inf")
        exposed_count = side_count = 0
        for vertex in vertices:
            point = chest_delta @ (centre + right * vertex.x + up * vertex.y + forward * vertex.z)
            nearest, normal, index, distance = body.find_nearest(point)
            if nearest is None: raise ValueError("Missing actual hero body surface")
            signed = distance if (point - nearest).dot(normal) >= 0. else -distance
            minimum_clearance = min(minimum_clearance, signed)
            penetration = max(penetration, -signed)
            shell_point, shell_normal, _, shell_distance = shell.find_nearest(point)
            # A front ray through the open zipper can hit the back of M65.
            # Measure the actual nearest outward surface, never that back hit.
            required = shell_distance if (point-shell_point).dot(shell_normal) >= 0. else 0.
            outside = max(outside, required)
            exposed_count += required > 0.
        if penetration > .0005: raise ValueError(f"Pistol carry intersects actual body {name}: {penetration:.6f}m")
        if max(outside, side) > .040: raise ValueError(f"Pistol carry requires excessive jacket drape {name}: {outside:.6f}/{side:.6f}m")
        records[name] = dict(vertices=len(vertices), minimum_body_clearance_m=minimum_clearance,
                             maximum_body_penetration_m=penetration, maximum_front_back_drape_m=outside,
                             maximum_side_drape_m=side, points_requiring_front_back_drape=exposed_count,
                             points_requiring_side_drape=side_count)
    extraction = PISTOL_LIFT[2]-SLOTS["Pistol"]["centre"][2]
    muzzle_clearance = PISTOL_POCKET_LIP+extraction-max(point.z for point in models["Pistol"])
    if muzzle_clearance < .010: raise ValueError(f"Pistol muzzle has insufficient actual pocket-lip clearance {muzzle_clearance:.6f}m")
    return dict(source="actual evaluated hero shirt, torso, pelvis and jacket meshes; outward runtime drape required",
                stowed=records, extraction_vertical_travel_m=extraction, muzzle_exit_clearance_m=muzzle_clearance,
                jacket_access_hand=list(FLAP), maximum_allowed_drape_m=.040,
                method="actual chest-bone carried vertices against evaluated M65 closest outward triangles; runtime separately proves complete garment occlusion")


def motion_samples(builder):
    rig = builder.result.rig
    output = {}
    for name, duration, _ in CLIPS:
        rig.animation_data.action = builder.result.actions[name].action
        track = []
        for index in range(25):
            frame = duration * FPS * index / 24.
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.); bpy.context.view_layer.update()
            track.append({bone.name: [list(row) for row in rig.matrix_world @ bone.matrix] for bone in rig.pose.bones})
        output[name] = track
    return output


def preview(model_dir, source_dir, destination):
    """Inspect the published actions with current hero geometry and actual weapon meshes."""
    bpy.ops.wm.open_mainfile(filepath=str(source_dir / "WeaponCarryActions.blend"))
    combat.pack_hero_atlases()
    rig = bpy.data.objects["RIG_Player"]
    config = common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20261010, "apose")
    builder = combat.CombatBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.result = common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={}, parts=[])
    models, mounts, imported = {}, {}, {}
    paths = {"Crowbar": ROOT / "Assets/Resources/Combat/Crowbar.fbx",
             "Pistol": ROOT / "Assets/Resources/CombatPistol/Pistol.fbx",
             "Shotgun": ROOT / "Assets/Resources/CombatShotgun/Shotgun.fbx"}
    for weapon, path in paths.items():
        for kind, asset in (("weapon", path), ("mount", model_dir / (NAMES[weapon] + ".fbx"))):
            before = set(bpy.data.objects)
            bpy.ops.import_scene.fbx(filepath=str(asset), use_anim=False)
            obj = next(o for o in bpy.data.objects if o not in before and o.parent is None)
            (models if kind == "weapon" else mounts)[weapon] = obj
            imported[weapon, kind] = obj.matrix_world.copy()
            for child in obj.children_recursive:
                if child.name.startswith("MuzzleFlash"): child.hide_render = True
    # Preview-only jacket pull, matching the runtime's local left-front access.
    pulls = []
    for obj in list(bpy.data.objects):
        if obj.type != "MESH" or not obj.name.startswith("CLO_Jacket"): continue
        if not any(word in obj.name for word in ("Body", "Placket.L", "PocketLower.L", "FlapLower.L")): continue
        obj.shape_key_add(name="Basis") if obj.data.shape_keys is None else None
        shape = obj.shape_key_add(name="CarryAccessPreview")
        inverse = obj.matrix_world.inverted().to_3x3()
        for vertex in shape.data:
            point = obj.matrix_world @ vertex.co
            weight = max(0., min(1., (point.x - .025) / .12)) * max(0., min(1., (.14 - point.y) / .10))
            weight *= max(0., min(1., (1.17 - point.z) / .17))
            if point.y >= 0. or point.z < .79: weight = 0.
            vertex.co += inverse @ Vector((.085, -.075, .045)) * weight
        pulls.append(shape)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 720; scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    if scene.camera is None:
        camera = bpy.data.objects.new("Carry Review Camera", bpy.data.cameras.new("Carry Review Camera"))
        bpy.context.collection.objects.link(camera); scene.camera = camera
    scene.camera.data.type = "ORTHO"; scene.camera.data.ortho_scale = 2.05
    destination.mkdir(parents=True, exist_ok=True)
    for weapon in DURATIONS:
        samples = ((0., "stowed"), (TRANSFER[weapon], "contact"),
                   (TRANSFER[weapon]+.25, "extract"), (DURATIONS[weapon], "held"))
        if weapon == "Shotgun":
            samples = ((0., "stowed"), (SHOTGUN_PUSH, "barrel-push"), (SHOTGUN_CATCH, "upper-catch"),
                       (1.10, "rotate"), (SHOTGUN_GRIP, "firing-grip"), (DURATIONS[weapon], "held"))
        for second, label in samples:
            rig.animation_data.action = bpy.data.actions[weapon + "Draw"]
            scene.frame_set(round(second * FPS)); bpy.context.view_layer.update()
            for held, obj in models.items():
                slot = SLOTS[held]
                if held == weapon == "Shotgun":
                    centre, orientation = shotgun_draw_gun_frame(builder, second)
                    up, forward = orientation@Vector((0., 1., 0.)), orientation@Vector((0., 0., 1.))
                elif held == weapon and second > TRANSFER[weapon]:
                    centre, up, forward, _ = posed_hand(builder, "R")
                else: centre, up, forward = (Vector(slot[k]) for k in ("centre", "up", "forward"))
                rotation = Matrix((forward.cross(up), forward, up)).transposed().to_4x4()
                obj.matrix_world = Matrix.Translation(centre) @ rotation @ imported[held, "weapon"]
                mount = mounts[held]
                su, sf = Vector(slot["up"]), Vector(slot["forward"])
                mount.matrix_world = Matrix.Translation(Vector(slot["centre"])) @ Matrix((sf.cross(su), sf, su)).transposed().to_4x4() @ imported[held, "mount"]
                visible = held == "Pistol" or held == weapon
                for child in (obj, *obj.children_recursive):
                    child.hide_render = not visible or child.name.startswith("MuzzleFlash")
                for child in (mount, *mount.children_recursive): child.hide_render = not visible
            amount = 0.
            if weapon == "Pistol":
                amount = min(1., max(0., (second - .05) / .15)) if second < .20 else 1. if second <= .99 else max(0., 1. - (second - .99) / .30)
            for shape in pulls: shape.value = amount
            for obj in bpy.data.objects:
                if obj.type == "MESH" and obj.data.shape_keys is not None:
                    shape = obj.data.shape_keys.key_blocks.get("CylindricalGrip")
                    if shape is not None: shape.value = (1. if obj.name.endswith(".R") else .9 if weapon == "Shotgun" and label == "held" else 0.) if second >= TRANSFER[weapon] else 0.
            bpy.context.view_layer.update()
            views = (("front", (-2.7, -3.1, 1.5)), ("back", (-2.7, 3.1, 1.55))) if label in ("stowed", "contact") else (("front", (-2.7, -3.1, 1.5)),)
            for view, position in views:
                scene.camera.location = position
                common.look_at(scene.camera, Vector((0., -.02, .95)))
                scene.render.filepath = str(destination / (weapon + "-" + label + "-" + view + ".png"))
                bpy.ops.render.render(write_still=True)
                print("CARRY REVIEW " + weapon + "/" + label + "/" + view, flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Resources/CombatWeaponCarry")
    parser.add_argument("--source-dir", type=Path, default=ROOT / "ArtSource/Combat")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--actions-only", action="store_true", help="Regenerate clips against unchanged published mount geometry.")
    parser.add_argument("--shotgun-only", action="store_true", help="Publish the revised sling and carry bank while retaining other mount FBX files.")
    parser.add_argument("--preview-only", action="store_true")
    parser.add_argument("--preview-dir", type=Path)
    parser.add_argument("--measure-sling-route", action="store_true", help="Measure candidate shoulder route and actual grasp spans without writing assets.")
    parser.add_argument("--probe-shotgun-draw", action="store_true", help="Check only the revised draw against the actual body without publishing assets.")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.preview_only:
        if args.preview_dir is None: raise ValueError("Preview-only requires --preview-dir")
        preview(args.model_dir, args.source_dir, (ROOT / args.preview_dir).resolve())
        return
    items = make_items()
    if geometry_signature(items) != geometry_signature(make_items()):
        raise ValueError("Weapon carry independent geometry builds differ")
    builder, neutral, rests, contacts = author_actions(shotgun_draw_only=args.probe_shotgun_draw)
    print("CARRY AUTHORED: validating sampled contacts, endpoints and body clearance", flush=True)
    if args.probe_shotgun_draw:
        print(json.dumps(validate_shotgun_draw(builder), indent=2), flush=True)
        return
    if args.measure_sling_route:
        print(json.dumps(measure_sling_route(builder), indent=2), flush=True)
        return
    report = validate_actions(builder, neutral, rests)
    report["shotgun_draw_handoffs"] = validate_shotgun_draw(builder)
    print("SHOTGUN DRAW: continuous handoffs, upper approach and actual-body clearance OK", flush=True)
    report["shotgun_left_shoulder_route"] = validate_sling_route(builder)
    if args.shotgun_only: validate_unchanged_carry_curves(builder)
    for obj, hidden in builder.carry_mesh_visibility: obj.hide_set(hidden)
    bpy.context.view_layer.update()
    data = payload(items, contacts, report)
    data["actions"]["animation_signature"] = combat.action_curve_signature(builder, tuple(name for name, _, _ in CLIPS))
    reference = motion_samples(builder)
    manifest = args.model_dir / "CombatWeaponCarry3D.json"
    geometry_dir = ROOT / "Assets/Resources/CombatWeaponCarry" if args.actions_only else args.model_dir
    if args.actions_only:
        existing_geometry = json.loads((geometry_dir / "CombatWeaponCarry3D.json").read_text(encoding="utf8"))
        if existing_geometry["build_signature"] != data["build_signature"]:
            raise ValueError("Actions-only generation cannot change published mount geometry")
    if not args.validate_only:
        args.model_dir.mkdir(parents=True, exist_ok=True); args.source_dir.mkdir(parents=True, exist_ok=True)
        common.export_animation_fbx(args.model_dir / "CarryActions.fbx", builder.result)
        common.save_blend(args.source_dir / "WeaponCarryActions.blend")
        if not args.actions_only:
            kit.SURFACES = SURFACES
            roots = kit.build_objects(items)
            for root in roots:
                if root.name != "ShotgunSling": continue
                ribbon = next(obj for obj in root.children_recursive if obj.type == "MESH" and obj.name.startswith("Ribbon__"))
                thickness = ribbon.data.uv_layers.new(name="Thickness")
                for loop in ribbon.data.loops:
                    index = loop.vertex_index
                    ribbon.data.uv_layers["UVMap"].data[loop.index].uv = (index // 4 / SLING_SEGMENTS, (-1., 1., 1., -1.)[index % 4])
                    thickness.data[loop.index].uv = ((-1., -1., 1., 1.)[index % 4], 0.)
            common.save_blend(args.source_dir / "WeaponCarryMounts.blend")
            for item, root in zip(items, roots):
                if not args.shotgun_only or item.name == "ShotgunSling": kit.export(root, args.model_dir / (item.name + ".fbx"))
    if args.shotgun_only:
        kit.verify_fbx(geometry_dir, dict(models=[model for model in data["models"] if model["name"] == "ShotgunSling"]))
        kit.verify_fbx(ROOT / "Assets/Resources/CombatWeaponCarry", dict(models=[model for model in data["models"] if model["name"] != "ShotgunSling"]))
    else: kit.verify_fbx(geometry_dir, data)
    verify_sling_parameters(geometry_dir / "ShotgunSling.fbx")
    fbx_report = verify_bank(args.model_dir / "CarryActions.fbx", reference)
    if args.shotgun_only:
        data["pistol_body_fit"] = json.loads((ROOT / "Assets/Resources/CombatWeaponCarry/CombatWeaponCarry3D.json").read_text(encoding="utf8"))["pistol_body_fit"]
    else: data["pistol_body_fit"] = validate_pistol_body_fit(geometry_dir, args.source_dir)
    if args.validate_only:
        existing = json.loads(manifest.read_text(encoding="utf8"))
        existing["actions"].pop("fbx_validation", None)
        if existing != json.loads(json.dumps(data)): raise ValueError("Carry manifest differs from deterministic rebuild")
    if not args.validate_only:
        data["actions"]["fbx_validation"] = fbx_report
        manifest.write_text(json.dumps(data, indent=2) + "\n", encoding="utf8")
    print("WEAPON CARRY IMPORTED METRES / GRIP TRANSFERS / ENDPOINTS / DETERMINISM OK", flush=True)
    if args.preview_dir: preview(args.model_dir, args.source_dir, (ROOT / args.preview_dir).resolve())


if __name__ == "__main__": main()
