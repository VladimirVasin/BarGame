using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class CityLaundryAssetSetup : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/City/Laundry/";
        public const string ManifestPath = ModelFolder + "CityLaundry3D.json";
        public override uint GetVersion() => 2;

        private void OnPreprocessTexture()
        {
            if (assetPath != ModelFolder + "LaundrySign.png" || !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true; importer.streamingMipmaps = true; importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 1024;
            importer.anisoLevel = 4; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Standalone", overridden = true, maxTextureSize = 1024,
                format = TextureImporterFormat.BC7, compressionQuality = 100 });
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) ||
                !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None; importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false; importer.importBlendShapes = false;
            importer.addCollider = false; importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true; importer.keepQuads = false;
            importer.generateSecondaryUV = false; importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal)) return;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int suffix = part.name.LastIndexOf('.');
                if (suffix > 0 && int.TryParse(part.name.Substring(suffix + 1), out _))
                    part.name = part.name.Substring(0, suffix);
            }
            // FBX's source wrapper can carry a quarter turn. Driven roots
            // use the imported model's Unity axes; folding leaves retain
            // their already normalized cloth parent. Preserve world geometry.
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                if (!part.name.StartsWith("MOVE_", StringComparison.Ordinal)) continue;
                Transform parent = part.name == "MOVE_ClothLeft" || part.name == "MOVE_ClothRight"
                    ? part.parent : model.transform;
                var children = new Transform[part.childCount];
                for (int i = 0; i < children.Length; i++) children[i] = part.GetChild(i);
                foreach (Transform child in children) child.SetParent(model.transform, true);
                part.SetParent(parent, true);
                part.localRotation = Quaternion.identity; part.localScale = Vector3.one;
                foreach (Transform child in children) child.SetParent(part, true);
            }
        }

        [MenuItem("Bar Promenade/City Laundry/Validate Imported Contract")]
        public static void ValidateOrThrow()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != "city_household_laundry_v1" || manifest.anchors == null)
                throw new InvalidOperationException("Laundry manifest is incomplete.");
            Texture2D sign = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder + "LaundrySign.png");
            var signImporter = AssetImporter.GetAtPath(ModelFolder + "LaundrySign.png") as TextureImporter;
            if (sign == null || signImporter == null || sign.isReadable || sign.mipmapCount < 2 ||
                !signImporter.streamingMipmaps || !signImporter.sRGBTexture || sign.width > 1024 ||
                signImporter.wrapMode != TextureWrapMode.Clamp ||
                signImporter.GetPlatformTextureSettings("Standalone").format != TextureImporterFormat.BC7)
                throw new InvalidOperationException("Laundry sign lost its bounded sRGB/BC7/mips/Clamp contract.");
            using (SHA256 hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(ModelFolder + "LaundrySign.png")))
                    .Replace("-", "").ToLowerInvariant() != manifest.sign_source_sha256)
                    throw new InvalidOperationException("Laundry sign source differs from its authored manifest.");
            GameObject model = CityLaundryAssetProvider.CreateStatic(null);
            try
            {
                Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                int triangles = 0, renderers = 0;
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || mesh.uv.Length != mesh.vertexCount || mesh.colors.Length != mesh.vertexCount)
                        throw new InvalidOperationException("Laundry mesh lost its shared surface data: " + filter.name);
                    for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
                    if (filter.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                    renderers++;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = filter.transform.TransformPoint(vertex);
                        low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                    }
                }
                Near(low, manifest.bounds_min, "minimum"); Near(high, manifest.bounds_max, "maximum");
                if (triangles != manifest.triangles || renderers != manifest.renderer_count ||
                    triangles > manifest.maximum_triangles || renderers > manifest.maximum_renderers)
                    throw new InvalidOperationException("Laundry measured mesh budget differs from its manifest.");
                foreach (Anchor anchor in manifest.anchors)
                    Near(CityLaundryAssetProvider.FindPart(model.transform, anchor.name).position, anchor.position, anchor.name);
                foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
                    if (part.name.StartsWith("MOVE_", StringComparison.Ordinal) &&
                        (Vector3.Dot(part.up, Vector3.up) < .999f ||
                         Vector3.Dot(part.forward, Vector3.forward) < .999f))
                        throw new InvalidOperationException("Laundry driven axes must be Unity metres: " + part.name);
                if (model.GetComponentsInChildren<Light>(true).Length != 0 ||
                    model.GetComponentsInChildren<Animator>(true).Length != 0 ||
                    model.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Laundry source must remain passive.");
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
            Debug.Log("CITY LAUNDRY IMPORTED METRES AND SHARED SURFACES OK");
        }

        private static void Near(Vector3 point, float[] expected, string name)
        {
            if (expected == null || expected.Length != 3 ||
                Vector3.Distance(point, new Vector3(expected[0], expected[1], expected[2])) > .025f)
                throw new InvalidOperationException("Laundry " + name + " differs from authored metres: " + point);
        }
        [Serializable] private sealed class Manifest
        {
            public string design_id;
            public string sign_source_sha256;
            public float[] bounds_min, bounds_max;
            public int triangles, renderer_count, maximum_triangles, maximum_renderers;
            public Anchor[] anchors;
        }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
    }
}
