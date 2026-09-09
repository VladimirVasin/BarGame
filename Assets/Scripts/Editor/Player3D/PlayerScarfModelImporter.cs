using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>Preserves the scarf's production skeleton, lowered shape and cloth topology.</summary>
    public sealed class PlayerScarfModelImporter : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/Player/Scarf/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) ||
                !(assetImporter is ModelImporter importer)) return;
            importer.animationType = assetPath.EndsWith("ScarfWorn.fbx", StringComparison.Ordinal)
                ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            // Minimal freshly-created .meta files deserialize this as false.
            // Preserve the FBX centimetre conversion, as the production hero does.
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;
            importer.weldVertices = false;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
