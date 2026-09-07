using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Small independent action bank; the production hero prefab is never rewritten.</summary>
    public static class HomeShowerCurtainActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/HomeShowerCurtainActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/HomeShowerCurtainActions.json";

        [MenuItem("Bar Promenade/Player 3D/Validate Shower Curtain Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != 2)
            {
                throw new InvalidOperationException("Shower curtain requires exactly two authored clips.");
            }

            foreach (string name in new[] { HomeShowerCurtainPose.OutsideClipName, HomeShowerCurtainPose.InsideClipName })
            {
                AnimationClip clip = clips.SingleOrDefault(candidate => candidate.name == name);
                if (clip == null || clip.isLooping || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - HomeShowerCurtainPose.DurationSeconds) > 0.003f)
                {
                    throw new InvalidOperationException("Invalid shower curtain action: " + name);
                }

                foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
                {
                    if (string.IsNullOrEmpty(curve.path) ||
                        curve.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) ||
                        curve.path.EndsWith("RIG_Player", StringComparison.Ordinal) ||
                        curve.path.EndsWith("/root", StringComparison.Ordinal))
                    {
                        AnimationCurve values = AnimationUtility.GetEditorCurve(clip, curve);
                        if (values != null && values.keys.Length > 1 &&
                            values.keys.Max(key => key.value) - values.keys.Min(key => key.value) > 0.00001f)
                        {
                            throw new InvalidOperationException("Curtain action contains root motion: " + curve.path);
                        }
                    }
                }
            }

            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.rig != "HeroV2" || manifest.bone_count != 31 ||
                manifest.root_motion || manifest.animation_events != 0 ||
                manifest.maximum_grip_error > 0.0002f || manifest.maximum_endpoint_error > 0.00001f ||
                manifest.duration != HomeShowerCurtainPose.DurationSeconds ||
                manifest.reach_end != HomeShowerCurtainPose.ReachEnd ||
                manifest.release_start != HomeShowerCurtainPose.ReleaseStart ||
                manifest.grip_above_root != HomeShowerCurtainPose.GripHeightAboveRoot)
            {
                throw new InvalidOperationException("Shower curtain manifest does not match the runtime action.");
            }

            ValidateImportedNeutral(clips);
        }

        private static void ValidateImportedNeutral(AnimationClip[] clips)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (prefab == null)
            {
                throw new InvalidOperationException("Curtain actions require the production hero prefab.");
            }

            GameObject sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Player3DAssetRegistry registry = sample.GetComponentInChildren<Player3DAssetRegistry>();
                registry.Animator.enabled = false;
                if (!registry.TryGetAnimation("Idle", out Player3DAnimationBinding idle))
                {
                    throw new InvalidOperationException("Production Idle endpoint is missing.");
                }

                idle.Clip.SampleAnimation(registry.Animator.gameObject, 0f);
                Transform[] bones = registry.Animator.GetComponentsInChildren<Transform>(true);
                Vector3[] positions = bones.Select(bone => bone.localPosition).ToArray();
                Quaternion[] rotations = bones.Select(bone => bone.localRotation).ToArray();
                foreach (AnimationClip clip in clips)
                {
                    foreach (float normalized in new[] { 0f, 1f })
                    {
                        clip.SampleAnimation(registry.Animator.gameObject, normalized * clip.length);
                        for (int index = 0; index < bones.Length; index++)
                        {
                            if (Vector3.Distance(bones[index].localPosition, positions[index]) > 0.0001f ||
                                Quaternion.Angle(bones[index].localRotation, rotations[index]) > 0.03f)
                            {
                                throw new InvalidOperationException("Curtain neutral endpoint differs from Idle: " + clip.name + "/" + bones[index].name);
                            }
                        }
                    }

                    clip.SampleAnimation(registry.Animator.gameObject, clip.length * 0.5f);
                    float torsoSpan = Vector3.Distance(registry.Anchors.Head.position, registry.Anchors.Pelvis.position);
                    if (torsoSpan < 0.25f || torsoSpan > 1.1f)
                    {
                        throw new InvalidOperationException("Curtain clip changed the imported metre scale.");
                    }
                }

                foreach (AnimationClip clip in clips)
                {
                    bool inside = clip.name == HomeShowerCurtainPose.InsideClipName;
                    sample.transform.position = (inside ? HomeShowerCurtainPose.InsideDock : HomeShowerCurtainPose.OutsideDock)
                        + Vector3.up * PlayerFactory.GroundedRootOffset;
                    sample.transform.rotation = inside ? HomeShowerCurtainPose.InsideFacing : HomeShowerCurtainPose.OutsideFacing;
                    foreach (float normalized in new[] { 0.25f, 0.5f, 0.75f })
                    {
                        clip.SampleAnimation(registry.Animator.gameObject, normalized * clip.length);
                        float opening = HomeShowerCurtainPose.PullProgress(normalized);
                        if (inside)
                        {
                            opening = 1f - opening;
                        }

                        Vector3 expected = new Vector3(Mathf.Lerp(4.515f, 3.846f, opening),
                            sample.transform.position.y + HomeShowerCurtainPose.GripHeightAboveRoot, 2.396f);
                        Transform hand = inside ? registry.Anchors.LeftGrip : registry.Anchors.RightGrip;
                        if (Vector3.Distance(hand.position, expected) > 0.015f)
                        {
                            throw new InvalidOperationException("Imported curtain grip misses the authored hem: " + clip.name);
                        }
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
            public float duration;
            public float reach_end;
            public float release_start;
            public float grip_above_root;
            public float maximum_grip_error;
            public float maximum_endpoint_error;
        }
    }

    public sealed class HomeShowerCurtainActionImporter : AssetPostprocessor
    {
        private bool IsCurtainBank => string.Equals(assetPath,
            HomeShowerCurtainActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);

        private void OnPreprocessModel()
        {
            if (!IsCurtainBank || !(assetImporter is ModelImporter importer))
            {
                return;
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsCurtainBank || !(assetImporter is ModelImporter importer))
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

                clip.loopTime = false;
                clip.loopPose = false;
            }

            importer.clipAnimations = clips;
        }
    }
}
