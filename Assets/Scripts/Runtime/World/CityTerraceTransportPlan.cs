using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public enum CityTerraceTransportBank { West, East }
    public enum CityTerraceTransportSegmentKind { Climb, Approach, TurnLanding, StopLanding }
    public enum CityTerraceReservationKind { Building, ProtectedPlace, Park, River, Coast, ExistingRoad, Terrace, ProtectedAccess }
    public enum CityTerraceRoadCrossingKind { CoastalAttachment, AtGradeUnresolved, GradeSeparatedUnresolved }

    [Flags]
    public enum CityTerracePlacementRequirement
    {
        None = 0, CorridorReservation = 1, RoadConnections = 2, VehicleSweptPath = 4,
        TerrainTieIns = 8, FootprintConflicts = 16, UnresolvedRoadCrossings = 32, GradeLimit = 64
    }

    /// <summary>
    /// A sampled overlap with one existing road. Surface separation is not
    /// under-deck clearance: neither a bridge nor a tunnel is approved here.
    /// A coastal attachment identifies the existing socket, not a built junction.
    /// </summary>
    public readonly struct CityTerraceRoadCrossing
    {
        internal CityTerraceRoadCrossing(string routeId, int segmentIndex, string reservationId,
            RoadEdge roadEdge, Vector2 worldXZ, float routeDatum, float roadDatum,
            bool centerlinesIntersect, CityTerraceRoadCrossingKind kind)
        {
            RouteId = routeId; SegmentIndex = segmentIndex; ReservationId = reservationId;
            Id = routeId + ":crossing:" + segmentIndex + ":" + reservationId;
            RoadEdge = roadEdge; WorldXZ = worldXZ; RouteDatumY = routeDatum; RoadDatumY = roadDatum;
            CenterlinesIntersect = centerlinesIntersect; Kind = kind;
        }
        public string Id { get; }
        public string RouteId { get; }
        public int SegmentIndex { get; }
        public string ReservationId { get; }
        public RoadEdge RoadEdge { get; }
        public Vector2 WorldXZ { get; }
        public float RouteDatumY { get; }
        public float RoadDatumY { get; }
        public Vector3 RoutePosition => new Vector3(WorldXZ.x, RouteDatumY + CityStreetSurfacePlanner.RoadTop, WorldXZ.y);
        public Vector3 RoadPosition => new Vector3(WorldXZ.x, RoadDatumY + CityStreetSurfacePlanner.RoadTop, WorldXZ.y);
        public float SignedSurfaceSeparation => RouteDatumY - RoadDatumY;
        public bool CenterlinesIntersect { get; }
        public CityTerraceRoadCrossingKind Kind { get; }
        public bool RequiresCrossingResolution => Kind != CityTerraceRoadCrossingKind.CoastalAttachment;
    }

    public readonly struct CityTerraceRouteSample
    {
        internal CityTerraceRouteSample(Vector3 position, float distance)
        { Position = position; HorizontalDistance = distance; }
        public Vector3 Position { get; }
        public float HorizontalDistance { get; }
    }

    public readonly struct CityTerraceTransportSegment
    {
        internal CityTerraceTransportSegment(Vector3 start, Vector3 end, CityTerraceTransportSegmentKind kind)
        {
            Start = start; End = end; Kind = kind;
            HorizontalLength = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(end.x, end.z));
            GradePercent = Mathf.Abs(end.y - start.y) / HorizontalLength * 100f;
        }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public CityTerraceTransportSegmentKind Kind { get; }
        public float HorizontalLength { get; }
        public float GradePercent { get; }
    }

    /// <summary>A conservative XZ corridor overlap, not a height-aware clearance verdict.</summary>
    public readonly struct CityTerraceReservation
    {
        internal CityTerraceReservation(string routeId, int segmentIndex, string id,
            CityTerraceReservationKind kind, Rect bounds)
        { RouteId = routeId; SegmentIndex = segmentIndex; Id = id; Kind = kind; Bounds = bounds; }
        public string RouteId { get; }
        /// <summary>Minus one identifies the separate courtyard candidate envelope.</summary>
        public int SegmentIndex { get; }
        public string Id { get; }
        public CityTerraceReservationKind Kind { get; }
        public Rect Bounds { get; }
        public bool IsProtected => Kind != CityTerraceReservationKind.Building &&
            Kind != CityTerraceReservationKind.ExistingRoad && Kind != CityTerraceReservationKind.ProtectedAccess;
    }

    /// <summary>A fixed surveyed obstacle, including upper-building projection.</summary>
    public sealed class CityTerraceTransportFootprint
    {
        internal CityTerraceTransportFootprint(string id, CityTerraceReservationKind kind, Vector2[] polygon)
        { Id = id; Kind = kind; Polygon = Array.AsReadOnly((Vector2[])polygon.Clone()); }
        public string Id { get; }
        public CityTerraceReservationKind Kind { get; }
        public IReadOnlyList<Vector2> Polygon { get; }
    }

    /// <summary>
    /// A surveyed street corridor with flat turns and stops. Placement requirements
    /// remain authoritative: geometry alone cannot approve junctions or vehicle paths.
    /// </summary>
    public sealed class CityTerraceTransportRoute
    {
        internal CityTerraceTransportRoute(string id, CityTerraceTransportBank bank,
            IList<CityTerraceRouteSample> samples, IList<CityTerraceTransportSegment> segments,
            CityRoadElevationProfile elevationProfile, IList<CityTerraceReservation> reservations,
            IReadOnlyList<Vector2[]> corridor)
        {
            Id = id; Bank = bank;
            ElevationProfile = elevationProfile;
            var polygons = new List<IReadOnlyList<Vector2>>(corridor.Count);
            foreach (Vector2[] polygon in corridor) polygons.Add(Array.AsReadOnly((Vector2[])polygon.Clone()));
            CorridorPolygons = polygons.AsReadOnly();
            float climb = 0f;
            foreach (CityTerraceTransportSegment segment in segments)
                if (segment.Kind == CityTerraceTransportSegmentKind.Climb) climb += segment.HorizontalLength;
            MaximumReachableSummitElevation = samples[0].Position.y + climb * CityElevationPlan.MaximumBusGradePercent * .01f;
            Samples = new ReadOnlyCollection<CityTerraceRouteSample>(samples);
            Segments = new ReadOnlyCollection<CityTerraceTransportSegment>(segments);
            Reservations = new ReadOnlyCollection<CityTerraceReservation>(reservations);
        }
        public string Id { get; }
        public CityTerraceTransportBank Bank { get; }
        public IReadOnlyList<CityTerraceRouteSample> Samples { get; }
        public IReadOnlyList<CityTerraceTransportSegment> Segments { get; }
        public IReadOnlyList<CityTerraceReservation> Reservations { get; }
        public CityRoadElevationProfile ElevationProfile { get; }
        public IReadOnlyList<IReadOnlyList<Vector2>> CorridorPolygons { get; }
        public float MaximumReachableSummitElevation { get; }
        public bool IsGradeFeasible => MaximumGradePercent <= CityElevationPlan.MaximumBusGradePercent + .001f;
        public float TotalHorizontalLength => ElevationProfile.HorizontalLength;
        public float MaximumGradePercent => ElevationProfile.MaximumGradePercent;
    }

    /// <summary>
    /// Editor-only consumer of the terrace proposal: two bounded street routes
    /// and a full-building overlap inventory. No live city graph or surface changes.
    /// </summary>
    public sealed class CityTerraceTransportPlan
    {
        public const float TurnRadius = CityBusPlanner.LeftTurnRadius;
        // Six metre asphalt and both existing two metre public pavements.
        // Retaining supports are still a separate, unresolved terrain contract.
        public const float CorridorWidth = CityGenerationSettings.DefaultRoadWidth -
            2f * CityStreetSurfacePlanner.SidewalkWidth + 2f * CityStreetSurfacePlanner.PublicSidewalkWidth;
        public const float StopLength = 12f;
        public const int MaximumRouteSamples = 192;
        private const int QuarterTurnSegments = 6;
        private const float FlatEndLength = 4f;
        private const float CoastalApproachLength = CorridorWidth * .5f;

        private sealed class Footprint
        {
            internal string Id;
            internal CityTerraceReservationKind Kind;
            internal Vector2[] Polygon;
            internal Rect Bounds;
            internal RoadEdge? RoadEdge;
        }

        private CityTerraceTransportPlan(CityLayout layout, CityTerraceMasterPlan master)
        {
            BlueprintId = layout.BlueprintId; Seed = layout.Seed;
            TargetSummitElevation = master.TargetSummitElevation;
            List<Footprint> footprints = CollectFootprints(layout, master);
            var surveyed = new List<CityTerraceTransportFootprint>();
            foreach (Footprint footprint in footprints)
                if (footprint.Kind != CityTerraceReservationKind.ExistingRoad)
                    surveyed.Add(new CityTerraceTransportFootprint(footprint.Id, footprint.Kind, footprint.Polygon));
            Footprints = surveyed.AsReadOnly();
            var routes = new List<CityTerraceTransportRoute>(2);
            routes.Add(CreateRoute(layout, master, CityTerraceTransportBank.West, footprints));
            routes.Add(CreateRoute(layout, master, CityTerraceTransportBank.East, footprints));
            Routes = new ReadOnlyCollection<CityTerraceTransportRoute>(routes);
            var junctions = new List<CityTerraceCoastalJunctionPlan>(routes.Count);
            foreach (CityTerraceTransportRoute route in routes)
                junctions.Add(CityTerraceCoastalJunctionPlan.Create(layout, route));
            CoastalJunctions = junctions.AsReadOnly();
            EntranceConnections = CityTerraceEntranceConnectionPlan.Create(layout, routes);
            RoadCrossings = CollectRoadCrossings(layout, routes, footprints).AsReadOnly();
            var reservations = new List<CityTerraceReservation>();
            foreach (CityTerraceTransportRoute route in routes) reservations.AddRange(route.Reservations);

            // Historical study envelope, retained for comparison. It overlaps
            // the now-built terrace precinct; it is not a vacant reservation.
            PilotCandidateBounds = new Rect(100f, 47f, 40f, 34f);
            var pilot = new List<CityTerraceReservation>();
            AppendOverlaps("east-courtyard-pilot", -1, CityRoadPolygon.Rectangle(PilotCandidateBounds), footprints, pilot);
            PilotReservations = new ReadOnlyCollection<CityTerraceReservation>(pilot);
            Reservations = new ReadOnlyCollection<CityTerraceReservation>(reservations);
            foreach (CityTerraceReservation reservation in reservations)
            {
                HasProtectedConflicts |= reservation.IsProtected;
                HasBuildingConflicts |= reservation.Kind == CityTerraceReservationKind.Building ||
                    reservation.Kind == CityTerraceReservationKind.ProtectedPlace || reservation.Kind == CityTerraceReservationKind.Terrace;
            }
            // Coastal surface plans do not resolve inland streets, entrance
            // regrading, vehicle clearance or supports, and cannot activate it.
            OutstandingPlacementRequirements = CityTerracePlacementRequirement.CorridorReservation |
                CityTerracePlacementRequirement.RoadConnections | CityTerracePlacementRequirement.VehicleSweptPath |
                CityTerracePlacementRequirement.TerrainTieIns;
            foreach (CityTerraceTransportRoute route in routes)
                foreach (CityTerraceReservation reservation in route.Reservations)
                    if (reservation.Kind != CityTerraceReservationKind.ExistingRoad &&
                        reservation.Kind != CityTerraceReservationKind.ProtectedAccess)
                        OutstandingPlacementRequirements |= CityTerracePlacementRequirement.FootprintConflicts;
            foreach (CityTerraceTransportRoute route in routes)
                if (!route.IsGradeFeasible) OutstandingPlacementRequirements |= CityTerracePlacementRequirement.GradeLimit;
            foreach (CityTerraceCoastalJunctionPlan junction in junctions)
                if (!junction.IsGradeFeasible) OutstandingPlacementRequirements |= CityTerracePlacementRequirement.GradeLimit;
            foreach (CityTerraceRoadCrossing crossing in RoadCrossings)
                if (crossing.RequiresCrossingResolution)
                    OutstandingPlacementRequirements |= CityTerracePlacementRequirement.UnresolvedRoadCrossings;
        }

        public string BlueprintId { get; }
        public int Seed { get; }
        public float TargetSummitElevation { get; }
        public IReadOnlyList<CityTerraceTransportRoute> Routes { get; }
        public IReadOnlyList<CityTerraceReservation> Reservations { get; }
        public IReadOnlyList<CityTerraceRoadCrossing> RoadCrossings { get; }
        public IReadOnlyList<CityTerraceCoastalJunctionPlan> CoastalJunctions { get; }
        public IReadOnlyList<CityTerraceEntranceConnectionPlan> EntranceConnections { get; }
        public CityTerracePlacementRequirement OutstandingPlacementRequirements { get; }
        public bool IsPlacementReady => OutstandingPlacementRequirements == CityTerracePlacementRequirement.None;
        public bool HasProtectedConflicts { get; }
        public bool HasBuildingConflicts { get; }
        public IReadOnlyList<CityTerraceTransportFootprint> Footprints { get; }
        public Rect PilotCandidateBounds { get; }
        public IReadOnlyList<CityTerraceReservation> PilotReservations { get; }

        public static CityTerraceTransportPlan Create(CityLayout layout, CityTerraceMasterPlan master)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (master == null) throw new ArgumentNullException(nameof(master));
            if (layout.BlueprintId != CityBlueprintCatalog.DefaultBlueprintId || !layout.SpatialPlan.IsCompactCoastal ||
                master.BlueprintId != layout.BlueprintId || master.Seed != layout.Seed)
                throw new ArgumentException("The transport study requires its matching production coastal terrace plan.", nameof(master));
            return new CityTerraceTransportPlan(layout, master);
        }

        private static CityTerraceTransportRoute CreateRoute(CityLayout layout, CityTerraceMasterPlan master,
            CityTerraceTransportBank bank, List<Footprint> footprints)
        {
            string id = bank == CityTerraceTransportBank.West ? "terrace-west-switchback" : "terrace-east-switchback";
            Alignment alignment = StreetAlignments.GetValue(layout, BuildAlignments)[(int)bank];
            var points = alignment.Points;
            var kinds = alignment.Kinds;
            Vector2 start = points[0];
            if (!layout.ElevationPlan.TrySampleSurface(start, CitySurfaceRole.RoadDatum, out float startY, out _))
                throw new InvalidOperationException("The coastal attachment has no surveyed road support.");
            float climbingLength = 0f;
            for (int i = 0; i < kinds.Count; i++)
                if (kinds[i] == CityTerraceTransportSegmentKind.Climb)
                    climbingLength += Vector2.Distance(points[i], points[i + 1]);
            float grade = (master.TargetSummitElevation - startY) / climbingLength * 100f;
            var stations = new CityRoadElevationStation[points.Count];
            stations[0] = new CityRoadElevationStation(0f, startY);
            float distance = 0f, climbed = 0f;
            for (int i = 0; i < kinds.Count; i++)
            {
                float length = Vector2.Distance(points[i], points[i + 1]);
                distance += length;
                if (kinds[i] == CityTerraceTransportSegmentKind.Climb) climbed += length;
                stations[i + 1] = new CityRoadElevationStation(distance,
                    Mathf.Lerp(startY, master.TargetSummitElevation, climbed / climbingLength));
            }
            // An infeasible requested summit remains inspectable, but the
            // explicit GradeLimit requirement prevents declaring it placed.
            var profile = new CityRoadElevationProfile(stations, Mathf.Max(CityElevationPlan.MaximumBusGradePercent, grade));
            var samples = new List<CityTerraceRouteSample>(points.Count);
            var segments = new List<CityTerraceTransportSegment>(kinds.Count);
            var reservations = new List<CityTerraceReservation>();
            IReadOnlyList<Vector2[]> corridor = alignment.Corridor;
            for (int i = 0; i < points.Count; i++)
            {
                float stationDistance = profile.Stations[i].HorizontalDistance;
                float height = profile.SampleDistance(stationDistance).Height;
                samples.Add(new CityTerraceRouteSample(new Vector3(points[i].x, height, points[i].y), stationDistance));
            }
            for (int i = 0; i < kinds.Count; i++)
            {
                segments.Add(new CityTerraceTransportSegment(samples[i].Position, samples[i + 1].Position, kinds[i]));
                AppendOverlaps(id, i, corridor[i], footprints, reservations);
            }
            return new CityTerraceTransportRoute(id, bank, samples, segments, profile, reservations, corridor);
        }

        // A rear wing is chosen before the inventory of final building masses
        // exists. Share just the XZ street ribbons with that placement step;
        // deriving its obstacle from Create(layout, master) would recurse.
        private sealed class Alignment
        {
            internal List<Vector2> Points;
            internal List<CityTerraceTransportSegmentKind> Kinds;
            internal IReadOnlyList<Vector2[]> Corridor;
            internal Rect[] Bounds;
        }

        private static readonly ConditionalWeakTable<CityLayout, Alignment[]> StreetAlignments =
            new ConditionalWeakTable<CityLayout, Alignment[]>();

        private static Alignment[] BuildAlignments(CityLayout layout) => new[] {
            BuildAlignment(layout, CityTerraceTransportBank.West), BuildAlignment(layout, CityTerraceTransportBank.East) };

        internal static bool OverlapsSurveyedStreet(CityLayout layout, IReadOnlyList<Vector2> body)
        {
            if (layout.BlueprintId != CityBlueprintCatalog.DefaultBlueprintId || !layout.SpatialPlan.IsCompactCoastal) return false;
            Rect bounds = CityRoadPolygon.Bounds(body);
            foreach (Alignment alignment in StreetAlignments.GetValue(layout, BuildAlignments))
                for (int i = 0; i < alignment.Corridor.Count; i++)
                    if (bounds.Overlaps(alignment.Bounds[i]) && Intersects(body, alignment.Corridor[i])) return true;
            return false;
        }

        private static Alignment BuildAlignment(CityLayout layout, CityTerraceTransportBank bank)
        {
            Vector2[] waypoints = StreetWaypoints(layout, bank);
            var points = new List<Vector2>(MaximumRouteSamples);
            var kinds = new List<CityTerraceTransportSegmentKind>(MaximumRouteSamples - 1);
            points.Add(waypoints[0]);
            void Append(Vector2 point, CityTerraceTransportSegmentKind kind)
            {
                if (Vector2.Distance(points[points.Count - 1], point) < .001f) return;
                if (points.Count >= MaximumRouteSamples)
                    throw new InvalidOperationException("The bounded transport route exceeds its station budget.");
                points.Add(point); kinds.Add(kind);
            }
            // Place stops on the three longest straight runs, stably ordered.
            // The rounded turns themselves are flat: extra arbitrary setbacks
            // would discard usable climbing length twice at each junction.
            var insets = new float[waypoints.Length];
            insets[0] = CoastalApproachLength;
            insets[insets.Length - 1] = FlatEndLength;
            for (int i = 1; i < waypoints.Length - 1; i++)
            {
                float turn = Vector2.Angle(waypoints[i] - waypoints[i - 1], waypoints[i + 1] - waypoints[i]);
                insets[i] = TurnRadius * Mathf.Tan(turn * .5f * Mathf.Deg2Rad);
            }
            var runs = new List<(int index, float length)>();
            for (int i = 0; i < waypoints.Length - 1; i++)
                runs.Add((i, Vector2.Distance(waypoints[i], waypoints[i + 1]) - insets[i] - insets[i + 1]));
            runs.Sort((a, b) => { int order = b.length.CompareTo(a.length); return order != 0 ? order : a.index.CompareTo(b.index); });
            var stops = new HashSet<int>();
            for (int i = 0; i < 3; i++)
            {
                if (runs[i].length < StopLength + 2f)
                    throw new InvalidOperationException("The route has no room for its three flat stops.");
                stops.Add(runs[i].index);
            }
            for (int i = 0; i < waypoints.Length - 1; i++)
            {
                Vector2 direction = (waypoints[i + 1] - waypoints[i]).normalized;
                if (i == 0) Append(points[0] + direction * CoastalApproachLength, CityTerraceTransportSegmentKind.Approach);
                Vector2 end = waypoints[i + 1] - direction * insets[i + 1];
                if (stops.Contains(i))
                {
                    Vector2 middle = (points[points.Count - 1] + end) * .5f;
                    Append(middle - direction * (StopLength * .5f), CityTerraceTransportSegmentKind.Climb);
                    Append(middle + direction * (StopLength * .5f), CityTerraceTransportSegmentKind.StopLanding);
                }
                Append(end, CityTerraceTransportSegmentKind.Climb);
                if (i == waypoints.Length - 2)
                {
                    Append(waypoints[i + 1], CityTerraceTransportSegmentKind.Approach);
                    continue;
                }
                Vector2 next = (waypoints[i + 2] - waypoints[i + 1]).normalized;
                float turn = Vector2.SignedAngle(direction, next);
                Vector2 inward = Vector2.Perpendicular(direction) * Mathf.Sign(turn);
                Vector2 center = end + inward * TurnRadius;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(turn) * QuarterTurnSegments / 90f));
                for (int step = 1; step <= steps; step++)
                {
                    float angle = Mathf.Abs(turn) * Mathf.Deg2Rad * step / steps;
                    Append(center - inward * (TurnRadius * Mathf.Cos(angle)) +
                        direction * (TurnRadius * Mathf.Sin(angle)), CityTerraceTransportSegmentKind.TurnLanding);
                }
            }
            var corridor = new CityRoadPath(points).Ribbon(CorridorWidth);
            var bounds = new Rect[corridor.Count];
            for (int i = 0; i < bounds.Length; i++) bounds[i] = CityRoadPolygon.Bounds(corridor[i]);
            return new Alignment { Points = points, Kinds = kinds, Corridor = corridor, Bounds = bounds };
        }

        private static Vector2[] StreetWaypoints(CityLayout layout, CityTerraceTransportBank bank)
        {
            // Metres relative to the production coastal grid. Keep existing
            // buildings fixed: the east route bypasses the northern rear house,
            // loops around Home/Bar/shop, passes west of the fair, and skips the
            // cafe's private north edge. Small corner offsets clear the actual
            // fixed-metre envelopes; the inventory remains authoritative for
            // every seed, so an altered layout cannot inherit a false clearance.
            // Paired offsets keep the relocated bar's private corner inside
            // the inner R6 arc, the church beyond the outer arc, and the shop
            // clear of the southern return. North of the new court link two
            // shallow R6 bends pass between the older stair landing and the
            // projecting house. The eastern leg rejoins the live (13,8) junction.
            Vector2[] coordinates = bank == CityTerraceTransportBank.West
                ? new[] { new Vector2(-104,156), new Vector2(-104,130), new Vector2(-128,130),
                    new Vector2(-128,104), new Vector2(-51.75f,104), new Vector2(-51.75f,78), new Vector2(-156,78),
                    new Vector2(-156,26), new Vector2(-78,26), new Vector2(-78,0), new Vector2(-156,0),
                    new Vector2(-156,-26), new Vector2(-104,-26), new Vector2(-104,-78), new Vector2(-156,-78),
                    new Vector2(-156,-104), new Vector2(-52,-104), new Vector2(-52,-130),
                    new Vector2(-156,-130), new Vector2(-156,-156), new Vector2(-52,-156) }
                : new[] { new Vector2(104,156), new Vector2(104,130), new Vector2(77.75f,130),
                    new Vector2(77.75f,104), new Vector2(130,104), new Vector2(130,131.25f),
                    new Vector2(151.5f,131.25f), new Vector2(162,130), new Vector2(182.5f,130),
                    new Vector2(182.5f,-52.25f), new Vector2(155.5f,-52.25f), new Vector2(155.5f,-26),
                    new Vector2(130,-26), new Vector2(130,-52), new Vector2(77.75f,-52), new Vector2(77.75f,-78),
                    new Vector2(156,-78), new Vector2(156,-103.875f), new Vector2(78,-103.875f),
                    new Vector2(78,-156.25f), new Vector2(181,-156.25f) };
            Vector3 zero = layout.GetGridWorldPosition(new Vector2Int(6, 6));
            for (int i = 0; i < coordinates.Length; i++) coordinates[i] += new Vector2(zero.x, zero.z);
            return coordinates;
        }

        private static List<Footprint> CollectFootprints(CityLayout layout, CityTerraceMasterPlan master)
        {
            var result = new List<Footprint>();
            void Add(string id, CityTerraceReservationKind kind, Vector2[] polygon, RoadEdge? road = null) => result.Add(
                new Footprint { Id = id, Kind = kind, Polygon = polygon, Bounds = CityRoadPolygon.Bounds(polygon), RoadEdge = road });
            var roads = new List<RoadEdge>(layout.RoadEdges);
            roads.Sort(RoadEdge.Compare);
            foreach (RoadEdge road in roads)
            {
                string id = $"existing-road:{road.A.x},{road.A.y}:{road.B.x},{road.B.y}";
                CityRoadPath path = layout.RoadGeometry.Get(road);
                float width = layout.GetTravelWidth(road);
                foreach (Vector2[] polygon in path.Ribbon(width))
                    Add(id, CityTerraceReservationKind.ExistingRoad, polygon, road);
                // The existing road surface also owns flat endpoint squares.
                foreach (Vector2 endpoint in new[] { path.Vertices[0], path.Vertices[path.Vertices.Count - 1] })
                    Add(id, CityTerraceReservationKind.ExistingRoad,
                        CityRoadPolygon.Rectangle(new Rect(endpoint - Vector2.one * (width * .5f), Vector2.one * width)), road);
                foreach (CityRoadJunction junction in layout.RoadGeometry.Junctions)
                    if (road == junction.OwnerEdge)
                        foreach (Vector2[] polygon in junction.RoadPolygons)
                            Add(id, CityTerraceReservationKind.ExistingRoad, polygon, road);
            }
            CityTerraceDistrictPlan terrace = CityTerraceDistrictPlan.ForLayout(layout);
            if (terrace.IsEnabled)
            {
                Add("terrace:precinct", CityTerraceReservationKind.Terrace, CityRoadPolygon.Rectangle(terrace.ReservedBounds));
                Add("terrace:lower-residence", CityTerraceReservationKind.Terrace, CityRoadPolygon.Rectangle(terrace.LowerCourtBounds));
                Add("terrace:lower-residence-entry", CityTerraceReservationKind.Terrace,
                    CityRoadPolygon.Rectangle(terrace.LowerCourtEntryBounds));
                if (terrace.HasCourtyardLink)
                    Add("terrace:courtyard-link-ramp", CityTerraceReservationKind.Terrace,
                        CityRoadPolygon.Rectangle(terrace.CourtyardLinkRampBounds));
                foreach (CityTerracePilotFloor floor in terrace.Floors)
                {
                    Vector2[] polygon = CityRoadPolygon.Rectangle(terrace.WorldBounds(floor.Bounds));
                    Add("terrace:floor:" + floor.Id, CityTerraceReservationKind.Terrace, polygon);
                    if (floor.HasFoundation)
                        Add("terrace:foundation:" + floor.Id, CityTerraceReservationKind.Terrace, polygon);
                }
                foreach (CityExteriorStairFlightDescriptor flight in terrace.Flights)
                {
                    Rect support = CityTerracePilotPlan.FlightBounds(flight);
                    if (Mathf.Abs(flight.Direction.x) > .5f) { support.yMin -= .15f; support.yMax += .15f; }
                    else { support.xMin -= .15f; support.xMax += .15f; }
                    Add("terrace:stair-support:" + flight.Id, CityTerraceReservationKind.Terrace,
                        CityRoadPolygon.Rectangle(terrace.WorldBounds(support)));
                }
                Add("terrace:street-approach", CityTerraceReservationKind.Terrace, CityRoadPolygon.Rectangle(terrace.EntryBounds));
                Add("terrace:lower-pass", CityTerraceReservationKind.Terrace, CityRoadPolygon.Rectangle(terrace.LowerPassBounds));
            }
            int massIndex = 0;
            foreach (BuildingLot lot in layout.BuildingMasses)
            {
                if (!lot.HasBuilding && !lot.IsCafe) continue;
                bool landmark = false;
                foreach (Vector2Int cell in layout.PrimaryLandmarkCells.Values) landmark |= cell == lot.Cell;
                bool protect = lot.IsPlayerHome || lot.IsBar || lot.IsSupermarket || lot.IsCafe || landmark;
                string id = lot.IsPlayerHome ? "player-home" : lot.IsBar ? lot.BarId :
                    lot.IsSupermarket ? "supermarket" : (protect ? "landmark:" : "building:") + lot.Cell + ":" + massIndex;
                CityTerraceReservationKind kind = protect
                    ? CityTerraceReservationKind.ProtectedPlace : CityTerraceReservationKind.Building;
                if (protect)
                {
                    // A grid cell reaches adjacent street centrelines. Keep
                    // its private ground, without pretending that the existing
                    // public pavement belongs to the building. Actual models
                    // and entrance aprons below remain separate hard obstacles.
                    Rect privateGround = layout.GetCellWorldBounds(lot.Cell);
                    privateGround.xMin += TransportPrivateInset(layout, lot.Cell, Vector2Int.left);
                    privateGround.xMax -= TransportPrivateInset(layout, lot.Cell, Vector2Int.right);
                    privateGround.yMin += TransportPrivateInset(layout, lot.Cell, Vector2Int.down);
                    privateGround.yMax -= TransportPrivateInset(layout, lot.Cell, Vector2Int.up);
                    Add(id, kind, CityRoadPolygon.Rectangle(privateGround));
                }
                Add(id, kind, TransportBuildingEnvelope(lot));
                if ((lot.IsPlayerHome || lot.IsBar || lot.IsSupermarket) && lot.HasRoadFrontage)
                {
                    float width = lot.IsSupermarket ? SupermarketEntranceGeometry.WalkwayWidth :
                        lot.IsPlayerHome ? PlayerHomeEntranceGeometry.WalkwayWidth : BarEntranceGeometry.WalkwayWidth;
                    CityEntranceForecourtPlan apron = CityStreetSurfacePlanner.CreateEntranceForecourt(layout, lot, width);
                    Add(id + ":forecourt", CityTerraceReservationKind.ProtectedAccess,
                        CityRoadPolygon.Rectangle(apron.Footprint));
                }
                massIndex++;
            }
            CityCafePlan cafe = CityCafePlan.Create(layout);
            if (cafe != null)
                Add("city-cafe:approach", CityTerraceReservationKind.ProtectedAccess,
                    CityRoadPolygon.Rectangle(cafe.ApproachBounds));
            CityLaundryPlan laundry = CityLaundryPlan.ForLayout(layout);
            if (laundry.IsEnabled)
            {
                Add("city-laundry:site", CityTerraceReservationKind.ProtectedPlace,
                    CityRoadPolygon.Rectangle(laundry.ReservationBounds));
                int index = 0;
                foreach (Vector2[] access in TransportAccessFootprints(laundry.AccessPath))
                    Add("city-laundry:access:" + index++, CityTerraceReservationKind.ProtectedAccess, access);
            }
            CityDogParkPlan dogs = CityDogParkPlan.ForLayout(layout);
            if (dogs.IsEnabled)
            {
                Add("city-dog-park:site", CityTerraceReservationKind.ProtectedPlace,
                    CityRoadPolygon.Rectangle(dogs.ReservationBounds));
                int index = 0;
                foreach (Vector2[] access in TransportAccessFootprints(dogs.AccessPath))
                    Add("city-dog-park:access:" + index++, CityTerraceReservationKind.ProtectedAccess, access);
            }
            CityFairGroundSitePlan fair = CityFairGroundSitePlan.ForLayout(layout);
            if (fair.IsEnabled)
                Add("city-fair:site", CityTerraceReservationKind.ProtectedPlace, CityRoadPolygon.Rectangle(fair.Bounds));
            if (CityArchShelterPlacementResolver.TryCreate(layout, out CityArchShelterPlacement arch))
            {
                Bounds bounds = arch.StructureBounds;
                Add("city-arch-shelter:structure", CityTerraceReservationKind.ProtectedPlace,
                    CityRoadPolygon.Rectangle(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z)));
                Add("city-arch-shelter:passage", CityTerraceReservationKind.ProtectedPlace,
                    CityRoadPolygon.Rectangle(arch.CommonFacadeFootprint));
            }
            foreach (CityDistrictPointOfInterestDescriptor point in layout.DistrictPointsOfInterest)
            {
                Add(point.Id, CityTerraceReservationKind.ProtectedPlace, CityRoadPolygon.Rectangle(point.PublicBounds));
                foreach (CityDistrictPointOfInterestAccessDescriptor access in point.Accesses)
                    Add(access.Id, CityTerraceReservationKind.ProtectedPlace, CityRoadPolygon.Rectangle(access.ApproachBounds));
            }
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (surface.Kind == CitySurfaceKind.BuildableGround || surface.Kind == CitySurfaceKind.OpenGround) continue;
                CityTerraceReservationKind kind = surface.Kind == CitySurfaceKind.ParkGround ? CityTerraceReservationKind.Park :
                    surface.Kind == CitySurfaceKind.RiverWater ? CityTerraceReservationKind.River :
                    surface.Kind == CitySurfaceKind.Beach || surface.Kind == CitySurfaceKind.Water ? CityTerraceReservationKind.Coast :
                    CityTerraceReservationKind.ProtectedPlace;
                Add(surface.AreaId + ":" + surface.Cell, kind, CityRoadPolygon.Rectangle(surface.WorldBounds));
            }
            CityRiverDefinition river = layout.Blueprint.River;
            float west = layout.GetGridWorldPosition(new Vector2Int(river.CorridorCellX - 1, 0)).x;
            float east = layout.GetGridWorldPosition(new Vector2Int(river.CorridorCellX + 2, 0)).x;
            Add("low-river-corridor", CityTerraceReservationKind.River, CityRoadPolygon.Rectangle(
                Rect.MinMaxRect(west, master.CoreWorldXZBounds.yMin, east, master.CoreWorldXZBounds.yMax)));
            CityPortPlan port = CitySeacoastPlanner.Create(layout).Port;
            if (port != null) Add("working-port", CityTerraceReservationKind.ProtectedPlace, CityRoadPolygon.Rectangle(port.LandBounds));
            return result;
        }

        private static Vector2[] TransportBuildingEnvelope(BuildingLot lot)
        {
            if (lot.IsPlayerHome)
            {
                // PlayerHomeExterior3D: the roof spans the entire 13 m front
                // and reaches 2.30 m beyond the door, not just the balcony.
                return TransportDoorEnvelope(lot, -6.5f, 6.5f, -12f,
                    PlayerHomeBalconyGeometry.BalconyDepth);
            }
            if (lot.IsBar)
            {
                // BarFacade3D fixed parts plus the mounted projecting sign.
                // Its authored +X faces the street; do not use a generic +Z
                // building rotation or lose the canopy/service-wing envelope.
                float width = lot.FrontageDirection.x != 0 ? lot.Size.y : lot.Size.x;
                float depth = lot.FrontageDirection.x != 0 ? lot.Size.x : lot.Size.y;
                return TransportDoorEnvelope(lot, -width * .5f - .08f,
                    width * .5f + .08f, -depth, 1.4f);
            }
            if (lot.IsCafe)
            {
                // MountainRoadCafe3D includes up to .17 m of roof/trim beyond
                // the logical five-sided wall footprint; its pose is unrotated.
                return CityRoadPolygon.Rectangle(Rect.MinMaxRect(lot.Center.x - 5.44f,
                    lot.Center.z - 4.73f, lot.Center.x + 4.65f, lot.Center.z + 5.56f));
            }
            if (lot.IsOrdinaryBuilding)
            {
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(lot.District, lot.BuildingVariant);
                var local = new[] { new Vector3(-envelope.x * .5f, 0f, -envelope.z * .5f),
                    new Vector3(envelope.x * .5f, 0f, -envelope.z * .5f),
                    new Vector3(envelope.x * .5f, 0f, envelope.z * .5f),
                    new Vector3(-envelope.x * .5f, 0f, envelope.z * .5f) };
                var points = new Vector2[local.Length];
                for (int i = 0; i < local.Length; i++)
                {
                    Vector3 world = pose.TransformPoint(local[i]);
                    points[i] = new Vector2(world.x, world.z);
                }
                // Ground collision alone omits upper arch spans/balconies.
                return CityRoadPolygon.CounterClockwise(points);
            }
            // The shop's authored exterior stays inside its fixed 15.5 m body.
            return lot.CreateCollisionPolygons()[0];
        }

        private static float TransportPrivateInset(CityLayout layout, Vector2Int cell, Vector2Int frontage)
        {
            RoadEdge edge = RoadEdge.ForCellFrontage(cell, frontage);
            if (!layout.HasRoad(edge)) return 0f;
            return layout.GetPathKind(edge) == CityPathKind.Street
                ? CityStreetSurfacePlanner.GetPublicSidewalkOuterDistance(layout)
                : layout.GetTravelWidth(edge) * .5f;
        }

        private static Vector2[] TransportDoorEnvelope(BuildingLot lot, float left,
            float right, float rear, float front)
        {
            Vector3 forward = lot.FacadeForward;
            Vector3 lateral = new Vector3(forward.z, 0f, -forward.x);
            Vector3[] corners = {
                lot.DoorPosition + lateral * left + forward * rear,
                lot.DoorPosition + lateral * right + forward * rear,
                lot.DoorPosition + lateral * right + forward * front,
                lot.DoorPosition + lateral * left + forward * front };
            var points = new Vector2[corners.Length];
            for (int i = 0; i < corners.Length; i++) points[i] = new Vector2(corners[i].x, corners[i].z);
            return CityRoadPolygon.CounterClockwise(points);
        }

        private static IEnumerable<Vector2[]> TransportAccessFootprints(IReadOnlyList<Vector3> access)
        {
            // The existing infill planners reserve an .8 m half-width for
            // these paths. Preserve the whole public connection and its caps.
            const float radius = .8f;
            for (int i = 1; i < access.Count; i++)
            {
                Vector2 a = new Vector2(access[i - 1].x, access[i - 1].z);
                Vector2 b = new Vector2(access[i].x, access[i].z);
                Vector2 along = (b - a).normalized * radius;
                Vector2 across = new Vector2(-along.y, along.x);
                if ((b - a).sqrMagnitude < .000001f) continue;
                yield return CityRoadPolygon.CounterClockwise(new[] {
                    a - along - across, b + along - across, b + along + across, a - along + across });
            }
        }

        private static void AppendOverlaps(string routeId, int segmentIndex, IReadOnlyList<Vector2> corridor,
            List<Footprint> footprints, List<CityTerraceReservation> output)
        {
            Rect bounds = CityRoadPolygon.Bounds(corridor);
            var included = new HashSet<string>(StringComparer.Ordinal);
            foreach (Footprint footprint in footprints)
            {
                if (!bounds.Overlaps(footprint.Bounds) || !Intersects(corridor, footprint.Polygon)) continue;
                if (!included.Add(footprint.Id)) continue;
                output.Add(new CityTerraceReservation(routeId, segmentIndex, footprint.Id, footprint.Kind, footprint.Bounds));
            }
        }

        private static List<CityTerraceRoadCrossing> CollectRoadCrossings(CityLayout layout,
            IReadOnlyList<CityTerraceTransportRoute> routes, IReadOnlyList<Footprint> footprints)
        {
            var result = new List<CityTerraceRoadCrossing>();
            foreach (CityTerraceTransportRoute route in routes)
            for (int index = 0; index < route.Segments.Count; index++)
            {
                CityTerraceTransportSegment segment = route.Segments[index];
                Vector2 start = new Vector2(segment.Start.x, segment.Start.z);
                Vector2 end = new Vector2(segment.End.x, segment.End.z);
                IReadOnlyList<Vector2> corridor = route.CorridorPolygons[index];
                Rect bounds = CityRoadPolygon.Bounds(corridor);
                var included = new HashSet<RoadEdge>();
                foreach (Footprint footprint in footprints)
                {
                    if (!footprint.RoadEdge.HasValue || !bounds.Overlaps(footprint.Bounds) ||
                        !TryOverlapPoint(corridor, footprint.Polygon, out Vector2 point)) continue;
                    RoadEdge road = footprint.RoadEdge.Value;
                    if (!included.Add(road)) continue;
                    CityRoadPath path = layout.RoadGeometry.Get(road);
                    bool centerlinesIntersect = TryCenterlineCrossing(start, end, path, out Vector2 centerlinePoint);
                    if (centerlinesIntersect) point = centerlinePoint;
                    bool attachment = index == 0 && layout.RoadGeometry.ContainsRoad(road, start);
                    if (attachment) point = start;
                    CityRoadProjection projection = path.Project(point);
                    float roadDatum = layout.ElevationPlan.SampleRoadDatum(road, projection.DistanceAlong / path.Length);
                    float amount = Mathf.Clamp01(Vector2.Dot(point - start, end - start) / (end - start).sqrMagnitude);
                    float routeDatum = route.ElevationProfile.SampleDistance(route.Samples[index].HorizontalDistance +
                        segment.HorizontalLength * amount).Height;
                    attachment &= Mathf.Abs(routeDatum - roadDatum) <= .02f;
                    CityTerraceRoadCrossingKind kind = attachment ? CityTerraceRoadCrossingKind.CoastalAttachment :
                        Mathf.Abs(routeDatum - roadDatum) <= .20f ? CityTerraceRoadCrossingKind.AtGradeUnresolved :
                        CityTerraceRoadCrossingKind.GradeSeparatedUnresolved;
                    result.Add(new CityTerraceRoadCrossing(route.Id, index, footprint.Id, road,
                        point, routeDatum, roadDatum, centerlinesIntersect, kind));
                }
            }
            return result;
        }

        private static bool TryOverlapPoint(IReadOnlyList<Vector2> first, Vector2[] second, out Vector2 point)
        {
            var overlap = new List<Vector2>(first);
            for (int i = 0; i < second.Length && overlap.Count >= 3; i++)
                overlap = CityRoadPolygon.Clip(overlap, second[i], second[(i + 1) % second.Length]);
            point = Vector2.zero;
            if (overlap.Count < 3 || CityRoadPolygon.Area(overlap) <= .00001f) return false;
            foreach (Vector2 vertex in overlap) point += vertex;
            point /= overlap.Count;
            return true;
        }

        private static bool TryCenterlineCrossing(Vector2 start, Vector2 end, CityRoadPath path, out Vector2 point)
        {
            Vector2 along = end - start;
            for (int i = 1; i < path.Vertices.Count; i++)
            {
                Vector2 roadStart = path.Vertices[i - 1], roadAlong = path.Vertices[i] - roadStart;
                float denominator = CityRoadPolygon.Cross(along, roadAlong);
                if (Mathf.Abs(denominator) <= .000001f) continue;
                float t = CityRoadPolygon.Cross(roadStart - start, roadAlong) / denominator;
                float u = CityRoadPolygon.Cross(roadStart - start, along) / denominator;
                if (t < 0f || t > 1f || u < 0f || u > 1f) continue;
                point = start + along * t;
                return true;
            }
            point = default;
            return false;
        }

        private static bool Intersects(IReadOnlyList<Vector2> first, IReadOnlyList<Vector2> second)
        {
            return !HasSeparatingAxis(first, second) && !HasSeparatingAxis(second, first);
        }

        private static bool HasSeparatingAxis(IReadOnlyList<Vector2> axes, IReadOnlyList<Vector2> other)
        {
            for (int i = 0; i < axes.Count; i++)
            {
                Vector2 edge = axes[(i + 1) % axes.Count] - axes[i];
                Vector2 axis = new Vector2(-edge.y, edge.x);
                float firstMin = float.PositiveInfinity, firstMax = float.NegativeInfinity;
                float secondMin = float.PositiveInfinity, secondMax = float.NegativeInfinity;
                foreach (Vector2 point in axes) { float dot = Vector2.Dot(point, axis); firstMin = Mathf.Min(firstMin, dot); firstMax = Mathf.Max(firstMax, dot); }
                foreach (Vector2 point in other) { float dot = Vector2.Dot(point, axis); secondMin = Mathf.Min(secondMin, dot); secondMax = Mathf.Max(secondMax, dot); }
                if (firstMax <= secondMin || secondMax <= firstMin) return true;
            }
            return false;
        }
    }
}
