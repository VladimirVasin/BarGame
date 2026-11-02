using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class PlayerVodkaActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/PlayerVodkaActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/PlayerVodkaActions.json";
        [MenuItem("Bar Promenade/Player 3D/Validate Vodka Actions")]
        public static void BuildOrThrow()
        {
            PlayerVodkaGripAssetSetup.BuildOrThrow();
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.generator != "player_vodka_v1" || manifest.rig != "HeroV2" ||
                manifest.bone_count != 43 || manifest.fps != 24 || manifest.root_motion || manifest.animation_events != 0 ||
                !ValidError(manifest.maximum_lower_body_error) || !ValidError(manifest.maximum_endpoint_error) ||
                !ValidError(manifest.maximum_drink_wrist_error_m) ||
                !ValidError(manifest.maximum_close_reverse_error) || manifest.empty_check_shake_count != 2 ||
                manifest.empty_check_markers == null || manifest.empty_check_markers.Length != 5 ||
                manifest.empty_check_shake_travel_m == null || manifest.empty_check_shake_travel_m.Length != 2 ||
                manifest.empty_check_opening_eye_height_m == null || manifest.empty_check_opening_eye_height_m.Length != 5 ||
                manifest.empty_check_gaze_dot == null || manifest.empty_check_gaze_dot.Length != 5 ||
                manifest.empty_check_inside_gaze_dot == null || manifest.empty_check_inside_gaze_dot.Length != 5 ||
                manifest.empty_check_opening_forward_clearance_m == null || manifest.empty_check_opening_forward_clearance_m.Length != 5 ||
                Mathf.Abs(manifest.empty_check_tilt_degrees - PlayerVodkaActions.EmptyCheckTiltDegrees) > .00001f ||
                Mathf.Abs(manifest.throw_windup_progress - PlayerVodkaActions.ThrowWindupProgress) > .00001f ||
                manifest.throw_release_speed_mps < 2.3f || manifest.throw_release_speed_mps < manifest.throw_windup_speed_mps * 2.5f ||
                manifest.throw_shoulder_sweep_degrees < 8f ||
                !ValidError(manifest.maximum_draw_neck_contact_error_m) ||
                manifest.minimum_left_arm_body_clearance_m < .02f ||
                manifest.minimum_right_arm_body_clearance_m < .02f ||
                manifest.draw_neck_contact_source == null || manifest.draw_neck_contact_source.Length != 3 ||
                Vector3.Distance(new Vector3(manifest.draw_neck_contact_source[0], manifest.draw_neck_contact_source[1],
                    manifest.draw_neck_contact_source[2]), PlayerVodkaActions.DrawNeckSource) > .00001f ||
                Mathf.Abs(manifest.draw_transfer_progress - PlayerVodkaActions.DrawTransferProgress) > .00001f ||
                Mathf.Abs(manifest.cap_open_progress - PlayerVodkaActions.CapOpenProgress) > .00001f)
                throw new InvalidOperationException("Vodka action source violated its rig, support or endpoint contract.");
            float[] shakeMarkers = { PlayerVodkaActions.EmptyCheckTurnProgress, PlayerVodkaActions.FirstShakeProgress,
                PlayerVodkaActions.ShakeRestProgress, PlayerVodkaActions.SecondShakeProgress, PlayerVodkaActions.EmptyCheckReturnProgress };
            for (int i = 0; i < shakeMarkers.Length; i++)
                if (Mathf.Abs(manifest.empty_check_markers[i] - shakeMarkers[i]) > .00001f)
                    throw new InvalidOperationException("Empty bottle check timing differs from the authored contacts.");
            foreach (float travel in manifest.empty_check_shake_travel_m)
                if (float.IsNaN(travel) || Mathf.Abs(travel - PlayerVodkaActions.EmptyCheckShakeDistance) > .003f)
                    throw new InvalidOperationException("Empty bottle check must contain two visible full shakes.");
            for (int i = 0; i < shakeMarkers.Length; i++)
                if (manifest.empty_check_gaze_dot[i] < .995f || manifest.empty_check_inside_gaze_dot[i] < .90f ||
                    manifest.empty_check_opening_forward_clearance_m[i] < .22f ||
                    i % 2 == 0 && (manifest.empty_check_opening_eye_height_m[i] < -.04f || manifest.empty_check_opening_eye_height_m[i] > .10f))
                    throw new InvalidOperationException("The hero must hold the opening at eye level and look into it.");
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != PlayerVodkaActions.ClipNames.Length)
                throw new InvalidOperationException("Vodka action count differs.");
            foreach (AnimationClip clip in clips)
            {
                if (!PlayerVodkaActions.ClipNames.Contains(clip.name) || clip.events.Length != 0 ||
                    clip.isLooping != PlayerVodkaActions.IsLoop(clip.name) ||
                    Mathf.Abs(clip.length - PlayerVodkaActions.Duration(clip.name)) > .003f)
                    throw new InvalidOperationException("Invalid imported vodka action " + clip.name);
                foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!string.IsNullOrEmpty(curve.path) && !curve.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) &&
                        !curve.path.EndsWith("RIG_Player", StringComparison.Ordinal)) continue;
                    AnimationCurve values = AnimationUtility.GetEditorCurve(clip, curve);
                    if (values != null && values.keys.Length > 1 && values.keys.Max(k => k.value) - values.keys.Min(k => k.value) > .00001f)
                        throw new InvalidOperationException("Vodka action has object/root motion.");
                }
            }
        }
        private static bool ValidError(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value <= .00002f;
        [Serializable] private sealed class Manifest
        {
            public string generator, rig;
            public int bone_count, fps, animation_events, empty_check_shake_count;
            public bool root_motion;
            public float maximum_lower_body_error, maximum_endpoint_error, maximum_drink_wrist_error_m, maximum_close_reverse_error;
            public float maximum_draw_neck_contact_error_m, minimum_left_arm_body_clearance_m, draw_transfer_progress, cap_open_progress;
            public float minimum_right_arm_body_clearance_m;
            public float empty_check_tilt_degrees, throw_windup_progress, throw_windup_speed_mps, throw_release_speed_mps, throw_shoulder_sweep_degrees;
            public float[] draw_neck_contact_source, empty_check_markers, empty_check_shake_travel_m;
            public float[] empty_check_opening_eye_height_m, empty_check_gaze_dot, empty_check_opening_forward_clearance_m;
            public float[] empty_check_inside_gaze_dot;
        }
    }

    public sealed class PlayerVodkaActionImporter : AssetPostprocessor
    {
        private bool IsBank => string.Equals(assetPath, PlayerVodkaActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);
        private void OnPreprocessModel()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importAnimation = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }
        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                clip.loopTime = PlayerVodkaActions.IsLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
