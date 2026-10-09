using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Transfers production grip and private cloth deformation through offline source correspondence.</summary>
    internal sealed class CombatBodySourceDeformation : IDisposable
    {
        private readonly Mesh template;
        private readonly SkinnedMeshRenderer source;
        private readonly PlayerJacketCloth cloth;
        private readonly int clothSurface = -1;
        private readonly int[] indicesA, indicesB, indicesC;
        private readonly Vector4[] barycentric;
        private readonly List<Vector3> sourceVertices = new List<Vector3>();
        private readonly List<Vector3> sourceNormals = new List<Vector3>();
        private readonly List<Vector4> sourceTangents = new List<Vector4>();
        private readonly bool normalMapped;
        private Vector3[] vertices, normals;
        private Vector4[] tangents;
        private Mesh owned, capturedSource;
        private uint capturedSourceVersion;
        private SkinnedMeshRenderer target;
        private bool disposed;

        internal uint GeometryVersion { get; private set; }

        internal CombatBodySourceDeformation(Mesh template, SkinnedMeshRenderer source)
        {
            this.template = template != null ? template : throw new ArgumentNullException(nameof(template));
            this.source = source != null ? source : throw new ArgumentNullException(nameof(source));
            normalMapped = source.sharedMaterial != null && source.sharedMaterial.IsKeywordEnabled("_NORMALMAP");
            var data = new List<Vector4>(); template.GetUVs(2, data);
            indicesA = new int[data.Count]; indicesB = new int[data.Count]; indicesC = new int[data.Count];
            for (int i = 0; i < data.Count; i++)
            {
                indicesA[i] = Mathf.RoundToInt(data[i].x);
                indicesB[i] = Mathf.RoundToInt(data[i].y);
                indicesC[i] = Mathf.RoundToInt(data[i].z);
            }
            data.Clear(); template.GetUVs(3, data); barycentric = data.ToArray();
            cloth = source.GetComponentInParent<PlayerJacketCloth>();
            if (cloth == null || !cloth.HasAuthoredBindings) return;
            for (int i = 0; i < cloth.SurfaceCount; i++)
                if (source.sharedMesh == cloth.SourceMesh(i) || source.sharedMesh == cloth.DeformedMesh(i))
                { clothSurface = i; break; }
        }

        internal bool IsMutableSource => !disposed && cloth != null && clothSurface >= 0 &&
            cloth.DeformedMesh(clothSurface) != null && source != null && source.sharedMesh == cloth.DeformedMesh(clothSurface);

        internal void Refresh(SkinnedMeshRenderer renderer, bool preserveCompositedMesh = false)
        {
            if (disposed || renderer == null || source == null || source.sharedMesh == null) return;
            target = renderer;
            Mesh current = source.sharedMesh;
            Mesh rendered = renderer.sharedMesh;
            int shapeCount = Mathf.Min(current.blendShapeCount, rendered != null ? rendered.blendShapeCount : 0);
            for (int shape = 0; shape < shapeCount; shape++)
                renderer.SetBlendShapeWeight(shape, source.GetBlendShapeWeight(shape));
            bool mutable = IsMutableSource;
            if (!mutable && owned == null) return;
            uint sourceVersion = mutable ? cloth.SurfaceGeometryVersion(clothSurface) : 0u;
            if (owned != null && capturedSource == current && capturedSourceVersion == sourceVersion)
            {
                // An unchanged cloth snapshot may already be composed into torso
                // erosion. Keep that renderer binding instead of swapping twice.
                if (!preserveCompositedMesh && renderer.sharedMesh != owned) renderer.sharedMesh = owned;
                renderer.localBounds = source.localBounds;
                return;
            }
            if (indicesA.Length != template.vertexCount || barycentric.Length != template.vertexCount)
                throw new InvalidOperationException("Combat cloth derivative lost its offline source correspondence: " + template.name);
            if (owned == null)
            {
                owned = UnityEngine.Object.Instantiate(template);
                owned.name = template.name + " Body Cloth Motion";
                owned.hideFlags = HideFlags.HideAndDontSave;
                owned.MarkDynamic();
                vertices = new Vector3[template.vertexCount]; normals = new Vector3[template.vertexCount];
                if (normalMapped) tangents = new Vector4[template.vertexCount];
            }
            current.GetVertices(sourceVertices); current.GetNormals(sourceNormals);
            if (normalMapped) current.GetTangents(sourceTangents);
            if (sourceNormals.Count != sourceVertices.Count)
                throw new InvalidOperationException("Production cloth source lacks vertex normals: " + current.name);
            for (int i = 0; i < vertices.Length; i++)
            {
                int a = indicesA[i], b = indicesB[i], c = indicesC[i];
                if ((uint)a >= sourceVertices.Count || (uint)b >= sourceVertices.Count || (uint)c >= sourceVertices.Count)
                    throw new InvalidOperationException("Production cloth topology changed under body damage: " + current.name);
                Vector4 bary = barycentric[i];
                vertices[i] = sourceVertices[a] * bary.x + sourceVertices[b] * bary.y + sourceVertices[c] * bary.z;
                normals[i] = (sourceNormals[a] * bary.x + sourceNormals[b] * bary.y + sourceNormals[c] * bary.z).normalized;
                if (normalMapped && sourceTangents.Count == sourceVertices.Count)
                {
                    Vector4 authored = sourceTangents[a] * bary.x + sourceTangents[b] * bary.y + sourceTangents[c] * bary.z;
                    Vector3 direction = new Vector3(authored.x, authored.y, authored.z);
                    direction = (direction - normals[i] * Vector3.Dot(normals[i], direction)).normalized;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, authored.w < 0f ? -1f : 1f);
                }
            }
            owned.vertices = vertices; owned.normals = normals; owned.RecalculateBounds();
            if (normalMapped)
            {
                if (sourceTangents.Count == sourceVertices.Count) owned.tangents = tangents;
                else owned.RecalculateTangents();
            }
            capturedSource = current;
            capturedSourceVersion = sourceVersion;
            unchecked { GeometryVersion++; }
            renderer.sharedMesh = owned;
            renderer.localBounds = source.localBounds;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (target != null && target.sharedMesh == owned) target.sharedMesh = template;
            if (owned != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(owned);
                else UnityEngine.Object.DestroyImmediate(owned);
            }
            owned = null;
        }
    }
}
