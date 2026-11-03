using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityTerraceEntranceConnectionState { PartialFrontage, GradedApron, RequiresStreetRegrading, RequiresAccessRoute }

    /// <summary>One surveyed forecourt row, with both actual pavement heights.</summary>
    public readonly struct CityTerraceEntranceSeam
    {
        internal CityTerraceEntranceSeam(int row, int segment, Vector3 existing, Vector3 proposed, float distance, bool direct)
        { Row = row; SegmentIndex = segment; Existing = existing; Proposed = proposed; RouteDistance = distance; HasDirectContact = direct; }
        public int Row { get; }
        public int SegmentIndex { get; }
        public Vector3 Existing { get; }
        public Vector3 Proposed { get; }
        public float RouteDistance { get; }
        public bool HasDirectContact { get; }
        public float HorizontalGap => Vector2.Distance(new Vector2(Existing.x, Existing.z), new Vector2(Proposed.x, Proposed.z));
        public float SignedRise => Proposed.y - Existing.y;
    }

    /// <summary>
    /// Authoring-only apron proposal. Keeps the existing threshold and measures
    /// every triangle, including crossfall. A steep apron is rejected, not
    /// disguised as stairs or used to move an entrance. Foundations, courtyard
    /// paths and inland street elevations remain separate placement work.
    /// </summary>
    public sealed class CityTerraceEntranceConnectionPlan
    {
        // Only the small lateral offsets of a surveyed street can be tied in
        // directly. A distant corridor is not this entrance's public pavement.
        public const float MaximumLateralAdjustment = .5f;
        public const float MaximumGradePercent = CityElevationPlan.MaximumPedestrianGradePercent;

        private CityTerraceEntranceConnectionPlan(string id, CityTerraceTransportRoute route,
            CityEntranceForecourtPlan existing, List<CityTerraceEntranceSeam> seams)
        {
            Id = id + ":" + route.Id; EntranceId = id; RouteId = route.Id;
            ExistingSurface = existing; Seams = seams.AsReadOnly();
            foreach (CityTerraceEntranceSeam seam in seams)
            {
                MaximumHeightChange = Mathf.Max(MaximumHeightChange, Mathf.Abs(seam.SignedRise));
                MaximumHorizontalGap = Mathf.Max(MaximumHorizontalGap, seam.HorizontalGap);
            }
            bool complete = seams.Count == existing.Rows;
            foreach (CityTerraceEntranceSeam seam in seams)
            {
                Vector3 inner = existing.Top[seam.Row * existing.Columns];
                Vector2 oldRun = XZ(seam.Existing - inner), newRun = XZ(seam.Proposed - inner);
                // A change of route segment may put a height knot between rows.
                // It needs a re-meshed seam; sampled agreement is insufficient.
                complete &= seam.HasDirectContact && seam.SegmentIndex == seams[0].SegmentIndex && Vector2.Dot(oldRun, newRun) > .0001f;
            }
            var top = new Vector3[complete ? existing.Top.Count : 0];
            if (top.Length == 0)
            {
                State = MaximumHorizontalGap > MaximumLateralAdjustment
                    ? CityTerraceEntranceConnectionState.RequiresAccessRoute : CityTerraceEntranceConnectionState.PartialFrontage;
                ProposedTop = Array.AsReadOnly(top);
                return;
            }
            for (int row = 0; row < existing.Rows; row++)
            {
                Vector3 inner = existing.Top[row * existing.Columns];
                Vector3 outer = seams[row].Proposed;
                for (int column = 0; column < existing.Columns; column++)
                    top[row * existing.Columns + column] = Vector3.Lerp(inner, outer, column / (float)(existing.Columns - 1));
            }
            for (int row = 0; row < existing.Rows - 1; row++)
            for (int column = 0; column < existing.Columns - 1; column++)
            {
                int a = row * existing.Columns + column, b = a + 1;
                int d = a + existing.Columns, c = d + 1;
                RequiredGradePercent = Mathf.Max(RequiredGradePercent,
                    TriangleGrade(top[a], top[b], top[c]), TriangleGrade(top[a], top[c], top[d]));
            }
            ProposedTop = Array.AsReadOnly(top);
            State = RequiredGradePercent <= MaximumGradePercent + .001f
                ? CityTerraceEntranceConnectionState.GradedApron
                : CityTerraceEntranceConnectionState.RequiresStreetRegrading;
        }

        public string Id { get; }
        public string EntranceId { get; }
        public string RouteId { get; }
        public CityEntranceForecourtPlan ExistingSurface { get; }
        public IReadOnlyList<CityTerraceEntranceSeam> Seams { get; }
        /// <summary>Empty for an incomplete frontage; otherwise same grid/diagonal as ExistingSurface.</summary>
        public IReadOnlyList<Vector3> ProposedTop { get; }
        public CityTerraceEntranceConnectionState State { get; }
        public float MaximumHeightChange { get; }
        public float MaximumHorizontalGap { get; }
        public float RequiredGradePercent { get; }

        public static IReadOnlyList<CityTerraceEntranceConnectionPlan> Create(CityLayout layout,
            IReadOnlyList<CityTerraceTransportRoute> routes)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            if (routes.Count == 0) throw new ArgumentException("Entrance planning requires a route.", nameof(routes));
            foreach (CityTerraceTransportRoute route in routes)
                if (route == null) throw new ArgumentException("A route is null.", nameof(routes));
            var connections = new List<CityTerraceEntranceConnectionPlan>();
            foreach (BuildingLot lot in layout.BuildingMasses)
            {
                if (!lot.HasRoadFrontage || !(lot.IsPlayerHome || lot.IsBar || lot.IsSupermarket)) continue;
                float width = lot.IsPlayerHome ? PlayerHomeEntranceGeometry.WalkwayWidth :
                    lot.IsBar ? BarEntranceGeometry.WalkwayWidth : SupermarketEntranceGeometry.WalkwayWidth;
                string id = lot.IsPlayerHome ? "player-home:forecourt" :
                    lot.IsBar ? lot.BarId + ":forecourt" : "supermarket:forecourt";
                CityEntranceForecourtPlan surface = CityStreetSurfacePlanner.CreateEntranceForecourt(layout, lot, width);
                Vector2 tangent = XZ(surface.Top[(surface.Rows - 1) * surface.Columns] - surface.Top[0]).normalized;
                CityTerraceEntranceConnectionPlan nearest = null;
                float nearestSquared = float.PositiveInfinity;
                foreach (CityTerraceTransportRoute route in routes)
                {
                    var seams = new List<CityTerraceEntranceSeam>(surface.Rows);
                    float squared = 0f;
                    for (int row = 0; row < surface.Rows; row++)
                    {
                        Vector3 outer = surface.Top[row * surface.Columns + surface.Columns - 1];
                        CityTerraceEntranceSeam seam = NearestPavement(route, row, outer, tangent);
                        seams.Add(seam); squared += seam.HorizontalGap * seam.HorizontalGap;
                    }
                    if (squared >= nearestSquared) continue;
                    nearestSquared = squared;
                    nearest = new CityTerraceEntranceConnectionPlan(id, route, surface, seams);
                }
                connections.Add(nearest);
            }
            return connections.AsReadOnly();
        }

        private static CityTerraceEntranceSeam NearestPavement(CityTerraceTransportRoute route, int row,
            Vector3 outer, Vector2 frontageTangent)
        {
            const float halfWidth = CityTerraceTransportPlan.CorridorWidth * .5f;
            float best = float.PositiveInfinity;
            CityTerraceEntranceSeam result = default;
            for (int i = 0; i < route.Segments.Count; i++)
            {
                CityTerraceTransportSegment segment = route.Segments[i];
                Vector2 start = XZ(segment.Start), forward = (XZ(segment.End) - start) / segment.HorizontalLength;
                IReadOnlyList<Vector2> polygon = route.CorridorPolygons[i];
                for (int edge = 0; edge < polygon.Count; edge++)
                {
                    Vector2 a = polygon[edge], b = polygon[(edge + 1) % polygon.Count];
                    float sideA = CityRoadPolygon.Cross(forward, a - start), sideB = CityRoadPolygon.Cross(forward, b - start);
                    // Only exposed long edges; internal miter cuts are not
                    // pavement arrivals. Project onto the actual finite edge.
                    if (Mathf.Abs(Mathf.Abs(sideA) - halfWidth) > .001f || Mathf.Abs(sideA - sideB) > .001f) continue;
                    Vector2 delta = b - a;
                    Vector2 point = a + delta * Mathf.Clamp01(Vector2.Dot(XZ(outer) - a, delta) / delta.sqrMagnitude);
                    float squared = (point - XZ(outer)).sqrMagnitude;
                    if (squared >= best) continue;
                    best = squared;
                    float along = Vector2.Dot(point - start, forward);
                    float distance = route.Samples[i].HorizontalDistance + Mathf.Clamp(along, 0f, segment.HorizontalLength);
                    Vector3 proposed = new Vector3(point.x, route.ElevationProfile.SampleDistance(distance).Height +
                        CityStreetSurfacePlanner.SidewalkTop, point.y);
                    bool direct = squared <= MaximumLateralAdjustment * MaximumLateralAdjustment &&
                        segment.Kind != CityTerraceTransportSegmentKind.TurnLanding &&
                        Mathf.Abs(Vector2.Dot(forward, frontageTangent)) >= .9999f &&
                        Mathf.Abs(Vector2.Dot(point - XZ(outer), forward)) < .001f &&
                        along >= 0f && along <= segment.HorizontalLength;
                    result = new CityTerraceEntranceSeam(row, i, outer, proposed, distance, direct);
                }
            }
            if (float.IsPositiveInfinity(best)) throw new InvalidOperationException("The route has no pavement edge.");
            return result;
        }

        private static float TriangleGrade(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            return Mathf.Abs(normal.y) <= .000001f ? float.PositiveInfinity :
                new Vector2(normal.x, normal.z).magnitude / Mathf.Abs(normal.y) * 100f;
        }

        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
    }
}
