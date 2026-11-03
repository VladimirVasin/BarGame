using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityTerraceTransportBank { West, East }
    public enum CityTerraceTransportSegmentKind { Climb, Approach, TurnLanding, StopLanding }
    public enum CityTerraceReservationKind { Building, ProtectedPlace, Park, River, Coast, ExistingRoad, Terrace }
    public enum CityTerraceRoadCrossingKind { CoastalAttachment, AtGradeUnresolved, GradeSeparatedUnresolved }

    [Flags]
    public enum CityTerracePlacementRequirement
    {
        None = 0, CorridorReservation = 1, RoadConnections = 2, VehicleSweptPath = 4,
        TerrainTieIns = 8, FootprintConflicts = 16, UnresolvedRoadCrossings = 32
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
        public bool IsProtected => Kind != CityTerraceReservationKind.Building;
    }

    /// <summary>
    /// A grade-feasible centreline study with flat turns and stops. Buildings,
    /// terrace tie-ins and vehicle swept paths are not thereby approved.
    /// </summary>
    public sealed class CityTerraceTransportRoute
    {
        internal CityTerraceTransportRoute(string id, CityTerraceTransportBank bank,
            IList<CityTerraceRouteSample> samples, IList<CityTerraceTransportSegment> segments,
            CityRoadElevationProfile elevationProfile, IList<CityTerraceReservation> reservations)
        {
            Id = id; Bank = bank;
            ElevationProfile = elevationProfile;
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
        public float TotalHorizontalLength => ElevationProfile.HorizontalLength;
        public float MaximumGradePercent => ElevationProfile.MaximumGradePercent;
    }

    /// <summary>
    /// Editor-only consumer of the terrace proposal: two bounded switchbacks
    /// and an explicit overlap inventory. No live city graph or surface changes.
    /// </summary>
    public sealed class CityTerraceTransportPlan
    {
        public const float TurnRadius = 15f;
        public const float CorridorWidth = 12f;
        public const float StopLength = 12f;
        public const int MaximumRouteSamples = 192;
        private const int ClimbLegs = 10, HalfTurnSegments = 12;
        private const float FlatEndLength = 4f, EdgeClearance = 8f, FirstRowInset = 24f;

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
            var routes = new List<CityTerraceTransportRoute>(2);
            routes.Add(CreateRoute(layout, master, CityTerraceTransportBank.West, footprints));
            routes.Add(CreateRoute(layout, master, CityTerraceTransportBank.East, footprints));
            Routes = new ReadOnlyCollection<CityTerraceTransportRoute>(routes);
            RoadCrossings = CollectRoadCrossings(layout, routes, footprints).AsReadOnly();
            var reservations = new List<CityTerraceReservation>();
            foreach (CityTerraceTransportRoute route in routes) reservations.AddRange(route.Reservations);

            // Historical study envelope, retained for comparison. It overlaps
            // the now-built terrace precinct; it is not a vacant reservation.
            PilotCandidateBounds = new Rect(100f, 47f, 40f, 34f);
            var pilot = new List<CityTerraceReservation>();
            AppendOverlaps("east-courtyard-pilot", -1, CityRoadPolygon.Rectangle(PilotCandidateBounds), footprints, pilot);
            PilotReservations = new ReadOnlyCollection<CityTerraceReservation>(pilot);
            reservations.AddRange(pilot);
            Reservations = new ReadOnlyCollection<CityTerraceReservation>(reservations);
            foreach (CityTerraceReservation reservation in reservations)
                HasProtectedConflicts = HasProtectedConflicts || reservation.IsProtected;
            // No physical corridor, junction or swept-path proof exists yet.
            // Grade feasibility and an empty overlap list cannot activate it.
            OutstandingPlacementRequirements = CityTerracePlacementRequirement.CorridorReservation |
                CityTerracePlacementRequirement.RoadConnections | CityTerracePlacementRequirement.VehicleSweptPath |
                CityTerracePlacementRequirement.TerrainTieIns;
            foreach (CityTerraceTransportRoute route in routes)
                foreach (CityTerraceReservation reservation in route.Reservations)
                    if (reservation.Kind != CityTerraceReservationKind.ExistingRoad)
                        OutstandingPlacementRequirements |= CityTerracePlacementRequirement.FootprintConflicts;
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
        public CityTerracePlacementRequirement OutstandingPlacementRequirements { get; }
        public bool IsPlacementReady => OutstandingPlacementRequirements == CityTerracePlacementRequirement.None;
        public bool HasProtectedConflicts { get; }
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
            bool west = bank == CityTerraceTransportBank.West;
            string id = west ? "terrace-west-switchback" : "terrace-east-switchback";
            Rect core = master.CoreWorldXZBounds;
            CityRiverDefinition river = layout.Blueprint.River;
            float riverX = layout.GetGridWorldPosition(new Vector2Int(river.CorridorCellX + (west ? 0 : 1), 0)).x;
            float lowEdgeX = layout.GetGridWorldPosition(new Vector2Int(river.CorridorCellX + (west ? -1 : 2), 0)).x;
            float direction = west ? 1f : -1f;
            float outer = (west ? core.xMin : core.xMax) + direction * (EdgeClearance + TurnRadius);
            float inner = lowEdgeX - direction * (EdgeClearance + TurnRadius);
            float span = Mathf.Abs(inner - outer);
            if (span <= StopLength + 2f * FlatEndLength || Mathf.Abs(riverX - lowEdgeX) < CorridorWidth)
                throw new InvalidOperationException("The proposed bank cannot contain the switchback section.");
            float firstZ = core.yMax - FirstRowInset;
            if (firstZ - (ClimbLegs - 1) * 2f * TurnRadius - CorridorWidth * .5f < core.yMin)
                throw new InvalidOperationException("The proposed switchback exceeds the southern core boundary.");
            var points = new List<Vector2>(MaximumRouteSamples);
            var kinds = new List<CityTerraceTransportSegmentKind>(MaximumRouteSamples - 1);
            Vector2 start = new Vector2(outer - direction * TurnRadius, core.yMax);
            points.Add(start);
            void Append(Vector2 point, CityTerraceTransportSegmentKind kind)
            {
                if (points.Count >= MaximumRouteSamples || Vector2.Distance(points[points.Count - 1], point) < .001f)
                    throw new InvalidOperationException("The bounded transport study contains an invalid segment.");
                points.Add(point); kinds.Add(kind);
            }
            Append(new Vector2(start.x, firstZ + TurnRadius), CityTerraceTransportSegmentKind.Approach);
            for (int step = 1; step <= HalfTurnSegments / 2; step++)
            {
                float angle = step * Mathf.PI / HalfTurnSegments;
                Append(new Vector2(outer - direction * TurnRadius * Mathf.Cos(angle),
                    firstZ + TurnRadius - TurnRadius * Mathf.Sin(angle)), CityTerraceTransportSegmentKind.TurnLanding);
            }
            for (int leg = 0; leg < ClimbLegs; leg++)
            {
                float travel = (leg & 1) == 0 ? direction : -direction;
                Vector2 from = points[points.Count - 1];
                Vector2 end = from + Vector2.right * (travel * span);
                Append(from + Vector2.right * (travel * FlatEndLength), CityTerraceTransportSegmentKind.Approach);
                if (leg == 2 || leg == 5 || leg == 8)
                {
                    Vector2 middle = (from + end) * .5f;
                    Append(middle - Vector2.right * (travel * StopLength * .5f), CityTerraceTransportSegmentKind.Climb);
                    Append(middle + Vector2.right * (travel * StopLength * .5f), CityTerraceTransportSegmentKind.StopLanding);
                }
                Append(end - Vector2.right * (travel * FlatEndLength), CityTerraceTransportSegmentKind.Climb);
                Append(end, CityTerraceTransportSegmentKind.Approach);
                if (leg == ClimbLegs - 1) continue;
                for (int step = 1; step <= HalfTurnSegments; step++)
                {
                    float angle = step * Mathf.PI / HalfTurnSegments;
                    Append(new Vector2(end.x + travel * TurnRadius * Mathf.Sin(angle),
                        end.y - TurnRadius + TurnRadius * Mathf.Cos(angle)), CityTerraceTransportSegmentKind.TurnLanding);
                }
            }
            if (!layout.ElevationPlan.TrySampleSurface(start, CitySurfaceRole.RoadDatum, out float startY, out _))
                throw new InvalidOperationException("The switchback's coastal attachment has no surveyed road support.");
            float climbingLength = 0f;
            for (int i = 0; i < kinds.Count; i++)
                if (kinds[i] == CityTerraceTransportSegmentKind.Climb)
                    climbingLength += Vector2.Distance(points[i], points[i + 1]);
            float grade = (master.TargetSummitElevation - startY) / climbingLength;
            if (grade <= 0f || grade * 100f > CityElevationPlan.MaximumBusGradePercent)
                throw new InvalidOperationException("The proposed switchback cannot reach the summit within six percent.");
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
            var profile = new CityRoadElevationProfile(stations, CityElevationPlan.MaximumBusGradePercent);
            var samples = new List<CityTerraceRouteSample>(points.Count);
            var segments = new List<CityTerraceTransportSegment>(kinds.Count);
            var reservations = new List<CityTerraceReservation>();
            for (int i = 0; i < points.Count; i++)
            {
                float stationDistance = profile.Stations[i].HorizontalDistance;
                float height = profile.SampleDistance(stationDistance).Height;
                samples.Add(new CityTerraceRouteSample(new Vector3(points[i].x, height, points[i].y), stationDistance));
            }
            for (int i = 0; i < kinds.Count; i++)
            {
                segments.Add(new CityTerraceTransportSegment(samples[i].Position, samples[i + 1].Position, kinds[i]));
                AppendOverlaps(id, i, CorridorPolygon(points[i], points[i + 1]), footprints, reservations);
            }
            return new CityTerraceTransportRoute(id, bank, samples, segments, profile, reservations);
        }

        private static Vector2[] CorridorPolygon(Vector2 start, Vector2 end)
        {
            Vector2 along = (end - start).normalized * (CorridorWidth * .5f);
            Vector2 across = new Vector2(-along.y, along.x);
            // Square caps conservatively cover sampled turn joins; no gap may
            // hide a footprint conflict between consecutive curve chords.
            return CityRoadPolygon.CounterClockwise(new[] {
                start - along - across, end + along - across,
                end + along + across, start - along + across });
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
                CityRoadJunction junction = layout.RoadGeometry.ObliqueJunction;
                if (junction != null && road == junction.OwnerEdge)
                    foreach (Vector2[] polygon in junction.RoadPolygons)
                        Add(id, CityTerraceReservationKind.ExistingRoad, polygon, road);
            }
            CityTerraceDistrictPlan terrace = CityTerraceDistrictPlan.ForLayout(layout);
            if (terrace.IsEnabled)
            {
                Add("terrace:precinct", CityTerraceReservationKind.Terrace, CityRoadPolygon.Rectangle(terrace.ReservedBounds));
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
                if (protect) Add(id, CityTerraceReservationKind.ProtectedPlace, CityRoadPolygon.Rectangle(layout.GetCellWorldBounds(lot.Cell)));
                else foreach (Vector2[] polygon in lot.CreateCollisionPolygons()) Add(id, CityTerraceReservationKind.Building, polygon);
                massIndex++;
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

        private static void AppendOverlaps(string routeId, int segmentIndex, Vector2[] corridor,
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
                Vector2[] corridor = CorridorPolygon(start, end);
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

        private static bool TryOverlapPoint(Vector2[] first, Vector2[] second, out Vector2 point)
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
