using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Actual outer seams of the street support union. The same measured
    /// segments constrain both the terrain sampler and its triangulation;
    /// internal asphalt/pavement edges never pull neighbouring earth down.
    /// This depends only on courtyard paving, never on sampled terrain.
    /// </summary>
    internal sealed class CityTerrainStreetBoundaryPlan
    {
        internal const float BlendDistance = 4f;
        private const float EdgeProbe = .01f;
        private const float Tolerance = .0001f;
        private const float BucketSize = 16f;
        private static readonly ConditionalWeakTable<CityLayout, CityTerrainStreetBoundaryPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityTerrainStreetBoundaryPlan>();
        private readonly Dictionary<Vector2Int, List<int>> buckets = new Dictionary<Vector2Int, List<int>>();
        private readonly List<Segment> segments = new List<Segment>();
        private readonly Dictionary<CitySurfaceDescriptor, SurfaceBoundaryContext> contexts =
            new Dictionary<CitySurfaceDescriptor, SurfaceBoundaryContext>();
        private readonly HashSet<(Vector3, Vector3)> unique = new HashSet<(Vector3, Vector3)>();
        private readonly CityStreetSupportPlan support;

        internal readonly struct Segment
        {
            internal Segment(Vector3 start, Vector3 end, Vector2 outward)
            {
                Start = start;
                End = end;
                Outward = outward;
                Bounds = Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.z, end.z),
                    Mathf.Max(start.x, end.x), Mathf.Max(start.z, end.z));
            }
            internal Vector3 Start { get; }
            internal Vector3 End { get; }
            internal Vector2 Outward { get; }
            internal Rect Bounds { get; }
        }

        internal readonly struct Cut
        {
            internal Cut(Vector2 first, Vector2 second)
            {
                First = first;
                Second = second;
                Bounds = Rect.MinMaxRect(Mathf.Min(first.x, second.x) - Tolerance,
                    Mathf.Min(first.y, second.y) - Tolerance,
                    Mathf.Max(first.x, second.x) + Tolerance, Mathf.Max(first.y, second.y) + Tolerance);
            }
            internal Vector2 First { get; }
            internal Vector2 Second { get; }
            internal Rect Bounds { get; }
        }

        internal sealed class SurfaceBoundaryContext
        {
            private const float SampleBucketSize = 4f;
            private readonly Segment[] segments;
            private readonly CityStreetSupportPlan support;
            private readonly Dictionary<Vector2Int, List<int>> sampleBuckets = new Dictionary<Vector2Int, List<int>>();
            internal SurfaceBoundaryContext(IReadOnlyList<Segment> source, CityStreetSupportPlan support)
            {
                this.support = support;
                segments = new Segment[source.Count];
                var cuts = new List<Cut>(source.Count * 3);
                var cutKeys = new HashSet<(Vector2, Vector2)>();
                for (int i = 0; i < source.Count; i++)
                {
                    Segment segment = source[i];
                    segments[i] = segment;
                    Rect reach = Expanded(segment.Bounds, BlendDistance);
                    for (int z = Mathf.FloorToInt(reach.yMin / SampleBucketSize); z <= Mathf.FloorToInt(reach.yMax / SampleBucketSize); z++)
                        for (int x = Mathf.FloorToInt(reach.xMin / SampleBucketSize); x <= Mathf.FloorToInt(reach.xMax / SampleBucketSize); x++)
                        {
                            var key = new Vector2Int(x, z);
                            if (!sampleBuckets.TryGetValue(key, out List<int> entries)) sampleBuckets.Add(key, entries = new List<int>());
                            entries.Add(i);
                        }
                    Vector2 first = XZ(segment.Start), second = XZ(segment.End);
                    AddCut(first, second);
                    // Carry each street grade-break station through the
                    // transition band, rather than interpolating over it.
                    AddCut(first, first + segment.Outward * BlendDistance);
                    AddCut(second, second + segment.Outward * BlendDistance);
                }
                Cuts = cuts.ToArray();

                void AddCut(Vector2 first, Vector2 second)
                {
                    if (cutKeys.Contains((first, second)) || cutKeys.Contains((second, first))) return;
                    cutKeys.Add((first, second));
                    cuts.Add(new Cut(first, second));
                }
            }
            internal IReadOnlyList<Segment> Segments => segments;
            internal IReadOnlyList<Cut> Cuts { get; }
            internal bool HasBoundaries => segments.Length != 0;

            internal float Apply(Vector2 point, float originalTop)
            {
                // A coating has one support even where the base-earth source
                // extends underneath it. Final partitioning selects its material.
                if (support.TrySampleTop(point, out float supportTop, out _)) return supportTop;
                if (!sampleBuckets.TryGetValue(new Vector2Int(Mathf.FloorToInt(point.x / SampleBucketSize),
                    Mathf.FloorToInt(point.y / SampleBucketSize)), out List<int> nearby)) return originalTop;
                double weightSum = 1d;
                double weightedTop = originalTop;
                float exactTop = float.NegativeInfinity;
                foreach (int i in nearby)
                {
                    Segment segment = segments[i];
                    Vector2 first = XZ(segment.Start), delta = XZ(segment.End) - first;
                    float amount = Mathf.Clamp01(Vector2.Dot(point - first, delta) / delta.sqrMagnitude);
                    Vector2 closest = first + delta * amount;
                    float distance = (point - closest).magnitude;
                    if (distance >= BlendDistance) continue;
                    float candidate = Mathf.Lerp(segment.Start.y, segment.End.y, amount);
                    float fade = 1f - Mathf.SmoothStep(0f, 1f, distance / BlendDistance);
                    if (distance <= Tolerance || fade >= 1f)
                    {
                        exactTop = Mathf.Max(exactTop, candidate);
                        continue;
                    }
                    // Odds weights reproduce the original smoothstep for one
                    // boundary and blend overlapping bands continuously. A
                    // nearest-edge switch formed a vertical crease in narrow
                    // fringe cells. All exterior edges may contribute outside
                    // the support union, including around segment endpoints;
                    // a half-plane gate would itself create an endpoint jump.
                    double weight = fade / (1d - fade);
                    weightSum += weight;
                    weightedTop += weight * candidate;
                }
                return !float.IsNegativeInfinity(exactTop) ? exactTop : (float)(weightedTop / weightSum);
            }

            internal bool TrySampleSharedEdge(Vector2 first, Vector2 second, out float firstTop, out float secondTop)
            {
                firstTop = secondTop = 0f;
                if ((second - first).sqrMagnitude < .00000001f) return false;
                for (int i = 0; i < segments.Length; i++)
                {
                    Segment segment = segments[i];
                    Vector2 start = XZ(segment.Start), delta = XZ(segment.End) - start;
                    float lengthSquared = delta.sqrMagnitude;
                    float firstAmount = Vector2.Dot(first - start, delta) / lengthSquared;
                    float secondAmount = Vector2.Dot(second - start, delta) / lengthSquared;
                    if (firstAmount < -.0001f || firstAmount > 1.0001f ||
                        secondAmount < -.0001f || secondAmount > 1.0001f) continue;
                    Vector2 firstProjection = start + delta * firstAmount;
                    Vector2 secondProjection = start + delta * secondAmount;
                    if ((first - firstProjection).sqrMagnitude > .00000001f ||
                        (second - secondProjection).sqrMagnitude > .00000001f) continue;
                    firstTop = Mathf.Lerp(segment.Start.y, segment.End.y, Mathf.Clamp01(firstAmount));
                    secondTop = Mathf.Lerp(segment.Start.y, segment.End.y, Mathf.Clamp01(secondAmount));
                    return true;
                }
                return false;
            }

            internal bool HasCut(Rect bounds)
            {
                for (int i = 0; i < Cuts.Count; i++)
                    if (Cuts[i].Bounds.Overlaps(bounds)) return true;
                return false;
            }

            internal void AppendAxisAnchors(Rect bounds, ICollection<float> x, ICollection<float> z)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    Segment segment = segments[i];
                    Rect reach = Expanded(segment.Bounds, BlendDistance);
                    if (!reach.Overlaps(bounds)) continue;
                    x.Add(segment.Start.x); x.Add(segment.End.x);
                    z.Add(segment.Start.z); z.Add(segment.End.z);
                    x.Add(segment.Start.x + segment.Outward.x * BlendDistance);
                    x.Add(segment.End.x + segment.Outward.x * BlendDistance);
                    z.Add(segment.Start.z + segment.Outward.y * BlendDistance);
                    z.Add(segment.End.z + segment.Outward.y * BlendDistance);
                }
            }
        }

        private CityTerrainStreetBoundaryPlan(CityLayout layout)
        {
            support = CityStreetSupportPlan.ForLayout(layout);
            var nearby = new List<CityStreetSupportPatch>();
            var divisions = new List<float>();
            foreach (CityStreetSupportPatch patch in support.Patches)
            {
                if (!patch.IsSupport) continue;
                support.GetNearby(Expanded(patch.Bounds, EdgeProbe * 2), nearby);
                for (int edge = 0; edge < patch.TopVertices.Count; edge++)
                {
                    Vector3 start = patch.TopVertices[edge];
                    Vector3 end = patch.TopVertices[(edge + 1) % patch.TopVertices.Count];
                    Vector2 first = XZ(start), delta = XZ(end) - first;
                    if (delta.sqrMagnitude < .000001f) continue;
                    divisions.Clear(); divisions.Add(0); divisions.Add(1);
                    foreach (CityStreetSupportPatch other in nearby)
                    {
                        for (int j = 0; j < other.TopVertices.Count; j++)
                        {
                            Vector2 a = XZ(other.TopVertices[j]);
                            Vector2 b = XZ(other.TopVertices[(j + 1) % other.TopVertices.Count]);
                            AppendIntersection(first, delta, a, b, divisions);
                        }
                    }
                    divisions.Sort();
                    Vector2 normal = new Vector2(-delta.y, delta.x).normalized;
                    for (int i = 1; i < divisions.Count; i++)
                    {
                        float low = divisions[i - 1], high = divisions[i];
                        if (high - low < Tolerance) continue;
                        Vector2 middle = first + delta * ((low + high) * .5f);
                        bool left = support.TrySampleTop(middle + normal * EdgeProbe, out _, out _);
                        bool right = support.TrySampleTop(middle - normal * EdgeProbe, out _, out _);
                        if (left == right) continue;
                        Vector2 outward = left ? -normal : normal;
                        // Only the source that actually owns the inward side
                        // contributes; overlapping lower road skins do not.
                        if (!support.TrySampleTop(middle - outward * EdgeProbe,
                            out float ownerTop, out _) ||
                            !patch.TrySampleTop(middle, out float patchTop) ||
                            Mathf.Abs(ownerTop - patchTop) > .01f) continue;
                        Add(new Segment(Vector3.Lerp(start, end, low), Vector3.Lerp(start, end, high), outward));
                    }
                }
            }
        }

        internal static SurfaceBoundaryContext ForSurface(CityLayout layout, CitySurfaceDescriptor surface)
        {
            if (!CityTerrainSurfacePlan.UsesContinuousTop(surface) ||
                surface.Kind == CitySurfaceKind.ChurchGround) return null;
            CityTerrainStreetBoundaryPlan plan = Plans.GetValue(layout, value => new CityTerrainStreetBoundaryPlan(value));
            lock (plan.contexts)
            {
                if (plan.contexts.TryGetValue(surface, out SurfaceBoundaryContext existing)) return existing;
                SurfaceBoundaryContext result = plan.CreateContext(surface);
                plan.contexts.Add(surface, result);
                return result;
            }
        }

        private SurfaceBoundaryContext CreateContext(CitySurfaceDescriptor surface)
        {
            Rect query = Expanded(surface.WorldBounds, BlendDistance + .2f);
            var selected = new List<Segment>();
            var seen = new HashSet<int>();
            for (int z = Mathf.FloorToInt(query.yMin / BucketSize); z <= Mathf.FloorToInt(query.yMax / BucketSize); z++)
                for (int x = Mathf.FloorToInt(query.xMin / BucketSize); x <= Mathf.FloorToInt(query.xMax / BucketSize); x++)
                    if (buckets.TryGetValue(new Vector2Int(x, z), out List<int> indices))
                        foreach (int index in indices)
                            if (seen.Add(index) && Expanded(segments[index].Bounds, BlendDistance).Overlaps(query))
                                selected.Add(segments[index]);
            return new SurfaceBoundaryContext(selected, support);
        }

        private void Add(Segment segment)
        {
            if (!unique.Add((segment.Start, segment.End)) || unique.Contains((segment.End, segment.Start))) return;
            int index = segments.Count;
            segments.Add(segment);
            Rect bounds = Expanded(segment.Bounds, BlendDistance);
            for (int z = Mathf.FloorToInt(bounds.yMin / BucketSize); z <= Mathf.FloorToInt(bounds.yMax / BucketSize); z++)
                for (int x = Mathf.FloorToInt(bounds.xMin / BucketSize); x <= Mathf.FloorToInt(bounds.xMax / BucketSize); x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!buckets.TryGetValue(key, out List<int> entries)) buckets.Add(key, entries = new List<int>());
                    entries.Add(index);
                }
        }

        private static void AppendIntersection(Vector2 first, Vector2 delta, Vector2 a, Vector2 b, List<float> divisions)
        {
            Vector2 other = b - a;
            float cross = CityRoadPolygon.Cross(delta, other);
            if (Mathf.Abs(cross) > .000001f)
            {
                float amount = CityRoadPolygon.Cross(a - first, other) / cross;
                float otherAmount = CityRoadPolygon.Cross(a - first, delta) / cross;
                if (amount > Tolerance && amount < 1f - Tolerance && otherAmount >= -Tolerance && otherAmount <= 1 + Tolerance)
                    divisions.Add(amount);
            }
            else if (Mathf.Abs(CityRoadPolygon.Cross(delta, a - first)) < .001f)
            {
                float firstAmount = Vector2.Dot(a - first, delta) / delta.sqrMagnitude;
                float lastAmount = Vector2.Dot(b - first, delta) / delta.sqrMagnitude;
                if (firstAmount > Tolerance && firstAmount < 1f - Tolerance) divisions.Add(firstAmount);
                if (lastAmount > Tolerance && lastAmount < 1f - Tolerance) divisions.Add(lastAmount);
            }
        }

        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
        private static Rect Expanded(Rect bounds, float amount) => Rect.MinMaxRect(
            bounds.xMin - amount, bounds.yMin - amount, bounds.xMax + amount, bounds.yMax + amount);
    }
}
