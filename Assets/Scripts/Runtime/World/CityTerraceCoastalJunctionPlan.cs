using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One original street arm, measured in its stable A-to-B path direction.</summary>
    public sealed class CityTerraceCoastalJunctionArm
    {
        internal CityTerraceCoastalJunctionArm(RoadEdge edge, CityRoadPath path, bool continuesAsRoute,
            float patchStart, float patchEnd, CityRoadElevationProfile elevationProfile)
        {
            Edge = edge; Path = path; ContinuesAsRoute = continuesAsRoute;
            PatchStartDistance = patchStart; PatchEndDistance = patchEnd;
            ElevationProfile = elevationProfile;
        }
        public RoadEdge Edge { get; }
        public CityRoadPath Path { get; }
        public bool ContinuesAsRoute { get; }
        public float PatchStartDistance { get; }
        public float PatchEndDistance { get; }
        /// <summary>
        /// Whole-edge replacement for a retained original arm, including its far flat inset.
        /// Null for the route-owned arm: beyond this patch the route owns the height, and
        /// the remaining original-edge overlap still needs resolution before activation.
        /// </summary>
        public CityRoadElevationProfile ElevationProfile { get; }
        public bool RequiresRemainingEdgeResolution => ContinuesAsRoute;
        /// <summary>Only tests retained original-arm grades; it does not approve route continuation.</summary>
        public bool IsGradeFeasible => ElevationProfile == null ||
            ElevationProfile.MaximumGradePercent <= CityElevationPlan.MaximumBusGradePercent + .001f;
    }

    /// <summary>
    /// One flat coastal junction per proposed route. Its disjoint asphalt and pavement
    /// polygons own only the mouth patch; retained coastal arms receive complete height
    /// profiles. This data does not activate, duplicate or approve the remaining roads.
    /// </summary>
    public sealed class CityTerraceCoastalJunctionPlan
    {
        private const float Tolerance = .001f;

        private CityTerraceCoastalJunctionPlan(CityLayout layout, CityTerraceTransportRoute route,
            Vector2Int node, Vector3 center, Vector2 forward)
        {
            Id = route.Id + ":coastal-junction"; RouteId = route.Id; Node = node;
            DatumY = center.y;
            HalfExtent = CityStreetSurfacePlanner.GetPublicSidewalkOuterDistance(layout);
            float asphaltWidth = layout.RoadWidth - 2f * CityStreetSurfacePlanner.SidewalkWidth;
            if (Mathf.Abs(HalfExtent * 2f - CityTerraceTransportPlan.CorridorWidth) > Tolerance || asphaltWidth <= 0f)
                throw new ArgumentException("The coastal junction requires the route's public street width.", nameof(layout));
            ValidateRouteMouth(route, center, forward, HalfExtent);

            var edges = new List<RoadEdge>();
            foreach (RoadEdge edge in layout.RoadEdges)
                if (edge.Contains(node) && layout.GetPathKind(edge) == CityPathKind.Street) edges.Add(edge);
            edges.Sort(RoadEdge.Compare);
            var arms = new List<CityTerraceCoastalJunctionArm>(edges.Count);
            var outer = new List<Vector2[]>();
            var asphalt = new List<Vector2[]>();
            int routeArms = 0;
            foreach (RoadEdge edge in edges)
            {
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                CityRoadPath outward = edge.A == node ? path : path.Reversed();
                if (path.Length <= HalfExtent + Tolerance)
                    throw new ArgumentException("A coastal street arm must extend beyond its junction mouth.", nameof(layout));
                bool continues = Vector2.Dot(outward.SampleDistance(0f).Tangent, forward) >= 1f - Tolerance;
                CityRoadPath mouth = CreateMouth(outward, HalfExtent);
                if (continues)
                {
                    routeArms++;
                    foreach (Vector2 vertex in mouth.Vertices)
                    {
                        Vector2 delta = vertex - XZ(center);
                        if (Mathf.Abs(CityRoadPolygon.Cross(forward, delta)) > Tolerance)
                            throw new ArgumentException("The route and its original street mouth must share a straight seam.", nameof(route));
                    }
                }
                outer.AddRange(mouth.Ribbon(HalfExtent * 2f));
                asphalt.AddRange(mouth.Ribbon(asphaltWidth));
                CityRoadElevationProfile profile = continues ? null : CreateRetainedProfile(layout, edge, path, node, DatumY, HalfExtent);
                arms.Add(new CityTerraceCoastalJunctionArm(edge, path, continues,
                    edge.A == node ? 0f : path.Length - HalfExtent,
                    edge.A == node ? HalfExtent : path.Length, profile));
            }
            if (routeArms > 1)
                throw new ArgumentException("A coastal route cannot own multiple incident street arms.", nameof(route));
            if (routeArms == 0)
            {
                // A courtyard layout may have only the two coastal streets at
                // this node. The new road then adds a branch, rather than
                // inventing an old edge and a phantom height profile for it.
                var routeMouth = new CityRoadPath(new[] { XZ(center), XZ(center) + forward * HalfExtent });
                outer.AddRange(routeMouth.Ribbon(HalfExtent * 2f));
                asphalt.AddRange(routeMouth.Ribbon(asphaltWidth));
            }
            RequiresRemainingEdgeResolution = routeArms != 0;
            Arms = arms.AsReadOnly();
            IsGradeFeasible = true;
            foreach (CityTerraceCoastalJunctionArm arm in arms)
                if (!arm.IsGradeFeasible) IsGradeFeasible = false;

            List<Vector2[]> road = Union(asphalt);
            List<Vector2[]> pavement = Union(outer);
            foreach (Vector2[] polygon in road) pavement = SubtractAll(pavement, polygon);
            RoadPolygons = Freeze(road); PavementPolygons = Freeze(pavement);

            Vector2 seamCenter = XZ(center) + forward * HalfExtent;
            Vector2 right = new Vector2(forward.y, -forward.x);
            RouteSeamStart = SamplePavementTop(seamCenter - right * HalfExtent);
            RouteSeamEnd = SamplePavementTop(seamCenter + right * HalfExtent);
            RouteRoadSeamStart = SampleRoadTop(seamCenter - right * asphaltWidth * .5f);
            RouteRoadSeamEnd = SampleRoadTop(seamCenter + right * asphaltWidth * .5f);
        }

        public string Id { get; }
        public string RouteId { get; }
        public Vector2Int Node { get; }
        public float DatumY { get; }
        public float HalfExtent { get; }
        public float RoadTopY => DatumY + CityStreetSurfacePlanner.RoadTop;
        public float PavementTopY => DatumY + CityStreetSurfacePlanner.SidewalkTop;
        public IReadOnlyList<CityTerraceCoastalJunctionArm> Arms { get; }
        public IReadOnlyList<IReadOnlyList<Vector2>> RoadPolygons { get; }
        public IReadOnlyList<IReadOnlyList<Vector2>> PavementPolygons { get; }
        /// <summary>Certifies retained branch grades only; unresolved route continuation remains explicit.</summary>
        public bool IsGradeFeasible { get; }
        /// <summary>The original route-aligned edge beyond the patch is not solved by this junction.</summary>
        public bool RequiresRemainingEdgeResolution { get; }
        /// <summary>
        /// Outer pavement endpoints of the route exit. Pavement occupies the two side
        /// strips, not a continuous surface between these endpoints across the asphalt.
        /// </summary>
        public Vector3 RouteSeamStart { get; }
        public Vector3 RouteSeamEnd { get; }
        public Vector3 RouteRoadSeamStart { get; }
        public Vector3 RouteRoadSeamEnd { get; }
        public Vector3 SampleRoadTop(Vector2 worldXZ) => new Vector3(worldXZ.x, RoadTopY, worldXZ.y);
        public Vector3 SamplePavementTop(Vector2 worldXZ) => new Vector3(worldXZ.x, PavementTopY, worldXZ.y);

        public static CityTerraceCoastalJunctionPlan Create(CityLayout layout, CityTerraceTransportRoute route)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (route.Samples.Count < 2)
                throw new ArgumentException("A coastal route requires an initial approach.", nameof(route));
            Vector3 start = route.Samples[0].Position;
            Vector2 forward = XZ(route.Samples[1].Position) - XZ(start);
            if (forward.sqrMagnitude <= Tolerance * Tolerance)
                throw new ArgumentException("A coastal route requires a finite initial direction.", nameof(route));
            Vector2Int node = default;
            Vector3 center = default;
            int matches = 0;
            foreach (Vector2Int candidate in layout.Nodes)
            {
                Vector3 position = layout.GetNodeWorldPosition(candidate);
                if ((XZ(position) - XZ(start)).sqrMagnitude > Tolerance * Tolerance) continue;
                node = candidate; center = position; matches++;
            }
            if (matches != 1 || Mathf.Abs(start.y - center.y) > Tolerance)
                throw new ArgumentException("A coastal route must start at one existing node and its original datum.", nameof(route));
            return new CityTerraceCoastalJunctionPlan(layout, route, node, center, forward.normalized);
        }

        private static void ValidateRouteMouth(CityTerraceTransportRoute route, Vector3 center, Vector2 forward, float extent)
        {
            if (route.TotalHorizontalLength < extent || route.Samples[0].HorizontalDistance != 0f ||
                Mathf.Abs(route.ElevationProfile.SampleDistance(extent).Height - center.y) > Tolerance)
                throw new ArgumentException("The route requires a flat approach covering its full public junction mouth.", nameof(route));
            foreach (CityRoadElevationStation station in route.ElevationProfile.Stations)
                if (station.HorizontalDistance <= extent && Mathf.Abs(station.Height - center.y) > Tolerance)
                    throw new ArgumentException("The route cannot climb inside its coastal junction.", nameof(route));
            for (int i = 1; i < route.Samples.Count; i++)
            {
                CityTerraceRouteSample previous = route.Samples[i - 1], current = route.Samples[i];
                float distance = Mathf.Min(extent, current.HorizontalDistance);
                float amount = Mathf.InverseLerp(previous.HorizontalDistance, current.HorizontalDistance, distance);
                Vector3 sample = Vector3.Lerp(previous.Position, current.Position, amount);
                if ((XZ(sample) - XZ(center) - forward * distance).sqrMagnitude > Tolerance * Tolerance ||
                    Mathf.Abs(sample.y - center.y) > Tolerance)
                    throw new ArgumentException("The route must remain straight and flat through the coastal junction.", nameof(route));
                if (current.HorizontalDistance >= extent) return;
            }
            throw new ArgumentException("The route samples do not cover its coastal junction.", nameof(route));
        }

        private static CityRoadElevationProfile CreateRetainedProfile(CityLayout layout, RoadEdge edge,
            CityRoadPath path, Vector2Int node, float nearY, float nearInset)
        {
            float farY = layout.GetNodeWorldPosition(edge.Other(node)).y;
            float farInset = layout.RoadGeometry.GetEndpointInset(edge, edge.Other(node));
            float run = path.Length - nearInset - farInset;
            if (run <= Tolerance)
            {
                if (nearY != farY)
                    throw new ArgumentException("The retained street is too short to preserve both flat junctions.", nameof(layout));
                return new CityRoadElevationProfile(new[] {
                    new CityRoadElevationStation(0f, nearY), new CityRoadElevationStation(path.Length, farY)
                }, CityElevationPlan.MaximumBusGradePercent);
            }
            float grade = Mathf.Abs(farY - nearY) / run * 100f;
            var stations = edge.A == node ? new[] {
                new CityRoadElevationStation(0f, nearY), new CityRoadElevationStation(nearInset, nearY),
                new CityRoadElevationStation(path.Length - farInset, farY), new CityRoadElevationStation(path.Length, farY)
            } : new[] {
                new CityRoadElevationStation(0f, farY), new CityRoadElevationStation(farInset, farY),
                new CityRoadElevationStation(path.Length - nearInset, nearY), new CityRoadElevationStation(path.Length, nearY)
            };
            // Retain the actual slope when infeasible; IsGradeFeasible remains the bus-limit verdict.
            return new CityRoadElevationProfile(stations, Mathf.Max(CityElevationPlan.MaximumBusGradePercent, grade));
        }

        private static CityRoadPath CreateMouth(CityRoadPath outward, float extent)
        {
            var vertices = new List<Vector2> { outward.Vertices[0] };
            float distance = 0f;
            for (int i = 1; i < outward.Vertices.Count; i++)
            {
                distance += Vector2.Distance(outward.Vertices[i - 1], outward.Vertices[i]);
                if (distance >= extent - Tolerance) break;
                vertices.Add(outward.Vertices[i]);
            }
            vertices.Add(outward.SampleDistance(extent).Position);
            return new CityRoadPath(vertices);
        }

        private static List<Vector2[]> Union(IReadOnlyList<Vector2[]> polygons)
        {
            var result = new List<Vector2[]>();
            foreach (Vector2[] polygon in polygons)
            {
                var pieces = new List<Vector2[]> { polygon };
                foreach (Vector2[] existing in result) pieces = SubtractAll(pieces, existing);
                result.AddRange(pieces);
            }
            return result;
        }

        private static List<Vector2[]> SubtractAll(IReadOnlyList<Vector2[]> pieces, IReadOnlyList<Vector2> cut)
        {
            var result = new List<Vector2[]>();
            foreach (Vector2[] piece in pieces) result.AddRange(CityRoadPolygon.Subtract(piece, cut));
            return result;
        }

        private static IReadOnlyList<IReadOnlyList<Vector2>> Freeze(IReadOnlyList<Vector2[]> polygons)
        {
            var result = new List<IReadOnlyList<Vector2>>(polygons.Count);
            foreach (Vector2[] polygon in polygons) result.Add(Array.AsReadOnly((Vector2[])polygon.Clone()));
            return result.AsReadOnly();
        }

        private static Vector2 XZ(Vector3 position) => new Vector2(position.x, position.z);
    }
}
