using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Filters pre-authored Blender triangles, preserving their complete
    /// vertex streams and the existing frame and glass materials.</summary>
    internal sealed class CityGroundedWindowMeshes : IDisposable
    {
        private readonly Dictionary<Mesh, SourceMesh> sources = new();
        private readonly Dictionary<(Mesh Source, ulong Mask), Mesh> variants = new();

        internal void Apply(CityBuildingAssetRegistry building, ulong hidden)
        {
            foreach (CityBuildingPartBinding part in building.Parts)
            {
                if (part.Role is not (CityBuildingMeshRole.WindowFrame or CityBuildingMeshRole.WindowGlass)) continue;
                MeshFilter filter = part.Renderer.GetComponent<MeshFilter>();
                Mesh source = filter.sharedMesh;
                var key = (source, hidden);
                if (!variants.TryGetValue(key, out Mesh variant))
                {
                    if (!sources.TryGetValue(source, out SourceMesh data))
                        sources.Add(source, data = new SourceMesh(source));
                    variants.Add(key, variant = data.Filter(source, hidden));
                }
                if (variant == null) part.Renderer.enabled = false;
                else filter.sharedMesh = variant;
            }
        }

        internal void ReleaseSourceData() => sources.Clear();

        public void Dispose()
        {
            foreach (Mesh mesh in variants.Values)
            {
                if (mesh == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                else UnityEngine.Object.DestroyImmediate(mesh);
            }
            variants.Clear();
            sources.Clear();
        }

        private sealed class SourceMesh
        {
            private readonly VertexAttributeDescriptor[] attributes;
            private readonly byte[][] streams;
            private readonly int[] indices;
            private readonly ulong[] triangleSlots;

            internal SourceMesh(Mesh source)
            {
                if (source.subMeshCount != 1 || source.GetTopology(0) != MeshTopology.Triangles ||
                    !source.HasVertexAttribute(VertexAttribute.TexCoord1))
                    throw new InvalidOperationException($"Window mesh '{source.name}' has no slot-tagged triangle stream.");
                attributes = source.GetVertexAttributes();
                streams = new byte[source.vertexBufferCount][];
                SubMeshDescriptor part = source.GetSubMesh(0);
                indices = new int[part.indexCount];
#if UNITY_EDITOR
                // Imported vertex data is already available to the Editor,
                // including in headless tests. Players keep the FBX GPU-only.
                using Mesh.MeshDataArray access = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(source);
                Mesh.MeshData data = access[0];
                for (int stream = 0; stream < streams.Length; stream++)
                    streams[stream] = data.GetVertexData<byte>(stream).ToArray();
                if (source.indexFormat == IndexFormat.UInt16)
                {
                    var input = data.GetIndexData<ushort>();
                    for (int i = 0; i < indices.Length; i++) indices[i] = input[part.indexStart + i] + part.baseVertex;
                }
                else
                {
                    var input = data.GetIndexData<int>();
                    for (int i = 0; i < indices.Length; i++) indices[i] = input[part.indexStart + i] + part.baseVertex;
                }
#else
                for (int stream = 0; stream < streams.Length; stream++)
                {
                    using GraphicsBuffer buffer = source.GetVertexBuffer(stream);
                    streams[stream] = new byte[source.vertexCount * source.GetVertexBufferStride(stream)];
                    buffer.GetData(streams[stream]);
                }
                using (GraphicsBuffer buffer = source.GetIndexBuffer())
                {
                    if (source.indexFormat == IndexFormat.UInt16)
                    {
                        var input = new ushort[buffer.count * buffer.stride / sizeof(ushort)];
                        buffer.GetData(input);
                        for (int i = 0; i < indices.Length; i++) indices[i] = input[part.indexStart + i] + part.baseVertex;
                    }
                    else
                    {
                        var input = new int[buffer.count * buffer.stride / sizeof(int)];
                        buffer.GetData(input);
                        for (int i = 0; i < indices.Length; i++) indices[i] = input[part.indexStart + i] + part.baseVertex;
                    }
                }
#endif
                int slotStream = source.GetVertexAttributeStream(VertexAttribute.TexCoord1);
                int stride = source.GetVertexBufferStride(slotStream);
                int offset = source.GetVertexAttributeOffset(VertexAttribute.TexCoord1);
                VertexAttributeFormat format = source.GetVertexAttributeFormat(VertexAttribute.TexCoord1);
                if (format is not (VertexAttributeFormat.Float32 or VertexAttributeFormat.Float16))
                    throw new InvalidOperationException($"Window mesh '{source.name}' has unsupported slot UVs: {format}.");
                byte[] slotData = streams[slotStream];
                triangleSlots = new ulong[indices.Length / 3];
                for (int triangle = 0; triangle < triangleSlots.Length; triangle++)
                {
                    int start = triangle * 3;
                    int slot = DecodeSlot(indices[start]);
                    if (slot < 0 || slot > CityBuildingAssetRegistry.MaximumWindowSlotId ||
                        slot != DecodeSlot(indices[start + 1]) || slot != DecodeSlot(indices[start + 2]))
                        throw new InvalidOperationException($"Window mesh '{source.name}' has inconsistent triangle slot IDs.");
                    // Slot zero is reserved for any non-window detail.
                    if (slot != 0) triangleSlots[triangle] = 1UL << slot;
                }

                int DecodeSlot(int vertex)
                {
                    int at = vertex * stride + offset;
                    float encoded = format == VertexAttributeFormat.Float32 ? BitConverter.ToSingle(slotData, at)
                        : Mathf.HalfToFloat(BitConverter.ToUInt16(slotData, at));
                    return Mathf.FloorToInt(encoded * CityBuildingAssetRegistry.WindowSlotUv2Divisor);
                }
            }

            internal Mesh Filter(Mesh source, ulong hidden)
            {
                var kept = new List<int>(indices.Length);
                for (int triangle = 0; triangle < triangleSlots.Length; triangle++)
                {
                    if ((triangleSlots[triangle] & hidden) != 0) continue;
                    int start = triangle * 3;
                    kept.Add(indices[start]); kept.Add(indices[start + 1]); kept.Add(indices[start + 2]);
                }
                if (kept.Count == 0) return null;
                var mesh = new Mesh { name = source.name + " above grade", indexFormat = source.indexFormat };
                mesh.SetVertexBufferParams(source.vertexCount, attributes);
                for (int stream = 0; stream < streams.Length; stream++)
                {
                    byte[] data = streams[stream];
                    mesh.SetVertexBufferData(data, 0, 0, data.Length, stream,
                        MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }
                mesh.subMeshCount = 1;
                mesh.SetTriangles(kept, 0, false);
                mesh.bounds = source.bounds;
                return mesh;
            }
        }
    }
}
