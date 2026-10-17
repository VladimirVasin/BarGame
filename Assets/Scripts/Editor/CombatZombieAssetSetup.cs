using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Dedicated Generic bank and point-filtered palettes, imported automatically.</summary>
    public sealed class CombatZombieAssetSetup : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Combat/";
        public const string BankPath = Folder + "ZombieActions.fbx";
        public const string OrdinaryBankPath = Folder + "ZombieOrdinaryActions.fbx";
        public override uint GetVersion() => 7;
        private bool IsOrdinaryBank => assetPath == OrdinaryBankPath;
        private bool IsBank => assetPath == BankPath || IsOrdinaryBank;
        private bool IsZombie => assetPath.StartsWith(Folder + "Zombie", StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            bool isCrate = assetPath == Folder + "ZombieCrate.fbx";
            if ((!IsBank && !isCrate) || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = false; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            if (isCrate)
            {
                // Passive cargo combines the static fusion mesh once at runtime.
                // Keep its shared source data available in players as well as Editor.
                importer.isReadable = true;
                importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                importer.sourceAvatar = null;
                return;
            }
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            bool isCrate = assetPath == Folder + "ZombieCrate.fbx";
            if ((!IsBank && !isCrate) || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (isCrate && clips.Length != 1)
                throw new InvalidOperationException("The fused catch requires exactly one authored presentation take.");
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                if (isCrate) clip.name = CombatZombieAssetProvider.CatchClip;
                else if (Array.IndexOf(IsOrdinaryBank ? CombatZombieAssetProvider.OrdinaryClipNames :
                    CombatZombieAssetProvider.ClipNames, clip.name) < 0)
                    throw new InvalidOperationException("Unexpected zombie action " + clip.name);
                clip.loopTime = isCrate || (IsOrdinaryBank ? CombatZombieAssetProvider.IsOrdinaryLoop(clip.name) :
                    CombatZombieAssetProvider.IsLoop(clip.name)); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (!IsZombie || !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true; importer.maxTextureSize = 512; importer.isReadable = false;
            if (assetPath.EndsWith("ZombieCrateAtlas.png", StringComparison.Ordinal))
            { importer.wrapMode = TextureWrapMode.Repeat; importer.textureCompression = TextureImporterCompression.CompressedHQ; }
            importer.alphaSource = TextureImporterAlphaSource.None;
        }

        private void OnPostprocessAnimation(GameObject rootObject, AnimationClip clip)
        {
            if (assetPath != Folder + "ZombieCrate.fbx") return;
            // FBX baking may add constant curves to the wooden skeleton and
            // motion roots. Their world poses belong solely to part physics.
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                int slash = binding.path.LastIndexOf('/');
                string bone = slash >= 0 ? binding.path.Substring(slash + 1) : binding.path;
                bool animated = bone.StartsWith("Catch", StringComparison.Ordinal) &&
                    (bone.EndsWith("Body", StringComparison.Ordinal) || bone.EndsWith("Head", StringComparison.Ordinal) ||
                    bone.EndsWith("Tail", StringComparison.Ordinal));
                if (!animated) AnimationUtility.SetEditorCurve(clip, binding, null);
            }
        }

        private void OnPreprocessAudio()
        {
            if (!IsZombie || !(assetImporter is AudioImporter importer)) return;
            importer.forceToMono = true;
            AudioImporterSampleSettings samples = importer.defaultSampleSettings;
            samples.loadType = AudioClipLoadType.DecompressOnLoad;
            samples.preloadAudioData = true;
            samples.compressionFormat = AudioCompressionFormat.PCM;
            samples.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = samples;
        }

        [MenuItem("Bar Promenade/Combat Test/Validate Zombie Assets")]
        public static void BuildOrThrow()
        {
            foreach (string file in new[] { "ZombieActions.fbx", "Zombie3D.json", "ZombieOrdinaryActions.fbx", "ZombieOrdinary3D.json",
                "ZombieCrate.fbx", "ZombieCrateAtlas.png",
                "ZombieFace1.png", "ZombieFace2.png", "ZombieFace3.png", "ZombieGroan.wav", "ZombieLunge.wav",
                "ZombieGroan2.wav", "ZombieGroan3.wav", "ZombieLunge2.wav", "ZombieReveal.wav",
                "ZombieHurtWood.wav", "ZombieHurtWood2.wav", "ZombieHurtFlesh.wav", "ZombieHurtFlesh2.wav",
                "ZombieDeath.wav", "ZombieDeath2.wav", "ZombieFrenzy.wav", "ZombieFrenzy2.wav" })
                AssetDatabase.ImportAsset(Folder + file, ImportAssetOptions.ForceSynchronousImport);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "Zombie3D.json"));
            if (manifest == null || !manifest.test_only || manifest.root_motion || manifest.animation_events != 0 ||
                manifest.rig != "NpcHumanV2" || manifest.clips == null || manifest.clips.Length != CombatZombieAssetProvider.ClipNames.Length ||
                Mathf.Abs(manifest.walk_cycle_distance_m - CombatZombieAssetProvider.WalkCycleDistance) > .0001f ||
                Mathf.Abs(manifest.walk_duration_seconds - CombatZombieAssetProvider.WalkCycleSeconds) > .0001f ||
                Mathf.Abs(manifest.prepare_seconds - MeleeCombatSettings.ZombieUnarmed.WindupSeconds) > .0001f ||
                Mathf.Abs(manifest.active_seconds - MeleeCombatSettings.ZombieUnarmed.ActiveSeconds) > .0001f ||
                Mathf.Abs(manifest.recovery_seconds - MeleeCombatSettings.ZombieUnarmed.RecoverySeconds) > .0001f)
                throw new InvalidOperationException("Zombie manifest violates the isolated grounded action contract.");
            foreach (string name in CombatZombieAssetProvider.ClipNames)
            {
                AnimationClip clip = CombatZombieAssetProvider.LoadClip(name);
                CombatTestAssetSetup.ValidateInPlaceClip(clip);
            }
            Manifest ordinary = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "ZombieOrdinary3D.json"));
            MeleeCombatSettings ordinarySettings = CombatActor.OrdinaryZombieCombatSettings;
            if (ordinary == null || !ordinary.test_only || ordinary.root_motion || ordinary.animation_events != 0 ||
                ordinary.rig != "NpcHumanV2" || ordinary.clips == null || ordinary.clips.Length != CombatZombieAssetProvider.OrdinaryClipNames.Length ||
                Mathf.Abs(ordinary.walk_cycle_distance_m - CombatZombieAssetProvider.OrdinaryWalkCycleDistance) > .0001f ||
                Mathf.Abs(ordinary.walk_duration_seconds - CombatZombieAssetProvider.OrdinaryWalkCycleSeconds) > .0001f ||
                Mathf.Abs(ordinary.prepare_seconds - ordinarySettings.WindupSeconds) > .0001f ||
                Mathf.Abs(ordinary.active_seconds - ordinarySettings.ActiveSeconds) > .0001f ||
                Mathf.Abs(ordinary.recovery_seconds - ordinarySettings.RecoverySeconds) > .0001f)
                throw new InvalidOperationException("Ordinary zombie manifest violates its undeformed grounded action contract.");
            foreach (string name in CombatZombieAssetProvider.OrdinaryClipNames)
                CombatTestAssetSetup.ValidateInPlaceClip(CombatZombieAssetProvider.LoadOrdinaryClip(name));
            for (int i = 0; i < 3; i++)
            {
                Texture2D atlas = CombatZombieAssetProvider.LoadFace(i);
                if (atlas.width != 512 || atlas.height != 512 || atlas.filterMode != FilterMode.Point)
                    throw new InvalidOperationException("Zombie appearance requires retained 512px atlas layout.");
            }
            CombatZombieAssetProvider.LoadGroan(); CombatZombieAssetProvider.LoadLunge();
            foreach (string name in new[] { "ZombieFrenzy", "ZombieFrenzy2" })
                if (Mathf.Abs(CombatZombieAssetProvider.LoadAudio(name).length - CombatActor.ZombieFrenzyDurationSeconds) > .003f)
                    throw new InvalidOperationException("Porter frenzy sound must match the authored action clock.");
            CombatZombieAssetProvider.LoadCrate();
            CombatZombieAssetProvider.LoadCatchClip();
            Debug.Log("COMBAT ZOMBIE IMPORTED ACTIONS AND PALETTES OK");
        }

        public static void PrepareCommandLine() => BuildOrThrow();
        [Serializable] private sealed class Manifest
        {
            public bool test_only, root_motion;
            public int animation_events;
            public string rig;
            public float walk_cycle_distance_m, walk_duration_seconds, prepare_seconds, active_seconds, recovery_seconds;
            public Clip[] clips;
        }
        [Serializable] private sealed class Clip { public string name; public float duration_seconds; public bool loop; }
    }
}
