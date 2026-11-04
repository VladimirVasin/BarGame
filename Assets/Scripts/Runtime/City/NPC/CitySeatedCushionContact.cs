using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Measures the selected trousers against the occupied part of a cushion.
    /// Each mesh shares immutable source geometry. Each visible body keeps only its
    /// current skin, morph weights and reusable clipping buffers.</summary>
    internal sealed class CitySeatedCushionContact
    {
        private const float Compression = .005f;
        private readonly SkinnedMeshRenderer[] candidates;
        private readonly Vector4[] clippedA = new Vector4[9], clippedB = new Vector4[9];
        private readonly ConditionalWeakTable<Mesh, Geometry>.CreateValueCallback createGeometry;
        private static readonly ConditionalWeakTable<Mesh, Geometry> Shapes = new();
        private SkinnedMeshRenderer skin;
        private Transform[] bones;
        private Geometry geometry;
        private Matrix4x4[] matrices;
        private Vector4[] posed;
        private int[] activeMorphs;
        private float[] amounts;

        internal CitySeatedCushionContact(CityPedestrianAssetRegistry registry)
        {
            var parts = new List<SkinnedMeshRenderer>(4);
            foreach (var renderer in registry.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.name == "GEO_LegsUpper" ||
                    renderer.name.StartsWith("CLO_Trousers_", StringComparison.Ordinal) &&
                    renderer.name.EndsWith("Upper", StringComparison.Ordinal))
                    parts.Add(renderer);
            candidates = parts.ToArray();
            createGeometry = mesh => new Geometry(mesh, bones);
        }

        internal bool Bind()
        {
            SkinnedMeshRenderer worn = null;
            foreach (var candidate in candidates)
            {
                if (!candidate.enabled || !candidate.gameObject.activeInHierarchy) continue;
                worn = candidate;
                if (candidate.name != "GEO_LegsUpper") break;
            }
            if (worn == null)
            {
                bool changed = skin != null;
                skin = null;
                return changed;
            }
            if (worn == skin && geometry != null && geometry.Mesh == worn.sharedMesh) return false;
            skin = worn;
            bones = skin.bones;
            geometry = Shapes.GetValue(skin.sharedMesh, createGeometry);
            matrices = new Matrix4x4[bones.Length];
            posed = new Vector4[geometry.Vertices.Length];
            activeMorphs = new int[geometry.Deltas.Length];
            amounts = new float[activeMorphs.Length];
            return true;
        }

        internal float MeasureCorrection(Vector3 seatCenter, Vector3 up, Vector3 forward,
            float halfWidth = .26f, float halfDepth = .24f)
        {
            if (skin == null) return 0f;
            // Work in metres in the plane of the actual cushion. Renderer transform
            // scale is already present in the bone matrices through the bind poses.
            Matrix4x4 seatFrame = Matrix4x4.TRS(seatCenter, Quaternion.LookRotation(forward, up), Vector3.one).inverse;
            foreach (int bone in geometry.UsedBones)
                matrices[bone] = seatFrame * bones[bone].localToWorldMatrix * geometry.BindPoses[bone];

            int count = 0;
            for (int s = 0; s < geometry.Deltas.Length; s++)
            {
                float weight = skin.GetBlendShapeWeight(s) / geometry.FrameWeights[s];
                if (Mathf.Abs(weight) <= .00001f) continue;
                activeMorphs[count] = s;
                amounts[count++] = weight;
            }
            for (int i = 0; i < posed.Length; i++)
            {
                Vector3 vertex = geometry.Vertices[i];
                for (int j = 0; j < count; j++)
                    vertex += geometry.Deltas[activeMorphs[j]][i] * amounts[j];
                BoneWeight w = geometry.Weights[i];
                Vector3 point = matrices[w.boneIndex0].MultiplyPoint3x4(vertex) * w.weight0;
                if (w.weight1 > 0f) point += matrices[w.boneIndex1].MultiplyPoint3x4(vertex) * w.weight1;
                if (w.weight2 > 0f) point += matrices[w.boneIndex2].MultiplyPoint3x4(vertex) * w.weight2;
                if (w.weight3 > 0f) point += matrices[w.boneIndex3].MultiplyPoint3x4(vertex) * w.weight3;
                posed[i] = new Vector4(point.x, point.y, point.z, geometry.Support[i]);
            }

            float minimum = MinimumHeight(halfWidth, -halfDepth, halfDepth);
            return float.IsPositiveInfinity(minimum) ? 0f : -Compression - minimum;
        }

        // A knee at the front edge can prop the entire wearer higher than the
        // actual buttocks. Search a bounded fore/aft range on this same skinned
        // sample before changing height. Horizontal motion does not change any
        // joint angles or the ground plane.
        internal float ForwardForRearSupport(float pelvisDepth, float verticalCorrection)
        {
            if (skin == null || pelvisDepth >= .18f) return 0f;
            const float depth = .24f, width = .26f;
            float cutoff = Mathf.Min(depth, pelvisDepth + .065f);
            float rear = MinimumHeight(width, -depth, cutoff);
            if (float.IsPositiveInfinity(rear) || rear + verticalCorrection <= .014f) return 0f;
            float lo = 0f, hi = .18f - pelvisDepth;
            for (int i = 0; i < 5; i++)
            {
                float d = (lo + hi) * .5f;
                float minimum = MinimumHeight(width, -depth - d, depth - d);
                if (rear - Compression - minimum > .012f) lo = d;
                else hi = d;
            }
            return hi;
        }

        private float MinimumHeight(float halfWidth, float back, float front)
        {
            float minimum = float.PositiveInfinity;
            int[] triangles = geometry.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector4 a = posed[triangles[i]], b = posed[triangles[i + 1]], c = posed[triangles[i + 2]];
                if (a.w < .5f && b.w < .5f && c.w < .5f) continue;
                // A clipped triangle cannot acquire a point below all its corners.
                if (a.y >= minimum && b.y >= minimum && c.y >= minimum) continue;
                clippedA[0] = a; clippedA[1] = b; clippedA[2] = c;
                int n = Clip(clippedA, clippedB, 3, 3, 1f, .5f);
                n = Clip(clippedB, clippedA, n, 0, 1f, -halfWidth);
                n = Clip(clippedA, clippedB, n, 0, -1f, -halfWidth);
                n = Clip(clippedB, clippedA, n, 2, 1f, back);
                n = Clip(clippedA, clippedB, n, 2, -1f, -front);
                for (int j = 0; j < n; j++) minimum = Mathf.Min(minimum, clippedB[j].y);
            }
            return minimum;
        }

        // Clip the real triangle, including the interpolated hip/thigh weight at
        // its edge. Lower calves are part of the same trouser mesh and hang below
        // the seat legitimately. A crossed cushion edge still contributes contact
        // even if all three authored vertices lie outside the footprint.
        internal bool TryThighTop(float x, float z, out float y, out Vector3 normal) =>
            TryThighTop(x, z, out y, out normal, out _);

        internal bool TryThighTop(float x, float z, out float y, out Vector3 normal, out float signedThigh)
        {
            signedThigh = 0f;
            y = float.NegativeInfinity;
            normal = Vector3.up;
            if (skin == null) return false;
            var triangles = geometry.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
                var a = posed[ia]; var b = posed[ib]; var c = posed[ic];
                if ((x < a.x && x < b.x && x < c.x) || (x > a.x && x > b.x && x > c.x) ||
                    (z < a.z && z < b.z && z < c.z) || (z > a.z && z > b.z && z > c.z)) continue;
                var ab = b - a; var ac = c - a;
                float det = ab.x * ac.z - ac.x * ab.z;
                if (Mathf.Abs(det) < .000001f) continue;
                float u = ((x - a.x) * ac.z - (z - a.z) * ac.x) / det;
                float v = (ab.x * (z - a.z) - ab.z * (x - a.x)) / det;
                if (u < 0f || v < 0f || u + v > 1f) continue;
                float height = a.y + u * ab.y + v * ac.y;
                if (height <= y) continue;
                Vector3 face = Vector3.Cross((Vector3)ab, (Vector3)ac).normalized;
                normal = face.y >= 0f ? face : -face;
                signedThigh = (1f - u - v) * geometry.ThighSide[ia] + u * geometry.ThighSide[ib] + v * geometry.ThighSide[ic];
                y = height;
            }
            return !float.IsNegativeInfinity(y);
        }

        private static int Clip(Vector4[] source, Vector4[] dest, int count, int axis, float sign, float limit)
        {
            if (count == 0) return 0;
            int written = 0;
            Vector4 previous = source[count - 1];
            float before = previous[axis] * sign - limit;
            for (int i = 0; i < count; i++)
            {
                Vector4 current = source[i];
                float after = current[axis] * sign - limit;
                if ((before >= 0f) != (after >= 0f))
                    dest[written++] = Vector4.LerpUnclamped(previous, current, before / (before - after));
                if (after >= 0f) dest[written++] = current;
                previous = current;
                before = after;
            }
            return written;
        }

        private sealed class Geometry
        {
            internal readonly Mesh Mesh;
            internal readonly Vector3[] Vertices;
            internal readonly BoneWeight[] Weights;
            internal readonly float[] Support, ThighSide, FrameWeights;
            internal readonly int[] Triangles, UsedBones;
            internal readonly Matrix4x4[] BindPoses;
            internal readonly Vector3[][] Deltas;

            internal Geometry(Mesh mesh, Transform[] bones)
            {
                Mesh = mesh;
                var supportBone = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    supportBone[i] = bones[i].name == "pelvis" || bones[i].name == "thigh.L" || bones[i].name == "thigh.R";
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                int[] indices = mesh.triangles;
                var support = new float[vertices.Length];
                var map = new int[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight w = weights[i];
                    support[i] = (supportBone[w.boneIndex0] ? w.weight0 : 0f) + (supportBone[w.boneIndex1] ? w.weight1 : 0f) +
                        (supportBone[w.boneIndex2] ? w.weight2 : 0f) + (supportBone[w.boneIndex3] ? w.weight3 : 0f);
                    map[i] = -1;
                }
                var sources = new List<int>();
                var triangles = new List<int>();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        int original = indices[i + j];
                        if (map[original] < 0) { map[original] = sources.Count; sources.Add(original); }
                        triangles.Add(map[original]);
                    }
                }
                Triangles = triangles.ToArray();
                Vertices = new Vector3[sources.Count];
                Weights = new BoneWeight[sources.Count];
                Support = new float[sources.Count];
                ThighSide = new float[sources.Count];
                var signs = new float[bones.Length];
                for (int j = 0; j < bones.Length; j++)
                    signs[j] = bones[j].name == "thigh.L" ? 1f : bones[j].name == "thigh.R" ? -1f : 0f;
                var used = new HashSet<int>();
                for (int i = 0; i < sources.Count; i++)
                {
                    int original = sources[i];
                    Vertices[i] = vertices[original];
                    BoneWeight w = Weights[i] = weights[original];
                    Support[i] = support[original];
                    ThighSide[i] = w.weight0 * signs[w.boneIndex0] + w.weight1 * signs[w.boneIndex1] +
                        w.weight2 * signs[w.boneIndex2] + w.weight3 * signs[w.boneIndex3];
                    used.Add(w.boneIndex0);
                    if (w.weight1 > 0f) used.Add(w.boneIndex1);
                    if (w.weight2 > 0f) used.Add(w.boneIndex2);
                    if (w.weight3 > 0f) used.Add(w.boneIndex3);
                }
                UsedBones = new int[used.Count]; used.CopyTo(UsedBones);
                BindPoses = mesh.bindposes;
                Deltas = new Vector3[mesh.blendShapeCount][];
                FrameWeights = new float[Deltas.Length];
                var scratch = new Vector3[vertices.Length];
                for (int s = 0; s < Deltas.Length; s++)
                {
                    // The production garments use a single authored endpoint
                    // for each stature, build, joint-volume and knee-fold morph.
                    if (mesh.GetBlendShapeFrameCount(s) != 1)
                        throw new InvalidOperationException("Seated garment has a non-linear morph: " + mesh.name);
                    FrameWeights[s] = mesh.GetBlendShapeFrameWeight(s, 0);
                    mesh.GetBlendShapeFrameVertices(s, 0, scratch, null, null);
                    var delta = Deltas[s] = new Vector3[sources.Count];
                    for (int i = 0; i < sources.Count; i++) delta[i] = scratch[sources[i]];
                }
            }
        }
    }
}
