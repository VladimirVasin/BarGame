using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class VillageOutdoorPlayerActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/VillageOutdoorPlayerActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/VillageOutdoorPlayerActions.json";

        [MenuItem("Bar Promenade/Player 3D/Validate Village Outdoor Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            Array actions = Enum.GetValues(typeof(VillageOutdoorPlayerAction));
            if (clips.Length != actions.Length) throw new InvalidOperationException("Outdoor hero clip count differs.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.generator != "village_outdoor_player_v1" || manifest.rig != "HeroV2" ||
                manifest.bone_count != 31 || manifest.root_motion || manifest.animation_events != 0 ||
                !ValidError(manifest.maximum_grip_error, .0002f) ||
                !ValidError(manifest.maximum_lower_body_error, .00001f) ||
                !ValidError(manifest.maximum_endpoint_error, .00001f))
                throw new InvalidOperationException("Outdoor source rig, feet or grip validation failed.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (prefab == null) throw new InvalidOperationException("Production hero is missing.");
            GameObject sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                sample.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Player3DAssetRegistry registry = sample.GetComponentInChildren<Player3DAssetRegistry>();
                registry.Animator.enabled = false;
                foreach (VillageOutdoorPlayerAction action in actions)
                {
                    string name = VillageOutdoorPlayerActions.ClipName(action);
                    AnimationClip clip = clips.SingleOrDefault(value => value.name == name);
                    float duration = VillageOutdoorPlayerActions.Duration(action);
                    if (clip == null || clip.isLooping != VillageOutdoorPlayerActions.IsLoop(action) ||
                        clip.events.Length != 0 || Mathf.Abs(clip.length - duration) > .003f)
                        throw new InvalidOperationException("Invalid outdoor action " + name);
                    foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
                        if (string.IsNullOrEmpty(curve.path) || curve.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) ||
                            curve.path.EndsWith("RIG_Player", StringComparison.Ordinal))
                        {
                            AnimationCurve values = AnimationUtility.GetEditorCurve(clip, curve);
                            if (values != null && values.keys.Length > 1 &&
                                values.keys.Max(key => key.value) - values.keys.Min(key => key.value) > .00001f)
                                throw new InvalidOperationException("Outdoor action contains object/root motion: " + curve.path);
                        }
                    for (int frame = 0; frame <= Mathf.RoundToInt(duration * 24f); frame += 6)
                    {
                        float time = frame / 24f;
                        clip.SampleAnimation(registry.Animator.gameObject, time);
                        registry.ModelRoot.position += VillageOutdoorPlayerActions.ActionPelvisFromGround - registry.Anchors.Pelvis.position;
                        VillageOutdoorPlayerFrame expected = VillageOutdoorPlayerActions.Sample(action, time);
                        if (expected.LeftContactWeight >= .9999f &&
                            Vector3.Distance(registry.Anchors.LeftGrip.position, expected.LeftGripFromGround) > .015f)
                            throw new InvalidOperationException($"Outdoor {name}@{time:F3}: imported left grip differs in metres.");
                        if (expected.RightContactWeight >= .9999f &&
                            Vector3.Distance(registry.Anchors.RightGrip.position, expected.RightGripFromGround) > .015f)
                            throw new InvalidOperationException($"Outdoor {name}@{time:F3}: imported right grip differs in metres.");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(sample); }
        }
        private static bool ValidError(float value, float maximum) => !float.IsNaN(value) && !float.IsInfinity(value) && value <= maximum;
        [Serializable] private sealed class Manifest
        {
            public string generator, rig; public int bone_count, animation_events; public bool root_motion;
            public float maximum_grip_error, maximum_lower_body_error, maximum_endpoint_error;
        }
    }

    public sealed class VillageOutdoorPlayerActionImporter : AssetPostprocessor
    {
        private bool IsBank => string.Equals(assetPath, VillageOutdoorPlayerActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);
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
                clip.loopTime = clip.name.EndsWith("Carry", StringComparison.Ordinal) ||
                    clip.name.EndsWith("Hold", StringComparison.Ordinal) || clip.name == "VillageShovelWork";
                clip.loopPose = clip.loopTime;
            }
            importer.clipAnimations = clips;
        }
    }
}
