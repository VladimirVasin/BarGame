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
                // A clean Library can import this FBX before the Hero V2 FBX.
                // The queued asset build imports Hero V2 first and then
                // reimports this model onto the canonical Generic Avatar.
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
                bool oneShot =
                    CityPedestrianAssetSetup.IsOneShotClip(clip.name);
                clip.loopTime = !oneShot;
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
                //
                // A one-shot transition opts out of loop-pose for a
                // stronger reason than an airborne design does: loop-pose
                // pulls a clip's two ends towards each other, and this one
                // is authored to END on the base pose of the clip that
                // follows it. Normalising it would drag that last frame
                // back towards the first and put a seam exactly where the
                // runtime crosses over.
                clip.lockRootHeightY = true;
                clip.loopPose = !airborne && !oneShot;
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
            if (NpcHumanV2AssetSetup.IsAnyPipelineBuilding ||
                Player3DV2AssetSetup.IsBuilding)
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
                        CityPedestrianAssetSetup.YardBabushkaModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.YardBabushkaManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.WeighAttendantModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.WeighAttendantManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.CemeteryMournerModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.CemeteryMournerManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.CemeteryWatchmanModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityPedestrianAssetSetup.CemeteryWatchmanManifestPath,
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
            return CityPedestrianAssetSetup.IsDeclaredModelPath(path);
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
