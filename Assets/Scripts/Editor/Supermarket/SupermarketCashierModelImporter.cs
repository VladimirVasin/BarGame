using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class SupermarketCashierModelImporter :
        AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!string.Equals(
                    assetPath,
                    SupermarketCashierAssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase) ||
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
                // A clean Library can discover the cashier before the
                // Player FBX. The queued setup imports Player first,
                // then reimports this FBX against the canonical
                // external Generic Avatar.
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
                    SupermarketCashierAssetSetup.PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (SupermarketCashierAssetSetup.IsBuilding ||
                Player3DAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string importedPath = importedAssets[index];
                if (string.Equals(
                        importedPath,
                        SupermarketCashierAssetSetup.ModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        SupermarketCashierAssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        SupermarketCashierAssetSetup.PlayerModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        SupermarketCashierAssetSetup.SharedMaterialPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    SupermarketCashierAssetSetup
                        .QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
