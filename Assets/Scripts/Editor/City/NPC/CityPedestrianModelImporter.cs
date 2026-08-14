using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class CityPedestrianModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer))
            {
                return;
            }

            bool isModel = IsPedestrianModel(assetPath);
            bool isAnimation = string.Equals(
                assetPath,
                CityPedestrianAssetSetup.AnimationPath,
                StringComparison.OrdinalIgnoreCase);
            if (!isModel && !isAnimation)
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
            importer.importAnimation = isAnimation;
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
            if (isAnimation)
            {
                ConfigureAnimationClips(importer);
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!string.Equals(
                    assetPath,
                    CityPedestrianAssetSetup.AnimationPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

            ConfigureAnimationClips(importer);
        }

        private static void ConfigureAnimationClips(ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips =
                importer.defaultClipAnimations;
            var names = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal);
            for (int index = 0; index < clips.Length; index++)
            {
                ModelImporterClipAnimation clip = clips[index];
                clip.name = NormalizeClipName(clip.name);
                bool airborne =
                    CityPedestrianAssetSetup.IsAirborneClip(clip.name);
                clip.loopTime = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootRotation = true;
                clip.lockRootPositionXZ = true;
                // Bake root height into the pose for every design. This rig's
                // Avatar treats the pelvis as the motion node, and an airborne
                // design authors its arc on exactly that bone, so leaving the
                // height unbaked extracts the whole hop into root motion —
                // which `CityPedestrianPresentation` then discards, because it
                // runs its Animator with `applyRootMotion = false`. Baking
                // keeps the arc inside the pose where it survives.
                // Loop-pose normalisation stays off for an airborne design
                // because it would re-level that same arc, and its clips
                // already loop exactly.
                clip.lockRootHeightY = true;
                clip.loopPose = !airborne;
                if (!names.Add(clip.name))
                {
                    throw new InvalidOperationException(
                        "Pedestrian locomotion FBX contains duplicate clip " +
                        $"'{clip.name}' after name normalization.");
                }
            }

            importer.clipAnimations = clips;
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
                        CityPedestrianAssetSetup.ChairCarrierModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.KettleHatModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.LongArmModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.HelmetLampModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.PipebackRollerModelPath,
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
                        CityPedestrianAssetSetup.ChairCarrierManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.KettleHatManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.LongArmManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.HelmetLampManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.PipebackRollerManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.AnimationPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.AnimationManifestPath,
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

        private static bool IsPedestrianModel(string path)
        {
            return string.Equals(
                       path,
                       CityPedestrianAssetSetup.ModelPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       CityPedestrianAssetSetup.ChairCarrierModelPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       CityPedestrianAssetSetup.KettleHatModelPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       CityPedestrianAssetSetup.LongArmModelPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       CityPedestrianAssetSetup.HelmetLampModelPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       CityPedestrianAssetSetup.PipebackRollerModelPath,
                       StringComparison.OrdinalIgnoreCase);
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
