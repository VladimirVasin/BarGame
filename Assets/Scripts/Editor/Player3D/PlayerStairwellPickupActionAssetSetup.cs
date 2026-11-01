using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class PlayerStairwellPickupActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/PlayerStairwellPickupActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/PlayerStairwellPickupActions.json";

        [MenuItem("Bar Promenade/Player 3D/Validate Stairwell Pickup Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != PlayerStairwellPickupActions.ClipNames.Count)
                throw new InvalidOperationException("Stairwell pickup action count differs.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            GameObject sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Player3DAssetRegistry registry = sample.GetComponentInChildren<Player3DAssetRegistry>();
                registry.Animator.enabled = false;
                if (!PlayerStairwellPickupActions.TryAttach(registry))
                    throw new InvalidOperationException("Pickup bank could not attach to the production hero.");
                Dictionary<string, PoseScale> Snapshot(string name, float progress)
                {
                    if (!registry.TryGetAnimation(name, out Player3DAnimationBinding binding))
                        throw new InvalidOperationException("Missing pickup clip " + name);
                    binding.Clip.SampleAnimation(registry.Animator.gameObject, binding.Clip.length * progress);
                    return registry.Animator.GetComponentsInChildren<Transform>(true)
                        .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, registry.Animator.transform), t => new PoseScale(t));
                }
                Dictionary<string, PoseScale> neutral = Snapshot("Relaxed", 0f);
                foreach (AnimationClip clip in clips)
                {
                    if (!PlayerStairwellPickupActions.ClipNames.Contains(clip.name) ||
                        clip.isLooping != PlayerStairwellPickupActions.IsLoop(clip.name) || clip.events.Length != 0 ||
                        Mathf.Abs(clip.length - PlayerStairwellPickupActions.Duration(clip.name)) > .003f)
                        throw new InvalidOperationException("Invalid imported pickup action " + clip.name);
                    Snapshot(clip.name, 0f);
                    Vector3 left = registry.Anchors.LeftFoot.position, right = registry.Anchors.RightFoot.position;
                    for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * 24f); frame++)
                    {
                        clip.SampleAnimation(registry.Animator.gameObject, frame / 24f);
                        if (Vector3.Distance(left, registry.Anchors.LeftFoot.position) > .001f ||
                            Vector3.Distance(right, registry.Anchors.RightFoot.position) > .001f)
                            throw new InvalidOperationException("Pickup unplanted an imported foot: " + clip.name);
                    }
                    foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!string.IsNullOrEmpty(curve.path) && !curve.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) &&
                            !curve.path.EndsWith("RIG_Player", StringComparison.Ordinal)) continue;
                        AnimationCurve values = AnimationUtility.GetEditorCurve(clip, curve);
                        if (values != null && values.keys.Length > 1 && values.keys.Max(k => k.value) - values.keys.Min(k => k.value) > .00001f)
                            throw new InvalidOperationException("Pickup contains object/root motion: " + curve.path);
                    }
                }
                foreach (InventoryItemId item in new[] { InventoryItemId.ChestFlashlight, InventoryItemId.CombatCrowbar, InventoryItemId.CombatPistol })
                {
                    string prefix = PlayerStairwellPickupActions.Prefix(item);
                    Compare(neutral, Snapshot(prefix + "Enter", 0f), "ordinary entry");
                    Compare(Snapshot(prefix + "Enter", 1f), Snapshot(prefix + "Loop", 0f), "enter -> loop");
                    Compare(Snapshot(prefix + "Loop", 0f), Snapshot(prefix + "Loop", 1f), "loop closure");
                    Compare(Snapshot(prefix + "Loop", 1f), Snapshot(prefix + "Exit", 0f), "loop -> exit");
                    Compare(neutral, Snapshot(prefix + "Exit", 1f), "ordinary exit");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(sample); }
        }
        private static void Compare(Dictionary<string, PoseScale> a, Dictionary<string, PoseScale> b, string seam)
        {
            foreach (KeyValuePair<string, PoseScale> pair in a)
                if (!b.TryGetValue(pair.Key, out PoseScale value) || Vector3.Distance(pair.Value.Position, value.Position) > .0001f ||
                    Quaternion.Angle(pair.Value.Rotation, value.Rotation) > .03f || Vector3.Distance(pair.Value.Scale, value.Scale) > .0001f)
                    throw new InvalidOperationException("Pickup imported endpoint mismatch: " + seam + "/" + pair.Key);
        }
        private readonly struct PoseScale
        {
            public readonly Vector3 Position, Scale;
            public readonly Quaternion Rotation;
            public PoseScale(Transform source) { Position = source.localPosition; Scale = source.localScale; Rotation = source.localRotation; }
        }
    }
    public sealed class PlayerStairwellPickupActionImporter : AssetPostprocessor
    {
        private bool IsBank => string.Equals(assetPath, PlayerStairwellPickupActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);
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
                clip.loopTime = PlayerStairwellPickupActions.IsLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
