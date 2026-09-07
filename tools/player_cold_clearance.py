"""Cross-arm clearance on the hero's evaluated, posed convex mesh parts.

Separating-axis checks include every face normal and edge-pair cross product.
Skin/clothing layers within one arm are deliberately not compared. The opposite
hand may touch an upper sleeve but receives the same 2 mm numerical allowance.
"""
from __future__ import annotations

import math

import bpy
import numpy as np

TOLERANCE_M = 0.002
LEFT_PARTS = ("GEO_UpperArm.L", "GEO_Forearm.L", "GEO_Hand.L", "GEO_Thumb.L",
              "CLO_JacketSleeve.L", "CLO_Bandage.L")
RIGHT_PARTS = ("GEO_UpperArm.R", "GEO_Forearm.R", "GEO_Hand.R", "GEO_Thumb.R",
               "CLO_JacketSleeve.R", "CLO_JacketForearm.R")


def unique_axes(values):
    lengths = np.linalg.norm(values, axis=1)
    values = values[lengths > 1e-8] / lengths[lengths > 1e-8, None]
    if not len(values):
        return values
    leading = np.argmax(np.abs(values) > 1e-7, axis=1)
    signs = np.sign(values[np.arange(len(values)), leading])
    values *= signs[:, None]
    _, indices = np.unique(np.round(values, 5), axis=0, return_index=True)
    return values[indices]


def evaluated_volume(obj, depsgraph, check_convex=False):
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        mesh.calc_loop_triangles()
        points = np.array([tuple(evaluated.matrix_world @ vertex.co) for vertex in mesh.vertices])
        faces = np.array([tuple(triangle.vertices) for triangle in mesh.loop_triangles])
        edges = np.array([tuple(edge.vertices) for edge in mesh.edges])
        raw_normals = np.cross(points[faces[:, 1]] - points[faces[:, 0]],
                               points[faces[:, 2]] - points[faces[:, 0]])
        if check_convex:
            for normal, face in zip(raw_normals, faces):
                length = np.linalg.norm(normal)
                if length < 1e-9:
                    continue
                signed = (points - points[face[0]]) @ (normal / length)
                if signed.min() < -0.00001 and signed.max() > 0.00001:
                    raise RuntimeError(f"{obj.name} is not convex; SAT cannot certify its mesh clearance")
        normals = unique_axes(raw_normals)
        directions = unique_axes(points[edges[:, 1]] - points[edges[:, 0]])
        return points, normals, directions
    finally:
        evaluated.to_mesh_clear()


def signed_clearance(first, second):
    a, normals_a, edges_a = first
    b, normals_b, edges_b = second
    # Broad-phase axes may already prove the volumes well separated.
    aabb_gap = np.maximum(b.min(axis=0) - a.max(axis=0),
                          a.min(axis=0) - b.max(axis=0)).max()
    if aabb_gap > 0.020:
        return float(aabb_gap)
    crossed = np.cross(edges_a[:, None, :], edges_b[None, :, :]).reshape((-1, 3))
    axes = unique_axes(np.concatenate((normals_a, normals_b, crossed)))
    projections_a = a @ axes.T
    projections_b = b @ axes.T
    overlaps = np.minimum(projections_a.max(axis=0) - projections_b.min(axis=0),
                          projections_b.max(axis=0) - projections_a.min(axis=0))
    # Negative is the translation required to separate intersecting parts.
    # Positive is a conservative separating-axis gap, not a Euclidean distance.
    return float(-overlaps.min())


