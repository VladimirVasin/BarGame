"""Selective forehand authoring on retained banks; imported anatomy is the oracle."""
from __future__ import annotations

import hashlib
import inspect
import json
import math
import shutil
from pathlib import Path
from types import SimpleNamespace

import bpy
from mathutils import Vector

SOURCE_SHA256 = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()


def _pose_payload(pose):
    return {name: dict(rotation_degrees=list(value.rotation_degrees),
        location_m=list(value.location_m), scale=list(value.scale)) for name, value in pose.items()}


def _read_pose(m, pose):
    return {name: m.common.BonePose(**value) for name, value in pose.items()}


def _builder(m, rig, profile):
    config = m.common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20260919, "apose")
    result = m.CombatBuilder(config, m.hero.DEFAULT_FACE_ATLAS, m.hero.DEFAULT_CLOTHING_ATLAS)
    result.hero_profile = profile
    result.points = result.create_pose_points()
    result.result = m.common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={},
        parts=[SimpleNamespace(obj=o) for o in bpy.data.objects if o.type == "MESH"])
    return result


def _apply_matrices(builder, matrices):
    rig = builder.result.rig
    for bone in rig.pose.bones:
        bone.rotation_mode = "QUATERNION"
        parent = dict(parent_matrix=matrices[bone.parent.name],
            parent_matrix_local=bone.parent.bone.matrix_local) if bone.parent else {}
        bone.matrix_basis = bone.bone.convert_local_to_pose(matrices[bone.name],
            bone.bone.matrix_local, invert=True, **parent)
    bpy.context.view_layer.update()
    return builder.snapshot_pose()


def _sample(rig, action, seconds):
    rig.animation_data_create().action = action
    frame = seconds * 100
    bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
    bpy.context.view_layer.update()
    return {bone.name: bone.matrix.copy() for bone in rig.pose.bones}


def _canonical(m, profile):
    builder = m.CombatBuilder(m.common.BuildConfig(None, None, None, None, None, None, None,
        1.75, 20260919, "apose"), m.hero.DEFAULT_FACE_ATLAS, m.hero.DEFAULT_CLOTHING_ATLAS)
    builder.hero_profile = profile
    builder.native_right_arm = True
    builder.points = builder.create_pose_points()
    collections = builder.create_collections()
    root = builder.create_root(collections["export"])
    specs = builder.create_bone_specs()
    rig = builder.create_armature(collections["rig"], root, specs)
    rig.animation_data_create()
    builder.bone_heads = {spec.name: spec.head for spec in specs}
    builder.bone_specs = {spec.name: spec for spec in specs}
    builder.result = m.common.BuildResult(root=root, rig=rig, collections=collections, materials={}, parts=[])
    return builder


def _validate_prefix(builder, keys, fps):
    previous = None
    maximum = 0.
    for frame, (_, pose) in enumerate(keys):
        for weight in ((.25, .5, .75, 1.) if frame else (1.,)):
            current = builder.linear_key_pose(keys[frame-1][1], pose, weight) if frame else pose
            builder.pin_left_grip(current, move_right=False, runtime_support=True)
            metric = builder.weapon_anatomy(f"CombatAttack@{(frame-1+weight)/fps:.5f}", 65.)
            if metric["body_gap_m"] < .005-1.e-6:
                raise ValueError("Checkpoint candidate lost weapon skin reserve")
            if frame == 56 and weight == 1.:
                origin, rotation = builder.weapon_frame()
                if -(origin + rotation @ Vector((0., .595, .145))).y < .95:
                    raise ValueError("Checkpoint candidate lost contact reach")
            elbows = {s: builder.result.rig.pose.bones["forearm."+s].head.copy() for s in ("R", "L")}
            if previous is not None:
                speed = max((elbows[s]-previous[s]).length*fps*4. for s in elbows)
                if speed > 6.+.0001:
                    raise ValueError(f"Checkpoint candidate exceeds elbow speed: {speed}")
                maximum = max(maximum, speed)
            previous = elbows
    print(f"FOREHAND PREFIX CANDIDATE REVALIDATED maximum_elbow_speed_m_s={maximum:.9f}", flush=True)


