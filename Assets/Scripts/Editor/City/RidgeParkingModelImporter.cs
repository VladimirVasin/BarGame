using UnityEditor;

namespace BarPromenade.Editor
{
    internal sealed class RidgeParkingModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != "Assets/Resources/City/RidgeParking/RidgeParking.fbx") return;
            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;
            importer.addCollider = false;
        }
    }
}
