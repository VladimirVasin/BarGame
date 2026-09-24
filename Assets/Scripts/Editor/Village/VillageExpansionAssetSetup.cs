using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class VillageExpansionAssetSetup
    {
        public const string ModelPath = "Assets/Resources/Village/Expansion/VillageExpansion3D.fbx";
        public const string ManifestPath = "Assets/Resources/Village/Expansion/VillageExpansion3D.json";
        public const string LodgePicturesPath = "Assets/Resources/" + VillageExpansionAssetProvider.LodgePicturesTexturePath + ".png";
        public static readonly string[] WreckTexturePaths =
        {
            "Assets/Resources/" + VillageExpansionAssetProvider.WreckRustTexturePath + ".png",
            "Assets/Resources/" + VillageExpansionAssetProvider.WreckPaintTexturePath + ".png"
        };
        public static readonly string[] AbandonedTexturePaths =
        {
            "Assets/Resources/Village/Textures/AbandonedWood.png",
            "Assets/Resources/Village/Textures/AbandonedPlaster.png",
            "Assets/Resources/Village/Textures/AbandonedRoof.png"
        };

        [MenuItem("Bar Promenade/Village/Import Expansion Pack")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(LodgePicturesPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            foreach (string path in WreckTexturePaths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            foreach (string path in AbandonedTexturePaths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ValidateOrThrow();
        }

        [MenuItem("Bar Promenade/Village/Validate Expansion Pack")]
        public static void ValidateOrThrow()
        {
            var manifest = VillageExpansionAssetProvider.ParseManifestOrThrow(File.ReadAllText(ManifestPath));
            var pictures = AssetDatabase.LoadAssetAtPath<Texture2D>(LodgePicturesPath);
            var pictureImporter = AssetImporter.GetAtPath(LodgePicturesPath) as TextureImporter;
            if (pictures == null || pictures.width < 1024 || pictures.width != pictures.height ||
                pictureImporter == null || pictureImporter.wrapMode != TextureWrapMode.Clamp ||
                !pictureImporter.sRGBTexture || !pictureImporter.mipmapEnabled ||
                pictureImporter.alphaSource != TextureImporterAlphaSource.None)
                throw new InvalidOperationException("Lodge pictures require one opaque square clamped atlas.");
            foreach (string path in AbandonedTexturePaths)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null || texture.width != 1024 || texture.height != 1024)
                    throw new InvalidOperationException("Missing measured abandoned village albedo: " + path);
            }
            foreach (string path in WreckTexturePaths)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texture == null || texture.width < 512 || texture.height < 512 ||
                    textureImporter == null || !textureImporter.sRGBTexture || !textureImporter.mipmapEnabled ||
                    textureImporter.wrapMode != TextureWrapMode.Repeat || textureImporter.alphaSource != TextureImporterAlphaSource.None)
                    throw new InvalidOperationException("Invalid opaque repeatable truck wreck texture: " + path);
            }
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || !Mathf.Approximately(importer.globalScale, 1f) || !importer.useFileScale ||
                !importer.bakeAxisConversion || !importer.isReadable || importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.None || importer.importCameras ||
                importer.importLights || importer.addCollider ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
                throw new InvalidOperationException("Village expansion must import as passive, readable metre geometry.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("Missing village expansion FBX.");
            var found = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !found.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Duplicate expansion mesh.");
            if (found.Count != manifest.mesh_count || model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                model.GetComponentsInChildren<Light>(true).Length != 0 || model.GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Expansion source must contain only the declared passive parts.");
            var importedAnchors = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("ANCHOR_Expansion_", StringComparison.Ordinal))
                    importedAnchors.Add(child.name, child);
            if (importedAnchors.Count != manifest.anchors.Length)
                throw new InvalidOperationException("Expansion imported action anchors differ from the manifest.");
            foreach (VillageExpansionAnchor anchor in manifest.anchors)
                if (!importedAnchors.TryGetValue("ANCHOR_Expansion_" + anchor.kind + "_" + anchor.name, out Transform imported) ||
                    Vector3.Distance(imported.position, V(anchor.position)) > .001f)
                    throw new InvalidOperationException("Expansion action anchor lost its metre position: " + anchor.name);
            var names = new HashSet<string>(StringComparer.Ordinal);
            int sign = 0;
            foreach (VillageExpansionPart part in manifest.parts)
            {
                if (!names.Add(part.mesh) || !found.TryGetValue(part.mesh, out MeshFilter source))
                    throw new InvalidOperationException("Expansion manifest differs from imported mesh catalog.");
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                Vector3[] vertices = source.sharedMesh.vertices;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 p = source.transform.TransformPoint(vertex);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
                if (Vector3.Distance(min, V(part.bounds_min)) > .005f || Vector3.Distance(max, V(part.bounds_max)) > .005f)
                    throw new InvalidOperationException("Expansion imported metre bounds drifted: " + part.mesh);
                if (source.transform.position.sqrMagnitude > .000001f ||
                    Quaternion.Angle(source.transform.rotation, Quaternion.identity) > .001f)
                    throw new InvalidOperationException("Expansion part origin or axes drifted: " + part.mesh);
                int[] triangles = source.sharedMesh.triangles;
                if (part.surface == "Fire" && (part.solid || part.flame_field_vertex_count <= 0 ||
                    source.sharedMesh.uv2.Length != vertices.Length || source.sharedMesh.colors.Length != vertices.Length))
                    throw new InvalidOperationException("Expansion thermal flame lost its UV1/colors: " + part.mesh);
                if (triangles.Length / 3 != part.triangles)
                    throw new InvalidOperationException("Expansion triangle count drifted: " + part.mesh);
                if (part.surface == "LodgePictures")
                {
                    Vector2[] uv = source.sharedMesh.uv;
                    if (uv.Length != vertices.Length)
                        throw new InvalidOperationException("Lodge artwork lost its atlas UVs: " + part.mesh);
                    foreach (Vector2 coordinate in uv)
                        if (coordinate.x < .005f || coordinate.x > .995f || coordinate.y < .005f || coordinate.y > .995f)
                            throw new InvalidOperationException("Lodge artwork escapes its padded atlas: " + part.mesh);
                }
                double volume = 0d;
                for (int i = 0; i < triangles.Length; i += 3)
                    volume += Vector3.Dot(vertices[triangles[i]],
                        Vector3.Cross(vertices[triangles[i + 1]], vertices[triangles[i + 2]])) / 6d;
                Vector3 importedScale = source.transform.lossyScale;
                double metreVolume = volume * importedScale.x * importedScale.y * importedScale.z;
                if (double.IsNaN(volume) || double.IsInfinity(volume) || Math.Abs(metreVolume) < 1e-10d ||
                    (sign != 0 && Math.Sign(volume) != sign))
                    throw new InvalidOperationException("Expansion solid has reversed winding: " + part.mesh);
                sign = Math.Sign(volume);
            }
        }

        private static Vector3 V(float[] a)
        {
            if (a == null || a.Length != 3) throw new InvalidOperationException("Invalid expansion bounds.");
            return new Vector3(a[0], a[1], a[2]);
        }
    }

    public sealed class VillageExpansionModelImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if ((Array.IndexOf(VillageExpansionAssetSetup.WreckTexturePaths, assetPath) < 0 &&
                Array.IndexOf(VillageExpansionAssetSetup.AbandonedTexturePaths, assetPath) < 0 &&
                assetPath != VillageExpansionAssetSetup.LodgePicturesPath) ||
                !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.wrapMode = assetPath == VillageExpansionAssetSetup.LodgePicturesPath
                ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
        }

        private void OnPreprocessModel()
        {
            if (assetPath != VillageExpansionAssetSetup.ModelPath || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None; importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false; importer.importBlendShapes = false;
            importer.addCollider = false; importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true; importer.keepQuads = false; importer.generateSecondaryUV = false;
            importer.isReadable = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
