using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    internal sealed class CityGrassModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if(!string.Equals(assetPath,"Assets/Resources/City/Grass/CityGrass3D.fbx",StringComparison.Ordinal))return;
            var importer=(ModelImporter)assetImporter;
            importer.animationType=ModelImporterAnimationType.None;importer.importAnimation=false;
            importer.globalScale=1;importer.bakeAxisConversion=true;importer.preserveHierarchy=true;
            importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
            importer.importBlendShapes=false;importer.importNormals=ModelImporterNormals.Import;
            importer.importTangents=ModelImporterTangents.None;importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.isReadable=false;importer.weldVertices=true;importer.keepQuads=false;
            importer.generateSecondaryUV=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;
        }
    }
}
