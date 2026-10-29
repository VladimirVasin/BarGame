using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>The Resources FBX is the passive prefab, retaining its authored unit wrapper.</summary>
    public sealed class CityResidentialCourtyardAssetSetup : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/City/ResidentialCourtyard/";
        public const string ManifestPath = ModelFolder + "CityResidentialCourtyard3D.json";
        public override uint GetVersion() => 1;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) || !(assetImporter is ModelImporter importer)) return;
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
                int dot = part.name.LastIndexOf('.');
                if (dot > 0 && int.TryParse(part.name.Substring(dot + 1), out _)) part.name = part.name.Substring(0, dot);
            }
        }

        [MenuItem("Bar Promenade/City Residential Courtyards/Validate Imported Contract")]
        public static void ValidateOrThrow()
        {
            EnsureImportedAssets();
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != "city_residential_courtyard_kit_v1" ||
                manifest.models == null || manifest.models.Length != CityResidentialCourtyardAssetProvider.PropKindCount + 1)
                throw new InvalidOperationException("Residential courtyard manifest is incomplete.");
            for (int index = 0; index < manifest.models.Length; index++)
            {
                GameObject root = index < CityResidentialCourtyardAssetProvider.PropKindCount
                    ? CityResidentialCourtyardAssetProvider.Create(null, (CityResidentialCourtyardPropKind)index, Vector3.zero, Quaternion.identity)
                    : CityResidentialCourtyardAssetProvider.CreateBasket(null, Vector3.zero, Quaternion.identity);
                try
                {
                    ValidateModel(root, manifest.models[index]);
                    if (index < CityResidentialCourtyardAssetProvider.PropKindCount)
                    {
                        Bounds bounds = CityResidentialCourtyardAssetProvider.GetLocalBounds((CityResidentialCourtyardPropKind)index);
                        Near(bounds.min, manifest.models[index].bounds_min, root.name + " planned minimum");
                        Near(bounds.max, manifest.models[index].bounds_max, root.name + " planned maximum");
                    }
                    if (index == (int)CityResidentialCourtyardPropKind.EntranceCanopy &&
                        Vector3.Dot(CityResidentialCourtyardAssetProvider.FindPart(root.transform,
                            CityResidentialCourtyardAssetProvider.EntranceLightAnchorName).forward, Vector3.down) < .999f)
                        throw new InvalidOperationException("Courtyard entrance source must aim down from its real diffuser.");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            Debug.Log("CITY RESIDENTIAL COURTYARD IMPORTED METRES, SUPPORT ANCHORS AND SHARED SURFACES OK");
        }

        // Imported Resources models are the passive prefabs; finish their
        // finite import set and measure the same wrappers runtime places.
        public static void BuildOrThrow() => ValidateOrThrow();

        public static void EnsureImportedAssets()
        {
            string[] names = { "CourtyardBench", "CourtyardPlanter", "CourtyardUtilityShelter",
                "CourtyardEntranceCanopy", "CourtyardDrain", "CourtyardBasket" };
            foreach (string name in names)
            {
                string path = ModelFolder + name + ".fbx";
                if (!File.Exists(path)) throw new InvalidOperationException("Courtyard authored source is missing: " + path);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null || Resources.Load<GameObject>(CityResidentialCourtyardAssetProvider.ResourceFolder + name) == null)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null || Resources.Load<GameObject>(CityResidentialCourtyardAssetProvider.ResourceFolder + name) == null)
                    throw new InvalidOperationException("Courtyard source failed typed synchronous import: " + path);
            }
        }

        private static void ValidateModel(GameObject root, Model manifest)
        {
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            int triangles = 0, count = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.uv.Length != mesh.vertexCount || mesh.colors.Length != mesh.vertexCount)
                    throw new InvalidOperationException("Courtyard mesh lost authored UV/color data: " + filter.name);
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++) triangles += (int)mesh.GetIndexCount(sub) / 3;
                count++;
            }
            Near(low, manifest.bounds_min, root.name + " minimum");
            Near(high, manifest.bounds_max, root.name + " maximum");
            if (triangles != manifest.triangles || triangles > manifest.maximum_triangles ||
                count != manifest.renderer_count || count > manifest.maximum_renderers)
                throw new InvalidOperationException("Courtyard imported mesh budget differs: " + root.name);
            foreach (Anchor anchor in manifest.anchors)
                Near(root.transform.InverseTransformPoint(CityResidentialCourtyardAssetProvider.FindPart(root.transform,
                    anchor.name).position), anchor.position, anchor.name);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Animator>(true).Length != 0 ||
                root.GetComponentsInChildren<Light>(true).Length != 0 || root.GetComponentsInChildren<AudioSource>(true).Length != 0)
                throw new InvalidOperationException("Courtyard imported art must remain passive.");
        }

        private static void Near(Vector3 actual, float[] expected, string name)
        {
            if (expected == null || expected.Length != 3 || Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .015f)
                throw new InvalidOperationException("Courtyard " + name + " differs from its authored metres: " + actual);
        }

        [Serializable] private sealed class Manifest { public string design_id; public Model[] models; }
        [Serializable] private sealed class Model
        {
            public string name;
            public float[] bounds_min, bounds_max;
            public int triangles, renderer_count, maximum_triangles, maximum_renderers;
            public Anchor[] anchors;
        }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
    }
}
