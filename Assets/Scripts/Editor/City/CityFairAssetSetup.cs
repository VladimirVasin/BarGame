using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Passive fair import and measured metre/anchor contracts. The
    /// provider supplies canonical moving pivots while preserving imported geometry.</summary>
    public sealed class CityFairAssetSetup : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/City/Fair/";
        public const string ManifestPath = ModelFolder + "CityFair3D.json";
        public override uint GetVersion() => 2;

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
        }

        private static bool IsDriven(string name) => name == "CrankPivot" || name == "BellSwingPivot" ||
            name == "RopePivot" || name == "RopeSpanPivot" || name == "Wire" || name.StartsWith("Fixture_", StringComparison.Ordinal) ||
            name.StartsWith("Mount_", StringComparison.Ordinal);

        [MenuItem("Bar Promenade/City Fair/Validate Imported Contract")]
        public static void ValidateOrThrow()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest?.models == null || manifest.models.Length != CityFairAssetProvider.ModelNames.Length)
                throw new InvalidOperationException("Fair art manifest is incomplete.");
            foreach (Model entry in manifest.models)
            {
                GameObject model = CityFairAssetProvider.Create(entry.name, null);
                try
                {
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    int triangles = 0, count = 0;
                    foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Mesh mesh = filter.sharedMesh;
                        if (mesh == null || mesh.uv.Length != mesh.vertexCount || mesh.subMeshCount != 1)
                            throw new InvalidOperationException("Fair mesh lost its UV or material contract: " + filter.name);
                        foreach (Vector3 vertex in mesh.vertices)
                        {
                            Vector3 position = filter.transform.TransformPoint(vertex);
                            low = Vector3.Min(low, position); high = Vector3.Max(high, position);
                        }
                        triangles += (int)mesh.GetIndexCount(0) / 3;
                        count++;
                    }
                    Near(low, entry.bounds_min, entry.name + " minimum");
                    Near(high, entry.bounds_max, entry.name + " maximum");
                    if (triangles != entry.triangle_count || count != entry.mesh_count)
                        throw new InvalidOperationException("Fair mesh/triangle count differs: " + entry.name);
                    foreach (Anchor anchor in entry.anchors)
                    {
                        Transform part = CityFairAssetProvider.FindPart(model, anchor.name);
                        Near(part.position, anchor.position, entry.name + " " + anchor.name);
                        if (!string.IsNullOrEmpty(anchor.parent) &&
                            !part.IsChildOf(CityFairAssetProvider.FindPart(model, anchor.parent)))
                            throw new InvalidOperationException("Fair hand anchor lost its moving parent: " + anchor.name);
                        if (IsDriven(anchor.name) &&
                            (Vector3.Dot(part.forward, Vector3.forward) < .999f || Vector3.Dot(part.up, Vector3.up) < .999f))
                            throw new InvalidOperationException("Fair pivot lacks canonical Unity axes: " + anchor.name);
                    }
                    if (model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                        model.GetComponentsInChildren<Light>(true).Length != 0 ||
                        model.GetComponentsInChildren<Animator>(true).Length != 0)
                        throw new InvalidOperationException("Fair asset must remain passive: " + entry.name);
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            Debug.Log("CITY FAIR IMPORTED METRES, MATERIALS, PIVOTS AND ANCHORS OK");
        }

        private static void Near(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 ||
                Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .025f)
                throw new InvalidOperationException($"Fair {label} differs from authored metres: {actual}.");
        }

        [Serializable] private sealed class Manifest { public Model[] models; }
        [Serializable] private sealed class Model
        {
            public string name; public float[] bounds_min; public float[] bounds_max;
            public int triangle_count; public int mesh_count; public Anchor[] anchors;
        }
        [Serializable] private sealed class Anchor { public string name; public float[] position; public string parent; }
    }
}
