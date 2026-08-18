using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class Player3DModelImporter : AssetPostprocessor
    {
        private const string ModelPath =
            "Assets/Player3D/Models/PlayerCharacter3D.fbx";
        private const string AnimationFolder =
            "Assets/Player3D/Animations/";

        private static readonly ISet<string> LoopingClips =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Idle",
                "Walk",
                "BedSleepLoop",
                "SmokeLoop",
                "CatFeedLoop",
                "BusRideLoop",
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
                    ModelPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                ConfigureShared(importer);
                importer.importAnimation = false;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.materialImportMode =
                    ModelImporterMaterialImportMode.None;
                return;
            }

            if (!assetPath.StartsWith(
                    AnimationFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ConfigureShared(importer);
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            Avatar sourceAvatar = FindSourceAvatar();
            if (sourceAvatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (Player3DAssetSetup.IsBuilding ||
                CityPedestrianAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string importedPath = importedAssets[index];
                if (string.Equals(
                        importedPath,
                        Player3DAssetSetup.ModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        Player3DAssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    importedPath.StartsWith(
                        AnimationFolder,
                        StringComparison.OrdinalIgnoreCase) &&
                    importedPath.EndsWith(
                        ".fbx",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Player3DAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(
                    AnimationFolder,
                    StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

            ModelImporterClipAnimation[] clips =
                importer.defaultClipAnimations;
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
                        $"Player 3D animation FBX contains duplicate clip " +
                        $"'{clip.name}' after name normalization.");
                }
            }

            importer.clipAnimations = clips;
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

        private static Avatar FindSourceAvatar()
        {
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
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
