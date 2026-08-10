using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class CityPedestrianModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!string.Equals(
                    assetPath,
                    CityPedestrianAssetSetup.ModelPath,
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
                // A clean Library can import this FBX before the Player FBX.
                // The queued asset build imports the Player first and then
                // reimports this model onto the canonical external Avatar.
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
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(
                CityPedestrianAssetSetup.PlayerModelPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (CityPedestrianAssetSetup.IsBuilding ||
                Player3DAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string importedPath = importedAssets[index];
                if (string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.ModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.PlayerModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.PlayerAnimationPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.SharedMaterialPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CityPedestrianAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
