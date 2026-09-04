using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class BarBartenderModelImporter :
        AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            bool isLegacy = string.Equals(
                assetPath,
                BarBartenderAssetSetup.ModelPath,
                StringComparison.OrdinalIgnoreCase);
            bool isOrdinary = string.Equals(
                assetPath,
                BarBartenderV2AssetSetup.ModelPath,
                StringComparison.OrdinalIgnoreCase);
            if ((!isLegacy && !isOrdinary) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

            Avatar playerAvatar = FindPlayerAvatar();
            if (playerAvatar != null)
            {
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = playerAvatar;
            }
            else
            {
                // A clean Library can discover the bartender before
                // the Hero V2 FBX. The queued setup imports Hero V2 first,
                // then reimports this FBX against the canonical Generic
                // Avatar.
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
        }

        private static Avatar FindPlayerAvatar()
        {
            return AssetDatabase
                .LoadAllAssetsAtPath(
                    BarBartenderAssetSetup.PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (NpcHumanV2AssetSetup.IsAnyPipelineBuilding ||
                Player3DV2AssetSetup.IsBuilding ||
                BarBartenderAssetSetup.IsBuilding ||
                BarBartenderV2AssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string importedPath = importedAssets[index];
                if (string.Equals(
                        importedPath,
                        BarBartenderV2AssetSetup.ModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        BarBartenderV2AssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        BarBartenderV2AssetSetup.AnimationPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        BarBartenderV2AssetSetup.PlayerModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        BarBartenderV2AssetSetup.SharedMaterialPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        BarBartenderV2AssetSetup.LegacyPrefabPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    BarBartenderV2AssetSetup
                        .QueueBuildWhenSourcesExist();
                }

                if (string.Equals(
                        importedPath,
                        BarBartenderAssetSetup.ModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        BarBartenderAssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        BarBartenderAssetSetup.PlayerModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        BarBartenderAssetSetup.SharedMaterialPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    BarBartenderAssetSetup
                        .QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
