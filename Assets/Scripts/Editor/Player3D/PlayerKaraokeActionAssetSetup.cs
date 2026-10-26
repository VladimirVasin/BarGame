using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class PlayerKaraokeActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/PlayerKaraokeActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/PlayerKaraokeActions.json";

        [MenuItem("Bar Promenade/Player 3D/Validate Karaoke Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.generator != "player_karaoke_v1" || manifest.rig != "HeroV2" ||
                manifest.bone_count != 43 || manifest.fps != 24 || manifest.root_motion || manifest.lip_sync ||
                manifest.animation_events != 0 || !ValidError(manifest.maximum_lower_body_error) ||
                !ValidError(manifest.maximum_endpoint_error) || !ValidError(manifest.maximum_neutral_error) ||
                !ValidError(manifest.maximum_mouth_motion) || !float.IsFinite(manifest.breathing_chest_travel_degrees) ||
                manifest.breathing_chest_travel_degrees <= .5f || manifest.breathing_chest_travel_degrees >= 2f)
                throw new InvalidOperationException("Karaoke source violated its rig, support, breathing or endpoint contract.");
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != PlayerKaraokeActions.ClipNames.Count)
                throw new InvalidOperationException("Karaoke action count differs.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            GameObject sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Player3DAssetRegistry registry = sample.GetComponentInChildren<Player3DAssetRegistry>();
                registry.Animator.enabled = false;
                if (!PlayerKaraokeActions.TryAttach(registry))
                    throw new InvalidOperationException("Karaoke bank could not attach to the production hero.");
                Dictionary<string, PoseScale> Snapshot(string name, float normalized)
                {
                    if (!registry.TryGetAnimation(name, out Player3DAnimationBinding binding))
                        throw new InvalidOperationException("Missing imported karaoke clip " + name);
                    binding.Clip.SampleAnimation(registry.Animator.gameObject, binding.Clip.length * normalized);
                    return registry.Animator.GetComponentsInChildren<Transform>(true)
                        .ToDictionary(value => AnimationUtility.CalculateTransformPath(value, registry.Animator.transform),
                            value => new PoseScale(value));
                }
                Dictionary<string, PoseScale> neutral = Snapshot("Relaxed", 0f);
                Compare(neutral, Snapshot(PlayerKaraokeActions.EnterClip, 0f), "ordinary entry");
                foreach (AnimationClip clip in clips)
                {
                    if (!PlayerKaraokeActions.ClipNames.Contains(clip.name) ||
                        clip.isLooping != PlayerKaraokeActions.IsLoop(clip.name) || clip.events.Length != 0 ||
                        Mathf.Abs(clip.length - PlayerKaraokeActions.Duration(clip.name)) > .003f)
                        throw new InvalidOperationException("Invalid imported karaoke action " + clip.name);
                    foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!string.IsNullOrEmpty(curve.path) && !curve.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) &&
                            !curve.path.EndsWith("RIG_Player", StringComparison.Ordinal)) continue;
                        AnimationCurve values = AnimationUtility.GetEditorCurve(clip, curve);
                        if (values != null && values.keys.Length > 1 && values.keys.Max(k => k.value) - values.keys.Min(k => k.value) > .00001f)
                            throw new InvalidOperationException("Karaoke contains object/root motion: " + curve.path);
                    }
                    Snapshot(clip.name, 0f);
                    Vector3 left = registry.Anchors.LeftFoot.position, right = registry.Anchors.RightFoot.position;
                    Quaternion leftRotation = registry.Anchors.LeftFoot.rotation, rightRotation = registry.Anchors.RightFoot.rotation;
                    for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * 24f); frame++)
                    {
                        clip.SampleAnimation(registry.Animator.gameObject, frame / 24f);
                        if (Vector3.Distance(left, registry.Anchors.LeftFoot.position) > .001f ||
                            Vector3.Distance(right, registry.Anchors.RightFoot.position) > .001f ||
                            Quaternion.Angle(leftRotation, registry.Anchors.LeftFoot.rotation) > .03f ||
                            Quaternion.Angle(rightRotation, registry.Anchors.RightFoot.rotation) > .03f)
                            throw new InvalidOperationException("Karaoke moved an imported foot contact: " + clip.name);
                    }
                }
                Compare(Snapshot(PlayerKaraokeActions.EnterClip, 1f), Snapshot(PlayerKaraokeActions.LoopClip, 0f), "enter -> loop");
                Compare(Snapshot(PlayerKaraokeActions.LoopClip, 0f), Snapshot(PlayerKaraokeActions.LoopClip, 1f), "loop closure");
                Compare(Snapshot(PlayerKaraokeActions.LoopClip, 1f), Snapshot(PlayerKaraokeActions.ExitClip, 0f), "loop -> exit");
                Compare(neutral, Snapshot(PlayerKaraokeActions.ExitClip, 1f), "ordinary exit");
            }
            finally { UnityEngine.Object.DestroyImmediate(sample); }
        }

        private static bool ValidError(float value) => float.IsFinite(value) && value >= 0f && value <= .00001f;
        private static void Compare(Dictionary<string, PoseScale> a, Dictionary<string, PoseScale> b, string seam)
        {
            foreach (KeyValuePair<string, PoseScale> pair in a)
                if (!b.TryGetValue(pair.Key, out PoseScale value) || Vector3.Distance(pair.Value.Position, value.Position) > .0001f ||
                    Quaternion.Angle(pair.Value.Rotation, value.Rotation) > .03f || Vector3.Distance(pair.Value.Scale, value.Scale) > .0001f)
                    throw new InvalidOperationException("Karaoke imported endpoint mismatch: " + seam + "/" + pair.Key);
        }
        private readonly struct PoseScale
        {
            public readonly Vector3 Position, Scale; public readonly Quaternion Rotation;
            public PoseScale(Transform source) { Position = source.localPosition; Scale = source.localScale; Rotation = source.localRotation; }
        }
        [Serializable] private sealed class Manifest
        {
            public string generator, rig; public int bone_count, fps, animation_events; public bool root_motion, lip_sync;
            public float maximum_lower_body_error, maximum_endpoint_error, maximum_neutral_error, maximum_mouth_motion;
            public float breathing_chest_travel_degrees;
        }
    }

    public sealed class PlayerKaraokeActionImporter : AssetPostprocessor
    {
        private bool IsBank => string.Equals(assetPath, PlayerKaraokeActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);
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
                clip.loopTime = PlayerKaraokeActions.IsLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
