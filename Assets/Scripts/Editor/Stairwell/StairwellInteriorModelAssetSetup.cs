using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    public sealed class StairwellInteriorModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != StairwellInteriorModelAssetSetup.ModelPath) return;
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

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(path => path == StairwellInteriorModelAssetSetup.ModelPath ||
                path == StairwellInteriorModelAssetSetup.ManifestPath)) StairwellInteriorModelAssetSetup.Schedule();
        }
    }

    [InitializeOnLoad]
    public static class StairwellInteriorModelAssetSetup
    {
        public const string ModelPath = "Assets/Stairwell/Interior/Models/StairwellInterior3D.fbx";
        public const string ManifestPath = "Assets/Stairwell/Interior/Models/StairwellInterior3D.json";
        public const string LibraryPath = "Assets/Resources/Stairwell/StairwellInteriorModels.asset";
        private static bool scheduled, building;
        static StairwellInteriorModelAssetSetup() => Schedule();
        internal static void Schedule()
        {
            // Batch verification owns its explicit prebuild setup. Importing
            // an unrelated scene must not silently rewrite this library.
            if (scheduled || Application.isBatchMode) return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                if (!EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode &&
                    File.Exists(ModelPath) && File.Exists(ManifestPath)) EnsureAssets();
            };
        }

        [MenuItem("Tools/Bar Promenade/Stairwell/Rebuild authored model library")]
        public static void EnsureAssets()
        {
            if (building) return;
            building = true;
            try
            {
                StairwellAuthoredManifest manifest = JsonUtility.FromJson<StairwellAuthoredManifest>(File.ReadAllText(ManifestPath));
                if (manifest == null || manifest.design_id != "stairwell_interior_v1" ||
                    manifest.parts == null || manifest.parts.Length > 128 || manifest.parts.Sum(part => part.triangles) > 16000)
                    throw new InvalidOperationException("Invalid Stairwell Blender manifest.");
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null) throw new InvalidOperationException("Stairwell FBX has not imported.");
                if (model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                    model.GetComponentsInChildren<Light>(true).Length != 0 ||
                    model.GetComponentsInChildren<Camera>(true).Length != 0 ||
                    model.GetComponentsInChildren<Animator>(true).Length != 0)
                    throw new InvalidOperationException("Stairwell model must be passive.");
                var filters = model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name, StringComparer.Ordinal);
                StairwellInteriorModelLibrary library = AssetDatabase.LoadAssetAtPath<StairwellInteriorModelLibrary>(LibraryPath);
                if (library == null)
                {
                    library = ScriptableObject.CreateInstance<StairwellInteriorModelLibrary>();
                    AssetDatabase.CreateAsset(library, LibraryPath);
                }
                var previous = AssetDatabase.LoadAllAssetsAtPath(LibraryPath).OfType<Mesh>().ToDictionary(m => m.name, StringComparer.Ordinal);
                foreach (StairwellAuthoredPart part in manifest.parts)
                {
                    if (!filters.TryGetValue(part.name, out MeshFilter filter))
                        throw new InvalidOperationException("Missing imported Stairwell part " + part.name);
                    Matrix4x4 metres = filter.transform.localToWorldMatrix;
                    metres.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                    Mesh normalized = Object.Instantiate(filter.sharedMesh);
                    normalized.name = part.name;
                    Vector3[] measured = normalized.vertices.Select(metres.MultiplyPoint3x4).ToArray();
                    var bounds = new Bounds(measured[0], Vector3.zero);
                    foreach (Vector3 vertex in measured) bounds.Encapsulate(vertex);
                    if (Vector3.Distance(bounds.min, StairwellAuthoredPart.Vector(part.bounds_min)) > .001f ||
                        Vector3.Distance(bounds.max, StairwellAuthoredPart.Vector(part.bounds_max)) > .001f)
                        throw new InvalidOperationException("Stairwell import scale/axis mismatch for " + part.name + ": " + bounds);
                    Vector3 size = part.Size;
                    Matrix4x4 basis = Matrix4x4.Scale(new Vector3(1f/size.x, 1f/size.y, 1f/size.z)) * metres;
                    normalized.vertices = normalized.vertices.Select(basis.MultiplyPoint3x4).ToArray();
                    Matrix4x4 normalBasis = basis.inverse.transpose;
                    normalized.normals = normalized.normals.Select(n => normalBasis.MultiplyVector(n).normalized).ToArray();
                    normalized.tangents = Array.Empty<Vector4>();
                    normalized.RecalculateBounds();
                    if (previous.TryGetValue(part.name, out Mesh existing))
                    {
                        EditorUtility.CopySerialized(normalized, existing);
                        Object.DestroyImmediate(normalized);
                        part.mesh = existing;
                    }
                    else { AssetDatabase.AddObjectToAsset(normalized, library); part.mesh = normalized; }
                }
                foreach (Mesh old in previous.Values)
                    if (!manifest.parts.Any(p => p.mesh == old)) Object.DestroyImmediate(old, true);
                library.Configure(manifest.parts, manifest.signature);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
            }
            finally { building = false; }
        }
    }
}
