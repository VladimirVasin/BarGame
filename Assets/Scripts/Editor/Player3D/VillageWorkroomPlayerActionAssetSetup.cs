using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports only the optional bank and samples a disposable production hero.</summary>
    public static class VillageWorkroomPlayerActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/VillageWorkroomPlayerActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/VillageWorkroomPlayerActions.json";

        [MenuItem("Bar Promenade/Player 3D/Validate Village Workroom Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != 3) throw new InvalidOperationException("Workroom help requires exactly three clips.");
            foreach (string name in VillageWorkroomPlayerActions.RequiredClipNames)
            {
                AnimationClip clip = clips.SingleOrDefault(value => value.name == name);
                bool loop = name == "VillageChairHelpLoop";
                if (clip == null || clip.isLooping != loop || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - (loop ? 6f : 1.5f)) > .003f)
                    throw new InvalidOperationException("Invalid workroom action " + name);
                foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
                    if (string.IsNullOrEmpty(curve.path) || curve.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) ||
                        curve.path.EndsWith("RIG_Player", StringComparison.Ordinal))
                    {
                        AnimationCurve values = AnimationUtility.GetEditorCurve(clip, curve);
                        if (values != null && values.keys.Length > 1 &&
                            values.keys.Max(key => key.value) - values.keys.Min(key => key.value) > .00001f)
                            throw new InvalidOperationException("Workroom bank contains object/root motion: " + curve.path);
                    }
            }
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.generator != "village_workroom_player_v1" || manifest.rig != "HeroV2" ||
                manifest.bone_count != 31 || manifest.root_motion || manifest.animation_events != 0 ||
                float.IsNaN(manifest.maximum_grip_error) || manifest.maximum_grip_error > .0002f)
                throw new InvalidOperationException("Workroom manifest does not match the production rig/contact contract.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (prefab == null) throw new InvalidOperationException("Production hero is missing.");
            GameObject sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Player3DAssetRegistry registry = sample.GetComponentInChildren<Player3DAssetRegistry>();
                registry.Animator.enabled = false;
                foreach (AnimationClip clip in clips)
                {
                    float t = clip.name.EndsWith("Enter", StringComparison.Ordinal) ? clip.length : 0f;
                    clip.SampleAnimation(registry.Animator.gameObject, t);
                    float span = Vector3.Distance(registry.Anchors.LeftGrip.position, registry.Anchors.RightGrip.position);
                    if (Mathf.Abs(span - .44f) > .01f)
                        throw new InvalidOperationException($"Workroom action {clip.name}: imported hand span {span:F5} m.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(sample); }
        }

        [Serializable] private sealed class Manifest
        {
            public string generator, rig;
            public int bone_count, animation_events;
            public bool root_motion;
            public float maximum_grip_error;
        }
    }

    public sealed class VillageWorkroomPlayerActionImporter : AssetPostprocessor
    {
        private bool IsBank => string.Equals(assetPath, VillageWorkroomPlayerActionAssetSetup.AnimationPath,
            StringComparison.OrdinalIgnoreCase);
        private void OnPreprocessModel()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importAnimation = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
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
                clip.loopTime = clip.name == "VillageChairHelpLoop";
                clip.loopPose = clip.loopTime;
            }
            importer.clipAnimations = clips;
        }
    }
}
