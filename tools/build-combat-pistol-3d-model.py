"""Deterministic, test-only worn pistol, bullet and production-hero action bank.

Run through tools/run-blender.py. --validate-only rebuilds the metric manifest,
checks two independent geometry builds and round-trips the published FBX files.
The existing CombatActions source is read, never modified or regenerated.
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
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / filename)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


combat = module("pistol_combat_source", "build-combat-test-3d-model.py")
kit, common, hero = combat.kit, combat.common, combat.hero
SURFACES = {"Steel": (.24, .265, .25), "WornSteel": (.47, .49, .44),
            "Rubber": (.115, .125, .105), "Brass": (.56, .43, .23), "Flash": (1., .64, .22)}
kit.SURFACES = SURFACES
FPS = 100
CLIPS = (("PistolRest", 4., True), ("PistolRaise", .25, False),
         ("PistolAim", 4., True), ("PistolFire", .4, False),
         ("PistolReload", 1.8, False), ("PistolLower", .25, False))
# Seconds, barrel rise, rearward/upward grip travel, spine/chest counter-pitch.
# Runtime shoulder aim keeps this same angular envelope instead of cancelling
# the authored firing motion back onto the crosshair each rendered frame.
FIRE_STOPS = ((0., "aim", 0., 0., 0., 0., 0.),
              (.04, "kick", 20., .045, .065, -1.2, -2.),
              (.09, "absorb", 12., .065, .040, -3., -4.5),
              (.17, "return", 3., .025, .012, -1.8, -2.),
              (.27, "settle", -1.5, -.006, -.004, .5, .7),
              (.4, "aim", 0., 0., 0., 0., 0.))
FIRE_POSES = {name: (pitch, back, up, spine, chest)
              for _, name, pitch, back, up, spine, chest in FIRE_STOPS}
ART_SHIFT = (0., .011, .035)
SUPPORT = (-.020, -.025, 0.)
SUPPORT_YAW_DEGREES = 90.
SUPPORT_WEIGHT = .75
AIM_RIGHT_POLE = (-.69976, .29116, -.65235)
AIM_LEFT_POLE = (.34942, .45092, -.82133)
COLLISION = [dict(center=[0., .079, .080], size=[.044, .068, .236]),
             dict(center=[0., .003, .003], size=[.044, .142, .086]),
             dict(center=[0., .013, .064], size=[.04, .067, .065])]
ANCHORS = {"Grip": (0., 0., 0.), "Muzzle": (0., .088, .197),
           "SupportGrip": SUPPORT, "MagazineSeat": (0., .044, .009),
           "Slide": (0., .084, .080), "Barrel": (0., .088, .175),
           "EjectionPort": (.020, .091, .061), "SlidePull": (-.027, .092, -.008)}
SLIDE_TRAVEL = .038
MAGAZINE_TILT = math.radians(12.)
MAGAZINE_GRIP = (-.026, -.084 * math.cos(MAGAZINE_TILT), -.084 * math.sin(MAGAZINE_TILT))
RELOAD_STOPS = ((0., "aim"), (.25, "reload_grab"), (.55, "reload_extract"),
                (.75, "reload_stash"), (.9, "reload_stash"), (1.15, "reload_align"),
                (1.3, "reload_grab"), (1.42, "reload_rack"), (1.5, "reload_pull"),
                (1.58, "reload_release"), (1.8, "aim"))


def make_magazine():
    magazine = kit.Item("Magazine")
    magazine.box("Steel", (0., -.050, 0.), (.026, .100, .034), chamfer=.003)
    magazine.box("WornSteel", (0., -.104, 0.), (.038, .008, .050), chamfer=.002)
    for x in (-.0132, .0132):
        for y in (-.020, -.040, -.060, -.080):
            magazine.box("Rubber", (x, y, 0.), (.001, .006, .012), chamfer=.0002)
    magazine.parts = {key: [kit.bp.u_rotated(solid, (12., 0., 0.)) for solid in solids]
                      for key, solids in magazine.parts.items()}
    magazine.anchor("Seat", (0., 0., 0.))
    magazine.anchor("Grip", MAGAZINE_GRIP)
    return magazine


def make_casing():
    casing = kit.Item("Casing")
    sides = 10
    vertices = [(radius * math.cos(i * math.tau / sides), radius * math.sin(i * math.tau / sides), z)
                for radius, z in ((.0048, -.009), (.0048, .009), (.0036, .009), (.0036, -.007))
                for i in range(sides)]
    faces = []
    for ring in range(3):
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((ring*sides+i, ring*sides+j, (ring+1)*sides+j, (ring+1)*sides+i))
    faces.extend((tuple(reversed(range(sides))), tuple(range(3*sides, 4*sides))))
    casing.add("Brass", (vertices, faces))
    casing.rod("WornSteel", (0., 0., -.0093), (0., 0., -.009), .0021, sides=10)
    casing.anchor("Centre", (0., 0., 0.))
    return casing


def make_items():
    p = kit.Item("Pistol")
    # Honest compact silhouette: slide, barrel, frame, sloping grip and open
    # trigger guard. Wear is sparse edge geometry, never a brand or inscription.
    # Real slide opening: rear/front masses and a continuous left rail leave
    # the chamber's right/top window open for the spent casing to leave.
    p.box("Steel", (-.014, .073, .045), (.009, .038, .224), "Slide", chamfer=.003)
    p.box("Steel", (.004, .073, -.033), (.029, .038, .068), "Slide", chamfer=.004)
    p.box("Steel", (.004, .073, .096), (.029, .038, .122), "Slide", chamfer=.004)
    p.box("WornSteel", (0., .052, .042), (.04, .014, .21), "Slide", chamfer=.003)
    p.box("Steel", (0., .032, -.007), (.034, .028, .116), chamfer=.004)
    p.rod("Steel", (0., .077, .008), (0., .077, .162), .009, "Barrel", sides=10)
    # Dark bore ends slightly ahead of the surrounding steel end cap.
    p.rod("Rubber", (0., .077, .162), (0., .077, .1625), .0055, "Barrel", sides=10)
    grip = kit.bp.u_rotated(kit.bp.u_box((0., 0., 0.), (.036, .108, .048), .005), (12., 0., 0.))
    p.add("Rubber", kit.translated(grip, (0., -.011, -.035)))
    for x in (-.0185, .0185):
        for y in (-.041, -.024, -.007, .01):
            p.box("Steel", (x, y, -.035), (.0015, .003, .031), chamfer=.0002)
        for z in (-.052, -.042, -.032, -.022):
            p.box("Rubber", (x, .074, z), (.0015, .027, .003), "Slide", chamfer=.0002)
        p.box("WornSteel", (x, .087, .094), (.0015, .002, .112), "Slide", chamfer=.0002)
    # An open ring avoids the opaque block that would swallow the trigger finger.
    for a, b in (((-.0, .023, .019), (0., .016, .058)),
                 ((0., .016, .058), (0., -.019, .052)),
                 ((0., -.019, .052), (0., -.026, .013))):
        p.rod("Steel", a, b, .0035, sides=6)
    p.rod("Steel", (0., .022, .024), (0., -.003, .032), .0028, sides=6)
    p.box("Steel", (0., .096, .143), (.008, .009, .009), "Slide", chamfer=.001)
    for x in (-.011, .011):
        p.box("Steel", (x, .096, -.051), (.006, .009, .012), "Slide", chamfer=.001)
    for angle in (0., 45.):
        flash = kit.bp.u_rotated(kit.bp.u_box((0., 0., 0.), (.052, .011, .002), .001), (0., 0., angle))
        p.add("Flash", kit.translated(flash, (0., .077, .178)), "MuzzleFlash")
    # The physical centre of the handle, rather than the trigger guard, is the
    # hand-contact origin. Shift the complete passive geometry together.
    p.parts = {key: [kit.translated(solid, ART_SHIFT) for solid in solids] for key, solids in p.parts.items()}
    for name, point in ANCHORS.items():
        p.anchor(name, point, "Slide" if name in ("EjectionPort", "SlidePull") else None)
    magazine = make_magazine()
    p.anchor("Magazine", ANCHORS["MagazineSeat"], "MagazineSeat")
    for (_, role), solids in magazine.parts.items():
        for solid in solids:
            p.add(role, kit.translated(solid, ANCHORS["MagazineSeat"]), "Magazine")
    p.anchor("MuzzleFlash", (0., .088, .213))
    bullet = kit.Item("Bullet")
    bullet.rod("Brass", (0., 0., -.006), (0., 0., .003), .0045, sides=8)
    bullet.add("WornSteel", kit.ellipsoid((0., 0., .003), (.009, .009, .011), 8, 4))
    bullet.anchor("Centre", (0., 0., 0.))
    return [p, bullet, magazine, make_casing()]


def geometry_signature(items):
    records = [(item.name, sorted((str(key), solids) for key, solids in item.parts.items()), item.anchors)
               for item in items]
    return hashlib.sha256(json.dumps(records, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def payload(items):
    signature = geometry_signature(items)
    if signature != geometry_signature(make_items()):
        raise ValueError("Pistol geometry is not deterministic")
    data = kit.manifest(items, signature)
    data.update(generator="tools/build-combat-pistol-3d-model.py", generator_version="1.2.0", test_only=True)
    data["models"][0]["collision_shapes"] = dict(space="grip_local_unity_metres", boxes=COLLISION)
    data["mechanism"] = dict(slide="Slide", barrel="Barrel", slide_back_axis=[0., 0., -1.],
                             slide_travel_m=SLIDE_TRAVEL, magazine="Magazine", magazine_seat="MagazineSeat",
                             magazine_grip=list(MAGAZINE_GRIP), magazine_tilt_degrees=12.,
                             ejection_port="EjectionPort", slide_contact="SlidePull",
                             reload_stops=[dict(seconds=second, phase=name) for second, name in RELOAD_STOPS])
    data["actions"] = dict(source="ArtSource/Combat/CombatActions.blend", root_motion=False,
                           animation_events=0, fps=FPS, bone_only=True,
                           clips=[dict(name=name, duration_seconds=duration, loop=loop) for name, duration, loop in CLIPS],
                           endpoints="Raise Rest→Aim; Lower Aim→Rest; Fire/Reload Aim→Aim",
                           hand_frame="right CylinderAxis=up; distal hand direction=barrel forward; Grip at handle/CylinderCentre; left palm cups right from the side",
                           support_grip_weight=SUPPORT_WEIGHT, support_grip=SUPPORT,
                           support_yaw_degrees=SUPPORT_YAW_DEGREES,
                           fire_recoil=dict(interpolation="smoothstep",
                               stops=[dict(seconds=t, barrel_rise_degrees=pitch,
                                           grip_back_m=back, grip_up_m=up,
                                           spine_degrees=spine, chest_degrees=chest)
                                      for t, _, pitch, back, up, spine, chest in FIRE_STOPS]))
    for (_, role), solids in items[0].parts.items():
        if role == "Flash":
            continue
        for vertices, _ in solids:
            for point in vertices:
                if not any(all(abs(point[i]-box["center"][i]) <= box["size"][i]*.5 + .0005
                               for i in range(3)) for box in COLLISION):
                    raise ValueError("Pistol collision misses authored geometry: " + str(point))
    return data


def hand_rotation(builder, side, axis, palm):
    _, rest_axis, rest_palm = builder.hand_frame(side)
    def frame(z, y):
        z = z.normalized()
        x = y.cross(z).normalized()
        return Matrix((x, z.cross(x), z)).transposed()
    delta = frame(axis, palm) @ frame(rest_axis, rest_palm).transposed()
    return delta @ builder.result.rig.data.bones["hand." + side].matrix_local.to_3x3()


def support_palm(axis, forward):
    # Unity yaw is expressed in the reflected authoring grip frame explicitly.
    angle = math.radians(SUPPORT_YAW_DEGREES)
    return forward * math.cos(angle) + forward.cross(axis) * math.sin(angle)


def capture_hand_meshes(builder):
    """The actual production hand vertices, including their authored morphs."""
    rig = builder.result.rig
    result = {}
    for side, weight in (("R", 1.), ("L", SUPPORT_WEIGHT)):
        centre, axis, palm = builder.hand_frame(side)
        basis = (palm.cross(axis), axis, palm)
        parts = []
        for obj in bpy.data.objects:
            if not obj.name.endswith("." + side) or not obj.name.startswith(("GEO_Hand.", "GEO_Finger", "GEO_Thumb.")):
                continue
            keys = obj.data.shape_keys.key_blocks
            matrix = rig.matrix_world.inverted() @ obj.matrix_world
            points = []
            for a, b in zip(keys["Basis"].data, keys["CylindricalGrip"].data):
                point = matrix @ a.co.lerp(b.co, weight) - centre
                points.append(Vector(tuple(point.dot(v) for v in basis)))
            # The source hand-contact frame reflected into Unity has palm as
            # forward. The pistol barrel follows the distal hand direction, a
            # quarter turn from that palm frame around the same handle axis.
            if side == "R":
                to_barrel = Matrix.Rotation(math.radians(-90.), 3, "Y")
                points = [to_barrel @ point for point in points]
            # The canonical Unity grip frame reflects the source handedness.
            triangles = [tuple(poly.vertices[j] for j in (0, i+1, i))
                         for poly in obj.data.polygons for i in range(1, len(poly.vertices)-1)]
            parts.append((obj.name, points, triangles))
        if len(parts) != 6:
            raise ValueError("Pistol requires all six production hand meshes: " + side)
        result[side] = parts
    return result


def validate_hand_meshes(builder):
    """Anchor equality alone cannot reject interpenetrating palms/fingers."""
    def tree(parts):
        points, faces = [], []
        for _, vertices, triangles in parts:
            start = len(points)
            points.extend(vertices)
            faces.extend(tuple(i+start for i in tri) for tri in triangles)
        edges = sorted({tuple(sorted((f[i], f[(i+1) % 3]))) for f in faces for i in range(3)})
        return BVHTree.FromPolygons(points, faces, all_triangles=True), points, edges
    rotation = Matrix.Rotation(math.radians(SUPPORT_YAW_DEGREES), 3, "Y")
    left = [(name, [rotation @ p + Vector(SUPPORT) for p in points], triangles)
            for name, points, triangles in builder.pistol_hand_meshes["L"]]
    right_tree, right_points, right_edges = tree(builder.pistol_hand_meshes["R"])
    left_tree, left_points, left_edges = tree(left)
    intersections = len(right_tree.overlap(left_tree))
    if intersections:
        raise ValueError(f"Pistol support hand crosses the right hand: {intersections} triangle pairs")
    for points, other in ((left_points, right_tree), (right_points, left_tree)):
        for point in points:
            nearest, normal, _, distance = other.find_nearest(point)
            if distance > .0001 and (point-nearest).dot(normal) < -.0001:
                raise ValueError("Pistol support hand contains an opposing hand surface")
    # Vertex-to-face alone misses the nearest crossing pair of mesh edges.
    lp, rp = np.asarray(left_points), np.asarray(right_points)
    le, re = np.asarray(left_edges), np.asarray(right_edges)
    p, q = lp[le[:, 0]][:, None, :], rp[re[:, 0]][None, :, :]
    d, e = lp[le[:, 1]][:, None, :] - p, rp[re[:, 1]][None, :, :] - q
    r = p - q
    a, b, c = np.sum(d*d, axis=2), np.sum(d*e, axis=2), np.sum(d*r, axis=2)
    f, g = np.sum(e*r, axis=2), np.sum(e*e, axis=2)
    den = a*g - b*b
    s = np.clip(np.divide(b*f-c*g, den, out=np.zeros_like(den), where=den > 1e-16), 0, 1)
    t = (b*s+f)/g
    s = np.where(t < 0, np.clip(-c/a, 0, 1), np.where(t > 1, np.clip((b-c)/a, 0, 1), s))
    t = np.clip(t, 0, 1)
    edge_gap = float(np.min(np.linalg.norm(r+d*s[:, :, None]-e*t[:, :, None], axis=2)))
    gap = min(edge_gap, min(right_tree.find_nearest(point)[3] for point in left_points),
              min(left_tree.find_nearest(point)[3] for point in right_points))
    if gap < .001 or gap > .003:
        raise ValueError(f"Pistol support hand clearance is outside 1–3mm: {gap:.7f}m")
    print(f"PISTOL ACTUAL HAND MESH CLEARANCE OK intersections={intersections} nearest={gap:.7f}m", flush=True)
    # Every Aim/Fire frame below proves this same relative rigid contact frame,
    # so the surface proof applies through the whole authored aiming/recoil bank.
    return dict(right_grip_weight=1., left_grip_weight=SUPPORT_WEIGHT,
                cross_hand_triangle_intersections=intersections,
                minimum_surface_gap_m=round(gap, 7), containment=False)


def author_actions():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / "ArtSource/Combat/CombatActions.blend"))
    rig = bpy.data.objects["RIG_Player"]
    rig.animation_data.action = bpy.data.actions["CombatReady"]
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    config = common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20260919, "apose")
    builder = combat.CombatBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.result = common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={}, parts=[])
    builder.points = builder.create_pose_points()
    builder.pistol_hand_meshes = capture_hand_meshes(builder)
    base = builder.snapshot_pose()
    rig.animation_data.action = None
    # The old bank is not exported and cannot be accidentally overwritten.
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    for obj in list(bpy.data.objects):
        if obj not in (rig, rig.parent):
            bpy.data.objects.remove(obj, do_unlink=True)
    common.ANIMATION_FPS = FPS
    bpy.context.scene.render.fps = FPS

    def pose(kind):
        builder._reset_pose()
        builder._apply_pose(base)
        two_handed = kind in FIRE_POSES
        if two_handed:
            # The hands catch the kick before the torso absorbs it. The pelvis
            # and both planted legs keep their unchanged production Ready pose.
            _, _, _, spine, chest = FIRE_POSES[kind]
            for name, degrees in (("spine", spine), ("chest", chest),
                                  ("head", -(spine + chest) * .45)):
                bone = rig.pose.bones[name]
                bone.rotation_quaternion = bone.rotation_quaternion @ Quaternion(
                    Vector((1., 0., 0.)), math.radians(degrees))
            bpy.context.view_layer.update()
            # Bring the supporting shoulder forward instead of reaching its
            # upper arm through the chest inherited from the crowbar stance.
            clavicle = rig.pose.bones["clavicle.L"]
            matrix = clavicle.matrix.copy()
            protract = Quaternion(Vector((0., 0., 1.)), math.radians(-20.))
            clavicle.matrix = Matrix.Translation(matrix.translation) @ protract.to_matrix().to_4x4() @ matrix.to_3x3().to_4x4()
            bpy.context.view_layer.update()
        # Source hero faces -Y; its prefab applies the established 180° yaw.
        axis = Vector((0., 0., 1.))
        forward = Vector((0., -1., 0.))
        centre = Vector((-.090, -.460, 1.10))
        left = centre + Vector((-SUPPORT[0], -SUPPORT[2], SUPPORT[1]))
        if kind == "rest":
            forward = Vector((0., -.28, -.96)).normalized()
            axis = Vector((0., -.96, .28)).normalized()
            centre = Vector((-.23, -.22, .85))
            left = Vector((.23, -.16, .98))
        elif two_handed:
            pitch, back, up, _, _ = FIRE_POSES[kind]
            recoil = Quaternion(Vector((1., 0., 0.)), math.radians(-pitch))
            axis, forward = recoil @ axis, recoil @ forward
            centre += Vector((0., back, up))
            left = centre + Vector((-SUPPORT[0], -SUPPORT[2], SUPPORT[1]))
        elif kind.startswith("reload_"):
            turn = Quaternion(Vector((1., 0., 0.)), math.radians(-24.))
            axis, forward = turn @ axis, turn @ forward
            centre = Vector((-.075, -.30, 1.22))
            right = forward.cross(axis)
            def point(local):
                return centre + right * local[0] + axis * local[1] + forward * local[2]
            magazine_axis = axis * math.cos(MAGAZINE_TILT) + forward * math.sin(MAGAZINE_TILT)
            left = point(Vector(ANCHORS["MagazineSeat"]) + Vector(MAGAZINE_GRIP))
            if kind == "reload_extract": left -= magazine_axis * .12
            elif kind == "reload_align": left -= magazine_axis * .04
            elif kind == "reload_stash": left = Vector((.25, -.15, .92))
            elif kind in ("reload_rack", "reload_pull", "reload_release"):
                left = point(ANCHORS["SlidePull"])
                if kind == "reload_pull": left -= forward * SLIDE_TRAVEL
                if kind == "reload_release": left += right * -.045
        if two_handed:
            left = centre + forward.cross(axis) * SUPPORT[0] + axis * SUPPORT[1] + forward * SUPPORT[2]
        right_palm = axis.cross(forward).normalized()
        left_axis = axis
        left_palm = support_palm(axis, forward) if two_handed else forward
        if kind.startswith("reload_"):
            left_axis = magazine_axis if kind in ("reload_grab", "reload_extract", "reload_align") else axis
            left_palm = forward.cross(axis)
            if kind == "reload_stash":
                # Carry the magazine toward the hip with the elbow lowered,
                # rather than keeping its forearm vertical beside the chest.
                left_axis = Vector((0., -1., 1.)).normalized()
        right_pole = (-.3, -.4, -1.) if kind == "rest" else (AIM_RIGHT_POLE if two_handed else (-.1, .2, -1.))
        left_pole = AIM_LEFT_POLE if two_handed else (.7, .25, -.7)
        for side, target, palm, pole in (("R", centre, right_palm, right_pole),
                                         ("L", left, left_palm, left_pole)):
            target_axis = left_axis if side == "L" else axis
            rotation = hand_rotation(builder, side, target_axis, palm)
            rest = rig.data.bones["hand." + side]
            grip_offset = builder.hand_frame(side)[0] - rest.head_local
            delta = rotation @ rest.matrix_local.to_3x3().inverted()
            wrist = target - delta @ grip_offset
            if two_handed and kind != "aim":
                # The elbow follows the raised barrel so the kick is caught by
                # the whole arm, rather than hinging an unsafe amount at the wrist.
                shoulder = rig.pose.bones["upper_arm." + side].head
                forearm = rig.data.bones["forearm." + side].length
                aligned_pole = (wrist - forward * forearm - shoulder).normalized()
                pole = Vector(pole).lerp(aligned_pole, min(1., abs(FIRE_POSES[kind][0]) / 20.))
            elif kind.startswith("reload_") and side == "L":
                shoulder = rig.pose.bones["upper_arm.L"].head
                forearm = rig.data.bones["forearm.L"].length
                distal = left_axis.cross(palm).normalized()
                pole = (wrist - distal * forearm - shoulder).normalized()
            try:
                builder.solve_arm(side, wrist, rotation, pole)
            except ValueError as error:
                raise ValueError(f"Pistol {kind}/{side}: {error}") from error
        return builder.snapshot_pose()

    poses = {name: pose(name) for name in dict.fromkeys(("rest", *FIRE_POSES,
                                                       *(name for _, name in RELOAD_STOPS)))}
    transition = []
    for name in ("rest", "aim"):
        builder._reset_pose()
        builder._apply_pose(poses[name])
        transition.append((rig.pose.bones["hand.L"].matrix.copy(),
                           rig.pose.bones["clavicle.L"].rotation_quaternion.copy()))

    def retain_transition(current, weight):
        # A local-bone blend arcs the supporting sleeve through the coat. Move
        # the wrist through world space and bring its shoulder forward first.
        builder._reset_pose()
        builder._apply_pose(current)
        a, b = transition
        rig.pose.bones["clavicle.L"].rotation_quaternion = a[1].slerp(b[1], min(2.*weight, 1.))
        bpy.context.view_layer.update()
        wrist = a[0].translation.lerp(b[0].translation, weight)
        rotation = a[0].to_quaternion().slerp(b[0].to_quaternion(), weight).to_matrix()
        pole = Vector((.7, .25, -.7)).lerp(Vector(AIM_LEFT_POLE), weight)
        pole += Vector((.3*math.sin(math.pi*weight), 0., 0.))
        builder.solve_arm("L", wrist, rotation, pole)
        return builder.snapshot_pose()

    def retain_support(current):
        builder._reset_pose()
        builder._apply_pose(current)
        hand = rig.pose.bones["hand.R"]
        delta = hand.matrix @ hand.bone.matrix_local.inverted()
        centre, rest_axis, rest_palm = builder.hand_frame("R")
        up = (delta.to_3x3() @ rest_axis).normalized()
        palm = (delta.to_3x3() @ rest_palm).normalized()
        forward = palm.cross(up).normalized()
        target = delta @ centre + forward.cross(up) * SUPPORT[0] + up * SUPPORT[1] + forward * SUPPORT[2]
        rotation = hand_rotation(builder, "L", up, support_palm(up, forward))
        rest = rig.data.bones["hand.L"]
        left_delta = rotation @ rest.matrix_local.to_3x3().inverted()
        wrist = target - left_delta @ (builder.hand_frame("L")[0] - rest.head_local)
        # Preserve the authored elbow response while correcting the grip after
        # bone interpolation; a fixed aim pole would erase that firing motion.
        elbow_pole = rig.pose.bones["forearm.L"].head - rig.pose.bones["upper_arm.L"].head
        builder.solve_arm("L", wrist, rotation, elbow_pole)
        return builder.snapshot_pose()
    tracks = {"PistolRaise": ((0., "rest"), (.25, "aim")),
              "PistolLower": ((0., "aim"), (.25, "rest")),
              "PistolFire": tuple((seconds, name) for seconds, name, *_ in FIRE_STOPS),
              "PistolReload": RELOAD_STOPS}
    for name, duration, loop in CLIPS:
        keys = []
        for frame in range(round(duration * FPS) + 1):
            seconds = frame / FPS
            if loop:
                current = poses["rest" if name == "PistolRest" else "aim"]
            else:
                stops = tracks[name]
                index = min(len(stops) - 2, next((i for i in range(len(stops)-1)
                                                if seconds <= stops[i+1][0] + 1e-8), len(stops)-2))
                a, b = stops[index:index+2]
                weight = combat.dialogue.smooth((seconds-a[0])/(b[0]-a[0]))
                current = builder.blend(poses[a[1]], poses[b[1]], weight)
            if name in ("PistolRaise", "PistolLower"):
                current = retain_transition(current, weight if name == "PistolRaise" else 1.-weight)
            if name in ("PistolAim", "PistolFire"):
                current = retain_support(current)
            keys.append((frame / round(duration * FPS), current))
        builder._create_action(name, "combat_pistol", duration, loop, round(duration * FPS), FPS, keys)
    return builder


def action_signature(builder):
    return combat.action_curve_signature(builder, tuple(name for name, _, _ in CLIPS))


def validate_actions(builder):
    rig = builder.result.rig
    sampled = {}
    for name, duration, _ in CLIPS:
        rig.animation_data.action = builder.result.actions[name].action
        endpoints = []
        for second in (0., duration):
            bpy.context.scene.frame_set(round(second * FPS))
            bpy.context.view_layer.update()
            endpoints.append({bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones})
        sampled[name] = endpoints
    for a, endpoint, b, other in (("PistolRaise", 0, "PistolRest", 0), ("PistolRaise", 1, "PistolAim", 0),
                                 ("PistolLower", 0, "PistolAim", 0), ("PistolLower", 1, "PistolRest", 0),
                                 ("PistolFire", 0, "PistolAim", 0), ("PistolFire", 1, "PistolAim", 0),
                                 ("PistolReload", 0, "PistolAim", 0), ("PistolReload", 1, "PistolAim", 0)):
        for name in sampled[a][endpoint]:
            if max(abs(v) for row in sampled[a][endpoint][name] - sampled[b][other][name] for v in row) > .00001:
                raise ValueError("Pistol action endpoint mismatch: " + a + "/" + name)
    # The whole lower body remains the original grounded Ready; runtime may
    # replace those tracks with the existing step, kick and recovery actions.
    lower = ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R")
    reference = sampled["PistolAim"][0]
    maximum_wrist, minimum_distal_forward = {}, 1.
    rest_upper_angle, rest_elbow_drop = 0., math.inf
    for name, duration, _ in CLIPS:
        rig.animation_data.action = builder.result.actions[name].action
        maximum_wrist[name] = 0.
        for frame in range(round(duration * FPS) + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for bone in lower:
                if max(abs(v) for row in rig.pose.bones[bone].matrix_basis-reference[bone] for v in row) > .00001:
                    raise ValueError("Pistol action moved grounded lower body: " + name + "/" + bone)
            forearm, hand = (rig.pose.bones[bone + ".R"] for bone in ("forearm", "hand"))
            distal = (hand.tail-hand.head).normalized()
            angle = math.degrees((hand.head-forearm.head).angle(distal))
            maximum_wrist[name] = max(maximum_wrist[name], angle)
            if angle > 35.:
                raise ValueError(f"Pistol right wrist bends beyond35deg: {name}/{frame}={angle:.4f}")
            if name == "PistolRest":
                shoulder = rig.pose.bones["upper_arm.R"].head
                upper_angle = math.degrees((forearm.head-shoulder).angle(Vector((0., 0., -1.))))
                drop = shoulder.z-forearm.head.z
                rest_upper_angle, rest_elbow_drop = max(rest_upper_angle, upper_angle), min(rest_elbow_drop, drop)
                if upper_angle > 45. or drop < .15:
                    raise ValueError(f"Pistol rest raises the elbow: {upper_angle:.4f}deg / drop={drop:.4f}m")
            delta = hand.matrix.to_3x3() @ hand.bone.matrix_local.to_3x3().inverted()
            _, rest_axis, rest_palm = builder.hand_frame("R")
            up, palm = (delta @ rest_axis).normalized(), (delta @ rest_palm).normalized()
            minimum_distal_forward = min(minimum_distal_forward, distal.dot(palm.cross(up).normalized()))
    if minimum_distal_forward < .9999:
        raise ValueError("Pistol barrel differs from the right hand's distal direction")
    print("PISTOL ACTION ENDPOINTS / GROUNDED LOWER BODY OK", flush=True)
    print("PISTOL RIGHT WRIST / DISTAL BARREL OK " + json.dumps(maximum_wrist, sort_keys=True), flush=True)
    maximum_contact, minimum_forward = 0., 1.
    maximum_left_wrist, minimum_left_distal_forward = {}, 1.
    for name in ("PistolAim", "PistolFire"):
        rig.animation_data.action = builder.result.actions[name].action
        maximum_left_wrist[name] = 0.
        for frame in range(round(next(duration for clip, duration, _ in CLIPS if clip == name) * FPS) + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            frames = {}
            for side in ("L", "R"):
                hand = rig.pose.bones["hand." + side]
                delta = hand.matrix @ hand.bone.matrix_local.inverted()
                centre, axis, palm = builder.hand_frame(side)
                frames[side] = (delta @ centre, (delta.to_3x3() @ axis).normalized(), (delta.to_3x3() @ palm).normalized())
            centre, up, right_palm = frames["R"]
            forward = right_palm.cross(up).normalized()
            left_hand = rig.pose.bones["hand.L"]
            left_forearm = rig.pose.bones["forearm.L"]
            left_distal = (left_hand.tail-left_hand.head).normalized()
            left_bend = math.degrees((left_hand.head-left_forearm.head).angle(left_distal))
            maximum_left_wrist[name] = max(maximum_left_wrist[name], left_bend)
            minimum_left_distal_forward = min(minimum_left_distal_forward, left_distal.dot(forward))
            if left_bend > 45.:
                raise ValueError(f"Pistol supporting wrist bends too far: {name}/{frame}: {left_bend:.4f}deg")
            target = centre + forward.cross(up) * SUPPORT[0] + up * SUPPORT[1] + forward * SUPPORT[2]
            maximum_contact = max(maximum_contact, (target-frames["L"][0]).length)
            if frames["L"][1].dot(up) < .9999 or frames["L"][2].dot(support_palm(up, forward)) < .9999:
                raise ValueError("Pistol left support frame differs from right hand")
            if name == "PistolAim":
                minimum_forward = min(minimum_forward, forward.dot(Vector((0., -1., 0.))))
    if maximum_contact > .0001 or minimum_forward < .9999:
        raise ValueError(f"Pistol contact/basis differs: {maximum_contact}m / forward dot {minimum_forward}")
    if minimum_left_distal_forward < .9999:
        raise ValueError("Pistol supporting fingers differ from the barrel direction")
    print(f"PISTOL HANDLE / LEFT SUPPORT / FORWARD BASIS OK contact={maximum_contact:.7f}m", flush=True)
    measured = validate_hand_meshes(builder)
    measured.update(maximum_right_wrist_bend_degrees=round(max(maximum_wrist.values()), 4),
                    right_wrist_bend_degrees={name: round(value, 4) for name, value in maximum_wrist.items()},
                    maximum_left_wrist_bend_degrees=round(max(maximum_left_wrist.values()), 4),
                    left_wrist_bend_degrees={name: round(value, 4) for name, value in maximum_left_wrist.items()},
                    minimum_left_distal_barrel_dot=round(minimum_left_distal_forward, 7),
                    minimum_distal_barrel_dot=round(minimum_distal_forward, 7),
                    rest_upper_arm_angle_from_down_degrees=round(rest_upper_angle, 4),
                    rest_elbow_drop_m=round(rest_elbow_drop, 7))
    return measured


def motion_samples(rig, actions):
    result = {}
    for name, duration, _ in CLIPS:
        action = actions[name]
        rig.animation_data.action = action
        first, last = action.frame_range
        values = []
        for index in range(round(duration * 20) + 1):
            frame = first + (last-first) * index / round(duration * 20)
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
            bpy.context.view_layer.update()
            values.append({bone.name: rig.matrix_world @ bone.matrix for bone in rig.pose.bones})
        result[name] = values
    return result


def validate_fire_motion(builder):
    """Measure the published firing contract, including the delayed body catch."""
    rig = builder.result.rig
    rig.animation_data.action = builder.result.actions["PistolAim"].action
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    reference = {name: rig.pose.bones[name].rotation_quaternion.copy()
                 for name in ("spine", "chest")}
    reference_grip = rig.pose.bones["hand.R"].matrix @ rig.data.bones["hand.R"].matrix_local.inverted() @ builder.hand_frame("R")[0]
    measured = []
    rig.animation_data.action = builder.result.actions["PistolFire"].action
    for second, _, pitch, back, up, spine, chest in FIRE_STOPS:
        bpy.context.scene.frame_set(round(second * FPS))
        bpy.context.view_layer.update()
        hand = rig.pose.bones["hand.R"]
        delta = hand.matrix @ hand.bone.matrix_local.inverted()
        centre, axis, palm = builder.hand_frame("R")
        forward = (delta.to_3x3() @ palm).cross(delta.to_3x3() @ axis).normalized()
        actual_pitch = math.degrees(math.atan2(forward.z, -forward.y))
        travel = delta @ centre - reference_grip
        if abs(actual_pitch-pitch) > .001 or (travel-Vector((0., back, up))).length > .00001:
            raise ValueError(f"Pistol firing kick differs at {second}: {actual_pitch}deg/{tuple(travel)}m")
        body = {}
        for name, expected in (("spine", spine), ("chest", chest)):
            change = reference[name].rotation_difference(rig.pose.bones[name].rotation_quaternion)
            angle = math.degrees(change.angle)
            if abs(angle-abs(expected)) > .02:
                raise ValueError(f"Pistol firing body response differs: {second}/{name}={angle}deg")
            body[name + "_rotation_degrees"] = round(angle, 4)
        measured.append(dict(seconds=second, barrel_rise_degrees=round(actual_pitch, 4),
                             grip_back_m=round(travel.y, 7), grip_up_m=round(travel.z, 7), **body))
    print("PISTOL FIRING KICK / ARM TRAVEL / DELAYED TORSO RETURN OK " + json.dumps(measured), flush=True)
    return measured


def validate_reload_contacts(builder):
    rig = builder.result.rig
    rig.animation_data.action = builder.result.actions["PistolReload"].action
    maximum_wrist = 0.
    measured = []
    for frame in range(181):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        hand, forearm = rig.pose.bones["hand.L"], rig.pose.bones["forearm.L"]
        bend = math.degrees((hand.head-forearm.head).angle(hand.tail-hand.head))
        maximum_wrist = max(maximum_wrist, bend)
        if bend > 45.:
            raise ValueError(f"Pistol reload left wrist bends beyond45deg: {frame}={bend:.4f}")
    for second, kind in RELOAD_STOPS[1:-1]:
        bpy.context.scene.frame_set(round(second * FPS))
        bpy.context.view_layer.update()
        right = rig.pose.bones["hand.R"]
        delta = right.matrix @ right.bone.matrix_local.inverted()
        centre, rest_axis, rest_palm = builder.hand_frame("R")
        centre = delta @ centre
        axis, palm = (delta.to_3x3() @ rest_axis).normalized(), (delta.to_3x3() @ rest_palm).normalized()
        forward = palm.cross(axis).normalized()
        lateral = forward.cross(axis)
        def point(local):
            return centre + lateral * local[0] + axis * local[1] + forward * local[2]
        left = rig.pose.bones["hand.L"]
        left_delta = left.matrix @ left.bone.matrix_local.inverted()
        actual = left_delta @ builder.hand_frame("L")[0]
        expected = point(Vector(ANCHORS["MagazineSeat"]) + Vector(MAGAZINE_GRIP))
        magazine_axis = axis * math.cos(MAGAZINE_TILT) + forward * math.sin(MAGAZINE_TILT)
        if kind == "reload_extract": expected -= magazine_axis * .12
        elif kind == "reload_align": expected -= magazine_axis * .04
        elif kind == "reload_stash": expected = Vector((.25, -.15, .92))
        elif kind in ("reload_rack", "reload_pull", "reload_release"):
            expected = point(ANCHORS["SlidePull"])
            if kind == "reload_pull": expected -= forward * SLIDE_TRAVEL
            if kind == "reload_release": expected -= lateral * .045
        error = (expected-actual).length
        if error > .00001:
            raise ValueError(f"Pistol reload left grip misses authored contact: {second}/{kind}={error:.7f}m")
        measured.append(dict(seconds=second, phase=kind, grip_error_m=round(error, 7)))
    print("PISTOL RELOAD MAGAZINE / SLIDE CONTACTS / LEFT WRIST OK " + json.dumps(measured), flush=True)
    return dict(maximum_left_wrist_bend_degrees=round(maximum_wrist, 4), contacts=measured)


def verify_bank(path, reference):
    rig, actions = combat.import_bank(path)
    if set(actions) != {name for name, _, _ in CLIPS}:
        raise ValueError("Published pistol bank has a missing or foreign clip")
    actual = motion_samples(rig, actions)
    maximum_position, maximum_angle = 0., 0.
    for name, track in reference.items():
        for source, imported in zip(track, actual[name]):
            for bone, a in source.items():
                b = imported[bone]
                maximum_position = max(maximum_position, (a.translation-b.translation).length)
                qa, qb = a.to_quaternion(), b.to_quaternion()
                cosine = min(1., abs(qa.normalized().dot(qb.normalized())))
                maximum_angle = max(maximum_angle, math.degrees(2.*math.acos(cosine)))
    if maximum_position > .0002 or maximum_angle > .08:
        raise ValueError(f"Published pistol motion differs: {maximum_position}m / {maximum_angle}deg")
    print(f"PISTOL PUBLISHED MOTION ROUND TRIP OK position={maximum_position:.7f}m angle={maximum_angle:.5f}deg", flush=True)


def verify_mechanism(model_dir):
    """Exercise the exported hierarchy, including the reflected metric axes."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(model_dir / "Pistol.fbx"), use_anim=False)
    parts = {obj.name.split(".")[0]: obj for obj in bpy.data.objects if obj.type == "EMPTY"}
    required = ("Slide", "Barrel", "MagazineSeat", "Magazine", "EjectionPort", "SlidePull", "Muzzle")
    if any(name not in parts for name in required):
        raise ValueError("Pistol exported mechanism is missing a named part")
    for name in ("EjectionPort", "SlidePull"):
        if parts[name].parent != parts["Slide"]:
            raise ValueError("Pistol sliding anchor is detached from its slide: " + name)
    if parts["Magazine"].parent != parts["MagazineSeat"]:
        raise ValueError("Pistol seated magazine has the wrong dock")
    before = {name: parts[name].matrix_world.translation.copy() for name in required}
    expected = Vector((0., -SLIDE_TRAVEL, 0.))
    slide = parts["Slide"].matrix_world.copy()
    slide.translation += expected
    parts["Slide"].matrix_world = slide
    bpy.context.view_layer.update()
    for name in ("Slide", "EjectionPort", "SlidePull"):
        if (parts[name].matrix_world.translation-before[name]-expected).length > .00001:
            raise ValueError("Pistol exported slide has the wrong metric travel: " + name)
    for name in ("Barrel", "Muzzle", "MagazineSeat", "Magazine"):
        if (parts[name].matrix_world.translation-before[name]).length > .00001:
            raise ValueError("Pistol slide moved a fixed contact: " + name)
    print("PISTOL EXPORTED SLIDE / FIXED BARREL / MAGAZINE DOCK / EJECTION AXES OK", flush=True)


