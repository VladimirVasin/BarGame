using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Spatial render batches of the continuous, shared collision grid.</summary>
    internal static class AlpineVillageTerrainChunks
    {
        private const float ChunkSize = 48f;

        public static void Create(Transform parent, Mesh source, MeshRenderer sourceRenderer)
        {
            Vector3[] positions = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uvs = source.uv;
            var chunks = new Dictionary<Vector2Int, Chunk>();
            for (int material = 0; material < source.subMeshCount; material++)
            {
                int[] indices = source.GetTriangles(material);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 centre = (positions[indices[i]] + positions[indices[i + 1]] +
                        positions[indices[i + 2]]) / 3f;
                    var key = new Vector2Int(Mathf.FloorToInt(centre.x / ChunkSize),
                        Mathf.FloorToInt(centre.z / ChunkSize));
                    if (!chunks.TryGetValue(key, out Chunk chunk))
                    {
                        chunk = new Chunk();
                        chunks.Add(key, chunk);
                    }
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int original = indices[i + corner];
                        if (!chunk.Remap.TryGetValue(original, out int local))
                        {
                            local = chunk.Positions.Count;
                            chunk.Remap.Add(original, local);
                            chunk.Positions.Add(positions[original]);
                            chunk.Normals.Add(normals[original]);
                            chunk.Uvs.Add(uvs[original]);
                        }
                        chunk.Triangles[material].Add(local);
                    }
                }
            }

            foreach (KeyValuePair<Vector2Int, Chunk> entry in chunks)
            {
                Chunk chunk = entry.Value;
                var mesh = new Mesh { name = "Village ground sector " + entry.Key,
                    indexFormat = chunk.Positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(chunk.Positions);
                mesh.SetNormals(chunk.Normals);
                mesh.SetUVs(0, chunk.Uvs);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(chunk.Triangles[0], 0);
                mesh.SetTriangles(chunk.Triangles[1], 1);
                mesh.RecalculateBounds();
                var host = new GameObject(mesh.name);
                host.transform.SetParent(parent, false);
                host.AddComponent<MeshFilter>().sharedMesh = mesh;
                host.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
                MeshRenderer renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                renderer.receiveShadows = sourceRenderer.receiveShadows;
                var properties = new MaterialPropertyBlock();
                sourceRenderer.GetPropertyBlock(properties);
                renderer.SetPropertyBlock(properties);
                for (int material = 0; material < 2; material++)
                {
                    properties.Clear();
                    sourceRenderer.GetPropertyBlock(properties, material);
                    renderer.SetPropertyBlock(properties, material);
                }
            }
            sourceRenderer.enabled = false;
        }

        private sealed class Chunk
        {
            public readonly Dictionary<int, int> Remap = new Dictionary<int, int>();
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int>[] Triangles = { new List<int>(), new List<int>() };
        }
    }
}
