using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Make the single imported mesh usable directly in metre-scaled spatial batches.</summary>
    public sealed class CityRockfallAssetSetup : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/City/Rockfall/";
        private const float Tolerance = .001f;
        public override uint GetVersion() => 1;

        private bool IsRockfallModel => assetPath.StartsWith(Folder + "CityRockfall", StringComparison.Ordinal) &&
                                        assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        private void OnPreprocessModel()
        {
            if (!IsRockfallModel) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsRockfallModel) return;
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != 1 || filters[0].sharedMesh == null)
                throw new InvalidDataException(assetPath + " requires a single rock mesh.");
            Mesh mesh = filters[0].sharedMesh;
            Matrix4x4 transform = filters[0].transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = transform.inverse.transpose;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = transform.MultiplyPoint3x4(vertices[i]);
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            if (transform.determinant < 0f)
            {
                var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                    (triangles[i], triangles[i + 1]) = (triangles[i + 1], triangles[i]);
                mesh.triangles = triangles;
            }
            mesh.RecalculateBounds();
            RequireUnitMesh(mesh);
            // FBX unit and axis conversion are now baked into the imported subasset.
            // Reimport always starts from the FBX, never from a previously baked mesh.
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
            {
                node.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                node.localScale = Vector3.one;
            }
        }

        [Serializable] private sealed class RockfallModel
        {
            public string name;
            public int triangles;
            public float front_max_y;
            public float rear_max_y;
        }

        [Serializable] private sealed class Manifest
        {
            public RockfallModel[] models;
        }

        [MenuItem("Bar Promenade/City/Validate Rockfall Meshes")]
        public static void BuildOrThrow()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "CityRockfall3D.json"));
            if (manifest?.models?.Length != CityRockfallAssetProvider.VariantCount)
                throw new InvalidDataException("Rockfall manifest has the wrong number of models.");

            for (int i = 0; i < manifest.models.Length; i++)
            {
                RockfallModel entry = manifest.models[i];
                string name = "CityRockfall" + i;
                string path = Folder + name + ".fbx";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Mesh mesh = CityRockfallAssetProvider.GetMesh(i);
                RequireUnitMesh(mesh);
                if (entry.name != name || !mesh.isReadable || mesh.GetIndexCount(0) != entry.triangles * 3 ||
                    mesh.uv.Length != mesh.vertexCount || mesh.normals.Length != mesh.vertexCount)
                    throw new InvalidDataException("Rockfall import differs from its manifest: " + path);
                float front = float.NegativeInfinity, rear = float.NegativeInfinity;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    if (vertex.z <= -.25f) front = Mathf.Max(front, vertex.y);
                    if (vertex.z >= .10f) rear = Mathf.Max(rear, vertex.y);
                }
                if (Mathf.Abs(front - entry.front_max_y) > Tolerance || Mathf.Abs(rear - entry.rear_max_y) > Tolerance)
                    throw new InvalidDataException("Rockfall no longer rises toward +Z: " + path);
            }
        }

        private static void RequireUnitMesh(Mesh mesh)
        {
            Bounds bounds = mesh.bounds;
            if (mesh.subMeshCount != 1 || bounds.center.sqrMagnitude > Tolerance * Tolerance ||
                (bounds.size - Vector3.one).sqrMagnitude > Tolerance * Tolerance)
                throw new InvalidDataException($"Rockfall {mesh.name} has incorrect placement bounds {bounds}.");
        }
    }
}
