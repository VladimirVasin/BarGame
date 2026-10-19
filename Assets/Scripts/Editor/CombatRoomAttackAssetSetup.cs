using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Own Generic, bone-only Room banks and passive metre tableau.</summary>
    public sealed class CombatRoomAttackAssetSetup : AssetPostprocessor
    {
        private const string Npc = "Assets/Resources/Combat/RoomAttackNpcActions.fbx";
        private const string Hero = "Assets/Resources/Player/RoomAttackHeroActions.fbx";
        private const string Tableau = "Assets/Resources/Combat/RoomAttackTableau.fbx";
        private const string ShadowCookie = "Assets/Resources/Combat/RoomAttackShadow.png";
        private const string MemorySound = "Assets/Resources/Combat/RoomAttackMemory.wav";
        private bool IsBank => assetPath == Npc || assetPath == Hero;
        public override uint GetVersion() => 2;

        private void OnPreprocessModel()
        {
            if ((!IsBank && assetPath != Tableau) || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = false; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = IsBank;
            importer.animationType = IsBank ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            if (!IsBank) return;
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
                bool known = assetPath == Hero ? clip.name == CombatRoomAttackAssetProvider.HeadPainClip :
                    Array.IndexOf(CombatRoomAttackAssetProvider.NpcClipNames, clip.name) >= 0;
                if (!known) throw new InvalidOperationException("Unexpected Room action: " + clip.name);
                clip.loopTime = clip.name == CombatRoomAttackAssetProvider.IdleClip;
                clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (IsShadowCookie(assetPath) && assetImporter is TextureImporter cookie)
            {
                cookie.textureType = TextureImporterType.SingleChannel;
                var channel = new TextureImporterSettings();
                cookie.ReadTextureSettings(channel);
                channel.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                cookie.SetTextureSettings(channel);
                cookie.sRGBTexture = false; cookie.mipmapEnabled = true; cookie.isReadable = false;
                cookie.alphaSource = TextureImporterAlphaSource.None;
                cookie.wrapMode = TextureWrapMode.Clamp; cookie.filterMode = FilterMode.Bilinear;
                cookie.anisoLevel = 1; cookie.maxTextureSize = CombatRoomAttackAssetProvider.ShadowFrameSize;
                cookie.textureCompression = TextureImporterCompression.Uncompressed;
                var platform = cookie.GetPlatformTextureSettings("Standalone");
                platform.overridden = true; platform.maxTextureSize = CombatRoomAttackAssetProvider.ShadowFrameSize; platform.format = TextureImporterFormat.R8;
                cookie.SetPlatformTextureSettings(platform);
                return;
            }
            if (assetPath != "Assets/Resources/Combat/RoomAttackWood.png" || !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
            importer.mipmapEnabled = true; importer.isReadable = false; importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1; importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var windows = importer.GetPlatformTextureSettings("Standalone");
            windows.overridden = true; windows.maxTextureSize = 512; windows.format = TextureImporterFormat.BC7;
            importer.SetPlatformTextureSettings(windows);
        }

        private static string ShadowPath(int frame) => frame == 0 ? ShadowCookie :
            "Assets/Resources/Combat/RoomAttackShadow" + frame + ".png";

        private static bool IsShadowCookie(string path)
        {
            if (!path.StartsWith("Assets/Resources/Combat/RoomAttackShadow", StringComparison.Ordinal)) return false;
            for (int frame = 0; frame < CombatRoomAttackAssetProvider.ShadowFrameCount; frame++)
                if (path == ShadowPath(frame)) return true;
            return false;
        }

        private void OnPreprocessAudio()
        {
            if ((assetPath != "Assets/Resources/Combat/RoomLaugh.wav" && assetPath != MemorySound) || !(assetImporter is AudioImporter importer)) return;
            importer.forceToMono = true; importer.loadInBackground = false;
            var sample = importer.defaultSampleSettings;
            sample.preloadAudioData = true;
            sample.loadType = AudioClipLoadType.DecompressOnLoad;
            sample.compressionFormat = assetPath == MemorySound ? AudioCompressionFormat.PCM : AudioCompressionFormat.ADPCM;
            sample.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = sample;
        }

        public static void BuildOrThrow()
        {
            foreach (string path in new[] { Npc, Hero, Tableau, "Assets/Resources/Combat/RoomLaugh.wav",
                "Assets/Resources/Combat/RoomAttackWood.png", ShadowCookie, MemorySound })
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            for (int frame = 1; frame < CombatRoomAttackAssetProvider.ShadowFrameCount; frame++)
                AssetDatabase.ImportAsset(ShadowPath(frame), ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ValidateImportedOrThrow();
        }

        public static void ValidateImportedOrThrow()
        {
            foreach (string name in CombatRoomAttackAssetProvider.NpcClipNames)
                CombatTestAssetSetup.ValidateInPlaceClip(CombatRoomAttackAssetProvider.LoadNpcClip(name));
            CombatTestAssetSetup.ValidateInPlaceClip(CombatRoomAttackAssetProvider.LoadHeroClip(CombatRoomAttackAssetProvider.HeadPainClip));
            CombatRoomAttackAssetProvider.LoadLaughAudio();
            CombatRoomAttackAssetProvider.LoadMemorySound();
            for (int frame = 0; frame < CombatRoomAttackAssetProvider.ShadowFrameCount; frame++)
            {
                Texture2D cookie = CombatRoomAttackAssetProvider.LoadShadowCookie(frame);
                var cookieImporter = (TextureImporter)AssetImporter.GetAtPath(ShadowPath(frame));
                if (cookie.width != CombatRoomAttackAssetProvider.ShadowFrameSize || cookie.height != CombatRoomAttackAssetProvider.ShadowFrameSize || cookie.isReadable ||
                    cookieImporter.sRGBTexture || cookieImporter.wrapMode != TextureWrapMode.Clamp ||
                    cookieImporter.textureType != TextureImporterType.SingleChannel)
                    throw new InvalidOperationException("Room shadow needs its finite linear single-channel frame bank.");
            }
            GameObject model = CombatRoomAttackAssetProvider.LoadTableau();
            foreach (string name in new[] { "HeroPelvis", "HeroForward", "Camera", "LookAt", "EntryCamera", "Light" })
                if (CityPedestrianHandProps.FindSocket(model.transform, "ANCHOR_" + name) == null)
                    throw new InvalidOperationException("Missing authored Room tableau anchor: " + name);
            foreach (string name in new[] { "GEO_HandFinger1.R", "GEO_HandFinger2.R", "GEO_HandFinger3.R", "GEO_HandThumb.R" })
            {
                Mesh mesh = CombatRoomAssetProvider.LoadBodyRenderer(name).sharedMesh;
                bool found = false;
                for (int index = 0; index < mesh.blendShapeCount; index++)
                    found |= mesh.GetBlendShapeName(index).EndsWith(CombatRoomAttackAssetProvider.PointingShape, StringComparison.Ordinal);
                if (!found) throw new InvalidOperationException("Room hand lacks authored pointing shape: " + name);
            }
        }
    }
}
