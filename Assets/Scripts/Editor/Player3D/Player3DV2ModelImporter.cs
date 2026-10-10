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
        // Version the mesh postprocess as well as importer settings. Tangent
        // repair changes the serialized FBX result without changing its .meta.
        public override uint GetVersion() => 2;

        private static readonly ISet<string> LoopingClips =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Idle",
                "ColdHold",
                "Walk",
                "WalkBack",
                "Run",
                "SnowWalk",
                "SnowWalkBackward",
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

            if (string.Equals(assetPath, Player3DV2CharacterSurfaces.SeatedModelPath, StringComparison.OrdinalIgnoreCase))
            {
                // This derived lower body borrows the selected trousers' material.
                importer.importBlendShapes = true;
                importer.skinWeights = ModelImporterSkinWeights.Custom;
                importer.maxBonesPerVertex = 4;
                importer.minBoneWeight = 0f;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                return;
            }

            if (string.Equals(
                    assetPath,
                    Player3DV2AssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                ConfigureShared(importer);
                // The UV-bound character normals need the imported tangent frame.
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                // The coat owns runtime mesh copies and reads source skin data
                // in player builds as well as in the Editor.
                importer.isReadable = true;
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

        private void OnPostprocessModel(GameObject model)
        {
            if (assetPath != Player3DV2AssetSetup.ModelPath &&
                assetPath != Player3DV2CharacterSurfaces.SeatedModelPath) return;
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                Vector3[] normals = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                bool changed = false;
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 normal = normals[i].normalized;
                    Vector4 source = tangents[i];
                    Vector3 direction = new Vector3(source.x, source.y, source.z);
                    if (float.IsFinite(direction.sqrMagnitude) && Mathf.Abs(direction.sqrMagnitude - 1f) < .001f &&
                        Mathf.Abs(Mathf.Abs(source.w) - 1f) < .001f &&
                        Mathf.Abs(Vector3.Dot(normal, direction)) < .001f) continue;
                    // The original flat caps have zero-area UVs. Mikk imports
                    // (1,0,0) there even when it is not perpendicular to normal.
                    // Retain valid Mikk frames and orthogonalize only undefined ones.
                    direction -= normal * Vector3.Dot(normal, direction);
                    if (!float.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude < .000001f)
                        direction = Vector3.Cross(normal, Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right);
                    direction.Normalize();
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, source.w < 0f ? -1f : 1f);
                    changed = true;
                }
                if (changed) mesh.tangents = tangents;
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
            return Player3DV2CharacterSurfaces.IsSource(path) || string.Equals(
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
            // Joint rings can blend torso, shoulder and arm fields. Preserve
            // every authored influence rather than inheriting an import preset.
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
