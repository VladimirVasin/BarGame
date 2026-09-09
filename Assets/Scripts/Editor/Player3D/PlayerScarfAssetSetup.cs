using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports the authored scarf and measures it; never generates source geometry.</summary>
    public static class PlayerScarfAssetSetup
    {
        public const string WornPath = "Assets/Resources/Player/Scarf/ScarfWorn.fbx";
        public const string FoldedPath = "Assets/Resources/Player/Scarf/ScarfFolded.fbx";
        public const string ManifestPath = "Assets/Resources/Player/Scarf/PlayerScarf3D.json";
        private const string GeneratorPath = "tools/build-player-scarf-3d-model.py";

        [Serializable] private sealed class Manifest
        {
            public string generator_sha256;
            public string source_hero;
            public string source_hero_sha256;
            public string atlas_cell;
            public Part[] parts;
        }
        [Serializable] private sealed class Part
        {
            public string name;
            public int triangles;
            public float[] bounds_min;
            public float[] bounds_max;
        }

        [MenuItem("Bar Promenade/Player/Import and Validate Scarf")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(WornPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(FoldedPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            if (!File.Exists(ManifestPath)) throw new InvalidOperationException("Scarf manifest is missing.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.parts == null || manifest.parts.Length != 4 ||
                manifest.atlas_cell != "BookCloth" || Hash(GeneratorPath) != manifest.generator_sha256 ||
                Hash(manifest.source_hero) != manifest.source_hero_sha256)
                throw new InvalidOperationException("The scarf manifest is stale; run its deterministic Blender generator.");
            var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
            foreach (Part part in manifest.parts) parts.Add(part.name, part);
            ValidateModel(WornPath, true, parts);
            ValidateModel(FoldedPath, false, parts);
            if (Resources.Load<Texture2D>(PlayerScarfResources.AtlasResourcePath) == null)
                throw new InvalidOperationException("Scarf BookCloth atlas is missing.");
        }

        private static void ValidateModel(string path, bool worn, Dictionary<string, Part> parts)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new InvalidOperationException("Missing scarf model " + path);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || !importer.isReadable || !importer.bakeAxisConversion || !importer.useFileScale ||
                importer.importAnimation || (worn && !importer.importBlendShapes))
                throw new InvalidOperationException("Incorrect scarf import settings " + path);
            // A standalone FBX can keep its metre conversion on its root. The
            // measurement frame must be outside that root, as it is in gameplay.
            GameObject frame = new GameObject("Scarf Import Metre Measurement");
            frame.hideFlags = HideFlags.HideAndDontSave;
            GameObject instance = UnityEngine.Object.Instantiate(source, frame.transform, false);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != (worn ? 3 : 1))
                    throw new InvalidOperationException("Unexpected scarf renderer count.");
                foreach (Renderer renderer in renderers)
                {
                    if (!parts.TryGetValue(renderer.name, out Part part))
                        throw new InvalidOperationException("Unexpected scarf part " + renderer.name);
                    var skinned = renderer as SkinnedMeshRenderer;
                    Mesh mesh = skinned != null ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh == null || mesh.triangles.Length / 3 != part.triangles)
                        throw new InvalidOperationException("Scarf topology does not match its manifest.");
                    foreach (Vector2 uv in mesh.uv)
                        if (uv.x <= 0f || uv.y <= 0f || uv.x >= .25f || uv.y >= .25f)
                            throw new InvalidOperationException("Scarf UVs leave the shared BookCloth tile.");
                    Bounds measured = Measure(renderer.transform, frame.transform, mesh.vertices);
                    Vector3 expected = new Vector3(part.bounds_max[0] - part.bounds_min[0],
                        part.bounds_max[2] - part.bounds_min[2], part.bounds_max[1] - part.bounds_min[1]);
                    if ((measured.size - expected).magnitude > .003f)
                        throw new InvalidOperationException("Scarf imported metre dimensions are incorrect: " + renderer.name +
                            "; measured=" + measured.size.ToString("F6") + "; expected=" + expected.ToString("F6") +
                            "; importedRootScale=" + instance.transform.localScale.ToString("F6") +
                            "; rendererWorldScale=" + renderer.transform.lossyScale.ToString("F6"));
                    if (worn && (skinned == null || skinned.bones.Length == 0))
                        throw new InvalidOperationException("The worn scarf lost its production bone weights.");
                    if (renderer.name == "ScarfWrap")
                    {
                        bool hasLowered = false;
                        for (int i = 0; i < mesh.blendShapeCount; i++)
                            hasLowered |= mesh.GetBlendShapeName(i).EndsWith("MouthLowered", StringComparison.Ordinal);
                        if (!hasLowered) throw new InvalidOperationException("The scarf cannot open the hero's mouth.");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(frame); }
        }

        private static Bounds Measure(Transform part, Transform root, Vector3[] vertices)
        {
            if (vertices.Length == 0) throw new InvalidOperationException("Empty scarf mesh.");
            Matrix4x4 matrix = root.worldToLocalMatrix * part.localToWorldMatrix;
            Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
            return bounds;
        }

        private static string Hash(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return string.Empty;
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
