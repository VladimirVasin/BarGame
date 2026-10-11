using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // One ordered solve of a panel, or its area-contact phase. Pose/skin and extreme folds remain
    // with the owner; the original managed solve is the portable fallback.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerJacketSurfaceJob : IJob
    {
        public struct BodyProxy { public Vector3 Minimum, Maximum; public int First, Count; }
        public struct Edge { public int A, B; }
        public NativeArray<Vector3> Positions;
        [ReadOnly] public NativeArray<float> Freedom, MaterialLengths;
        [ReadOnly] public NativeArray<int> Canonical, Layers, Pinned, LayerFirst, LayerMembers, PopulatedLayers, DuplicateVertices, Triangles;
        [ReadOnly] public NativeArray<Edge> Edges;
        [ReadOnly] public NativeArray<BodyProxy> Bodies;
        [ReadOnly] public NativeArray<Plane> Planes;
        [ReadOnly] public NativeArray<PlayerEquipmentContacts.BoxSnapshot> Boxes;
        public NativeArray<int> MovedLayers, ClearVertices, ClearArea, PlaneHints;
        public NativeArray<Vector3> ClearVertexPoints, ClearAreaPoints;
        public int ManagedExecution, ContactCount;
        public int InitialResolve, FinalMaterial, ConstrainMaterial, AreaPasses, MaterialPasses;
        public int HasEquipmentBounds;
        public Vector3 EquipmentMinimum, EquipmentMaximum;
        public float MaximumStretch;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerJacketSurfaceJob job) => job.Execute();

        public void Execute()
        {
            RecordManagedExecution(ref ManagedExecution);
            for (int i = 0; i < ClearVertices.Length; i++) ClearVertices[i] = 0;
            for (int i = 0; i < ClearArea.Length; i++) ClearArea[i] = 0;
            ContactCount = InitialResolve != 0 ? ResolveVertices() : 0;
            for (int pass = 0; pass < AreaPasses; pass++)
            {
                bool shortened = ConstrainMaterial != 0 && LimitMaterialDistances();
                int corrected = 0;
                for (int triangle = 0; triangle < Triangles.Length; triangle += 3)
                {
                    int a = Triangles[triangle], b = Triangles[triangle + 1], c = Triangles[triangle + 2];
                    if (Freedom[a] <= 0f && Freedom[b] <= 0f && Freedom[c] <= 0f) continue;
                    int sample = triangle / 3 * 4;
                    corrected += Distribute(a, b, c, new Vector3(1f / 3f, 1f / 3f, 1f / 3f), sample);
                    corrected += Distribute(a, b, c, new Vector3(.5f, .5f, 0f), sample + 1);
                    corrected += Distribute(a, b, c, new Vector3(0f, .5f, .5f), sample + 2);
                    corrected += Distribute(a, b, c, new Vector3(.5f, 0f, .5f), sample + 3);
                }
                if (corrected == 0 && !shortened) break;
                ContactCount += corrected;
                for (int i = 0; i < Positions.Length; i++) Positions[i] = Positions[Canonical[i]];
                ContactCount += ResolveVertices();
            }
            if (FinalMaterial != 0) LimitMaterialDistances();
        }

        private int ResolveVertices()
        {
            int count = 0;
            for (int i = 0; i < Positions.Length; i++)
            {
                if (Freedom[i] <= 0f) continue;
                Vector3 point = Positions[i];
                if (ClearVertices[i] != 0 && Equal(point, ClearVertexPoints[i])) continue;
                if (ResolvePoint(ref point, i))
                { Positions[i] = point; ClearVertices[i] = 0; count++; }
                else { ClearVertices[i] = 1; ClearVertexPoints[i] = point; }
            }
            return count;
        }

        private bool LimitMaterialDistances()
        {
            bool changed = false;
            for (int pass = 0; pass < MaterialPasses; pass++)
            {
                bool shortened = false;
                for (int i = 0; i < MovedLayers.Length; i++) MovedLayers[i] = 0;
                for (int i = 0; i < Edges.Length; i++)
                {
                    float maximum = (float)((double)MaterialLengths[i] * MaximumStretch);
                    if (maximum <= 0f) continue;
                    Edge edge = Edges[i];
                    float wa = Pinned[edge.A] != 0 ? 0f : Freedom[edge.A], wb = Pinned[edge.B] != 0 ? 0f : Freedom[edge.B];
                    if ((double)wa + wb <= 0d) continue;
                    Vector3 delta = Subtract(Positions[edge.B], Positions[edge.A]);
                    float length = Magnitude(delta);
                    if (length <= (double)maximum + .000001f) continue;
                    float factor = (float)(((double)length - maximum) / ((double)length * ((double)wa + wb)));
                    Vector3 correction = Multiply(delta, factor);
                    if (wa > 0f) MoveLayer(edge.A, Multiply(correction, wa));
                    if (wb > 0f) MoveLayer(edge.B, Multiply(-correction, wb));
                    shortened = changed = true;
                }
                for (int i = 0; i < PopulatedLayers.Length; i++)
                {
                    int layer = PopulatedLayers[i];
                    if (MovedLayers[layer] == 0) continue;
                    for (int contactPass = 0; contactPass < 2; contactPass++)
                    {
                        bool escaped = false;
                        for (int index = LayerFirst[layer]; index < LayerFirst[layer + 1]; index++)
                        {
                            int member = LayerMembers[index];
                            Vector3 point = Positions[member];
                            if (!ResolvePoint(ref point, member)) continue;
                            Vector3 escape = Subtract(point, Positions[member]);
                            for (int peer = LayerFirst[layer]; peer < LayerFirst[layer + 1]; peer++)
                                Positions[LayerMembers[peer]] = Add(Positions[LayerMembers[peer]], escape);
                            escaped = true;
                        }
                        if (!escaped) break;
                    }
                }
                for (int i = 0; i < DuplicateVertices.Length; i++)
                { int vertex = DuplicateVertices[i]; Positions[vertex] = Positions[Canonical[vertex]]; }
                if (!shortened) break;
            }
            return changed;
        }

        private void MoveLayer(int vertex, Vector3 correction)
        {
            int layer = Layers[vertex];
            for (int i = LayerFirst[layer]; i < LayerFirst[layer + 1]; i++)
            { int member = LayerMembers[i]; Positions[member] = Add(Positions[member], correction); }
            MovedLayers[layer] = 1;
        }

        private int Distribute(int a, int b, int c, Vector3 weights, int sample)
        {
            Vector3 point = Add(Add(Multiply(Positions[a], weights.x), Multiply(Positions[b], weights.y)), Multiply(Positions[c], weights.z));
            if (ClearArea[sample] != 0 && Equal(point, ClearAreaPoints[sample])) return 0;
            Vector3 resolved = point;
            if (!ResolvePoint(ref resolved, Positions.Length + sample))
            { ClearArea[sample] = 1; ClearAreaPoints[sample] = point; return 0; }
            ClearArea[sample] = 0;
            float denominator = (float)((((double)Freedom[a] * weights.x * weights.x) +
                (double)Freedom[b] * weights.y * weights.y) + (double)Freedom[c] * weights.z * weights.z);
            if (denominator <= 1e-12f) return 0;
            Vector3 correction = Divide(Subtract(resolved, point), denominator);
            AddCorrection(a, weights.x, correction); AddCorrection(b, weights.y, correction); AddCorrection(c, weights.z, correction);
            return 1;
        }

        private void AddCorrection(int vertex, float amount, Vector3 correction)
        {
            if (amount <= 0f || Freedom[vertex] <= 0f) return;
            Positions[vertex] = Add(Positions[vertex], Multiply(correction, (float)((double)Freedom[vertex] * amount)));
        }

        internal bool ResolveSingle(ref Vector3 point, int location) => ResolvePoint(ref point, location);

        private bool ResolvePoint(ref Vector3 point, int location)
        {
            bool corrected = false;
            for (int pass = 0; pass < 2; pass++)
            {
                bool moved = false;
                for (int proxy = 0; proxy < Bodies.Length; proxy++)
                {
                    BodyProxy body = Bodies[proxy];
                    if (body.Count == 0 || !InsideBounds(point, body.Minimum, body.Maximum)) continue;
                    int slot = location * Bodies.Length + proxy;
                    int hint = PlaneHints[slot];
                    if ((uint)hint < body.Count && PlaneDistance(Planes[body.First + hint], point) >= 0f) continue;
                    float nearest = float.NegativeInfinity;
                    int winner = 0;
                    bool outside = false;
                    for (int plane = 0; plane < body.Count; plane++)
                    {
                        float distance = PlaneDistance(Planes[body.First + plane], point);
                        if (distance >= 0f) { winner = plane; outside = true; break; }
                        if (distance > nearest) { nearest = distance; winner = plane; }
                    }
                    PlaneHints[slot] = winner;
                    if (outside) continue;
                    point = Add(point, Multiply(Planes[body.First + winner].normal, (float)(-(double)nearest + .0001f)));
                    moved = corrected = true;
                }
                if (HasEquipmentBounds != 0 && InsideBounds(point, EquipmentMinimum, EquipmentMaximum))
                for (int box = 0; box < Boxes.Length; box++)
                {
                    PlayerEquipmentContacts.BoxSnapshot shape = Boxes[box];
                    if (!InsideBounds(point, shape.WorldMinimum, shape.WorldMaximum)) continue;
                    Vector3 local = TransformPoint(shape.ToLocal, point);
                    Vector3 offset = Subtract(local, shape.LocalCentre), extent = shape.LocalExtent;
                    if (Math.Abs(offset.x) >= extent.x || Math.Abs(offset.y) >= extent.y || Math.Abs(offset.z) >= extent.z) continue;
                    float x = (float)(((double)extent.x - Math.Abs(offset.x)) / Math.Max(.000001f, shape.Padding.x));
                    float y = (float)(((double)extent.y - Math.Abs(offset.y)) / Math.Max(.000001f, shape.Padding.y));
                    float z = (float)(((double)extent.z - Math.Abs(offset.z)) / Math.Max(.000001f, shape.Padding.z));
                    if (x <= y && x <= z) offset.x = offset.x < 0f ? -extent.x : extent.x;
                    else if (y <= z) offset.y = offset.y < 0f ? -extent.y : extent.y;
                    else offset.z = offset.z < 0f ? -extent.z : extent.z;
                    point = TransformPoint(shape.ToWorld, Add(shape.LocalCentre, offset));
                    moved = corrected = true;
                }
                if (!moved) break;
            }
            return corrected;
        }

        private static float PlaneDistance(Plane plane, Vector3 point)
        {
            Vector3 normal = plane.normal;
            float dot = (float)(((double)normal.x * point.x + (double)normal.y * point.y) + (double)normal.z * point.z);
            return (float)((double)dot + plane.distance);
        }
        internal static Vector3 TransformPoint(Matrix4x4 m, Vector3 p) => new Vector3(
            (float)((((double)m.m00 * p.x + (double)m.m01 * p.y) + (double)m.m02 * p.z) + m.m03),
            (float)((((double)m.m10 * p.x + (double)m.m11 * p.y) + (double)m.m12 * p.z) + m.m13),
            (float)((((double)m.m20 * p.x + (double)m.m21 * p.y) + (double)m.m22 * p.z) + m.m23));
        private static bool InsideBounds(Vector3 p, Vector3 min, Vector3 max) =>
            p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
        private static bool Equal(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        private static float Magnitude(Vector3 v) => (float)Math.Sqrt(((double)v.x * v.x + (double)v.y * v.y) + (double)v.z * v.z);
        private static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        private static Vector3 Subtract(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        private static Vector3 Multiply(Vector3 v, float s) => new Vector3(v.x * s, v.y * s, v.z * s);
        private static Vector3 Divide(Vector3 v, float s) => new Vector3((float)((double)v.x / s), (float)((double)v.y / s), (float)((double)v.z / s));
        [BurstDiscard] private static void RecordManagedExecution(ref int value) => value = 1;
    }
}
