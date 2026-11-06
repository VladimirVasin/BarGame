using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One physical zebra shared by every service using this site. WalkPath
    /// runs pavement–asphalt–asphalt–pavement in surveyed world metres.</summary>
    public sealed class CityBusStopCrossing
    {
        private readonly List<string> stops = new List<string>(2);
        public string Id { get; }
        public IReadOnlyList<string> StopIds { get; }
        public RoadEdge Edge { get; }
        public CityTerraceTransportBank? HillsideBank { get; }
        public int ExistingCrosswalkIndex { get; }
        public Vector3 Center { get; }
        public IReadOnlyList<Vector3> WalkPath { get; }
        public IReadOnlyList<RuntimeOrientedBox> Stripes { get; }
        public Rect Bounds { get; }

        internal CityBusStopCrossing(string id, RoadEdge edge, CityTerraceTransportBank? bank, int existing,
            Vector3 center, Vector3[] path, RuntimeOrientedBox[] stripes, Rect bounds)
        {
            Id = id; Edge = edge; HillsideBank = bank; ExistingCrosswalkIndex = existing; Center = center;
            WalkPath = Array.AsReadOnly(path); Stripes = Array.AsReadOnly(stripes); Bounds = bounds;
            StopIds = stops.AsReadOnly();
        }

        internal void AddStop(string id) => stops.Add(id);
    }

    internal static class CityBusStopCrossingPlanner
    {
        // Leave the entire eight-metre bus and both open doors clear of the paint.
        internal const float PreferredOffset = 8f;
        internal const float MaximumStopDistance = 26f;
        private const float LandingMargin = .45f;
        private const int ZebraStripes = 6;

        internal static List<CityBusStopCrossing> Create(CityLayout layout, IReadOnlyList<CityCrosswalkDescriptor> existing)
        {
            var sites = new List<CityBusStopCrossing>();
            if (!layout.SpatialPlan.IsCompactCoastal) return sites;
            var routes = CityDistrictBusPlanner.CreateRoadRouting(layout);
            var allStops = new List<CityBusStopDescriptor>();
            foreach (var route in routes) allStops.AddRange(route.Stops);
            var ground = CityStreetSupportPlan.ForLayout(layout);
            foreach (var route in routes)
            foreach (var stop in route.Stops)
            {
                var road = stop.RoadEdge.A == stop.RoadEdge.B ? null : layout.RoadGeometry.Get(stop.RoadEdge);
                CityHillsideStreetRoute hill = null;
                if (road == null)
                    foreach (var slope in CityHillsideStreetPlan.ForLayout(layout).Routes)
                        if (slope.TrySampleTop(Xz(stop.Position), out float y, out var kind) &&
                            kind == CityStreetSupportKind.Asphalt && Mathf.Abs(stop.Position.y - y) < .5f)
                        { hill = slope; road = slope.Path; break; }
                if (road == null)
                    throw new InvalidOperationException("No road supports crossing for " + stop.Id);
                float halt = road.Project(Xz(stop.Position)).DistanceAlong;
                CityBusStopCrossing chosen = null;
                foreach (var site in sites)
                    if (site.Edge == stop.RoadEdge && site.HillsideBank == hill?.Bank &&
                        (Xz(site.Center) - Xz(stop.Position)).magnitude <= MaximumStopDistance &&
                        Mathf.Abs(road.Project(Xz(site.Center)).DistanceAlong - halt) <= MaximumStopDistance)
                    { chosen = site; break; }
                if (chosen == null)
                {
                    // Reuse a reachable junction zebra on this same physical road.
                    float nearest = MaximumStopDistance;
                    for (int i = 0; hill == null && i < existing.Count; i++)
                    {
                        var cross = existing[i];
                        if (cross.ApproachEdge != stop.RoadEdge) continue;
                        float station = road.Project(Xz(cross.Center)).DistanceAlong;
                        float distance = Mathf.Abs(station - halt);
                        if (distance > nearest) continue;
                        var candidate = At(station, i);
                        if (candidate != null) { chosen = candidate; nearest = distance; }
                    }
                    // Both ends of a transfer prefer the landing of the same public
                    // stair, if its projection clears the stopped bus and the bend.
                    if (chosen == null && stop.TransferNumber > 0 && stop.TargetId != "transfer:mouth")
                    {
                        var bank = stop.TargetId == "transfer:west-slope" ? CityTerraceTransportBank.West : CityTerraceTransportBank.East;
                        foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).Transfers)
                        {
                            if (stair.Bank != bank) continue;
                            Vector3 landing = stair.RouteWorld[hill == null ? 0 : stair.RouteWorld.Count - 1];
                            float station = road.Project(Xz(landing)).DistanceAlong;
                            if (Mathf.Abs(station - halt) <= MaximumStopDistance) chosen = At(station, -1);
                        }
                    }
                    // Alternating the two sides of the halt keeps the chosen legal
                    // crossing close even at the end of a short block or a hairpin.
                    float direction = Vector2.Dot(road.SampleDistance(halt).Tangent, Xz(stop.Forward)) < 0f ? -1f : 1f;
                    for (int k = 0; chosen == null && k < 39; k++)
                    {
                        float offset = k == 0 ? PreferredOffset : k == 1 ? 7f : 7.5f + (k - 2) * .5f;
                        chosen = At(halt - direction * offset, -1) ?? At(halt + direction * offset, -1);
                    }
                    if (chosen == null)
                        throw new InvalidOperationException("No two-pavement crossing beside " + stop.Id + " at " + stop.Position);
                    sites.Add(chosen);
                }
                chosen.AddStop(stop.Id);

                CityBusStopCrossing At(float station, int reuse)
                {
                    float margin = hill != null ? 2f : layout.RoadWidth * .5f + 1.2f;
                    if (station < margin - .01f || station > road.Length - margin + .01f) return null;
                    var at = road.SampleDistance(station);
                    if (Vector2.Dot(road.SampleDistance(station - 2f).Tangent, at.Tangent) < .985f ||
                        Vector2.Dot(road.SampleDistance(station + 2f).Tangent, at.Tangent) < .985f) return null;
                    float half = hill == null ? layout.RoadWidth * .5f - CityStreetSurfacePlanner.SidewalkWidth :
                        CityHillsideStreetRoute.RoadHalfWidth;
                    float sidewalk = hill == null ? CityPedestrianPlanner.GetSidewalkCenterOffset(layout) :
                        (CityHillsideStreetRoute.OuterHalfWidth + half) * .5f;
                    if (!Sample(at.Position, FootstepGroundKind.Concrete, out var center)) return null;
                    var path = new Vector3[4];
                    if (!Sample(at.Position - at.Right * sidewalk, FootstepGroundKind.Stone, out path[0]) ||
                        !Sample(at.Position + at.Right * sidewalk, FootstepGroundKind.Stone, out path[3]) ||
                        !Sample(at.Position - at.Right * (half - .16f), FootstepGroundKind.Concrete, out path[1]) ||
                        !Sample(at.Position + at.Right * (half - .16f), FootstepGroundKind.Concrete, out path[2]) ||
                        Mathf.Abs(path[0].y - path[1].y) > .20f || Mathf.Abs(path[3].y - path[2].y) > .20f) return null;

                    float depth = CityStreetSurfacePlanner.CrosswalkDepth * .5f;
                    Vector2 far = new Vector2(Mathf.Abs(at.Right.x), Mathf.Abs(at.Right.y)) * (sidewalk + LandingMargin) +
                        new Vector2(Mathf.Abs(at.Tangent.x), Mathf.Abs(at.Tangent.y)) * depth;
                    Rect bounds = Rect.MinMaxRect(at.Position.x - far.x, at.Position.y - far.y, at.Position.x + far.x, at.Position.y + far.y);
                    foreach (var other in allStops)
                    {
                        if (Mathf.Abs(other.Position.y - center.y) > 3f) continue;
                        if (bounds.Overlaps(CityBusStopWorldBuilder.DescribeFurnitureFootprint(other))) return null;
                        Vector2 relative = Xz(other.Position) - at.Position;
                        Vector2 along = Xz(other.Forward).normalized;
                        float alignment = Mathf.Abs(Vector2.Dot(along, at.Tangent));
                        float width = Mathf.Abs(Vector2.Dot(along, at.Right));
                        float length = CityBusDesignVehicle.Default.BodyLength * .5f + .65f;
                        float breadth = CityBusDesignVehicle.Default.BodyWidth * .5f + .35f;
                        if (Mathf.Abs(Vector2.Dot(relative, at.Tangent)) < depth + length * alignment + breadth * width &&
                            Mathf.Abs(Vector2.Dot(relative, at.Right)) < sidewalk + length * width + breadth * alignment) return null;
                    }

                    // Paint alternating white rectangles from kerb to kerb. The long
                    // side follows the traffic lane, pitched to the asphalt under it.
                    if (!TryCreatePaint(ground, at.Position, at.Right, at.Tangent, half, half, out var paint)) return null;
                    return new CityBusStopCrossing(reuse < 0 ? "zebra:" + stop.Id : "junction-zebra:" + reuse,
                        stop.RoadEdge, hill?.Bank, reuse, center, path, paint, bounds);
                }
            }
            return sites;

            bool Sample(Vector2 position, FootstepGroundKind required, out Vector3 point)
            {
                if (ground.TrySampleTop(position, out float top, out var kind) && kind == required)
                {
                    point = new Vector3(position.x, top, position.y);
                    return true;
                }
                point = default;
                return false;
            }
        }

        /// <summary>The same six stripes serve stops and bounded landmark crossings.
        /// Unequal side widths preserve the real outside apron beside a bend.</summary>
        internal static bool TryCreatePaint(CityStreetSupportPlan ground, Vector2 center, Vector2 across,
            Vector2 along, float negativeHalf, float positiveHalf, out RuntimeOrientedBox[] paint)
        {
            paint = new RuntimeOrientedBox[ZebraStripes];
            float pitch = (negativeHalf + positiveHalf - .20f) / paint.Length;
            float halfWidth = pitch * .27f;
            float depth = CityStreetSurfacePlanner.CrosswalkDepth * .5f;
            for (int stripe = 0; stripe < paint.Length; stripe++)
            {
                Vector2 mid = center + across * (-negativeHalf + .10f + (stripe + .5f) * pitch);
                Vector2 width = across * halfWidth, length = along * depth;
                if (!Sample(mid - width - length, out var a) || !Sample(mid + width - length, out var b) ||
                    !Sample(mid - width + length, out var c) || !Sample(mid + width + length, out var d) ||
                    !Sample(mid, out var asphalt)) return false;
                Vector3 forward = ((c - a) + (d - b)) * .5f;
                Vector3 right = ((b - a) + (d - c)) * .5f;
                Vector3 normal = Vector3.Cross(forward, right).normalized;
                Vector3 middle = (a + b + c + d) * .25f;
                if (normal.y < .8f || Mathf.Abs(Vector3.Dot(asphalt - middle, normal)) > .012f ||
                    Mathf.Abs(Vector3.Dot(a - middle, normal)) > .012f) return false;
                paint[stripe] = new RuntimeOrientedBox(middle + normal * .008f,
                    Quaternion.LookRotation(forward, normal), new Vector3(right.magnitude, .012f, forward.magnitude));
            }
            return true;

            bool Sample(Vector2 point, out Vector3 result)
            {
                if (ground.TrySampleTop(point, out float top, out var kind) && kind == FootstepGroundKind.Concrete)
                { result = new Vector3(point.x, top, point.y); return true; }
                result = default; return false;
            }
        }

        private static Vector2 Xz(Vector3 p) => new Vector2(p.x, p.z);
    }
}
