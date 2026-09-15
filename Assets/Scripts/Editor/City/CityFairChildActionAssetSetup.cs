using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class CityFairChildActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/City/FairChild/ChildActions.fbx";
        public const string ManifestPath = "Assets/Resources/City/FairChild/ChildActions.json";

        [MenuItem("Bar Promenade/City Fair/Validate Child Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }
        public static void ValidateOrThrow()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null) throw new InvalidOperationException("Child action JSON did not deserialize: " + ManifestPath);
            var errors = new List<string>();
            void Check(bool accepted, string field, object value, object expected)
            { if (!accepted) errors.Add($"{field}: actual={value ?? "<missing>"}, expected={expected}"); }
            Check(manifest.generator == "city_fair_child_actions_v1", "generator", manifest.generator, "city_fair_child_actions_v1");
            Check(manifest.rig == "FairChild", "rig", manifest.rig, "FairChild");
            Check(manifest.fps == 24, "fps", manifest.fps, 24);
            Check(Mathf.Abs(manifest.height - 1.30f) < .001f, "height", manifest.height, 1.30f);
            Check(!manifest.root_motion, "root_motion", manifest.root_motion, false);
            Check(manifest.animation_events == 0, "animation_events", manifest.animation_events, 0);
            Check(manifest.bones != null && manifest.bones.Length == manifest.bone_count, "bone_count", manifest.bone_count, manifest.bones?.Length);
            Check(manifest.tracks != null && manifest.tracks.Length == Enum.GetValues(typeof(CityFairChildAction)).Length,
                "tracks.Length", manifest.tracks?.Length, Enum.GetValues(typeof(CityFairChildAction)).Length);
            Check(Within(manifest.maximum_grip_error, .0002f), "maximum_grip_error", manifest.maximum_grip_error, "finite [0,.0002]");
            Check(Within(manifest.maximum_endpoint_error, .0001f), "maximum_endpoint_error", manifest.maximum_endpoint_error, "finite [0,.0001]");
            Check(Within(manifest.maximum_lower_body_error, .00001f), "maximum_lower_body_error", manifest.maximum_lower_body_error, "finite [0,.00001]");
            if (errors.Count > 0) throw new InvalidOperationException("Child action manifest:\n" + string.Join("\n", errors));
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != manifest.tracks.Length)
                throw new InvalidOperationException($"Child action imported clip count={clips.Length}, expected={manifest.tracks.Length}; {string.Join(",", clips.Select(clip => clip.name))}");
            GameObject actor = CityFairChildAssetProvider.Create(0, null);
            try
            {
                Animator animator = actor.GetComponentInChildren<Animator>(true);
                if (animator == null) throw new InvalidOperationException("The actual child model has no Generic animator.");
                animator.enabled = false;
                var bones = actor.GetComponentsInChildren<Transform>(true).GroupBy(value => value.name).ToDictionary(group => group.Key, group => group.First());
                string[] missing = manifest.bones.Where(name => !bones.ContainsKey(name)).ToArray();
                if (missing.Length != 0) throw new InvalidOperationException("The action/model child rig differs; missing " + string.Join(",", missing));
                float maxImported = 0f;
                foreach (Track track in manifest.tracks)
                {
                    if (!Enum.TryParse(track.name, out CityFairChildAction action) || track.clip != CityFairChildActions.ClipName(action))
                        throw new InvalidOperationException($"Child track name/clip mismatch: {track.name}/{track.clip}");
                    AnimationClip clip = clips.SingleOrDefault(value => value.name == track.clip);
                    if (clip == null || clip.isLooping != track.loop || clip.events.Length != 0 ||
                        Mathf.Abs(clip.length - track.duration_seconds) > .003f || track.frames == null ||
                        track.frames.Length != Mathf.RoundToInt(track.duration_seconds * 24f) + 1)
                        throw new InvalidOperationException($"Child track {track.name}: clip={clip?.name}, length={clip?.length}, loop={clip?.isLooping}, frames={track.frames?.Length}; " +
                            $"expected duration={track.duration_seconds}, loop={track.loop}, frames={Mathf.RoundToInt(track.duration_seconds * 24f) + 1}.");
                    foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!string.IsNullOrEmpty(binding.path) && binding.path != "FairChild" && !binding.path.EndsWith("RIG_FairChild", StringComparison.Ordinal)) continue;
                        AnimationCurve values = AnimationUtility.GetEditorCurve(clip, binding);
                        if (values != null && values.keys.Length > 1 && values.keys.Max(key => key.value) - values.keys.Min(key => key.value) > .00001f)
                            throw new InvalidOperationException($"Child {track.name} has object motion: {binding.path}/{binding.propertyName}");
                    }
                    for (int frame = 0; frame < track.frames.Length; frame++)
                    {
                        clip.SampleAnimation(animator.gameObject, frame / 24f);
                        Frame expected = track.frames[frame];
                        Compare("pelvis", expected.pelvis);
                        Compare("foot.L", expected.left_foot);
                        Compare("foot.R", expected.right_foot);
                        if (expected.contact_weight >= .9999f)
                        { Compare("SOCKET_Grip.L", expected.left); Compare("SOCKET_Grip.R", expected.right); }
                        void Compare(string bone, float[] point)
                        {
                            if (point == null || point.Length != 3) throw new InvalidOperationException($"Child {track.name}@{frame}: missing {bone} samples.");
                            Vector3 actual = actor.transform.InverseTransformPoint(bones[bone].position);
                            Vector3 target = new Vector3(point[0], point[1], point[2]);
                            float error = Vector3.Distance(actual, target); maxImported = Mathf.Max(maxImported, error);
                            if (error > .012f) throw new InvalidOperationException($"Child {track.name}@{frame} {bone}: actual={actual:F6}, expected={target:F6}, error={error:F6}m.");
                        }
                    }
                }
                Debug.Log($"CITY FAIR CHILD ACTIONS: native bones, imported contacts, feet/pelvis and exact endpoints OK; maximum imported error {maxImported:F6}m.");
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }
        private static bool Within(float value, float maximum) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= maximum;
        [Serializable] private sealed class Manifest
        {
            public string generator, rig; public int fps, bone_count, animation_events; public bool root_motion;
            public float height, maximum_grip_error, maximum_endpoint_error, maximum_lower_body_error;
            public string[] bones; public Track[] tracks;
        }
        [Serializable] private sealed class Track { public string name, clip; public float duration_seconds; public bool loop; public Frame[] frames; }
        [Serializable] private sealed class Frame { public float[] pelvis, left, right, left_foot, right_foot; public float contact_weight; }
    }

    public sealed class CityFairChildActionImporter : AssetPostprocessor
    {
        private bool IsBank => string.Equals(assetPath, CityFairChildActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);
        private void OnPreprocessModel()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importAnimation = true; importer.importCameras = false; importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
        }
        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                string action = clip.name.Substring("FairChild".Length);
                clip.loopTime = action == "Idle" || action == "Walk" || action.EndsWith("Loop", StringComparison.Ordinal);
                clip.loopPose = clip.loopTime;
            }
            importer.clipAnimations = clips;
        }
    }
}