def _author(m, source, profile, *, prepare_only=False):
    # Direct focused authoring uses the same written clock as the official CLI.
    m.common.ANIMATION_FPS = m.FPS
    family = m.SWING_FAMILIES[0]
    old_rig = source.result.rig
    old = source.result.actions[family["attack"]].action
    ready = _sample(old_rig, source.result.actions["CombatReady"].action, 0.)
    original = [_sample(old_rig, old, frame / m.FPS) for frame in range(129)]
    old_rig.animation_data.action = None
    retained = {family[key]: source.result.actions.pop(family[key])
        for key in ("attack", "light", "heavy", "charge", "recoil")}
    for record in retained.values():
        bpy.data.actions.remove(record.action)
    builder = _canonical(m, profile)
    actual_ready = _apply_matrices(builder, ready)
    builder.combat_pose_cache = {(profile, "ready"): actual_ready}
    stops = tuple((.40 if second == .45 else second, name)
        for second, name in builder.profile_stops(m.ATTACK_STOPS))
    poses = {name: builder.combat_pose(name) for _, name in stops}
    keys = []
    # Two overwritten checkpoints retain prepared inputs, never an old proof.
    # The DAG and actual-file certificate still execute on every invocation.
    checkpoint = m.ROOT / ("Captures/CombatTest/recovery-authoring/forehand-prepared-" +
        ("hero" if profile else "npc") + ".json")
    identity = dict(source_sha256=SOURCE_SHA256,
        gate_source_sha256=hashlib.sha256(Path(m.__file__).read_bytes()).hexdigest(),
        profile=profile, fps=m.FPS, stops=stops, ready=_pose_payload(actual_ready),
        rest={bone.name: [list(row) for row in bone.matrix_local] for bone in builder.result.rig.data.bones})
    input_hash = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
    if checkpoint.exists():
        cached = json.loads(checkpoint.read_text(encoding="utf8"))
        if cached.get("prepared_input_sha256") == input_hash:
            keys = [(time, _read_pose(m, pose)) for time, pose in cached["keys"]]
            print("FOREHAND PREPARED INPUT CHECKPOINT " + ("hero" if profile else "npc"), flush=True)
    shoulder = None
    left = None
    for frame in range(0 if keys else 64):
        second = frame / m.FPS
        for (a, p), (b, q) in zip(stops, stops[1:]):
            if second > b + .00001:
                continue
            weight = m.dialogue.smooth((second - a) / (b - a))
            blended = builder.track_pose(stops, poses, second)
            posed = builder.pin_supports(blended)
            posed = builder.author_support_grip(posed, m.SUPPORT_GRIP,
                m.blended_left_pole(p, q, weight), context=f"CombatAttack@{second}")
            if frame == 0:
                posed = actual_ready
                shoulder = builder.weapon_arm_state(posed, second, posed)
            else:
                # Clear the whole arm at its shoulder before selecting the
                # continuous elbow branches. An elbow-only path cannot fix a
                # hand dock whose entire anatomical circle enters the torso.
                raw = posed
                metric = builder.guard_metrics(posed)
                right = metric["wrist_components"][0]
                nominal = abs(right["deviation"]) <= 25. and abs(right["flexion"]) <= 55. and metric["wrist_angles"][0] <= 65.
                hint = shoulder["shoulder_correction"] if shoulder is not None else None
                if not nominal and hint is not None:
                    upper = builder.result.rig.pose.bones["upper_arm.R"]
                    parent = upper.parent.matrix.to_quaternion()
                    turn = parent @ hint @ parent.inverted()
                    matrix = upper.matrix.copy()
                    matrix = turn.to_matrix().to_4x4() @ matrix
                    matrix.translation = upper.head.copy()
                    upper.matrix = matrix
                    bpy.context.view_layer.update()
                    posed = builder.snapshot_pose()
                projection = dict(context=f"CombatAttack@{second}",
                    shoulder_hint=hint if nominal else None,
                    support_hint=shoulder["left_elbow"] if shoulder is not None else None,
                    # Raw NPC hand steps peak at75.109mm; its otherwise legal
                    # early shoulder clearance needs80.521mm. Keep that arc
                    # while rejecting the measured138mm shoulder-dock jump.
                    hand_hint=shoulder["hand"] if shoulder is not None and not profile else None,
                    hand_travel_limit=.081 if not profile else None,
                    support_clearance=.007 if not profile else .005,
                    weapon_reserve=.005 if not profile else .0005)
                try:
                    projected = builder.project_weapon_arm(posed, **projection)
                except ValueError:
                    if profile or projection["support_hint"] is None:
                        raise
                    # Preserve the previous supporting branch whenever it is
                    # compatible. The paired DAG owns alternatives when that
                    # one preliminary pole would force a hand-dock jump.
                    projected = builder.project_weapon_arm(posed, **dict(projection, support_hint=None))
                    print(f"FOREHAND NPC PRELIMINARY LEFT HINT YIELDED @{second}", flush=True)
                posed = projected
                shoulder = builder.weapon_arm_state(posed, second, raw)
                angle = math.degrees(2. * math.acos(min(1., abs(shoulder["shoulder_correction"].w))))
                if angle > 45. + .0001:
                    raise ValueError(f"Forehand dock exceeds existing shoulder envelope @{second}: {angle}")
                if left is not None:
                    try:
                        posed = builder.pin_left_grip(posed,m.SUPPORT_GRIP,move_right=False,
                            radial_limit=24.,continuation=left)
                    except ValueError as error:
                        # The two-arm DAG is authoritative; a preliminary
                        # branch cannot veto a dock it can connect differently.
                        print(f"FOREHAND SUPPORT BRANCH DEFERRED @{second}: {error}",flush=True)
                        builder._reset_pose();builder._apply_pose(posed)
                left = builder.support_branch(posed)
                shoulder["left_elbow"] = builder.result.rig.pose.bones["forearm.L"].head.copy()
            keys.append((frame / 128, posed))
            break
        if frame%20==0:print("FOREHAND DOCK "+("hero" if profile else "npc")+f" {second:.2f}s",flush=True)
    checkpoint.parent.mkdir(parents=True, exist_ok=True)
    checkpoint.write_text(json.dumps(dict(prepared_input_sha256=input_hash,
        identity=identity, keys=[(time, _pose_payload(pose)) for time, pose in keys])), encoding="utf8")
    print("FOREHAND LANDMARKS READY " + ("hero" if profile else "npc"), flush=True)
    if prepare_only:
        return keys
    prefix_checkpoint = checkpoint.with_name("forehand-prefix-" + ("hero" if profile else "npc") + ".json")
    gate_functions = ("solve_backhand_arm_path", "pin_left_grip", "grip_elbow", "right_arm_native_gap",
        "weapon_anatomy", "weapon_clearance", "body_boxes", "arm_core_clearance", "linear_key_pose")
    prefix_input = dict(profile=profile, fps=m.FPS, prepared_keys=[(time, _pose_payload(pose)) for time, pose in keys],
        gate_source_sha256=hashlib.sha256("\n".join(inspect.getsource(getattr(builder, name)) for name in gate_functions).encode()).hexdigest())
    prefix_hash = hashlib.sha256(json.dumps(prefix_input, sort_keys=True).encode()).hexdigest()
    cached_prefix = json.loads(prefix_checkpoint.read_text(encoding="utf8")) if prefix_checkpoint.exists() else None
    if cached_prefix is not None and cached_prefix.get("candidate_input_sha256") == prefix_hash:
        cached_keys = [(time, _read_pose(m, pose)) for time, pose in cached_prefix["keys"]]
        if len(cached_keys) != len(keys) or _pose_payload(cached_keys[0][1]) != _pose_payload(keys[0][1]):
            raise ValueError("Checkpoint candidate changed exact Ready or prefix clock")
        keys = cached_keys
    else:
        keys = builder.solve_backhand_arm_path(keys, clip_name="CombatAttack", body_reserve=.005,
            presented_ready=True, carry_left=True)
        prefix_checkpoint.write_text(json.dumps(dict(candidate_input_sha256=prefix_hash,
            gate_source_sha256=prefix_input["gate_source_sha256"], keys=[(time, _pose_payload(pose)) for time, pose in keys])), encoding="utf8")
    _validate_prefix(builder, keys, m.FPS)
    return_speeds = builder.measure_recoil_speeds(keys)
    returned = builder.recoil_keys(.65, keys, return_speeds, "CombatAttack return",source_end=.63)
    returned = builder.clear_retimed_keys(returned, .65, "CombatForehandReturn")
    keys += [((.63 + time*.65)/1.28, pose) for time, pose in returned[1:]]
    builder._create_action(family["attack"], "combat", 1.28, False, 128, m.FPS, keys)
    reference = keys[:57]
    speeds = builder.measure_recoil_speeds(reference)
    recoil = builder.recoil_keys(.48, reference, speeds, family["recoil"])
    recoil = builder.clear_retimed_keys(recoil, .48, family["recoil"])
    builder._create_action(family["recoil"], "combat", .48, False, 48, m.FPS, recoil)
    builder.build_charge_actions(family, poses)
    # Convert absolute final poses onto the retained bank's original rest;
    # unrelated relative animation curves and the exported skeleton stay put.
    tracks = {}
    for name, record in builder.result.actions.items():
        count = round(record.duration_seconds * m.FPS)
        tracks[name] = [_sample(builder.result.rig, record.action, frame / m.FPS)
            for frame in range(count + 1)]
    arc = dict(maximum_right_wrist_position_change_m=0., maximum_right_wrist_rotation_change_degrees=0.)
    for before, after in zip(original, tracks[family["attack"]]):
        first, second = before["hand.R"], after["hand.R"]
        arc["maximum_right_wrist_position_change_m"] = max(arc["maximum_right_wrist_position_change_m"],
            (first.translation-second.translation).length)
        angle = math.degrees(first.to_quaternion().rotation_difference(second.to_quaternion()).angle)
        arc["maximum_right_wrist_rotation_change_degrees"] = max(arc["maximum_right_wrist_rotation_change_degrees"],
            min(angle, 360.-angle))
    for record in list(builder.result.actions.values()):
        bpy.data.actions.remove(record.action)
    builder.result.rig.animation_data.action = None
    bpy.data.objects.remove(builder.result.rig, do_unlink=True)
    bpy.data.objects.remove(builder.result.root, do_unlink=True)
    for name, track in tracks.items():
        old_record = retained[name]
        converted = [(frame / (len(track) - 1), _apply_matrices(source, matrices))
            for frame, matrices in enumerate(track)]
        source._create_action(name, "combat", old_record.duration_seconds, False,
            len(track) - 1, m.FPS, converted)
    return ready, arc


