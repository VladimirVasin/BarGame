using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports the independent toilet bank without rebuilding the production hero.</summary>
    public static class HomeToiletSeatedActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/HomeToiletSeatedActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/HomeToiletSeatedActions.json";

        [MenuItem("Bar Promenade/Player 3D/Validate Toilet Seated Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != HomeToiletActorPresentation.ClipNames.Length)
                throw new InvalidOperationException("The toilet action bank is incomplete.");
            for (int index = 0; index < HomeToiletActorPresentation.ClipNames.Length; index++)
            {
                string name = HomeToiletActorPresentation.ClipNames[index];
                AnimationClip clip = clips.SingleOrDefault(value => value.name == name);
                if (clip == null || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - HomeToiletActorPresentation.Duration((HomeToiletActorPhase)index)) > .003f ||
                    clip.isLooping != (index == (int)HomeToiletActorPhase.Seated))
                    throw new InvalidOperationException("Invalid toilet action: " + name);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!string.IsNullOrEmpty(binding.path) && !binding.path.EndsWith("/root", StringComparison.Ordinal) &&
                        !binding.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) &&
                        !binding.path.EndsWith("RIG_Player", StringComparison.Ordinal)) continue;
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.keys.Length > 1 &&
                        curve.keys.Max(key => key.value) - curve.keys.Min(key => key.value) > .00001f)
                        throw new InvalidOperationException("Toilet action contains gameplay root motion: " + name);
                }
            }
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.rig != "HeroV2" || manifest.bone_count < 31 ||
                manifest.root_motion || manifest.animation_events != 0 ||
                manifest.maximum_grip_error > .0003f || manifest.maximum_endpoint_error > .00002f ||
                manifest.maximum_flush_grip_error > .0003f || manifest.minimum_inspect_gaze_alignment < .999f ||
                manifest.minimum_dress_camera_clearance < .01f || manifest.flush_lid_clearance_samples < 61 ||
                Mathf.Abs(manifest.flush_cue_seconds - HomeToiletActorPresentation.FlushCueSeconds) > .0001f ||
                manifest.minimum_foot_anchor_height < .091f)
                throw new InvalidOperationException("The toilet action manifest failed contact/endpoint validation.");
            ValidateEndpoints(clips);
        }

        private static void ValidateEndpoints(AnimationClip[] clips)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (prefab == null) throw new InvalidOperationException("Toilet actions require the production hero.");
            GameObject sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Player3DAssetRegistry registry = sample.GetComponentInChildren<Player3DAssetRegistry>();
                registry.Animator.enabled = false;
                if (!registry.TryGetAnimation("Idle", out Player3DAnimationBinding idle))
                    throw new InvalidOperationException("Production Idle is missing.");
                Transform[] bones = registry.Animator.GetComponentsInChildren<Transform>(true);
                foreach (string name in new[] { "ToiletOpenLid", "ToiletPrepare", "ToiletDress", "ToiletCloseLid" })
                    foreach (float endpoint in new[] { 0f, 1f })
                        Compare(idle.Clip, 0f, clips.Single(clip => clip.name == name), endpoint, registry, bones);
                string[] chain = { "ToiletPrepare", "ToiletSit", "ToiletSeated", "ToiletRise", "ToiletDress" };
                for (int index = 0; index < chain.Length - 1; index++)
                    Compare(clips.Single(clip => clip.name == chain[index]), 1f,
                        clips.Single(clip => clip.name == chain[index + 1]), 0f, registry, bones);
                Compare(idle.Clip, 0f, clips.Single(clip => clip.name == "ToiletInspect"), 0f, registry, bones);
                Compare(clips.Single(clip => clip.name == "ToiletInspect"), 1f,
                    clips.Single(clip => clip.name == "ToiletFlush"), 0f, registry, bones);
                Compare(clips.Single(clip => clip.name == "ToiletFlush"), 1f,
                    clips.Single(clip => clip.name == "ToiletDress"), 0f, registry, bones);
                sample.transform.SetPositionAndRotation(new Vector3(3.32f, PlayerFactory.GroundedRootOffset, 1.40f),
                    Quaternion.LookRotation(Vector3.right));
                AnimationClip flush = clips.Single(clip => clip.name == "ToiletFlush");
                foreach (float seconds in new[] { 1f, HomeToiletActorPresentation.FlushCueSeconds, 1.625f })
                {
                    float normalized = seconds / HomeToiletActorPresentation.FlushDurationSeconds;
                    flush.SampleAnimation(registry.Animator.gameObject, seconds);
                    Vector3 top = new Vector3(4.49f, 1.165f - HomeToiletActorPresentation.FlushPressDepth *
                        HomeToiletActorPresentation.FlushPressProgress(normalized), 1.60f);
                    if (Vector3.Distance(registry.Anchors.RightGrip.position, top) > .015f)
                        throw new InvalidOperationException("Imported toilet hand misses the flush button.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(sample); }
        }

        private static void Compare(AnimationClip first, float firstTime, AnimationClip second, float secondTime,
            Player3DAssetRegistry registry, Transform[] bones)
        {
            first.SampleAnimation(registry.Animator.gameObject, firstTime * first.length);
            Vector3[] positions = bones.Select(bone => bone.localPosition).ToArray();
            Quaternion[] rotations = bones.Select(bone => bone.localRotation).ToArray();
            second.SampleAnimation(registry.Animator.gameObject, secondTime * second.length);
            for (int index = 0; index < bones.Length; index++)
                if (Vector3.Distance(positions[index], bones[index].localPosition) > .0001f ||
                    Quaternion.Angle(rotations[index], bones[index].localRotation) > .03f)
                    throw new InvalidOperationException("Toilet action endpoint mismatch: " + first.name + " -> " + second.name + "/" + bones[index].name);
        }

        [Serializable] private sealed class Manifest
        {
            public string rig;
            public int bone_count, animation_events, flush_lid_clearance_samples;
            public bool root_motion;
            public float maximum_grip_error, maximum_endpoint_error, minimum_foot_anchor_height;
            public float maximum_flush_grip_error, minimum_inspect_gaze_alignment, minimum_dress_camera_clearance, flush_cue_seconds;
        }
    }

    public sealed class HomeToiletSeatedActionImporter : AssetPostprocessor
    {
        private bool IsToiletBank => string.Equals(assetPath, HomeToiletSeatedActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);
        private void OnPreprocessModel()
        {
            if (!IsToiletBank || !(assetImporter is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsToiletBank || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                clip.loopTime = clip.name == "ToiletSeated";
                clip.loopPose = false;
            }
            importer.clipAnimations = clips;
        }
    }
}
