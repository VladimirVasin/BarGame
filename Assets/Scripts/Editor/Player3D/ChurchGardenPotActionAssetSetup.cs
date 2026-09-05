using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Independent five-clip import; never rewrites the production hero bank.</summary>
    public static class ChurchGardenPotActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/ChurchGardenPotActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/ChurchGardenPotActions.json";

        [MenuItem("Bar Promenade/Player 3D/Validate Church Garden Pot Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != ChurchGardenPotActions.RequiredClipNames.Length)
            {
                throw new InvalidOperationException("Garden pot requires exactly five authored clips.");
            }

            foreach (string name in ChurchGardenPotActions.RequiredClipNames)
            {
                AnimationClip clip = clips.SingleOrDefault(candidate => candidate.name == name);
                bool looping = name == "ChurchPotInspectLoop";
                if (clip == null || clip.isLooping != looping || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - (looping ? 5f : 3f)) > 0.003f)
                {
                    throw new InvalidOperationException("Invalid church garden action: " + name);
                }

                foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
                {
                    if (string.IsNullOrEmpty(curve.path) ||
                        curve.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) ||
                        curve.path.EndsWith("RIG_Player", StringComparison.Ordinal))
                    {
                        // FBX can retain constant authoring-axis channels on
                        // the root; none may move the physical gameplay root.
                        AnimationCurve values = AnimationUtility.GetEditorCurve(clip, curve);
                        if (values != null && values.keys.Length > 1 &&
                            values.keys.Max(key => key.value) - values.keys.Min(key => key.value) > 0.00001f)
                        {
                            throw new InvalidOperationException("Garden pot action contains root motion: " + curve.path);
                        }
                    }
                }
            }

            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.rig != "HeroV2" || manifest.bone_count != 31 ||
                manifest.root_motion || manifest.animation_events != 0 ||
                manifest.maximum_grip_error > 0.0002f ||
                manifest.transfer_contact_progress != ChurchGardenPotPlan.ContactProgress ||
                Mathf.Abs(manifest.pot_grip_height - ChurchGardenPotPlan.GripHeight) > 0.00001f)
            {
                throw new InvalidOperationException("Garden pot action manifest does not match measured contact.");
            }

            ValidateImportedPoses(clips);
        }

        private static void ValidateImportedPoses(AnimationClip[] clips)
        {
            const string playerPath = "Assets/Resources/Player/Player3DV2.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
            if (prefab == null)
            {
                throw new InvalidOperationException("The garden bank requires the production hero prefab.");
            }

            GameObject sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Player3DAssetRegistry registry = sample.GetComponentInChildren<Player3DAssetRegistry>();
                registry.Animator.enabled = false;
                foreach (AnimationClip clip in clips)
                {
                    // Every transfer is in exact two-hand contact halfway
                    // through; the loop is held throughout. Sampling the real
                    // imported rig detects ignored FBX units and path drift.
                    clip.SampleAnimation(registry.Animator.gameObject, clip.length * 0.5f);
                    Player3DBoneAnchors anchors = registry.Anchors;
                    float rimSpan = Vector3.Distance(anchors.LeftGrip.position, anchors.RightGrip.position);
                    float torsoSpan = Vector3.Distance(anchors.Head.position, anchors.Pelvis.position);
                    float footSpan = Vector3.Distance(anchors.LeftFoot.position, anchors.RightFoot.position);
                    if (Mathf.Abs(rimSpan - 2f * ChurchGardenPotPlan.GripRadius) > 0.02f ||
                        torsoSpan < 0.25f || torsoSpan > 1.1f || footSpan > 0.35f)
                    {
                        throw new InvalidOperationException(
                            $"Garden action {clip.name} changed imported metre scale or hand contact: " +
                            $"rim {rimSpan:F4} m, torso {torsoSpan:F4} m, feet {footSpan:F4} m.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sample);
            }
        }

        [Serializable]
        private sealed class Manifest
        {
            public string rig;
            public int bone_count;
            public bool root_motion;
            public int animation_events;
            public float maximum_grip_error;
            public float transfer_contact_progress;
            public float pot_grip_height;
        }
    }

    public sealed class ChurchGardenPotActionImporter : AssetPostprocessor
    {
        private bool IsGardenBank => string.Equals(assetPath,
            ChurchGardenPotActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);

        private void OnPreprocessModel()
        {
            if (!IsGardenBank || !(assetImporter is ModelImporter importer))
            {
                return;
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.globalScale = 1f;
            // Match the production bank's explicit FBX-unit convention. New
            // minimal .meta files otherwise default this to false and import
            // bone translations at a hundred times the model's scale.
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath)
                .OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther
                : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsGardenBank || !(assetImporter is ModelImporter importer))
            {
                return;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0)
                {
                    clip.name = clip.name.Substring(separator + 1);
                }
                clip.loopTime = clip.name == "ChurchPotInspectLoop";
                clip.loopPose = clip.loopTime;
            }
            importer.clipAnimations = clips;
        }
    }
}