def preview(model_dir, source_dir, destination):
    """Review the authored grip with the real hero; never publish preview scenes."""
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / "ArtSource/Combat/CombatActions.blend"))
    combat.pack_hero_atlases()
    with bpy.data.libraries.load(str(source_dir / "PistolActions.blend"), link=False) as (available, selected):
        selected.actions = [name for name in available.actions if name in {n for n, _, _ in CLIPS}]
    actions = {action.name.split(".")[0]: action for action in selected.actions}
    rig = bpy.data.objects["RIG_Player"]
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.data.shape_keys is None:
            continue
        shape = obj.data.shape_keys.key_blocks.get("CylindricalGrip")
        if shape is not None:
            shape.value = 1.
    config = common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20260919, "apose")
    builder = combat.CombatBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.result = common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={}, parts=[])
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(model_dir / "Pistol.fbx"), use_anim=False)
    model = next(obj for obj in bpy.data.objects if obj not in before and obj.parent is None)
    imported = model.matrix_world.copy()
    mounted = next(obj for obj in model.children_recursive if obj.name.split(".")[0] == "Magazine")
    slide = next(obj for obj in model.children_recursive if obj.name.split(".")[0] == "Slide")
    slide_rest = slide.matrix_basis.copy()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(model_dir / "Magazine.fbx"), use_anim=False)
    magazine = next(obj for obj in bpy.data.objects if obj not in before and obj.parent is None)
    magazine_imported = magazine.matrix_world.copy()
    magazine_grip = next(obj for obj in magazine.children_recursive if obj.name.split(".")[0] == "Grip").matrix_world.translation.copy()
    for obj in model.children_recursive:
        if obj.name.startswith("MuzzleFlash"):
            obj.hide_render = True
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.camera.data.type = "ORTHO"
    scene.camera.data.ortho_scale = 2.1
    destination.mkdir(parents=True, exist_ok=True)
    samples = (("PistolRest", 0., "PistolRest"), ("PistolAim", 0., "PistolAim"),
               *(("PistolFire", second, "PistolFire-" + phase)
                 for second, phase, *_ in FIRE_STOPS[1:-1]),
               *(("PistolReload", second, "PistolReload-" + phase.removeprefix("reload_") + "-" + str(round(second * FPS)))
                 for second, phase in RELOAD_STOPS[1:-1]))
    for name, second, label in samples:
        rig.animation_data.action = actions[name]
        scene.frame_set(round(second * FPS))
        for obj in bpy.data.objects:
            if obj.type == "MESH" and obj.name.endswith(".L") and obj.data.shape_keys is not None:
                shape = obj.data.shape_keys.key_blocks.get("CylindricalGrip")
                if shape is not None:
                    shape.value = SUPPORT_WEIGHT if name in ("PistolAim", "PistolFire") else .8 if name == "PistolReload" and second < 1.58 else 0.
        bpy.context.view_layer.update()
        hand = rig.pose.bones["hand.R"]
        delta = hand.matrix @ hand.bone.matrix_local.inverted()
        centre, rest_axis, rest_palm = builder.hand_frame("R")
        up = (delta.to_3x3() @ rest_axis).normalized()
        palm = (delta.to_3x3() @ rest_palm).normalized()
        forward = palm.cross(up).normalized()
        rotation = Matrix((forward.cross(up), forward, up)).transposed().to_4x4()
        model.matrix_world = Matrix.Translation(delta @ centre) @ rotation @ imported
        holding = name == "PistolReload" and (.25 <= second < .75 or .9 <= second < 1.3)
        for obj in mounted.children_recursive:
            obj.hide_render = name == "PistolReload" and .25 <= second < 1.3
        for obj in magazine.children_recursive:
            obj.hide_render = not holding
        slide.matrix_basis = slide_rest.copy()
        bpy.context.view_layer.update()
        slide_back = .8 if name == "PistolFire" and second == .04 else 0.
        if name == "PistolReload" and second == 1.5:
            slide_back = 1.
        if slide_back:
            matrix = slide.matrix_world.copy()
            matrix.translation -= forward * SLIDE_TRAVEL * slide_back
            slide.matrix_world = matrix
        if holding:
            hand = rig.pose.bones["hand.L"]
            delta_left = hand.matrix @ hand.bone.matrix_local.inverted()
            contact, rest_axis, rest_palm = builder.hand_frame("L")
            left_axis = (delta_left.to_3x3() @ rest_axis).normalized()
            left_palm = (delta_left.to_3x3() @ rest_palm).normalized()
            distal = left_axis.cross(left_palm).normalized()
            mag_up = left_axis * math.cos(MAGAZINE_TILT) - distal * math.sin(MAGAZINE_TILT)
            mag_forward = distal * math.cos(MAGAZINE_TILT) + left_axis * math.sin(MAGAZINE_TILT)
            mag_rotation = Matrix((left_palm, mag_forward, mag_up)).transposed().to_4x4()
            magazine.matrix_world = Matrix.Translation(delta_left @ contact) @ mag_rotation @ Matrix.Translation(-magazine_grip) @ magazine_imported
        for view, position in (("front", (-2.6, -3.2, 1.65)), ("side", (-3.5, -.25, 1.45))):
            scene.camera.location = position
            common.look_at(scene.camera, Vector((0., -.16, .9)))
            scene.render.filepath = str(destination / (label + "-" + view + ".png"))
            bpy.ops.render.render(write_still=True)
            print("PISTOL REVIEW FRAME " + name + "/" + view, flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Resources/CombatPistol")
    parser.add_argument("--source-dir", type=Path, default=ROOT / "ArtSource/Combat")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--preview-dir", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if args.preview_dir is not None:
        args.preview_dir = (ROOT / args.preview_dir).resolve()
    items = make_items()
    data = payload(items)
    builder = author_actions()
    data["actions"]["animation_signature"] = action_signature(builder)
    data["actions"]["hand_mesh_validation"] = validate_actions(builder)
    data["actions"]["fire_motion_validation"] = validate_fire_motion(builder)
    data["actions"]["reload_contact_validation"] = validate_reload_contacts(builder)
    reference = motion_samples(builder.result.rig, {name: record.action for name, record in builder.result.actions.items()})
    manifest = args.model_dir / "CombatPistol3D.json"
    if args.validate_only:
        if json.loads(manifest.read_text(encoding="utf8")) != json.loads(json.dumps(data)):
            raise ValueError("Published pistol manifest differs from deterministic authoring")
        kit.verify_fbx(args.model_dir, data)
        verify_mechanism(args.model_dir)
        verify_bank(args.model_dir / "PistolActions.fbx", reference)
        if args.preview_dir:
            preview(args.model_dir, args.source_dir, args.preview_dir)
        print("COMBAT PISTOL DETERMINISM / PUBLISHED ASSETS OK", flush=True)
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    common.export_animation_fbx(args.model_dir / "PistolActions.fbx", builder.result)
    common.save_blend(args.source_dir / "PistolActions.blend")
    roots = kit.build_objects(items)
    common.save_blend(args.source_dir / "CombatPistol.blend")
    for item, root in zip(items, roots):
        kit.export(root, args.model_dir / (item.name + ".fbx"))
    manifest.write_text(json.dumps(data, indent=2) + "\n", encoding="utf8")
    kit.verify_fbx(args.model_dir, data)
    verify_mechanism(args.model_dir)
    verify_bank(args.model_dir / "PistolActions.fbx", reference)
    if args.preview_dir:
        preview(args.model_dir, args.source_dir, args.preview_dir)
    print("COMBAT PISTOL GENERATED", flush=True)


if __name__ == "__main__":
    main()
