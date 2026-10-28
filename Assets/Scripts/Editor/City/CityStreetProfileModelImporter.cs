using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>Passive, readable metre topology; no imported gameplay or materials.</summary>
    public sealed class CityStreetProfileModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!string.Equals(assetPath, "Assets/Resources/City/StreetProfile/CityStreetProfile3D.fbx",
                    StringComparison.OrdinalIgnoreCase) || !(assetImporter is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;
            importer.weldVertices = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
