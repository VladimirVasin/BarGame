using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class MountainRoadCafeCastAssetSetup
    {
        public const string PlayerModelPath = "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string AnimationPath = "Assets/Pedestrians/Animations/MountainRoadCafeCast.fbx";
        private const string ProviderPath = "Assets/Resources/MountainRoad/MountainRoadCafeCastProvider.asset";
        public static bool IsBuilding { get; private set; }
        private static readonly (MountainRoadCafeCastRole Role, MountainRoadCafeCastClipKind Kind, string Name, bool Loop)[] Layout =
        {
            (MountainRoadCafeCastRole.LonePatron, MountainRoadCafeCastClipKind.Idle, "CafeLoneSleep", true),
            (MountainRoadCafeCastRole.LonePatron, MountainRoadCafeCastClipKind.Interject, "CafeLoneInterject", false),
            (MountainRoadCafeCastRole.PairMan, MountainRoadCafeCastClipKind.Idle, "CafeManIdle", true),
            (MountainRoadCafeCastRole.PairMan, MountainRoadCafeCastClipKind.Drink, "CafeManDrink", false),
            (MountainRoadCafeCastRole.PairWoman, MountainRoadCafeCastClipKind.Idle, "CafeWomanIdle", true),
            (MountainRoadCafeCastRole.PairWoman, MountainRoadCafeCastClipKind.Drink, "CafeWomanDrink", false),
            (MountainRoadCafeCastRole.Attendant, MountainRoadCafeCastClipKind.Wipe, "CafeAttendantWipe", true),
            (MountainRoadCafeCastRole.Attendant, MountainRoadCafeCastClipKind.Walk, "CafeAttendantWalk", true),
            (MountainRoadCafeCastRole.Attendant, MountainRoadCafeCastClipKind.Pour, "CafeAttendantPour", false),
            (MountainRoadCafeCastRole.Attendant, MountainRoadCafeCastClipKind.Notice, "CafeAttendantNotice", false),
            (MountainRoadCafeCastRole.Attendant, MountainRoadCafeCastClipKind.ClosingAddressEnter, "BarClosingAddressEnter", false),
            (MountainRoadCafeCastRole.Attendant, MountainRoadCafeCastClipKind.ClosingAddressLoop, "BarClosingAddressLoop", true),
            (MountainRoadCafeCastRole.Attendant, MountainRoadCafeCastClipKind.ClosingAddressExit, "BarClosingAddressExit", false),
        };

        public static bool TryGetClipLoopFlag(string name, out bool loop)
        {
            var found = Array.FindIndex(Layout, x => x.Name == name);
            loop = found >= 0 && Layout[found].Loop;
            return found >= 0;
        }

        [MenuItem("Bar Promenade/City Cafe/Rebuild Role Animations")]
        public static void BuildOrThrow()
        {
            if (IsBuilding) return;
            IsBuilding = true;
            try
            {
                var bank = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                    .ToDictionary(clip => clip.name, StringComparer.Ordinal);
                var provider = AssetDatabase.LoadAssetAtPath<MountainRoadCafeCastProvider>(ProviderPath);
                if (provider == null)
                {
                    provider = ScriptableObject.CreateInstance<MountainRoadCafeCastProvider>();
                    AssetDatabase.CreateAsset(provider, ProviderPath);
                }
                provider.Configure(Layout.GroupBy(row => row.Role).Select(group => new CafeRoleAnimations(group.Key,
                    group.Select(row => new MountainRoadCafeCastClipBinding(row.Kind, bank[row.Name], row.Loop))
                        .ToArray())).ToArray());
                EditorUtility.SetDirty(provider);
                AssetDatabase.SaveAssetIfDirty(provider);
                ValidateOrThrow();
            }
            finally { IsBuilding = false; }
        }

        public static void ValidateOrThrow()
        {
            var provider = AssetDatabase.LoadAssetAtPath<MountainRoadCafeCastProvider>(ProviderPath);
            if (provider == null || !provider.HasCompleteCast) throw new InvalidOperationException("Missing cafe role animations.");
            foreach (var row in Layout)
                if (!provider.ForRole(row.Role).TryGetClip(row.Kind, out var clip, out bool loop) ||
                    clip.name != row.Name || clip.length <= 0 || loop != row.Loop ||
                    AssetDatabase.GetAssetPath(clip) != AnimationPath)
                    throw new InvalidOperationException("Missing or mismatched cafe animation " + row.Name);
        }
    }

    public sealed class MountainRoadCafeCastModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer))
            {
                return;
            }

            if (assetPath != MountainRoadCafeCastAssetSetup.AnimationPath) return;

            Avatar playerAvatar = FindPlayerAvatar();
            if (playerAvatar != null)
            {
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = playerAvatar;
            }
            else
            {
                // A clean Library may discover the cafe cast before the
                // Hero V2 FBX. The queued setup imports Hero V2 first, then
                // reimports this source onto that canonical Generic Avatar.
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = true;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 4;
            importer.minBoneWeight = 0f;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
            ConfigureAnimationClips(importer);
        }

        private void OnPreprocessAnimation()
        {
            if (!(assetImporter is ModelImporter importer) ||
                !string.Equals(
                    assetPath,
                    MountainRoadCafeCastAssetSetup.AnimationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ConfigureAnimationClips(importer);
        }

        private static void ConfigureAnimationClips(
            ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips =
                importer.defaultClipAnimations;
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < clips.Length; index++)
            {
                ModelImporterClipAnimation clip = clips[index];
                clip.name = NormalizeClipName(clip.name);
                if (!MountainRoadCafeCastAssetSetup.TryGetClipLoopFlag(
                        clip.name,
                        out bool loop))
                {
                    throw new InvalidOperationException(
                        "Cafe animation FBX contains undeclared clip " +
                        $"'{clip.name}'.");
                }

                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                if (!names.Add(clip.name))
                {
                    throw new InvalidOperationException(
                        "Cafe animation FBX contains duplicate clip " +
                        $"'{clip.name}' after name normalization.");
                }
            }

            importer.clipAnimations = clips;
        }

        private static Avatar FindPlayerAvatar()
        {
            return AssetDatabase.LoadAllAssetsAtPath(
                    MountainRoadCafeCastAssetSetup.PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static string NormalizeClipName(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName))
            {
                return sourceName;
            }

            int separator = sourceName.LastIndexOf('|');
            return separator >= 0 && separator + 1 < sourceName.Length
                ? sourceName.Substring(separator + 1)
                : sourceName;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!NpcHumanV2AssetSetup.IsAnyPipelineBuilding &&
                importedAssets.Contains(MountainRoadCafeCastAssetSetup.AnimationPath))
                EditorApplication.delayCall += MountainRoadCafeCastAssetSetup.BuildOrThrow;
        }
    }
}
