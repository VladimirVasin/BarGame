using UnityEngine;

namespace BarPromenade
{
    /// <summary>Capture-only reconstruction of the slide's former flat face normals.</summary>
    public static class PistolSlideNormalPilot
    {
        public const string TargetName = "Slide__Steel";

        public static Vector3[] CreateFlatBaseline(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var normals = new Vector3[vertices.Length];
            // The FBX retains separate indexed corners between hard faces.
            // Summing a triangulated polygon recovers its former flat normal,
            // without changing the mesh, UVs, triangles or transform.
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += normal; normals[b] += normal; normals[c] += normal;
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
            return normals;
        }
    }
}
