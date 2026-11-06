using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityPawnshopCrossing
    {
        public string Id { get; }
        public int Lane { get; }
        public float Width => CityStreetSurfacePlanner.CrosswalkDepth;
        public Vector3 Center { get; }
        public Rect Bounds { get; }
        public IReadOnlyList<Vector3> WalkPath { get; }
        public IReadOnlyList<Vector3> ApproachPath { get; }
        public IReadOnlyList<RuntimeOrientedBox> Stripes { get; }

        internal CityPawnshopCrossing(int lane, Vector3 center, Rect bounds, Vector3[] path,
            List<Vector3> approach, RuntimeOrientedBox[] stripes)
        {
            Id = "pawnshop-crossing:" + lane; Lane = lane; Center = center; Bounds = bounds;
            WalkPath = Array.AsReadOnly(path); ApproachPath = approach.AsReadOnly(); Stripes = Array.AsReadOnly(stripes);
        }
    }

    /// <summary>Two measured zebras on the unchanged roads beside the turning island.</summary>
    public sealed class CityPawnshopCrosswalkPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityPawnshopCrosswalkPlan> Plans = new();
        public IReadOnlyList<CityPawnshopCrossing> Crossings { get; }
        public IReadOnlyList<RuntimeOrientedBox> Markings { get; }

        public static CityPawnshopCrosswalkPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityPawnshopCrosswalkPlan(value));

        private CityPawnshopCrosswalkPlan(CityLayout layout)
        {
            var crossings = new List<CityPawnshopCrossing>(2);
            var markings = new List<RuntimeOrientedBox>(12);
            Crossings = crossings.AsReadOnly(); Markings = markings.AsReadOnly();
            if (!CityPawnshopPlan.TryCreate(layout, out var site)) return;
            CityHillsideStreetRoute road = null;
            foreach (var candidate in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if (candidate.Bank == CityTerraceTransportBank.West) { road = candidate; break; }
            if (road == null) return;
            var support = CityStreetSupportPlan.ForLayout(layout);
            CityHillsideGroundGrid island = null;
            foreach (var grid in CityHillsideGroundGrid.ForLayout(layout))
                if (grid.Road.Bank == CityTerraceTransportBank.West) { island = grid; break; }
            if (island == null) throw new InvalidOperationException("Pawnshop crossings need the actual hillside ground.");
            foreach (int lane in new[] { 4, 5 })
            {
                // The last/first four metres of these lanes are level and straight.
                // Survey both kerbs: the bend's outer apron widens only one side.
                var center = new Vector2(road.LaneMaxX - 2f, road.GetLaneZ(lane));
                float positive = Edge(1f), negative = Edge(-1f);
                float outward = lane == 4 ? 1f : -1f;
                float outerHalf = outward > 0f ? positive : negative;
                float innerHalf = outward > 0f ? negative : positive;
                Vector3[] path = {
                    Sample(center + Vector2.up * (outward * (outerHalf + .95f)), FootstepGroundKind.Stone),
                    Sample(center + Vector2.up * (outward * (outerHalf - .16f)), FootstepGroundKind.Concrete),
                    Sample(center - Vector2.up * (outward * (innerHalf - .16f)), FootstepGroundKind.Concrete),
                    Sample(center - Vector2.up * (outward * (innerHalf + .95f)), FootstepGroundKind.Stone)
                };
                // Positive across is +Z, so traffic forward is -X: the shared
                // stripe recipe requires forward x right to produce the upward normal.
                if (!CityBusStopCrossingPlanner.TryCreatePaint(support, center, Vector2.up, Vector2.left,
                    negative, positive, out var stripes))
                    throw new InvalidOperationException("Pawnshop zebra must follow supported asphalt: " + lane);
                Vector3 middle = Sample(center, FootstepGroundKind.Concrete);
                var bounds = Rect.MinMaxRect(center.x - CityStreetSurfacePlanner.CrosswalkDepth * .5f,
                    center.y - negative - 1.45f, center.x + CityStreetSurfacePlanner.CrosswalkDepth * .5f,
                    center.y + positive + 1.45f);
                // Do not place new paint or its landings under an existing platform.
                foreach (var route in CityDistrictBusPlanner.CreateRoadRouting(layout))
                foreach (var stop in route.Stops)
                    if (Mathf.Abs(stop.Position.y - middle.y) < 3f &&
                        bounds.Overlaps(CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop)))
                        throw new InvalidOperationException("Pawnshop crossing overlaps a bus platform: " + stop.Id);
                var approach = new List<Vector3>(path);
                Vector2 origin = new Vector2(site.Origin.x, site.Origin.z);
                Vector2 near = new Vector2(path[3].x, path[3].z) - origin;
                float from = Mathf.Atan2(near.y, near.x) * Mathf.Rad2Deg;
                float to = Mathf.Atan2(site.DoorOutward.z, site.DoorOutward.x) * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(from, to);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(delta) / 12f));
                for (int step = 0; step <= steps; step++)
                {
                    float angle = (from + delta * step / steps) * Mathf.Deg2Rad;
                    Vector2 point = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 7f;
                    if (!island.TryHeight(point, out float top))
                        throw new InvalidOperationException("Pawnshop island approach leaves the measured ground.");
                    approach.Add(new Vector3(point.x, top, point.y));
                }
                approach.Add(site.ReturnWorld - Vector3.up * PlayerFactory.GroundedRootOffset);
                crossings.Add(new CityPawnshopCrossing(lane, middle, bounds, path, approach, stripes));
                markings.AddRange(stripes);

                float Edge(float sign)
                {
                    float inside = 0f, outside = 0f;
                    for (int probe = 1; probe <= 140; probe++)
                    {
                        outside = probe * .05f;
                        Vector2 point = center + Vector2.up * (sign * outside);
                        if (!support.TrySampleTop(point, out _, out var kind) || kind != FootstepGroundKind.Concrete) break;
                        inside = outside;
                    }
                    if (outside <= inside) throw new InvalidOperationException("No pawnshop road kerb found.");
                    for (int probe = 0; probe < 12; probe++)
                    {
                        float mid = (inside + outside) * .5f;
                        if (support.TrySampleTop(center + Vector2.up * (sign * mid), out _, out var kind) &&
                            kind == FootstepGroundKind.Concrete) inside = mid;
                        else outside = mid;
                    }
                    return (inside + outside) * .5f;
                }
            }

            Vector3 Sample(Vector2 point, FootstepGroundKind required)
            {
                if (!support.TrySampleTop(point, out float top, out var kind) || kind != required)
                    throw new InvalidOperationException("Pawnshop crossing needs both real public footways and asphalt: " + point);
                return new Vector3(point.x, top, point.y);
            }
        }
    }
}
