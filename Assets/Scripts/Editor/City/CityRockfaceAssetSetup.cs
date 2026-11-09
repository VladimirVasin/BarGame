using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Bake FBX axes/units into the single stitchable source mesh on import.</summary>
    public sealed class CityRockfaceAssetSetup : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/City/Rockface/";
        private const float Epsilon = .00001f;

        public override uint GetVersion() => 1;
        private bool IsRockface => assetPath.StartsWith(Folder + "CityRockface", StringComparison.Ordinal) &&
                                   assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        private void OnPreprocessModel()
        {
            if (!IsRockface) return;
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
            importer.weldVertices = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsRockface) return;
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != 1 || filters[0].sharedMesh == null)
                throw new InvalidDataException("Expected one rockface source mesh: " + assetPath);
            Mesh mesh = filters[0].sharedMesh;
            Matrix4x4 frame = filters[0].transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = frame.inverse.transpose;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = frame.MultiplyPoint3x4(vertices[i]);
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            if (frame.determinant < 0f)
            {
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                    (triangles[i], triangles[i + 1]) = (triangles[i + 1], triangles[i]);
                mesh.triangles = triangles;
            }
            mesh.RecalculateBounds();
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
            {
                node.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                node.localScale = Vector3.one;
            }
            ValidatePatch(mesh);
        }

        [Serializable] private sealed class Variant
        {
            public string name;
            public int triangles;
        }

        [Serializable] private sealed class Manifest
        {
            public Variant[] models;
        }

        [MenuItem("Bar Promenade/City/Validate Rockface Meshes")]
        public static void BuildOrThrow()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "CityRockface3D.json"));
            if (manifest?.models?.Length != CityRockfaceAssetProvider.VariantCount)
                throw new InvalidDataException("Missing rockface source manifest entries.");
            for (int i = 0; i < manifest.models.Length; i++)
            {
                string name = "CityRockface" + i;
                AssetDatabase.ImportAsset(Folder + name + ".fbx", ImportAssetOptions.ForceSynchronousImport);
                Mesh mesh = CityRockfaceAssetProvider.GetMesh(i);
                if (manifest.models[i].name != name || mesh.GetIndexCount(0) != manifest.models[i].triangles * 3)
                    throw new InvalidDataException("Rockface does not match its authored source: " + name);
                ValidatePatch(mesh);
            }
        }

        private static void ValidatePatch(Mesh mesh)
        {
            if (!mesh.isReadable || mesh.subMeshCount != 1 || mesh.bounds.min.magnitude > Epsilon ||
                (mesh.bounds.max - Vector3.one).magnitude > Epsilon || mesh.GetIndexCount(0) > 160 * 3)
                throw new InvalidDataException("Unexpected City rockface envelope: " + mesh.name + " " + mesh.bounds);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            var boundary = new HashSet<Vector2Int>();
            foreach (Vector3 vertex in vertices)
            {
                if (vertex.x > Epsilon && vertex.x < 1f-Epsilon && vertex.y > Epsilon && vertex.y < 1f-Epsilon)
                    continue;
                var quarter = Vector2Int.RoundToInt(new Vector2(vertex.x, vertex.y) * 4f);
                if ((vertex - new Vector3(quarter.x, quarter.y, 0f) * .25f).magnitude > Epsilon)
                    throw new InvalidDataException("Rockface seam leaves its shared quarter interval: " + mesh.name);
                boundary.Add(quarter);
            }
            double projected = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                Vector3 direction = cross.normalized;
                if (cross.z <= 0f || Vector3.Dot(normals[a], direction) < .995f ||
                    Vector3.Dot(normals[b], direction) < .995f || Vector3.Dot(normals[c], direction) < .995f)
                    throw new InvalidDataException("Rockface triangle has incorrect City-facing winding or normals: " + mesh.name);
                projected += cross.z * .5;
            }
            if (boundary.Count != 16 || Math.Abs(projected - 1d) > Epsilon)
                throw new InvalidDataException("Rockface lost its stitching perimeter or projected area: " + mesh.name);
        }
    }
}