def _published_certificate(m, path, profile, names, ready):
    imported, actions = m.import_bank(path)
    published_ready = _sample(imported, actions["CombatReady"], 0.)
    def difference(a, b):
        matrix = max(abs(a[bone][i][j]-b[bone][i][j]) for bone in a for i in range(4) for j in range(4))
        position = max((a[bone].translation-b[bone].translation).length for bone in a)
        rotation = 0.
        for bone in a:
            first, second = a[bone].to_quaternion(), b[bone].to_quaternion()
            cosine = abs(sum(x*y for x,y in zip(first, second)))/math.sqrt(sum(x*x for x in first)*sum(x*x for x in second))
            rotation = max(rotation, math.degrees(2.*math.acos(min(1., cosine))))
        return dict(maximum_matrix_error=matrix, maximum_position_error_m=position,
            maximum_rotation_error_degrees=rotation)
    print("READY FBX SOURCE ROUNDTRIP " + json.dumps(difference(published_ready, ready)), flush=True)
    builder = _canonical(m, profile)
    result = {}
    for name in names:
        duration = 1. if name == "CombatCharge" else .48 if name == "CombatRecoil" else 1.28
        record = dict(maximum_wrist_degrees=0., minimum_body_clearance_m=10.,
            minimum_floor_clearance_m=10., maximum_elbow_speed_m_s=0., minimum_native_right_arm_gap_m=10.)
        previous = None
        active_tips=[]
        for sample in range(round(duration * m.FPS * 2) + 1):
            seconds = sample / (m.FPS * 2)
            matrices = _sample(imported, actions[name], seconds)
            if name == "CombatAttack" and sample in (0, 256):
                endpoint = difference(matrices, published_ready)
                record.setdefault("ready_endpoints", []).append(dict(seconds=seconds, **endpoint))
                print(f"PUBLISHED FOREHAND READY {profile}/{seconds} " + json.dumps(endpoint), flush=True)
                if endpoint["maximum_matrix_error"] > .00001:
                    raise ValueError(f"Published forehand lost exact Ready: {profile}/{seconds}/{endpoint}")
            imported.animation_data.action = None
            pose = _apply_matrices(builder, matrices)
            builder.pin_left_grip(pose, move_right=False, runtime_support=True)
            metric = builder.weapon_anatomy(f"{name}@{seconds:.5f}", 65.)
            if metric["body_gap_m"] < .0045:
                raise ValueError(f"Published forehand lacks native 4mm weapon skin reserve: {name}@{seconds}/{metric}")
            record["maximum_wrist_degrees"] = max(record["maximum_wrist_degrees"], metric["maximum_wrist_degrees"])
            record["minimum_native_right_arm_gap_m"] = min(record["minimum_native_right_arm_gap_m"], metric["native_right_arm_gap_m"])
            for label in ("body", "floor"):
                key = f"minimum_{label}_clearance_m"
                record[key] = min(record[key], metric[label + "_gap_m"])
            elbows = {s: builder.result.rig.pose.bones["forearm." + s].head.copy() for s in ("R", "L")}
            if previous is not None:
                speed = max((elbows[s] - previous[s]).length * m.FPS * 2 for s in elbows)
                if speed > 6. + .0001:
                    raise ValueError(f"Published forehand elbow exceeds existing speed: {name}@{seconds}/{speed}")
                record["maximum_elbow_speed_m_s"] = max(record["maximum_elbow_speed_m_s"], speed)
            previous = elbows
            if name in ("CombatAttack", "CombatReleaseLight", "CombatReleaseHeavy") and .45<=seconds<=.63:
                origin,rotation=builder.weapon_frame()
                active_tips.append(origin+rotation @ m.Vector((0.,.595,.145)))
        if active_tips:
            record["minimum_reach_m"]=max(-tip.y for tip in active_tips)
            record["active_tip_travel_m"]=max((tip-active_tips[0]).length for tip in active_tips)
            if record["minimum_reach_m"]<.95 or record["active_tip_travel_m"]<1.:
                raise ValueError(f"Published forehand lost existing reach/Active arc: {profile}/{record}")
        result[name] = record
        print("PUBLISHED FOREHAND ANATOMY " + ("hero" if profile else "npc") + "/" + name + " " + json.dumps(record), flush=True)
    return result


