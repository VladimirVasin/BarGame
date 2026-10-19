using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Independent, bone-only paired ambush banks on the retained production rigs.</summary>
    public sealed class CombatDoorAmbushAssetSetup : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/CombatDoorAmbush/";
        public const string HeroBankPath = Folder + "HeroActions.fbx";
        public const string CraneBankPath = Folder + "CraneActions.fbx";
        public const string CableGuidePath = Folder + "CraneDoorCableGuide.fbx";
        public const string ManifestPath = Folder + "DoorAmbush3D.json";
        private static readonly string[] HeroNames = { "DoorAmbushEnter", "DoorAmbushHold", "DoorAmbushExit" };
        private static readonly string[] CraneNames = { "CraneDoorAmbush", "CraneDoorHold", "CraneDoorRetreat" };
        private static readonly float[] HeroDurations = { 8f, 1f, .65f };
        private static readonly float[] CraneDurations = { 5f, 1f, 1.2f };

        public override uint GetVersion() => 1;
        private bool IsBank => assetPath == HeroBankPath || assetPath == CraneBankPath;

        private void OnPreprocessModel()
        {
            if ((!IsBank && assetPath != CableGuidePath) || !(assetImporter is ModelImporter importer)) return;
            importer.animationType = IsBank ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.importAnimation = IsBank;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            if (!IsBank) return;
            string sourcePath = assetPath == HeroBankPath ? Player3DV2AssetSetup.ModelPath :
                "Assets/Resources/VillageLife/StationWorker.fbx";
            context.DependsOnArtifact(sourcePath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            string[] names = assetPath == HeroBankPath ? HeroNames : CraneNames;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                if (Array.IndexOf(names, clip.name) < 0)
                    throw new InvalidOperationException("Unexpected warehouse ambush action: " + clip.name);
                clip.loopTime = clip.name == names[1];
                clip.loopPose = false;
                clip.keepOriginalOrientation = clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = true;
                clip.lockRootRotation = clip.lockRootPositionXZ = clip.lockRootHeightY = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
        }

        [MenuItem("Bar Promenade/Combat Test/Validate Door Ambush Actions")]
        public static void BuildOrThrow()
        {
            ValidateBank(HeroBankPath, HeroNames, HeroDurations);
            ValidateBank(CraneBankPath, CraneNames, CraneDurations);
            GameObject guide = AssetDatabase.LoadAssetAtPath<GameObject>(CableGuidePath);
            if (guide == null || guide.GetComponentsInChildren<MeshRenderer>(true).Length != 1 ||
                guide.GetComponentsInChildren<Transform>(true).Count(value => value.name.StartsWith("ANCHOR_Guide", StringComparison.Ordinal)) != 6 ||
                guide.GetComponentsInChildren<Transform>(true).Count(value => value.name.StartsWith("ANCHOR_NeckGuide", StringComparison.Ordinal)) != 2)
                throw new InvalidOperationException("The warehouse ambush requires six open fairleads and two ceiling cable guides.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.root_motion || manifest.animation_events != 0 ||
                manifest.hero == null || manifest.crane == null || manifest.hero.rig != "HeroV2" ||
                manifest.crane.rig != "NpcHumanV2" || manifest.hero.bone_count < 31 || manifest.crane.bone_count < 31 ||
                manifest.hero.maximum_endpoint_error > .00003f || manifest.crane.maximum_endpoint_error > .00003f ||
                Mathf.Abs(manifest.attack_cue_seconds - 3f) > .00001f ||
                Mathf.Abs(manifest.capture_seconds - HeroDurations[0]) > .00001f ||
                Mathf.Abs(manifest.final_jab_start_seconds - 7.7f) > .00001f ||
                manifest.hero_hook_victim_part != "RightShin" || manifest.hero_hook_contact == null ||
                manifest.hero_hook_contact.Length != 3 || manifest.hero_hook_contact.Any(value => !float.IsFinite(value)) ||
                Mathf.Abs(manifest.hook_wrist_to_impact_m - .69f) > .00001f ||
                manifest.signature == null || manifest.signature.Length != 64)
                throw new InvalidOperationException("Warehouse ambush manifest violates the paired action contract.");
        }

        private static void ValidateBank(string path, string[] names, float[] durations)
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != names.Length)
                throw new InvalidOperationException("Incomplete warehouse ambush bank: " + path);
            for (int index = 0; index < names.Length; index++)
            {
                AnimationClip clip = clips.SingleOrDefault(value => value.name == names[index]);
                if (clip == null || Mathf.Abs(clip.length - durations[index]) > .003f ||
                    clip.events.Length != 0 || clip.isLooping != (index == 1))
                    throw new InvalidOperationException("Invalid warehouse ambush clip: " + names[index]);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!string.IsNullOrEmpty(binding.path) && !binding.path.EndsWith("/root", StringComparison.Ordinal) &&
                        !binding.path.EndsWith("ROOT_PlayerV2", StringComparison.Ordinal) &&
                        !binding.path.EndsWith("ROOT_Player", StringComparison.Ordinal) &&
                        !binding.path.EndsWith("RIG_Player", StringComparison.Ordinal)) continue;
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.keys.Length > 1 &&
                        curve.keys.Max(key => key.value) - curve.keys.Min(key => key.value) > .00001f)
                        throw new InvalidOperationException("Warehouse ambush contains gameplay root motion: " + clip.name);
                }
            }
        }

        [Serializable] private sealed class Manifest
        {
            public bool root_motion;
            public int animation_events;
            public float attack_cue_seconds, capture_seconds, final_jab_start_seconds, hook_wrist_to_impact_m;
            public string hero_hook_victim_part;
            public float[] hero_hook_contact;
            public Bank hero, crane;
            public string signature;
        }

        [Serializable] private sealed class Bank
        {
            public string rig;
            public int bone_count;
            public float maximum_endpoint_error;
        }
    }
}
