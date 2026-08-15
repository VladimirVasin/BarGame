using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityVerticalTraversalTransitionKind
    {
        GroundSeam = 0,
        RoadFrontage = 1
    }

    public enum CityVerticalTraversalClassification
    {
        Authorized = 0,
        UnsafeStep = 1,
        Unclassified = 2
    }

    public enum CityVerticalTraversalDefectType
    {
        UnsafeStep = 0,
        UnclassifiedSeam = 1,
        DisconnectedFromRoad = 2
    }

    public readonly struct CityVerticalTraversalTransitionDescriptor
    {
        internal CityVerticalTraversalTransitionDescriptor(
            CityVerticalTraversalTransitionKind kind,
            Vector2Int cell,
            string areaId,
            CitySurfaceKind surfaceKind,
            bool hasOtherCell,
            Vector2Int otherCell,
            string otherAreaId,
            CitySurfaceKind otherSurfaceKind,
            bool hasRoadEdge,
            RoadEdge roadEdge,
            Vector2 startWorldXZ,
            Vector2 endWorldXZ,
            float maximumStepDelta,
            CityVerticalTraversalClassification classification)
        {
            Kind = kind;
            Cell = cell;
            AreaId = areaId ?? string.Empty;
            SurfaceKind = surfaceKind;
            HasOtherCell = hasOtherCell;
            OtherCell = otherCell;
            OtherAreaId = otherAreaId ?? string.Empty;
            OtherSurfaceKind = otherSurfaceKind;
            HasRoadEdge = hasRoadEdge;
            RoadEdge = roadEdge;
            StartWorldXZ = startWorldXZ;
            EndWorldXZ = endWorldXZ;
            MaximumStepDelta = maximumStepDelta;
            Classification = classification;
        }

        public CityVerticalTraversalTransitionKind Kind { get; }
        public Vector2Int Cell { get; }
        public string AreaId { get; }
        public CitySurfaceKind SurfaceKind { get; }
        public bool HasOtherCell { get; }
        public Vector2Int OtherCell { get; }
        public string OtherAreaId { get; }
        public CitySurfaceKind OtherSurfaceKind { get; }
        public bool HasRoadEdge { get; }
        public RoadEdge RoadEdge { get; }
        public Vector2 StartWorldXZ { get; }
        public Vector2 EndWorldXZ { get; }
        public float MaximumStepDelta { get; }
        public CityVerticalTraversalClassification Classification { get; }
        public bool IsAuthorized =>
            Classification == CityVerticalTraversalClassification.Authorized;
    }

    public readonly struct CityVerticalTraversalDefectRecord
    {
        internal CityVerticalTraversalDefectRecord(
            Vector2Int cell,
            string areaId,
            CitySurfaceKind surfaceKind,
            float delta,
            CityVerticalTraversalDefectType type,
            CityVerticalTraversalTransitionKind transitionKind,
            bool hasRelatedCell,
            Vector2Int relatedCell,
            bool hasRoadEdge,
            RoadEdge roadEdge)
        {
            Cell = cell;
            AreaId = areaId ?? string.Empty;
            SurfaceKind = surfaceKind;
            Delta = delta;
            Type = type;
            TransitionKind = transitionKind;
            HasRelatedCell = hasRelatedCell;
            RelatedCell = relatedCell;
            HasRoadEdge = hasRoadEdge;
            RoadEdge = roadEdge;
        }

        public Vector2Int Cell { get; }
        public string AreaId { get; }
        public CitySurfaceKind SurfaceKind { get; }
        public float Delta { get; }
        public CityVerticalTraversalDefectType Type { get; }
        public CityVerticalTraversalTransitionKind TransitionKind { get; }
        public bool HasRelatedCell { get; }
        public Vector2Int RelatedCell { get; }
        public bool HasRoadEdge { get; }
        public RoadEdge RoadEdge { get; }
    }

    /// <summary>
    /// Immutable result of the pure vertical-traversal audit. The authorized
    /// graph contains physical ground seams plus authored road frontages; its
    /// road component is rooted only in the road graph containing SpawnNode.
    /// </summary>
    public sealed class CityVerticalTraversalPlan
    {
        private static readonly IReadOnlyList<Vector2Int> EmptyCells =
            Array.Empty<Vector2Int>();

        private readonly HashSet<Vector2Int> roadReachableCellSet;
        private readonly Dictionary<Vector2Int, IReadOnlyList<Vector2Int>>
            authorizedNeighbours;

        internal CityVerticalTraversalPlan(
            IList<CityVerticalTraversalTransitionDescriptor> transitions,
            IList<CityVerticalTraversalDefectRecord> defects,
            IList<Vector2Int> roadSeedCells,
            IList<Vector2Int> roadReachableCells,
            IDictionary<Vector2Int, List<Vector2Int>> sourceNeighbours)
        {
            Transitions = new ReadOnlyCollection<
                CityVerticalTraversalTransitionDescriptor>(
                new List<CityVerticalTraversalTransitionDescriptor>(
                    transitions ??
                    throw new ArgumentNullException(nameof(transitions))));
            Defects = new ReadOnlyCollection<
                CityVerticalTraversalDefectRecord>(
                new List<CityVerticalTraversalDefectRecord>(
                    defects ??
                    throw new ArgumentNullException(nameof(defects))));
            RoadSeedCells = new ReadOnlyCollection<Vector2Int>(
                new List<Vector2Int>(
                    roadSeedCells ??
                    throw new ArgumentNullException(nameof(roadSeedCells))));
            RoadReachableCells = new ReadOnlyCollection<Vector2Int>(
                new List<Vector2Int>(
                    roadReachableCells ??
                    throw new ArgumentNullException(
                        nameof(roadReachableCells))));
            roadReachableCellSet = new HashSet<Vector2Int>(
                RoadReachableCells);
            authorizedNeighbours =
                new Dictionary<Vector2Int, IReadOnlyList<Vector2Int>>();
            foreach (KeyValuePair<Vector2Int, List<Vector2Int>> pair in
                     sourceNeighbours ??
                     throw new ArgumentNullException(
                         nameof(sourceNeighbours)))
            {
                authorizedNeighbours.Add(
                    pair.Key,
                    new ReadOnlyCollection<Vector2Int>(
                        new List<Vector2Int>(pair.Value)));
            }
        }

        public IReadOnlyList<CityVerticalTraversalTransitionDescriptor>
            Transitions { get; }
        public IReadOnlyList<CityVerticalTraversalDefectRecord> Defects
        {
            get;
        }
        public IReadOnlyList<Vector2Int> RoadSeedCells { get; }
        public IReadOnlyList<Vector2Int> RoadReachableCells { get; }

        public bool IsInAuthorizedRoadComponent(Vector2Int cell)
        {
            return roadReachableCellSet.Contains(cell);
        }

        public IReadOnlyList<Vector2Int> GetAuthorizedNeighbours(
            Vector2Int cell)
        {
            return authorizedNeighbours.TryGetValue(
                cell,
                out IReadOnlyList<Vector2Int> neighbours)
                ? neighbours
                : EmptyCells;
        }
    }

    /// <summary>
    /// Builds a deterministic, Unity-object-free inventory of vertical
    /// traversal. Height decisions are sampled from the same terrain contract
    /// used by physical city geometry instead of the legacy flat cell datum.
    /// </summary>
    public static class CityVerticalTraversalAudit
    {
        public const float MaximumSafeStep =
            CityRoadGroundBoundaryPlanner.MaximumSafeStep;

        private const float GeometryTolerance = 0.001f;
        private const int HeightSampleIntervals = 8;
        private const float MinimumPassageWidth =
            (CityGroundTraversalPlanner.MaximumAgentRadius * 2f) + 0.1f;

        private static readonly Vector2Int[] ForwardDirections =
        {
            Vector2Int.right,
            Vector2Int.up
        };

        public static CityVerticalTraversalPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            List<CitySurfaceDescriptor> surfaces =
                CreateOrderedTraversableSurfaces(layout);
            var surfacesByCell =
                new Dictionary<Vector2Int, CitySurfaceDescriptor>();
            var neighbours =
                new Dictionary<Vector2Int, List<Vector2Int>>();
            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                surfacesByCell.Add(surface.Cell, surface);
                neighbours.Add(surface.Cell, new List<Vector2Int>());
            }

            var transitions = new List<
                CityVerticalTraversalTransitionDescriptor>();
            var defects = new List<CityVerticalTraversalDefectRecord>();
            AddGroundSeams(
                layout,
                surfaces,
                surfacesByCell,
                neighbours,
                transitions,
                defects);

            HashSet<RoadEdge> reachableRoadEdges =
                CreateSpawnRoadComponent(layout);
            var roadSeeds = new HashSet<Vector2Int>();
            AddRoadFrontages(
                layout,
                reachableRoadEdges,
                roadSeeds,
                transitions,
                defects);

            transitions.Sort(CompareTransitions);
            SortAndDeduplicateNeighbours(neighbours);
            List<Vector2Int> orderedSeeds =
                CreateOrderedCells(roadSeeds);
            List<Vector2Int> roadReachable = TraverseAuthorizedGraph(
                orderedSeeds,
                neighbours);
            var reachableSet = new HashSet<Vector2Int>(roadReachable);
            AddDisconnectedDefects(
                surfaces,
                reachableSet,
                transitions,
                defects);
            defects.Sort(CompareDefects);

            return new CityVerticalTraversalPlan(
                transitions,
                defects,
                orderedSeeds,
                roadReachable,
                neighbours);
        }

        private static List<CitySurfaceDescriptor>
            CreateOrderedTraversableSurfaces(CityLayout layout)
        {
            var result = new List<CitySurfaceDescriptor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (CityRoadGroundBoundaryPlanner
                    .SupportsGroundTraversal(surface))
                {
                    result.Add(surface);
                }
            }

            result.Sort((left, right) =>
                CompareCells(left.Cell, right.Cell));
            return result;
        }

        private static void AddGroundSeams(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            IReadOnlyDictionary<Vector2Int, CitySurfaceDescriptor>
                surfacesByCell,
            IDictionary<Vector2Int, List<Vector2Int>> neighbours,
            ICollection<CityVerticalTraversalTransitionDescriptor>
                transitions,
            ICollection<CityVerticalTraversalDefectRecord> defects)
        {
            for (int surfaceIndex = 0;
                 surfaceIndex < surfaces.Count;
                 surfaceIndex++)
            {
                CitySurfaceDescriptor surface = surfaces[surfaceIndex];
                for (int directionIndex = 0;
                     directionIndex < ForwardDirections.Length;
                     directionIndex++)
                {
                    Vector2Int direction =
                        ForwardDirections[directionIndex];
                    Vector2Int otherCell = surface.Cell + direction;
                    if (!surfacesByCell.TryGetValue(
                            otherCell,
                            out CitySurfaceDescriptor other))
                    {
                        continue;
                    }

                    RoadEdge boundary = RoadEdge.ForCellFrontage(
                        surface.Cell,
                        direction);
                    if (layout.HasRoad(boundary))
                    {
                        continue;
                    }

                    bool hasGeometry = TryCreateGroundSeam(
                        surface,
                        other,
                        direction,
                        out Vector2 start,
                        out Vector2 end);
                    CityVerticalTraversalClassification classification;
                    float delta = 0f;
                    if (!hasGeometry ||
                        Vector2.Distance(start, end) <
                        MinimumPassageWidth)
                    {
                        classification =
                            CityVerticalTraversalClassification.Unclassified;
                    }
                    else if (!TrySampleGroundSeamDelta(
                                 layout,
                                 surface,
                                 other,
                                 start,
                                 end,
                                 out delta))
                    {
                        classification =
                            CityVerticalTraversalClassification.Unclassified;
                    }
                    else
                    {
                        classification = ClassifyDelta(delta);
                    }

                    var transition =
                        new CityVerticalTraversalTransitionDescriptor(
                            CityVerticalTraversalTransitionKind.GroundSeam,
                            surface.Cell,
                            surface.AreaId,
                            surface.Kind,
                            true,
                            other.Cell,
                            other.AreaId,
                            other.Kind,
                            false,
                            default,
                            start,
                            end,
                            delta,
                            classification);
                    transitions.Add(transition);
                    if (transition.IsAuthorized)
                    {
                        AddUndirectedConnection(
                            neighbours,
                            surface.Cell,
                            other.Cell);
                    }
                    else
                    {
                        defects.Add(CreateDefect(transition));
                    }
                }
            }
        }

        private static bool TryCreateGroundSeam(
            CitySurfaceDescriptor first,
            CitySurfaceDescriptor second,
            Vector2Int direction,
            out Vector2 start,
            out Vector2 end)
        {
            if (direction == Vector2Int.right)
            {
                float firstX = first.WorldBounds.xMax;
                float secondX = second.WorldBounds.xMin;
                float minimumZ = Mathf.Max(
                    first.WorldBounds.yMin,
                    second.WorldBounds.yMin);
                float maximumZ = Mathf.Min(
                    first.WorldBounds.yMax,
                    second.WorldBounds.yMax);
                float x = (firstX + secondX) * 0.5f;
                start = new Vector2(x, minimumZ);
                end = new Vector2(x, maximumZ);
                return Mathf.Abs(firstX - secondX) <=
                           GeometryTolerance &&
                       maximumZ >= minimumZ;
            }

            float firstZ = first.WorldBounds.yMax;
            float secondZ = second.WorldBounds.yMin;
            float minimumX = Mathf.Max(
                first.WorldBounds.xMin,
                second.WorldBounds.xMin);
            float maximumX = Mathf.Min(
                first.WorldBounds.xMax,
                second.WorldBounds.xMax);
            float z = (firstZ + secondZ) * 0.5f;
            start = new Vector2(minimumX, z);
            end = new Vector2(maximumX, z);
            return Mathf.Abs(firstZ - secondZ) <= GeometryTolerance &&
                   maximumX >= minimumX;
        }

        private static bool TrySampleGroundSeamDelta(
            CityLayout layout,
            CitySurfaceDescriptor first,
            CitySurfaceDescriptor second,
            Vector2 start,
            Vector2 end,
            out float maximumDelta)
        {
            maximumDelta = 0f;
            for (int index = 0;
                 index <= HeightSampleIntervals;
                 index++)
            {
                float amount = index / (float)HeightSampleIntervals;
                Vector2 point = Vector2.Lerp(start, end, amount);
                float firstTop = CityTerrainSurfacePlan.SampleTop(
                    layout,
                    first,
                    point);
                float secondTop = CityTerrainSurfacePlan.SampleTop(
                    layout,
                    second,
                    point);
                if (!IsFinite(firstTop) || !IsFinite(secondTop))
                {
                    maximumDelta = 0f;
                    return false;
                }

                maximumDelta = Mathf.Max(
                    maximumDelta,
                    Mathf.Abs(firstTop - secondTop));
            }

            return true;
        }

        private static void AddRoadFrontages(
            CityLayout layout,
            ISet<RoadEdge> reachableRoadEdges,
            ISet<Vector2Int> roadSeeds,
            ICollection<CityVerticalTraversalTransitionDescriptor>
                transitions,
            ICollection<CityVerticalTraversalDefectRecord> defects)
        {
            CityRoadGroundBoundaryPlan boundaryPlan =
                CityRoadGroundBoundaryPlanner.Create(layout);
            var spans = new List<CityRoadGroundBoundarySpan>(
                boundaryPlan.SafeConnections);
            spans.Sort(CompareBoundarySpans);
            for (int index = 0; index < spans.Count; index++)
            {
                AddRoadFrontage(
                    layout,
                    spans[index],
                    spans[index].MinimumCoordinate,
                    spans[index].MaximumCoordinate,
                    true,
                    reachableRoadEdges,
                    roadSeeds,
                    transitions,
                    defects);
            }

            spans.Clear();
            spans.AddRange(boundaryPlan.ProtectedDrops);
            spans.Sort(CompareBoundarySpans);
            for (int index = 0; index < spans.Count; index++)
            {
                CityRoadGroundBoundarySpan span = spans[index];
                List<CoordinateInterval> intended =
                    CreateIntendedProtectedIntervals(layout, span);
                for (int intervalIndex = 0;
                     intervalIndex < intended.Count;
                     intervalIndex++)
                {
                    AddRoadFrontage(
                        layout,
                        span,
                        intended[intervalIndex].Minimum,
                        intended[intervalIndex].Maximum,
                        false,
                        reachableRoadEdges,
                        roadSeeds,
                        transitions,
                        defects);
                }
            }
        }

        private static void AddRoadFrontage(
            CityLayout layout,
            CityRoadGroundBoundarySpan span,
            float minimumCoordinate,
            float maximumCoordinate,
            bool boundaryAuthorized,
            ISet<RoadEdge> reachableRoadEdges,
            ISet<Vector2Int> roadSeeds,
            ICollection<CityVerticalTraversalTransitionDescriptor>
                transitions,
            ICollection<CityVerticalTraversalDefectRecord> defects)
        {
            Vector2 start = span.IsHorizontal
                ? new Vector2(
                    minimumCoordinate,
                    span.FixedCoordinate)
                : new Vector2(
                    span.FixedCoordinate,
                    minimumCoordinate);
            Vector2 end = span.IsHorizontal
                ? new Vector2(
                    maximumCoordinate,
                    span.FixedCoordinate)
                : new Vector2(
                    span.FixedCoordinate,
                    maximumCoordinate);
            CityVerticalTraversalClassification classification;
            float delta = 0f;
            if (maximumCoordinate - minimumCoordinate <
                MinimumPassageWidth)
            {
                classification =
                    CityVerticalTraversalClassification.Unclassified;
            }
            else if (!TrySampleRoadFrontageDelta(
                    layout,
                    span,
                    start,
                    end,
                    out delta))
            {
                classification =
                    CityVerticalTraversalClassification.Unclassified;
            }
            else if (!boundaryAuthorized &&
                     delta <= MaximumSafeStep + GeometryTolerance)
            {
                classification =
                    CityVerticalTraversalClassification.Unclassified;
            }
            else
            {
                classification = ClassifyDelta(delta);
            }

            var transition =
                new CityVerticalTraversalTransitionDescriptor(
                    CityVerticalTraversalTransitionKind.RoadFrontage,
                    span.Surface.Cell,
                    span.Surface.AreaId,
                    span.Surface.Kind,
                    false,
                    default,
                    string.Empty,
                    span.Surface.Kind,
                    true,
                    span.Edge,
                    start,
                    end,
                    delta,
                    classification);
            transitions.Add(transition);
            if (transition.IsAuthorized &&
                reachableRoadEdges.Contains(span.Edge))
            {
                roadSeeds.Add(span.Surface.Cell);
            }
            else if (!transition.IsAuthorized)
            {
                defects.Add(CreateDefect(transition));
            }
        }

        private static List<CoordinateInterval>
            CreateIntendedProtectedIntervals(
                CityLayout layout,
                CityRoadGroundBoundarySpan span)
        {
            var result = new List<CoordinateInterval>();
            CitySurfaceDescriptor surface = span.Surface;
            if (surface.Kind == CitySurfaceKind.BuildableGround ||
                (surface.Kind == CitySurfaceKind.ParkGround &&
                 layout.GetPathKind(span.Edge) ==
                 CityPathKind.ParkPath))
            {
                result.Add(new CoordinateInterval(
                    span.MinimumCoordinate,
                    span.MaximumCoordinate));
                return result;
            }

            if (surface.Kind == CitySurfaceKind.ParkGround)
            {
                for (int index = 0;
                     index < layout.Park.Gates.Count;
                     index++)
                {
                    CityParkGateDescriptor gate =
                        layout.Park.Gates[index];
                    float fixedCoordinate = span.IsHorizontal
                        ? gate.Center.z
                        : gate.Center.x;
                    if (Mathf.Abs(
                            fixedCoordinate - span.FixedCoordinate) >
                        GeometryTolerance)
                    {
                        continue;
                    }

                    AddClippedInterval(
                        span,
                        span.IsHorizontal
                            ? gate.Center.x
                            : gate.Center.z,
                        gate.Width,
                        result);
                }

                result.Sort(CompareIntervals);
                return result;
            }

            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[index];
                if (access.Cell != surface.Cell ||
                    access.FrontageEdge != span.Edge)
                {
                    continue;
                }

                AddClippedInterval(
                    span,
                    span.IsHorizontal
                        ? access.Center.x
                        : access.Center.z,
                    access.Width,
                    result);
            }

            result.Sort(CompareIntervals);
            return result;
        }

        private static void AddClippedInterval(
            CityRoadGroundBoundarySpan span,
            float center,
            float width,
            ICollection<CoordinateInterval> destination)
        {
            float halfWidth = width * 0.5f;
            float minimum = Mathf.Max(
                span.MinimumCoordinate,
                center - halfWidth);
            float maximum = Mathf.Min(
                span.MaximumCoordinate,
                center + halfWidth);
            if (maximum - minimum > GeometryTolerance)
            {
                destination.Add(new CoordinateInterval(
                    minimum,
                    maximum));
            }
        }

        private static bool TrySampleRoadFrontageDelta(
            CityLayout layout,
            CityRoadGroundBoundarySpan span,
            Vector2 start,
            Vector2 end,
            out float maximumDelta)
        {
            maximumDelta = 0f;
            for (int index = 0;
                 index <= HeightSampleIntervals;
                 index++)
            {
                float amount = index / (float)HeightSampleIntervals;
                Vector2 point = Vector2.Lerp(start, end, amount);
                float groundTop = CityTerrainSurfacePlan.SampleTop(
                    layout,
                    span.Surface,
                    point);
                float travelTop = SampleRoadTravelTop(
                    layout,
                    span.Edge,
                    point);
                if (!IsFinite(groundTop) || !IsFinite(travelTop))
                {
                    maximumDelta = 0f;
                    return false;
                }

                maximumDelta = Mathf.Max(
                    maximumDelta,
                    Mathf.Abs(groundTop - travelTop));
            }

            return true;
        }

        private static float SampleRoadTravelTop(
            CityLayout layout,
            RoadEdge edge,
            Vector2 worldXZ)
        {
            Vector3 first = layout.GetNodeWorldPosition(edge.A);
            Vector3 second = layout.GetNodeWorldPosition(edge.B);
            var firstXZ = new Vector2(first.x, first.z);
            var secondXZ = new Vector2(second.x, second.z);
            Vector2 edgeDelta = secondXZ - firstXZ;
            float amount = edgeDelta.sqrMagnitude > GeometryTolerance
                ? Mathf.Clamp01(
                    Vector2.Dot(worldXZ - firstXZ, edgeDelta) /
                    edgeDelta.sqrMagnitude)
                : 0f;
            float topOffset = layout.GetPathKind(edge) ==
                              CityPathKind.Street
                ? CityStreetSurfacePlanner.SidewalkTop
                : CityStreetSurfacePlanner.RoadTop;
            return layout.ElevationPlan.SampleRoadDatum(edge, amount) +
                   topOffset;
        }

        private static HashSet<RoadEdge> CreateSpawnRoadComponent(
            CityLayout layout)
        {
            var edgesByNode =
                new Dictionary<Vector2Int, List<RoadEdge>>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                AddRoadEdge(edgesByNode, edge.A, edge);
                AddRoadEdge(edgesByNode, edge.B, edge);
            }

            var result = new HashSet<RoadEdge>();
            var visited = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            visited.Add(layout.SpawnNode);
            queue.Enqueue(layout.SpawnNode);
            while (queue.Count > 0)
            {
                Vector2Int node = queue.Dequeue();
                if (!edgesByNode.TryGetValue(
                        node,
                        out List<RoadEdge> edges))
                {
                    continue;
                }

                edges.Sort(RoadEdge.Compare);
                for (int index = 0; index < edges.Count; index++)
                {
                    RoadEdge edge = edges[index];
                    result.Add(edge);
                    Vector2Int other = edge.A == node
                        ? edge.B
                        : edge.A;
                    if (visited.Add(other))
                    {
                        queue.Enqueue(other);
                    }
                }
            }

            return result;
        }

        private static void AddRoadEdge(
            IDictionary<Vector2Int, List<RoadEdge>> edgesByNode,
            Vector2Int node,
            RoadEdge edge)
        {
            if (!edgesByNode.TryGetValue(
                    node,
                    out List<RoadEdge> edges))
            {
                edges = new List<RoadEdge>();
                edgesByNode.Add(node, edges);
            }

            edges.Add(edge);
        }

        private static void AddUndirectedConnection(
            IDictionary<Vector2Int, List<Vector2Int>> neighbours,
            Vector2Int first,
            Vector2Int second)
        {
            neighbours[first].Add(second);
            neighbours[second].Add(first);
        }

        private static void SortAndDeduplicateNeighbours(
            IDictionary<Vector2Int, List<Vector2Int>> neighbours)
        {
            foreach (List<Vector2Int> cells in neighbours.Values)
            {
                cells.Sort(CompareCells);
                for (int index = cells.Count - 1; index > 0; index--)
                {
                    if (cells[index] == cells[index - 1])
                    {
                        cells.RemoveAt(index);
                    }
                }
            }
        }

        private static List<Vector2Int> TraverseAuthorizedGraph(
            IReadOnlyList<Vector2Int> seeds,
            IReadOnlyDictionary<Vector2Int, List<Vector2Int>> neighbours)
        {
            var visited = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            for (int index = 0; index < seeds.Count; index++)
            {
                if (visited.Add(seeds[index]))
                {
                    queue.Enqueue(seeds[index]);
                }
            }

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                if (!neighbours.TryGetValue(
                        cell,
                        out List<Vector2Int> connected))
                {
                    continue;
                }

                for (int index = 0; index < connected.Count; index++)
                {
                    if (visited.Add(connected[index]))
                    {
                        queue.Enqueue(connected[index]);
                    }
                }
            }

            return CreateOrderedCells(visited);
        }

        private static void AddDisconnectedDefects(
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            ISet<Vector2Int> roadReachable,
            IReadOnlyList<CityVerticalTraversalTransitionDescriptor>
                transitions,
            ICollection<CityVerticalTraversalDefectRecord> defects)
        {
            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                if (roadReachable.Contains(surface.Cell))
                {
                    continue;
                }

                CityVerticalTraversalTransitionDescriptor blocker = default;
                bool hasBlocker = TryFindLowestBlockingTransition(
                    surface.Cell,
                    transitions,
                    out blocker);
                defects.Add(new CityVerticalTraversalDefectRecord(
                    surface.Cell,
                    surface.AreaId,
                    surface.Kind,
                    hasBlocker ? blocker.MaximumStepDelta : 0f,
                    CityVerticalTraversalDefectType.DisconnectedFromRoad,
                    hasBlocker
                        ? blocker.Kind
                        : CityVerticalTraversalTransitionKind.GroundSeam,
                    hasBlocker && blocker.HasOtherCell,
                    hasBlocker ? blocker.OtherCell : default,
                    hasBlocker && blocker.HasRoadEdge,
                    hasBlocker ? blocker.RoadEdge : default));
            }
        }

        private static bool TryFindLowestBlockingTransition(
            Vector2Int cell,
            IReadOnlyList<CityVerticalTraversalTransitionDescriptor>
                transitions,
            out CityVerticalTraversalTransitionDescriptor selected)
        {
            bool found = false;
            selected = default;
            for (int index = 0; index < transitions.Count; index++)
            {
                CityVerticalTraversalTransitionDescriptor candidate =
                    transitions[index];
                bool touchesCell = candidate.Cell == cell ||
                                   (candidate.HasOtherCell &&
                                    candidate.OtherCell == cell);
                if (!touchesCell || candidate.IsAuthorized)
                {
                    continue;
                }

                if (!found ||
                    candidate.MaximumStepDelta <
                    selected.MaximumStepDelta)
                {
                    selected = candidate;
                    found = true;
                }
            }

            return found;
        }

        private static CityVerticalTraversalDefectRecord CreateDefect(
            CityVerticalTraversalTransitionDescriptor transition)
        {
            CityVerticalTraversalDefectType type =
                transition.Classification ==
                CityVerticalTraversalClassification.UnsafeStep
                    ? CityVerticalTraversalDefectType.UnsafeStep
                    : CityVerticalTraversalDefectType.UnclassifiedSeam;
            return new CityVerticalTraversalDefectRecord(
                transition.Cell,
                transition.AreaId,
                transition.SurfaceKind,
                transition.MaximumStepDelta,
                type,
                transition.Kind,
                transition.HasOtherCell,
                transition.OtherCell,
                transition.HasRoadEdge,
                transition.RoadEdge);
        }

        private static CityVerticalTraversalClassification ClassifyDelta(
            float delta)
        {
            return delta <= MaximumSafeStep + GeometryTolerance
                ? CityVerticalTraversalClassification.Authorized
                : CityVerticalTraversalClassification.UnsafeStep;
        }

        private static List<Vector2Int> CreateOrderedCells(
            IEnumerable<Vector2Int> source)
        {
            var result = new List<Vector2Int>(source);
            result.Sort(CompareCells);
            return result;
        }

        private static int CompareBoundarySpans(
            CityRoadGroundBoundarySpan left,
            CityRoadGroundBoundarySpan right)
        {
            int comparison = CompareCells(
                left.Surface.Cell,
                right.Surface.Cell);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = RoadEdge.Compare(left.Edge, right.Edge);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.FixedCoordinate.CompareTo(
                right.FixedCoordinate);
            return comparison != 0
                ? comparison
                : left.MinimumCoordinate.CompareTo(
                    right.MinimumCoordinate);
        }

        private static int CompareIntervals(
            CoordinateInterval left,
            CoordinateInterval right)
        {
            int comparison = left.Minimum.CompareTo(right.Minimum);
            return comparison != 0
                ? comparison
                : left.Maximum.CompareTo(right.Maximum);
        }

        private static int CompareTransitions(
            CityVerticalTraversalTransitionDescriptor left,
            CityVerticalTraversalTransitionDescriptor right)
        {
            int comparison = left.Kind.CompareTo(right.Kind);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareCells(left.Cell, right.Cell);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareCells(left.OtherCell, right.OtherCell);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = RoadEdge.Compare(left.RoadEdge, right.RoadEdge);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.StartWorldXZ.x.CompareTo(
                right.StartWorldXZ.x);
            return comparison != 0
                ? comparison
                : left.StartWorldXZ.y.CompareTo(
                    right.StartWorldXZ.y);
        }

        private static int CompareDefects(
            CityVerticalTraversalDefectRecord left,
            CityVerticalTraversalDefectRecord right)
        {
            int comparison = left.Type.CompareTo(right.Type);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareCells(left.Cell, right.Cell);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.TransitionKind.CompareTo(
                right.TransitionKind);
            return comparison != 0
                ? comparison
                : left.Delta.CompareTo(right.Delta);
        }

        private static int CompareCells(Vector2Int left, Vector2Int right)
        {
            int yComparison = left.y.CompareTo(right.y);
            return yComparison != 0
                ? yComparison
                : left.x.CompareTo(right.x);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct CoordinateInterval
        {
            internal CoordinateInterval(float minimum, float maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            internal float Minimum { get; }
            internal float Maximum { get; }
        }
    }
}