def measure(rig, actions, names=("ColdHold", "ColdShoulderRub"), step=0.5, pose_callback=None):
    objects = {name: bpy.data.objects.get(name) for name in LEFT_PARTS + RIGHT_PARTS}
    missing = [name for name, obj in objects.items() if obj is None]
    if missing:
        raise RuntimeError(f"Cannot measure cold arm clearance without {missing}")
    previous = rig.animation_data_create().action
    previous_frame = bpy.context.scene.frame_current
    report = {"method": "SAT over evaluated mesh parts", "sample_step_frames": step,
              "tolerance_m": TOLERANCE_M, "actions": {}}
    try:
        for name in names:
            action = actions[name]
            rig.animation_data.action = action if pose_callback is None else None
            worst = {"clearance_m": float("inf")}
            worst_non_contact = {"clearance_m": float("inf")}
            pairs = {}
            failures = 0
            for sample_index in range(math.ceil(action.frame_end / step) + 1):
                frame = min(action.frame_end, sample_index * step)
                bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.0)
                if pose_callback is not None:
                    pose_callback(name, frame / action.frame_end)
                depsgraph = bpy.context.evaluated_depsgraph_get()
                volumes = {part: evaluated_volume(obj, depsgraph, sample_index == 0) for part, obj in objects.items()}
                for left in LEFT_PARTS:
                    for right in RIGHT_PARTS:
                        clearance = signed_clearance(volumes[left], volumes[right])
                        sample = {"clearance_m": clearance, "frame": frame,
                                  "left": left, "right": right}
                        key = (left, right)
                        if key not in pairs or clearance < pairs[key]["clearance_m"]:
                            pairs[key] = sample
                        if clearance < worst["clearance_m"]:
                            worst = dict(sample)
                            worst["joints"] = {bone: list(rig.pose.bones[bone].head) for bone in (
                                "upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R")}
                        hand_upper_contact = (("Hand" in left or "Thumb" in left) and
                                              ("UpperArm" in right or "JacketSleeve" in right)) or (
                                              ("Hand" in right or "Thumb" in right) and
                                              ("UpperArm" in left or "JacketSleeve" in left))
                        if not hand_upper_contact and clearance < worst_non_contact["clearance_m"]:
                            worst_non_contact = sample
                        if clearance < -TOLERANCE_M:
                            failures += 1
            report["actions"][name] = {"worst": worst, "worst_non_contact": worst_non_contact,
                                         "penetrating_samples": failures,
                                         "closest_pairs": sorted(pairs.values(), key=lambda row: row["clearance_m"])[:8]}
            print(f"{name}: worst cross-arm clearance {worst['clearance_m'] * 1000:.3f} mm "
                  f"at frame {worst['frame']} ({worst['left']} / {worst['right']}), "
                  f"{failures} penetrating samples", flush=True)
    finally:
        rig.animation_data.action = previous
        bpy.context.scene.frame_set(previous_frame)
        if previous is None:
            for bone in rig.pose.bones:
                bone.location = (0, 0, 0)
                bone.rotation_quaternion = (1, 0, 0, 0)
                bone.scale = (1, 1, 1)
            bpy.context.view_layer.update()
    return report


if __name__ == "__main__":
    import argparse
    import json
    import sys
    from pathlib import Path

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-blend", type=Path,
                        default=Path("ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend"))
    parser.add_argument("--report", type=Path,
                        default=Path("Captures/Tooling/cold-clearance.json"))
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--sample-step", type=float, default=0.5)
    parser.add_argument("--probe-current-authoring", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    bpy.ops.wm.open_mainfile(filepath=str(args.source_blend.resolve()))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    callback = None
    if args.probe_current_authoring:
        import importlib.util
        import types
        root = Path(__file__).resolve().parents[1]
        sys.path.insert(0, str(root / "tools"))
        spec = importlib.util.spec_from_file_location("hero_v2_clearance_probe", root / "tools/build-player-3d-model-v2.py")
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        config = module.common.BuildConfig(output=args.source_blend, preview=None, portrait=None,
            manifest=None, glb=None, fbx=None, animation_fbx=None, height=1.75, seed=17301, pose="apose")
        builder = module.HeroV2Builder(config, module.DEFAULT_FACE_ATLAS, module.DEFAULT_CLOTHING_ATLAS)
        builder.result = types.SimpleNamespace(rig=rig)
        callback = lambda name, phase: module.player_cold_actions.cold_pose(
            builder, module.common, phase, name == "ColdShoulderRub")
    report = measure(rig, bpy.data.actions, step=args.sample_step, pose_callback=callback)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if any(action["penetrating_samples"] for action in report["actions"].values()):
        raise RuntimeError("Cold arms interpenetrate; see the reported pair/frame/depth")