def repair(m, published, sources, output, source_output):
    output.mkdir(parents=True, exist_ok=True)
    source_output.mkdir(parents=True, exist_ok=True)
    payload = json.loads((published / "CombatTest3D.json").read_text(encoding="utf8"))
    family = m.SWING_FAMILIES[0]
    names = tuple(family[key] for key in ("attack", "light", "heavy", "charge", "recoil"))
    certificates = {}
    for profile, bank in ((True, "CombatActions"), (False, "CombatNpcActions")):
        bpy.ops.wm.open_mainfile(filepath=str(sources / (bank + ".blend")))
        rig = bpy.data.objects["RIG_Player"]
        source = _builder(m, rig, profile)
        data = payload["actions"] if profile else payload["actions"]["npc"]
        for clip in m.bank_clip_specs(data):
            action = bpy.data.actions[clip["name"]]
            source.result.actions[clip["name"]] = m.common.ActionRecord(action=action,
                category=action.get("bp_category", "combat"), duration_seconds=clip["duration_seconds"],
                loop=clip["loop"], source_frame_count=round(clip["duration_seconds"] * m.FPS), source_fps=m.FPS)
        preserved = tuple(name for name in source.result.actions if name not in names)
        signature = m.action_curve_signature(source, preserved)
        source.suspend_mesh_deformation()
        ready, arc = _author(m, source, profile)
        if signature != m.action_curve_signature(source, preserved):
            raise ValueError("Forehand repair changed unrelated Action curves: " + bank)
        data["animation_signature"] = m.action_curve_signature(source, tuple(name for name, _, _ in m.SHARED_CLIPS))
        data["base_action_signature"] = m.action_curve_signature(source, ("CombatReady", "CombatAttack", "CombatBlock", "CombatHit"))
        source.restore_mesh_deformation()
        rig.animation_data.action = source.result.actions["CombatReady"].action
        bpy.context.scene.frame_set(0)
        m.common.export_animation_fbx(output / (bank + ".fbx"), source.result)
        m.common.save_blend(source_output / (bank + ".blend"))
        certificate = _published_certificate(m, output / (bank + ".fbx"), profile, names, ready)
        certificate["unchanged_action_signature"] = signature
        certificate["source_sha256"] = SOURCE_SHA256
        certificate["gate_source_sha256"] = hashlib.sha256(Path(m.__file__).read_bytes()).hexdigest()
        certificate["fbx_sha256"] = hashlib.sha256((output / (bank + ".fbx")).read_bytes()).hexdigest()
        certificate["arc_changes"] = arc
        # Fixed, overwritten artifacts survive a later profile's failed gate.
        # They are evidence/candidates; publication still requires both profiles.
        retained_output = m.ROOT / "Captures/CombatTest/recovery-authoring/forehand-verified-banks"
        retained_output.mkdir(parents=True, exist_ok=True)
        shutil.copy2(output / (bank + ".fbx"), retained_output / (bank + ".fbx"))
        shutil.copy2(source_output / (bank + ".blend"), retained_output / (bank + ".blend"))
        (retained_output / (bank + ".certificate.json")).write_text(
            json.dumps(certificate, indent=2) + "\n", encoding="utf8")
        data["forehand_anatomy"] = certificate
        swing = next(row for row in data["swings"] if row["name"] == "forehand")
        for field in ("minimum_reach_m", "active_tip_travel_m"):
            swing[field] = certificate[family["attack"]][field]
        swing["charging"]["minimum_reach_m"] = min(certificate[family[key]]["minimum_reach_m"] for key in ("light", "heavy"))
        data["charging"]["minimum_reach_m"] = swing["charging"]["minimum_reach_m"]
        certificates[bank] = certificate
    (output / "CombatTest3D.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
    print("SELECTIVE FOREHAND REPAIR OK " + json.dumps(certificates), flush=True)


def validate_retained_forehand(m, path, profile):
    """A retained bank needs a fresh actual-file proof, never a trusted manifest."""
    rig, actions = m.import_bank(path)
    ready = _sample(rig, actions["CombatReady"], 0.)
    family = m.SWING_FAMILIES[0]
    names = tuple(family[key] for key in ("attack", "light", "heavy", "charge", "recoil"))
    result = _published_certificate(m, path, profile, names, ready)
    result["fbx_sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
    result["source_sha256"] = SOURCE_SHA256
    return result
