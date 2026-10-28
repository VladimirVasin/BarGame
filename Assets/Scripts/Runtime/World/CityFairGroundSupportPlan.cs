using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Small planar earth supports under rigid fair feet. The pure
    /// site owns placement; this plan fits the pre-pad terrain once, then both
    /// physical terrain and prop fitting read the same plane. No fair runtime
    /// plan or final terrain sampler participates in constructing these pads.</summary>
    internal sealed class CityFairGroundSupportPlan
    {
        private const float MaximumBlendDistance = 1f;
        private const float FootMargin = .06f;
        private const float BucketSize = 4f;
        private static readonly ConditionalWeakTable<CityLayout, CityFairGroundSupportPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityFairGroundSupportPlan>();
        private readonly List<Pad> pads = new List<Pad>();
        private readonly Dictionary<Vector2Int, List<int>> buckets = new Dictionary<Vector2Int, List<int>>();
        private readonly CityStreetSupportPlan street;

        private readonly struct Pad
        {
            internal Pad(Rect core, Vector2 center, float height, Vector2 slope, float blendDistance)
            { Core = core; Center = center; Height = height; Slope = slope; BlendDistance = blendDistance; }
            internal Rect Core { get; }
            internal Rect Reach => Expanded(Core, BlendDistance);
            internal float BlendDistance { get; }
            private Vector2 Center { get; }
            private float Height { get; }
            private Vector2 Slope { get; }
            internal float Sample(Vector2 point) => Height + Vector2.Dot(point - Center, Slope);
        }

        private CityFairGroundSupportPlan(CityLayout layout)
        {
            CityFairGroundSitePlan site = CityFairGroundSitePlan.ForLayout(layout);
            if (!site.IsEnabled) return;
            street = CityStreetSupportPlan.ForLayout(layout);
            foreach (CityFairGroundPropSite prop in site.Supports)
            {
                Vector2 forward = new Vector2(prop.Facing.x, prop.Facing.z).normalized;
                Vector2 right = new Vector2(forward.y, -forward.x);
                float h00 = RawTop(layout, prop.CenterXZ - right * (prop.Width * .5f) - forward * (prop.Depth * .5f));
                float h10 = RawTop(layout, prop.CenterXZ + right * (prop.Width * .5f) - forward * (prop.Depth * .5f));
                float h01 = RawTop(layout, prop.CenterXZ - right * (prop.Width * .5f) + forward * (prop.Depth * .5f));
                float h11 = RawTop(layout, prop.CenterXZ + right * (prop.Width * .5f) + forward * (prop.Depth * .5f));
                float height = (h00 + h10 + h01 + h11) * .25f;
                float rightSlope = (h10 + h11 - h00 - h01) / (2f * prop.Width);
                float forwardSlope = (h01 + h11 - h00 - h10) / (2f * prop.Depth);
                Vector2 extent = new Vector2(Mathf.Abs(right.x) * prop.Width + Mathf.Abs(forward.x) * prop.Depth,
                    Mathf.Abs(right.y) * prop.Width + Mathf.Abs(forward.y) * prop.Depth) * .5f + Vector2.one * FootMargin;
                Rect core = Rect.MinMaxRect(prop.CenterXZ.x - extent.x, prop.CenterXZ.y - extent.y,
                    prop.CenterXZ.x + extent.x, prop.CenterXZ.y + extent.y);
                float blendDistance = ResolveStreetClearBlend(core, prop.Id);
                var pad = new Pad(core, prop.CenterXZ, height, right * rightSlope + forward * forwardSlope, blendDistance);
                int index = pads.Count;
                pads.Add(pad);
                Rect reach = pad.Reach;
                for (int z = Bucket(reach.yMin); z <= Bucket(reach.yMax); z++)
                    for (int x = Bucket(reach.xMin); x <= Bucket(reach.xMax); x++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!buckets.TryGetValue(key, out List<int> entries)) buckets.Add(key, entries = new List<int>());
                        entries.Add(index);
                    }
            }
        }

        internal static CityFairGroundSupportPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityFairGroundSupportPlan(value));

        internal float Apply(Vector2 point, float rawTop)
        {
            if (!buckets.TryGetValue(new Vector2Int(Bucket(point.x), Bucket(point.y)), out List<int> nearby)) return rawTop;
            // The shared street boundary retains its exact profile even if a
            // local earth blend reaches a distant corner of the corridor.
            if (street.TrySampleTop(point, out _, out _)) return rawTop;
            double weighted = rawTop, sum = 1d;
            foreach (int index in nearby)
            {
                Pad pad = pads[index];
                float distance = DistanceOutside(pad.Core, point);
                if (distance <= .00001f) return pad.Sample(point);
                if (distance >= pad.BlendDistance) continue;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, distance / pad.BlendDistance);
                if (fade >= 1f) return pad.Sample(point);
                double weight = fade / (1d - fade);
                weighted += weight * pad.Sample(point); sum += weight;
            }
            return (float)(weighted / sum);
        }

        internal bool HasCut(Rect bounds)
        {
            foreach (Pad pad in pads)
                if (Touches(pad.Core, bounds) && (!pad.Core.Contains(bounds.min) || !pad.Core.Contains(bounds.max))) return true;
            return false;
        }

        internal void AppendAxisAnchors(Rect bounds, ICollection<float> x, ICollection<float> z)
        {
            foreach (Pad pad in pads)
            {
                if (!pad.Reach.Overlaps(bounds)) continue;
                x.Add(pad.Core.xMin); x.Add(pad.Core.xMax);
                z.Add(pad.Core.yMin); z.Add(pad.Core.yMax);
                x.Add(pad.Reach.xMin); x.Add(pad.Reach.xMax);
                z.Add(pad.Reach.yMin); z.Add(pad.Reach.yMax);
            }
        }

        internal void AppendCuts(Rect bounds, ICollection<CityTerrainStreetBoundaryPlan.Cut> cuts)
        {
            foreach (Pad pad in pads)
            {
                if (!Touches(pad.Core, bounds)) continue;
                Vector2[] corners = CityRoadPolygon.Rectangle(pad.Core);
                for (int i = 0; i < corners.Length; i++)
                    cuts.Add(new CityTerrainStreetBoundaryPlan.Cut(corners[i], corners[(i + 1) % corners.Length]));
            }
        }

        private float ResolveStreetClearBlend(Rect core, string id)
        {
            var nearby = new List<CityStreetSupportPatch>();
            street.GetNearby(Expanded(core, MaximumBlendDistance), nearby);
            Vector2[] corners = CityRoadPolygon.Rectangle(core);
            float clearanceSquared = float.PositiveInfinity;
            foreach (CityStreetSupportPatch patch in nearby)
            {
                var polygon = new Vector2[patch.TopVertices.Count];
                for (int i = 0; i < polygon.Length; i++)
                    polygon[i] = new Vector2(patch.TopVertices[i].x, patch.TopVertices[i].z);
                foreach (Vector2 point in corners)
                    if (CityRoadPolygon.Contains(polygon, point))
                        throw new InvalidOperationException("Rigid fair support intersects the street: " + id);
                foreach (Vector2 point in polygon)
                    if (core.Contains(point))
                        throw new InvalidOperationException("Rigid fair support intersects the street: " + id);
                for (int first = 0; first < corners.Length; first++)
                    for (int second = 0; second < polygon.Length; second++)
                        clearanceSquared = Mathf.Min(clearanceSquared, SegmentDistanceSquared(corners[first],
                            corners[(first + 1) % corners.Length], polygon[second], polygon[(second + 1) % polygon.Length]));
            }
            if (clearanceSquared <= .00000001f)
                throw new InvalidOperationException("Rigid fair support touches a street seam: " + id);
            // The blend uses an axis-aligned rectangle, whose corner can
            // travel sqrt(2) times its radius. Keep a further half-clearance
            // margin so the complete transition ends before any street seam.
            return Mathf.Min(MaximumBlendDistance, Mathf.Sqrt(clearanceSquared) / (2f * Mathf.Sqrt(2f)));
        }

        private static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 first = b - a, second = d - c;
            float cross = CityRoadPolygon.Cross(first, second);
            if (Mathf.Abs(cross) > .000001f)
            {
                float u = CityRoadPolygon.Cross(c - a, second) / cross;
                float v = CityRoadPolygon.Cross(c - a, first) / cross;
                if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) return 0f;
            }
            return Mathf.Min(Mathf.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d)),
                Mathf.Min(PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b)));
        }

        private static float PointSegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float amount = delta.sqrMagnitude > .00000001f ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0f;
            return (point - (a + delta * amount)).sqrMagnitude;
        }

        private static float RawTop(CityLayout layout, Vector2 point)
        {
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
                if (!surface.IsWater && surface.WorldBounds.Contains(point))
                    return CityTerrainSurfacePlan.SampleTopWithoutRigidSupports(layout, surface, point);
            throw new InvalidOperationException("A rigid fair support must remain on declared earth.");
        }

        private static int Bucket(float value) => Mathf.FloorToInt(value / BucketSize);
        private static float DistanceOutside(Rect bounds, Vector2 point) => Mathf.Max(
            Mathf.Max(bounds.xMin - point.x, point.x - bounds.xMax), Mathf.Max(bounds.yMin - point.y, point.y - bounds.yMax));
        private static bool Touches(Rect a, Rect b) => a.xMin <= b.xMax && a.xMax >= b.xMin && a.yMin <= b.yMax && a.yMax >= b.yMin;
        private static Rect Expanded(Rect bounds, float amount) => Rect.MinMaxRect(
            bounds.xMin - amount, bounds.yMin - amount, bounds.xMax + amount, bounds.yMax + amount);
    }
}
