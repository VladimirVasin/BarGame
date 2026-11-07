using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class CityLoftPigeonImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/City/LoftPigeons/";

        private void OnPreprocessModel()
        {
            if (assetPath != Folder + "LoftPigeon.fbx") return;
            var model = (ModelImporter)assetImporter;
            model.globalScale = 1f;
            model.bakeAxisConversion = true;
            model.preserveHierarchy = true;
            model.optimizeGameObjects = false;
            model.importAnimation = false;
            model.animationType = ModelImporterAnimationType.None;
            model.importCameras = false;
            model.importLights = false;
            model.addCollider = false;
            model.importBlendShapes = false;
            model.importNormals = ModelImporterNormals.Import;
            model.importTangents = ModelImporterTangents.None;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPreprocessTexture()
        {
            if (assetPath != Folder + "PigeonPlumages.png") return;
            var texture = (TextureImporter)assetImporter;
            texture.textureType = TextureImporterType.Default;
            texture.textureShape = TextureImporterShape.Texture2D;
            texture.sRGBTexture = true;
            texture.mipmapEnabled = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.npotScale = TextureImporterNPOTScale.None;
            texture.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
