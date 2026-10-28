using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Read-only proof of the passive topology required by City and Home.</summary>
    public static class CityStreetProfileAssetValidation
    {
        private const string Resource = "City/StreetProfile/CityStreetProfile3D";
        private const string ModelPath = "Assets/Resources/" + Resource + ".fbx";
        private const string ManifestPath = "Assets/Resources/" + Resource + ".json";

        [Serializable] private sealed class Part { public int triangles; public float[] min, max; }
        [Serializable] private sealed class Parts { public Part SurfacePatch, BoundaryStrip, StreetProfile; }
        [Serializable] private sealed class Manifest
        {
            public string version, signature;
            public float asphalt_width, public_sidewalk_width, road_top, sidewalk_top;
            public Parts parts;
        }

        [MenuItem("Bar Promenade/City/Validate Street Profile (Read Only)")]
        public static void ValidateOrThrow()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (model == null || model != Resources.Load<GameObject>(Resource) || source == null)
                throw new InvalidOperationException("Missing required Blender street profile model or manifest.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || !importer.isReadable || !importer.useFileScale ||
                !importer.bakeAxisConversion || !importer.preserveHierarchy || importer.globalScale != 1f ||
                importer.importAnimation || importer.importLights || importer.importCameras || importer.addCollider ||
                importer.importBlendShapes || importer.weldVertices || importer.generateSecondaryUV ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
                throw new InvalidOperationException("Street profile lost its passive readable metre import contract.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(source.text);
            if (manifest == null || manifest.version != "1.0.0" || manifest.signature?.Length != 64 ||
                manifest.asphalt_width != 6f || manifest.public_sidewalk_width != 2f ||
                manifest.road_top != .08f || manifest.sidewalk_top != .14f || manifest.parts == null)
                throw new InvalidOperationException("Street profile manifest lost its authored metric contract.");
            ValidateModel(model);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Part part = filter.name switch
                {
                    "SurfacePatch" => manifest.parts.SurfacePatch,
                    "BoundaryStrip" => manifest.parts.BoundaryStrip,
                    "StreetProfile" => manifest.parts.StreetProfile,
                    _ => null
                };
                if (part == null || part.min?.Length != 3 || part.max?.Length != 3 ||
                    filter.sharedMesh.triangles.Length / 3 != part.triangles)
                    throw new InvalidOperationException("Street profile manifest topology differs: " + filter.name);
                Bounds bounds = BoundsOf(filter);
                if ((bounds.min - Read(part.min)).sqrMagnitude > .000001f ||
                    (bounds.max - Read(part.max)).sqrMagnitude > .000001f)
                    throw new InvalidOperationException("Street profile manifest metres differ: " + filter.name);
            }
        }

        /// <summary>Also used by controlled negative fixtures; never changes the supplied model.</summary>
        public static void ValidateModel(GameObject model)
        {
            if (model == null) throw new InvalidOperationException("Missing required Blender street profile model.");
            if (model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                model.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                model.GetComponentsInChildren<Animator>(true).Length != 0 ||
                model.GetComponentsInChildren<Light>(true).Length != 0 ||
                model.GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Street profile source must remain passive.");
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != 3) throw new InvalidOperationException("Street profile requires exactly three authored parts.");
            bool patch = false, strip = false, section = false;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.sharedMesh.vertexCount == 0)
                    throw new InvalidOperationException("Street profile mesh is empty or unreadable: " + filter.name);
                Bounds bounds = BoundsOf(filter);
                if (!Finite(bounds.min) || !Finite(bounds.max))
                    throw new InvalidOperationException("Street profile contains non-finite metres: " + filter.name);
                if (filter.name == "SurfacePatch" && !patch)
                {
                    patch = true;
                    ValidateDeformationPart(filter, new[] { Vector3.zero, Vector3.right, Vector3.forward },
                        new[] { 0, 2, 1 });
                }
                else if (filter.name == "BoundaryStrip" && !strip)
                {
                    strip = true;
                    ValidateDeformationPart(filter, new[] { Vector3.zero, Vector3.right, Vector3.down, Vector3.right + Vector3.down },
                        new[] { 0, 1, 2, 2, 1, 3 });
                }
                else if (filter.name == "StreetProfile" && !section)
                {
                    section = true;
                    if ((bounds.min - new Vector3(-5f, -.16f, 0f)).sqrMagnitude > .000001f ||
                        (bounds.max - new Vector3(5f, .14f, 1f)).sqrMagnitude > .000001f ||
                        filter.sharedMesh.triangles.Length != 36 * 3)
                        throw new InvalidOperationException("Street profile section lost its authored envelope.");
                }
                else throw new InvalidOperationException("Unexpected or duplicate street profile part: " + filter.name);
            }
        }

        private static void ValidateDeformationPart(MeshFilter filter, Vector3[] expected, int[] triangles)
        {
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] indices = filter.sharedMesh.triangles;
            if (indices.Length != triangles.Length)
                throw new InvalidOperationException("Street deformation topology differs: " + filter.name);
            var mapped = new int[vertices.Length];
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                Vector3 point = filter.transform.TransformPoint(vertices[vertex]);
                mapped[vertex] = -1;
                for (int corner = 0; corner < expected.Length; corner++)
                    if ((point - expected[corner]).sqrMagnitude < .000001f) { mapped[vertex] = corner; break; }
                if (mapped[vertex] < 0) throw new InvalidOperationException("Street deformation corner differs: " + filter.name);
            }
            var seen = new bool[triangles.Length / 3];
            for (int index = 0; index < indices.Length; index += 3)
            {
                bool found = false;
                for (int face = 0; face < seen.Length && !found; face++)
                for (int shift = 0; shift < 3 && !found; shift++)
                    if (!seen[face] && mapped[indices[index]] == triangles[face * 3 + shift] &&
                        mapped[indices[index + 1]] == triangles[face * 3 + (shift + 1) % 3] &&
                        mapped[indices[index + 2]] == triangles[face * 3 + (shift + 2) % 3])
                    { seen[face] = true; found = true; }
                if (!found) throw new InvalidOperationException("Street deformation winding or coverage differs: " + filter.name);
            }
        }

        private static Bounds BoundsOf(MeshFilter filter)
        {
            Vector3[] vertices = filter.sharedMesh.vertices;
            Bounds bounds = new Bounds(filter.transform.TransformPoint(vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(filter.transform.TransformPoint(vertex));
            return bounds;
        }
        private static Vector3 Read(float[] value) => new Vector3(value[0], value[1], value[2]);
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
