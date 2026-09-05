using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    public sealed class HomeInteriorModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != HomeInteriorModelAssetSetup.ModelPath) return;
            var model = (ModelImporter)assetImporter;
            model.globalScale = 1f;
            model.useFileScale = true;
            model.bakeAxisConversion = true;
            model.importCameras = false;
            model.importLights = false;
            model.importAnimation = false;
            model.animationType = ModelImporterAnimationType.None;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.addCollider = false;
            model.preserveHierarchy = true;
            model.isReadable = true;
            model.meshCompression = ModelImporterMeshCompression.Off;
            model.optimizeMeshPolygons = false;
            model.optimizeMeshVertices = false;
            model.weldVertices = false;
            model.importNormals = ModelImporterNormals.Import;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            if (imported.Any(path => path == HomeInteriorModelAssetSetup.ModelPath ||
                                     path == HomeInteriorModelAssetSetup.ManifestPath))
                HomeInteriorModelAssetSetup.Schedule();
        }
    }

    [InitializeOnLoad]
    public static class HomeInteriorModelAssetSetup
    {
        public const string ModelPath = "Assets/Home/Interior/Models/HomeInterior3D.fbx";
        public const string ManifestPath = "Assets/Home/Interior/Models/HomeInterior3D.json";
        public const string LibraryPath = "Assets/Resources/Home/HomeInteriorModels.asset";
        private static bool scheduled;
        private static bool building;

        static HomeInteriorModelAssetSetup() => Schedule();

        internal static void Schedule()
        {
            if (scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                if (!EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode &&
                    File.Exists(ModelPath) && File.Exists(ManifestPath)) EnsureAssets();
            };
        }

        [MenuItem("Tools/Bar Promenade/Home/Rebuild authored model library")]
        public static void EnsureAssets()
        {
            if (building) return;
            building = true;
            try
            {
                HomeAuthoredManifest manifest = JsonUtility.FromJson<HomeAuthoredManifest>(
                    File.ReadAllText(ManifestPath));
                if (manifest == null || manifest.design_id != "home_interior_v1" || manifest.parts == null)
                    throw new InvalidOperationException("Invalid Home Blender manifest.");
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null) throw new InvalidOperationException("Home FBX has not imported.");
                if (model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                    model.GetComponentsInChildren<Light>(true).Length != 0 ||
                    model.GetComponentsInChildren<Camera>(true).Length != 0 ||
                    model.GetComponentsInChildren<Animator>(true).Length != 0)
                    throw new InvalidOperationException("Home model must be passive.");
                var filters = model.GetComponentsInChildren<MeshFilter>(true)
                    .ToDictionary(filter => filter.name, StringComparer.Ordinal);
                HomeInteriorModelLibrary library =
                    AssetDatabase.LoadAssetAtPath<HomeInteriorModelLibrary>(LibraryPath);
                if (library == null)
                {
                    library = ScriptableObject.CreateInstance<HomeInteriorModelLibrary>();
                    AssetDatabase.CreateAsset(library, LibraryPath);
                }
                var previousMeshes = AssetDatabase.LoadAllAssetsAtPath(LibraryPath).OfType<Mesh>()
                    .ToDictionary(mesh => mesh.name, StringComparer.Ordinal);
                foreach (HomeAuthoredPart part in manifest.parts)
                {
                    if (!filters.TryGetValue(part.name, out MeshFilter filter))
                        throw new InvalidOperationException($"Missing imported Home part '{part.name}'.");
                    // Bake the imported root's 100 unit factor and axis conversion once.
                    // Object placement is plan-owned; only its linear transform belongs in the mesh.
                    Matrix4x4 basis = filter.transform.localToWorldMatrix;
                    basis.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                    Mesh normalized = Object.Instantiate(filter.sharedMesh);
                    normalized.name = part.name;
                    normalized.vertices = normalized.vertices.Select(basis.MultiplyPoint3x4).ToArray();
                    Matrix4x4 normals = basis.inverse.transpose;
                    normalized.normals = normalized.normals.Select(value => normals.MultiplyVector(value).normalized).ToArray();
                    if (normalized.tangents.Length != 0)
                        normalized.tangents = normalized.tangents.Select(value =>
                        {
                            Vector3 direction = basis.MultiplyVector(new Vector3(value.x, value.y, value.z)).normalized;
                            return new Vector4(direction.x, direction.y, direction.z, value.w);
                        }).ToArray();
                    normalized.RecalculateBounds();
                    if (part.role == "grid") ReorderGrid(normalized, part);
                    Vector3 minimum = HomeAuthoredPart.Vector(part.bounds_min);
                    Vector3 maximum = HomeAuthoredPart.Vector(part.bounds_max);
                    if (Vector3.Distance(normalized.bounds.min, minimum) > 0.012f ||
                        Vector3.Distance(normalized.bounds.max, maximum) > 0.012f)
                        throw new InvalidOperationException($"Home import scale/axis mismatch for '{part.name}': " +
                            $"{normalized.bounds.min}/{normalized.bounds.max}, expected {minimum}/{maximum}.");
                    if (previousMeshes.TryGetValue(part.name, out Mesh existing))
                    {
                        EditorUtility.CopySerialized(normalized, existing);
                        Object.DestroyImmediate(normalized);
                        part.mesh = existing;
                    }
                    else
                    {
                        AssetDatabase.AddObjectToAsset(normalized, library);
                        part.mesh = normalized;
                    }
                }
                foreach (Mesh old in previousMeshes.Values)
                    if (!manifest.parts.Any(part => part.mesh == old)) Object.DestroyImmediate(old, true);
                library.Configure(manifest.parts, manifest.signature);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
            }
            finally { building = false; }
        }

        private static void ReorderGrid(Mesh mesh, HomeAuthoredPart part)
        {
            int topCount = part.grid_columns * part.grid_rows * 4;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            if (vertices.Length != topCount + 20)
                throw new InvalidOperationException($"'{part.name}' must preserve independent grid quads: " +
                    $"{vertices.Length} vertices instead of {topCount + 20}.");
            var order = new int[vertices.Length];
            var used = new bool[vertices.Length];
            int output = 0;
            for (int row = 0; row < part.grid_rows; row++)
                for (int column = 0; column < part.grid_columns; column++)
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Vector3 expected = new Vector3(
                            -part.Size.x * 0.5f + (column + (corner % 2)) * part.Size.x / part.grid_columns,
                            part.Size.y * 0.5f,
                            -part.Size.z * 0.5f + (row + (corner / 2)) * part.Size.z / part.grid_rows);
                        int found = -1;
                        for (int source = 0; source < vertices.Length; source++)
                            if (!used[source] && normals[source].y > 0.9f &&
                                (vertices[source] - expected).sqrMagnitude < 0.000001f)
                            { found = source; break; }
                        if (found < 0) throw new InvalidOperationException($"Missing bed grid corner {expected}.");
                        used[found] = true;
                        order[output++] = found;
                    }
            for (int source = 0; source < vertices.Length; source++)
                if (!used[source]) order[output++] = source;
            var inverse = new int[vertices.Length];
            for (int index = 0; index < order.Length; index++) inverse[order[index]] = index;
            int[] triangles = mesh.triangles;
            mesh.vertices = order.Select(index => vertices[index]).ToArray();
            mesh.normals = order.Select(index => normals[index]).ToArray();
            mesh.uv = order.Select(index => uv[index]).ToArray();
            mesh.tangents = Array.Empty<Vector4>();
            mesh.triangles = triangles.Select(index => inverse[index]).ToArray();
        }
    }
}
