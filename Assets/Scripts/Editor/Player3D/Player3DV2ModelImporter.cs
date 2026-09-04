using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports Hero V2. Its animation avatar always comes from the production
    /// model FBX.
    /// </summary>
    public sealed class Player3DV2ModelImporter : AssetPostprocessor
    {
        private static readonly ISet<string> LoopingClips =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Idle",
                "Walk",
                "WalkBack",
                "Run",
                "TurnLeft",
                "TurnRight",
                "BedSleepLoop",
                "SmokeLoop",
                "CatFeedLoop",
                "DoorUseLoop",
                "BusRideLoop",
                "BarDrinkSipLoop",
                "ChessSeatPlayLoop"
            };

        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer))
            {
                return;
            }

            if (string.Equals(
                    assetPath,
                    Player3DV2AssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                ConfigureShared(importer);
                importer.importAnimation = false;
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CreateFromThisModel;
                importer.materialImportMode =
                    ModelImporterMaterialImportMode.None;
                return;
            }

            if (!string.Equals(
                    assetPath,
                    Player3DV2AssetSetup.AnimationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ConfigureShared(importer);
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            Avatar v2Avatar = FindV2SourceAvatar();
            if (v2Avatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = v2Avatar;
            }
            else
            {
                // Initial parallel imports can reach the animation before the
                // model. BuildOrThrow imports the model first and then forces
                // this asset through CopyFromOther on the next pass.
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!string.Equals(
                    assetPath,
                    Player3DV2AssetSetup.AnimationPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < clips.Length; index++)
            {
                ModelImporterClipAnimation clip = clips[index];
                clip.name = NormalizeClipName(clip.name);
                clip.loopTime = LoopingClips.Contains(clip.name);
                clip.loopPose = clip.loopTime;
                if (!names.Add(clip.name))
                {
                    throw new InvalidOperationException(
                        "Hero V2 animation FBX contains duplicate clip " +
                        $"'{clip.name}' after name normalization.");
                }
            }

            importer.clipAnimations = clips;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (Application.isBatchMode ||
                Player3DV2AssetSetup.IsBuilding ||
                CityPedestrianAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                if (IsV2Source(importedAssets[index]))
                {
                    Player3DV2AssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }

        private static bool IsV2Source(string path)
        {
            return string.Equals(
                       path,
                       Player3DV2AssetSetup.ModelPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       Player3DV2AssetSetup.ManifestPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       Player3DV2AssetSetup.AnimationPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       Player3DV2AssetSetup.AtlasPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       Player3DV2AssetSetup.ClothingAtlasPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       Player3DV2AssetSetup.PortraitPath,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void ConfigureShared(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
        }

        private static Avatar FindV2SourceAvatar()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(
                Player3DV2AssetSetup.ModelPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
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
    }
}
