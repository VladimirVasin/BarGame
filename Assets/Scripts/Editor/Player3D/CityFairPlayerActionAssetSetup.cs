using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports the optional bank and measures it on a disposable production hero.</summary>
    public static class CityFairPlayerActionAssetSetup
    {
        public const string AnimationPath = "Assets/Resources/Player/CityFairPlayerActions.fbx";
        public const string ManifestPath = "Assets/Resources/Player/CityFairPlayerActions.json";

        [MenuItem("Bar Promenade/Player 3D/Validate City Fair Actions")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != 6) throw new InvalidOperationException($"Fair action clip count: expected 6, actual {clips.Length} ({string.Join(", ", clips.Select(value => value.name))}).");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null) throw new InvalidOperationException("Fair action manifest did not deserialize: " + ManifestPath);
            const string productionManifestPath = "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json";
            ProductionRigManifest production = JsonUtility.FromJson<ProductionRigManifest>(File.ReadAllText(productionManifestPath));
            if (production?.bones == null || production.bones.Length != production.body_bone_count + production.hair_bone_count)
                throw new InvalidOperationException("Production hero manifest must enumerate its body and optional hair bones: " + productionManifestPath);
            // The current rig adds twelve secondary hair bones to the unchanged
            // thirty-one body bones. Compare against the actual production list,
            // then require every listed bone in both imported bank and prefab.
            var errors = new List<string>();
            void Check(bool accepted, string field, object actual, string expected)
            {
                if (!accepted) errors.Add($"{field}: expected {expected}, actual {actual ?? "<null>"}");
            }
            Check(manifest.generator == "city_fair_player_v1", "generator", manifest.generator, "city_fair_player_v1");
            Check(manifest.rig == "HeroV2", "rig", manifest.rig, "HeroV2");
            Check(manifest.bone_count == production.bones.Length, "bone_count", manifest.bone_count,
                $"{production.bones.Length} production bones ({production.body_bone_count} body + {production.hair_bone_count} hair)");
            Check(!manifest.root_motion, "root_motion", manifest.root_motion, "false");
            Check(manifest.animation_events == 0, "animation_events", manifest.animation_events, "0");
            Check(manifest.fps == 24, "fps", manifest.fps, "24");
            Check(Within(manifest.maximum_grip_error, .0002f), "maximum_grip_error", manifest.maximum_grip_error, "finite [0, 0.0002]");
            Check(Within(manifest.maximum_endpoint_error, .00001f), "maximum_endpoint_error", manifest.maximum_endpoint_error, "finite [0, 0.00001]");
            Check(Within(manifest.maximum_lower_body_error, .00001f), "maximum_lower_body_error", manifest.maximum_lower_body_error, "finite [0, 0.00001]");
            Check(manifest.tracks != null && manifest.tracks.Length == 6, "tracks.Length", manifest.tracks?.Length, "6");
            if (errors.Count > 0) throw new InvalidOperationException("Fair action manifest contract:\n" + string.Join("\n", errors));
            Vector3 pelvis = Point(manifest.action_pelvis_from_ground);
            if (Vector3.Distance(pelvis, Point(manifest.entry_pelvis_from_ground)) > .00001f ||
                Vector3.Distance(pelvis, Point(manifest.exit_pelvis_from_ground)) > .00001f ||
                Point(manifest.entry_ground_offset) != Vector3.zero || Point(manifest.exit_ground_offset) != Vector3.zero ||
                Point(manifest.entry_facing) != Vector3.forward || Point(manifest.exit_facing) != Vector3.forward)
                throw new InvalidOperationException($"Fair stationary poses: entry/action/exit pelvis={Point(manifest.entry_pelvis_from_ground):F6}/{pelvis:F6}/{Point(manifest.exit_pelvis_from_ground):F6}; " +
                    $"entry/exit ground={Point(manifest.entry_ground_offset):F6}/{Point(manifest.exit_ground_offset):F6}; " +
                    $"entry/exit facing={Point(manifest.entry_facing):F6}/{Point(manifest.exit_facing):F6}; expected matching pelvis, zero ground offsets and +Z facing.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (prefab == null) throw new InvalidOperationException("The production hero is missing.");
            GameObject bank = AssetDatabase.LoadAssetAtPath<GameObject>(AnimationPath);
            if (bank == null) throw new InvalidOperationException("The imported fair action rig is missing: " + AnimationPath);
            AssertRigBones(bank, production.bones, "fair action bank");
            AssertRigBones(prefab, production.bones, "production hero prefab");
            GameObject sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var registry = sample.GetComponentInChildren<Player3DAssetRegistry>();
                registry.Animator.enabled = false;
                foreach (string name in CityFairPlayerActions.RequiredClipNames)
                {
                    AnimationClip clip = clips.SingleOrDefault(value => value.name == name);
                    Track track = manifest.tracks.SingleOrDefault(value => value.clip == name);
                    bool loop = name.EndsWith("Loop", StringComparison.Ordinal);
                    float duration = loop ? (name.Contains("Organ") ? 4f : 2f) : 1.25f;
                    if (clip == null || track == null || clip.isLooping != loop || track.loop != loop || clip.events.Length != 0 ||
                        Mathf.Abs(clip.length - duration) > .003f || Mathf.Abs(track.duration_seconds - duration) > .0001f ||
                        track.frames == null || track.frames.Length != Mathf.RoundToInt(duration * 24f) + 1)
                        throw new InvalidOperationException($"Fair action {name}: expected loop={loop}, duration={duration:F6}, frames={Mathf.RoundToInt(duration * 24f) + 1}; " +
                            $"actual clip={clip?.name ?? "<missing>"}, loop={clip?.isLooping}, length={clip?.length:F6}, events={clip?.events.Length}; " +
                            $"track={track?.clip ?? "<missing>"}, loop={track?.loop}, duration={track?.duration_seconds:F6}, frames={track?.frames?.Length}.");
                    foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!string.IsNullOrEmpty(curve.path) && !curve.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) &&
                            !curve.path.EndsWith("RIG_Player", StringComparison.Ordinal)) continue;
                        AnimationCurve values = AnimationUtility.GetEditorCurve(clip, curve);
                        if (values != null && values.keys.Length > 1 && values.keys.Max(key => key.value) - values.keys.Min(key => key.value) > .00001f)
                            throw new InvalidOperationException("Fair actions contain root/object motion.");
                    }
                    for (int frame = 0; frame < track.frames.Length; frame++)
                    {
                        if (track.frames[frame].contact_weight < .9999f) continue;
                        clip.SampleAnimation(registry.Animator.gameObject, frame / 24f);
                        Vector3 expected = Point(track.frames[frame].right) - pelvis;
                        Vector3 actual = sample.transform.InverseTransformVector(registry.Anchors.RightGrip.position - registry.Anchors.Pelvis.position);
                        if (Vector3.Distance(actual, expected) > .012f)
                            throw new InvalidOperationException($"Fair action {name}@{frame}: imported grip error {Vector3.Distance(actual, expected):F5} m; " +
                                $"actual pelvis-relative={actual:F6}, expected={expected:F6}, authored pelvis={pelvis:F6}.");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(sample); }
        }

        private static void AssertRigBones(GameObject model, string[] expected, string label)
        {
            var names = new HashSet<string>(model.GetComponentsInChildren<Transform>(true).Select(part => part.name));
            string[] missing = expected.Where(name => !names.Contains(name)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"The {label} is missing production bones: {string.Join(", ", missing)}.");
        }

        private static bool Within(float value, float limit) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= limit;
        private static Vector3 Point(float[] value)
        {
            if (value == null || value.Length != 3) throw new InvalidOperationException("Missing fair action pose/anchor.");
            return new Vector3(value[0], value[1], value[2]);
        }
        [Serializable] private sealed class Manifest
        {
            public string generator, rig;
            public int bone_count, animation_events, fps;
            public bool root_motion;
            public float maximum_grip_error, maximum_lower_body_error, maximum_endpoint_error;
            public float[] entry_ground_offset, exit_ground_offset, entry_facing, exit_facing;
            public float[] entry_pelvis_from_ground, action_pelvis_from_ground, exit_pelvis_from_ground;
            public Track[] tracks;
        }
        [Serializable] private sealed class Track
        {
            public string clip;
            public bool loop;
            public float duration_seconds;
            public Frame[] frames;
        }
        [Serializable] private sealed class Frame { public float[] right; public float contact_weight; }
        [Serializable] private sealed class ProductionRigManifest { public string[] bones; public int body_bone_count, hair_bone_count; }
    }

    public sealed class CityFairPlayerActionImporter : AssetPostprocessor
    {
        private bool IsBank => string.Equals(assetPath, CityFairPlayerActionAssetSetup.AnimationPath, StringComparison.OrdinalIgnoreCase);
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
                clip.loopTime = clip.name.EndsWith("Loop", StringComparison.Ordinal);
                clip.loopPose = clip.loopTime;
            }
            importer.clipAnimations = clips;
        }
    }
}
