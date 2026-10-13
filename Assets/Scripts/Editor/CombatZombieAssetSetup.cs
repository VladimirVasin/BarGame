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
        public override uint GetVersion() => 1;
        private bool IsBank => assetPath == BankPath;
        private bool IsZombie => assetPath.StartsWith(Folder + "Zombie", StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = false; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true; importer.animationType = ModelImporterAnimationType.Generic;
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
                if (Array.IndexOf(CombatZombieAssetProvider.ClipNames, clip.name) < 0)
                    throw new InvalidOperationException("Unexpected zombie action " + clip.name);
                clip.loopTime = CombatZombieAssetProvider.IsLoop(clip.name); clip.loopPose = false;
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
            importer.alphaSource = TextureImporterAlphaSource.None;
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
            foreach (string file in new[] { "ZombieActions.fbx", "Zombie3D.json", "ZombieFace1.png", "ZombieFace2.png", "ZombieFace3.png", "ZombieGroan.wav", "ZombieLunge.wav" })
                AssetDatabase.ImportAsset(Folder + file, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "Zombie3D.json"));
            if (manifest == null || !manifest.test_only || manifest.root_motion || manifest.animation_events != 0 ||
                manifest.rig != "NpcHumanV2" || manifest.clips == null || manifest.clips.Length != CombatZombieAssetProvider.ClipNames.Length ||
                Mathf.Abs(manifest.walk_cycle_distance_m - CombatZombieAssetProvider.WalkCycleDistance) > .0001f ||
                Mathf.Abs(manifest.prepare_seconds - .6f) > .0001f || Mathf.Abs(manifest.active_seconds - .22f) > .0001f || Mathf.Abs(manifest.recovery_seconds - .95f) > .0001f)
                throw new InvalidOperationException("Zombie manifest violates the isolated grounded action contract.");
            foreach (string name in CombatZombieAssetProvider.ClipNames)
            {
                AnimationClip clip = CombatZombieAssetProvider.LoadClip(name);
                CombatTestAssetSetup.ValidateInPlaceClip(clip);
            }
            for (int i = 0; i < 3; i++)
            {
                Texture2D atlas = CombatZombieAssetProvider.LoadFace(i);
                if (atlas.width != 512 || atlas.height != 512 || atlas.filterMode != FilterMode.Point)
                    throw new InvalidOperationException("Zombie appearance requires retained 512px atlas layout.");
            }
            CombatZombieAssetProvider.LoadGroan(); CombatZombieAssetProvider.LoadLunge();
            Debug.Log("COMBAT ZOMBIE IMPORTED ACTIONS AND PALETTES OK");
        }

        public static void PrepareCommandLine() => BuildOrThrow();
        [Serializable] private sealed class Manifest
        {
            public bool test_only, root_motion;
            public int animation_events;
            public string rig;
            public float walk_cycle_distance_m, prepare_seconds, active_seconds, recovery_seconds;
            public Clip[] clips;
        }
        [Serializable] private sealed class Clip { public string name; public float duration_seconds; public bool loop; }
    }
}
