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
                !ValidError(manifest.maximum_draw_neck_contact_error_m) ||
                manifest.minimum_left_arm_body_clearance_m < .02f ||
                manifest.minimum_right_arm_body_clearance_m < .02f ||
                manifest.draw_neck_contact_source == null || manifest.draw_neck_contact_source.Length != 3 ||
                Vector3.Distance(new Vector3(manifest.draw_neck_contact_source[0], manifest.draw_neck_contact_source[1],
                    manifest.draw_neck_contact_source[2]), PlayerVodkaActions.DrawNeckSource) > .00001f ||
                Mathf.Abs(manifest.draw_transfer_progress - PlayerVodkaActions.DrawTransferProgress) > .00001f ||
                Mathf.Abs(manifest.cap_open_progress - PlayerVodkaActions.CapOpenProgress) > .00001f)
                throw new InvalidOperationException("Vodka action source violated its rig, support or endpoint contract.");
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
            public int bone_count, fps, animation_events;
            public bool root_motion;
            public float maximum_lower_body_error, maximum_endpoint_error, maximum_drink_wrist_error_m;
            public float maximum_draw_neck_contact_error_m, minimum_left_arm_body_clearance_m, draw_transfer_progress, cap_open_progress;
            public float minimum_right_arm_body_clearance_m;
            public float[] draw_neck_contact_source;
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
