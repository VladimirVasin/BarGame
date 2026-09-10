using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Passive imported hierarchy, metre measurements and normalized
    /// moving pivots; runtime never reconstructs the authored geometry.</summary>
    public sealed class CityCanneryAssetSetup : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/City/Cannery/";
        public const string ManifestPath = ModelFolder + "CityCannery3D.json";
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ModelFolder+"Textures/",StringComparison.Ordinal)||
                !(assetImporter is TextureImporter importer)) return;
            importer.textureType=TextureImporterType.Default;
            importer.textureShape=TextureImporterShape.Texture2D;
            importer.sRGBTexture=true;
            importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.filterMode=FilterMode.Bilinear;
            importer.wrapMode=TextureWrapMode.Repeat;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=512;
            importer.anisoLevel=4;
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) ||
                !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.isReadable = true;
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
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                if (!part.name.StartsWith("MOVE_", StringComparison.Ordinal)) continue;
                var children = new Transform[part.childCount];
                for (int i = 0; i < children.Length; i++) children[i] = part.GetChild(i);
                foreach (Transform child in children) child.SetParent(model.transform, true);
                part.SetParent(model.transform, true);
                part.localRotation = Quaternion.identity;
                part.localScale = Vector3.one;
                foreach (Transform child in children) child.SetParent(part, true);
            }
        }

        [MenuItem("Bar Promenade/City Cannery/Validate Imported Contract")]
        public static void ValidateOrThrow()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != "city_compact_fish_cannery_v1" ||
                manifest.parts == null || manifest.parts.Length != CityCanneryAssetProvider.ModelNames.Length)
                throw new InvalidOperationException("Cannery art manifest is incomplete.");
            foreach (Part part in manifest.parts)
            {
                GameObject model = CityCanneryAssetProvider.Create(part.name, null);
                try
                {
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    int triangles = 0;
                    foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Mesh mesh = filter.sharedMesh;
                        if (mesh == null || mesh.colors.Length != mesh.vertexCount || mesh.uv.Length != mesh.vertexCount)
                            throw new InvalidOperationException("Cannery lost painted mesh/UV data: " + filter.name);
                        for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
                        if (filter.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                        foreach (Vector3 vertex in mesh.vertices)
                        {
                            Vector3 point = filter.transform.TransformPoint(vertex);
                            low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                        }
                    }
                    Near(low, part.bounds_min, part.name + " minimum");
                    Near(high, part.bounds_max, part.name + " maximum");
                    if (triangles != part.triangles)
                        throw new InvalidOperationException("Cannery mesh triangle contract differs: " + part.name);
                    foreach (Anchor anchor in part.anchors)
                    {
                        Transform point = CityCanneryAssetProvider.FindPart(model, anchor.name);
                        Near(point.position, anchor.position, part.name + " " + anchor.name);
                        if (anchor.name.StartsWith("MOVE_", StringComparison.Ordinal) &&
                            (Vector3.Dot(point.up, Vector3.up) < .999f || Vector3.Dot(point.forward, Vector3.forward) < .999f))
                            throw new InvalidOperationException("Cannery moving part lacks normalized Unity basis: " + anchor.name);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            Debug.Log("CITY CANNERY IMPORTED METRES, PIVOTS AND ANCHORS OK");
        }

        private static void Near(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 ||
                Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .025f)
                throw new InvalidOperationException($"Cannery {label} differs from authored metres: {actual}.");
        }
        [Serializable] private sealed class Manifest { public string design_id; public Part[] parts; }
        [Serializable] private sealed class Part
        {
            public string name; public float[] bounds_min; public float[] bounds_max;
            public int triangles; public Anchor[] anchors;
        }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
    }
}
