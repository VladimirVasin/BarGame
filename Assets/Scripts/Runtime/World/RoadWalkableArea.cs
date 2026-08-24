using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityGroundTraversalPlan
    {
        internal CityGroundTraversalPlan(
            IList<Rect> groundRectangles,
            IList<Rect> connectorRectangles)
        {
            GroundRectangles = new ReadOnlyCollection<Rect>(
                new List<Rect>(groundRectangles));
            ConnectorRectangles = new ReadOnlyCollection<Rect>(
                new List<Rect>(connectorRectangles));
        }

        public IReadOnlyList<Rect> GroundRectangles { get; }
        public IReadOnlyList<Rect> ConnectorRectangles { get; }
    }

    public static class CityGroundTraversalPlanner
    {
        public const float MaximumAgentRadius = 0.35f;

        private const float ConnectorMargin = 0.10f;
        internal const float ConnectorReach =
            (MaximumAgentRadius * 2f) + ConnectorMargin;

        public static CityGroundTraversalPlan CreatePlan(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            layout.ValidateOrThrow();
            var eligibleByCell =
                new Dictionary<Vector2Int, CitySurfaceDescriptor>();
            var ground = new List<Rect>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Kind != CitySurfaceKind.BuildableGround &&
                    !surface.IsWalkable)
                {
                    continue;
                }

                eligibleByCell.Add(surface.Cell, surface);
                AddRiverClippedGround(layout, surface, ground);
            }

            // The seacoast precinct draws real walkable ground over
            // cells the blueprint marks as water — the mol, the pier
            // and the mouth footbridge all carry a person out past the
            // waterline. A water cell is never walkable, so without
            // this the player is clamped at the water-cell boundary
            // and every deck is sealed behind an invisible box. The
            // open sea itself stays out.
            CitySeacoastPlanner.AppendWalkableFootprints(layout, ground);

            var connectors = new List<Rect>();
            CityRoadGroundBoundaryPlan roadGroundBoundaries =
                CityRoadGroundBoundaryPlanner.Create(layout);
            for (int index = 0;
                 index < roadGroundBoundaries.SafeConnections.Count;
                 index++)
            {
                CityRoadGroundBoundarySpan safe =
                    roadGroundBoundaries.SafeConnections[index];
                connectors.Add(safe.CreateConnector(ConnectorReach));
                AddParkLawnReach(layout, safe, connectors);
            }

            foreach (KeyValuePair<Vector2Int, CitySurfaceDescriptor> pair
                     in eligibleByCell)
            {
                AddGroundConnection(
                    layout,
                    pair.Key,
                    pair.Value.WorldBounds,
                    Vector2Int.right,
                    eligibleByCell,
                    connectors);
                AddGroundConnection(
                    layout,
                    pair.Key,
                    pair.Value.WorldBounds,
                    Vector2Int.up,
                    eligibleByCell,
                    connectors);
            }

            return new CityGroundTraversalPlan(ground, connectors);
        }

        private static void AddRiverClippedGround(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            ICollection<Rect> destination)
        {
            var patches = new List<Rect> { surface.WorldBounds };
            if (layout.River.IsEnabled)
            {
                for (int segmentIndex = 0;
                     segmentIndex < layout.River.Segments.Count;
                     segmentIndex++)
                {
                    Rect cut = layout.River.Segments[segmentIndex]
                        .WaterBounds;
                    if (!surface.WorldBounds.Overlaps(cut))
                    {
                        continue;
                    }

                    var next = new List<Rect>();
                    for (int patchIndex = 0;
                         patchIndex < patches.Count;
                         patchIndex++)
                    {
                        SubtractRectangle(patches[patchIndex], cut, next);
                    }

                    patches = next;
                }
            }

            for (int index = 0; index < patches.Count; index++)
            {
                destination.Add(patches[index]);
            }
        }

        private static void SubtractRectangle(
            Rect source,
            Rect cut,
            ICollection<Rect> destination)
        {
            float xMin = Mathf.Max(source.xMin, cut.xMin);
            float xMax = Mathf.Min(source.xMax, cut.xMax);
            float zMin = Mathf.Max(source.yMin, cut.yMin);
            float zMax = Mathf.Min(source.yMax, cut.yMax);
            if (xMax <= xMin || zMax <= zMin)
            {
                destination.Add(source);
                return;
            }

            AddRect(destination, source.xMin, source.yMin, xMin, source.yMax);
            AddRect(destination, xMax, source.yMin, source.xMax, source.yMax);
            AddRect(destination, xMin, source.yMin, xMax, zMin);
            AddRect(destination, xMin, zMax, xMax, source.yMax);
        }

        private static void AddRect(
            ICollection<Rect> destination,
            float xMin,
            float zMin,
            float xMax,
            float zMax)
        {
            if (xMax - xMin > 0.001f && zMax - zMin > 0.001f)
            {
                destination.Add(Rect.MinMaxRect(xMin, zMin, xMax, zMax));
            }
        }

        // A park region's lawn sits further inside its cells than the
        // fixed connector reach, so a gate span needs a strip that runs
        // from the seam all the way onto the lawn.
        private static void AddParkLawnReach(
            CityLayout layout,
            CityRoadGroundBoundarySpan span,
            ICollection<Rect> destination)
        {
            if (span.Surface.Kind != CitySurfaceKind.ParkGround ||
                !TryGetParkLawn(layout, span.Surface.Cell, out Rect lawn))
            {
                return;
            }

            Rect surface = span.Surface.WorldBounds;
            float inward = span.IsHorizontal
                ? Mathf.Sign(surface.center.y - span.FixedCoordinate)
                : Mathf.Sign(surface.center.x - span.FixedCoordinate);
            float lawnEdge = span.IsHorizontal
                ? (inward > 0f ? lawn.yMin : lawn.yMax)
                : (inward > 0f ? lawn.xMin : lawn.xMax);
            float depth = (lawnEdge - span.FixedCoordinate) * inward;
            if (depth <= ConnectorReach)
            {
                return;
            }

            float outerEdge =
                span.FixedCoordinate - (inward * ConnectorReach);
            float innerEdge = lawnEdge + (inward * ConnectorReach);
            destination.Add(span.IsHorizontal
                ? Rect.MinMaxRect(
                    span.MinimumCoordinate,
                    Mathf.Min(outerEdge, innerEdge),
                    span.MaximumCoordinate,
                    Mathf.Max(outerEdge, innerEdge))
                : Rect.MinMaxRect(
                    Mathf.Min(outerEdge, innerEdge),
                    span.MinimumCoordinate,
                    Mathf.Max(outerEdge, innerEdge),
                    span.MaximumCoordinate));
        }

        private static bool TryGetParkLawn(
            CityLayout layout,
            Vector2Int cell,
            out Rect lawn)
        {
            for (int index = 0;
                 index < layout.Park.Regions.Count;
                 index++)
            {
                CityParkRegionPlan region = layout.Park.Regions[index];
                if (region.ContainsCell(cell))
                {
                    lawn = region.WalkableBounds;
                    return true;
                }
            }

            lawn = default;
            return false;
        }

        private static void AddGroundConnection(
            CityLayout layout,
            Vector2Int cell,
            Rect ground,
            Vector2Int direction,
            IReadOnlyDictionary<Vector2Int, CitySurfaceDescriptor>
                eligibleByCell,
            ICollection<Rect> destination)
        {
            Vector2Int neighbourCell = cell + direction;
            if (!eligibleByCell.TryGetValue(
                    neighbourCell,
                out CitySurfaceDescriptor neighbour) ||
                layout.HasRoad(
                    RoadEdge.ForCellFrontage(cell, direction)))
            {
                return;
            }

            CitySurfaceDescriptor surface = eligibleByCell[cell];
            Rect other = neighbour.WorldBounds;
            if (direction.x != 0)
            {
                float minimum = Mathf.Max(ground.yMin, other.yMin);
                float maximum = Mathf.Min(ground.yMax, other.yMax);
                if (maximum <= minimum)
                {
                    return;
                }

                float boundary = (ground.xMax + other.xMin) * 0.5f;
                if (!CityRoadGroundBoundaryPlanner.IsGroundBoundarySafe(
                        layout,
                        surface,
                        neighbour,
                        false,
                        boundary,
                        minimum,
                        maximum))
                {
                    return;
                }

                destination.Add(Rect.MinMaxRect(
                    boundary - ConnectorReach,
                    minimum,
                    boundary + ConnectorReach,
                    maximum));
                return;
            }

            float xMinimum = Mathf.Max(ground.xMin, other.xMin);
            float xMaximum = Mathf.Min(ground.xMax, other.xMax);
            if (xMaximum <= xMinimum)
            {
                return;
            }

            float zBoundary = (ground.yMax + other.yMin) * 0.5f;
            if (!CityRoadGroundBoundaryPlanner.IsGroundBoundarySafe(
                    layout,
                    surface,
                    neighbour,
                    true,
                    zBoundary,
                    xMinimum,
                    xMaximum))
            {
                return;
            }

            destination.Add(Rect.MinMaxRect(
                xMinimum,
                zBoundary - ConnectorReach,
                xMaximum,
                zBoundary + ConnectorReach));
        }

    }

    public sealed class RoadWalkableArea : IWalkableArea
    {
        private const float BoundaryEpsilon = 0.0001f;

        private readonly List<Rect> rectangles = new List<Rect>();
        private readonly ReadOnlyCollection<Rect> readOnlyRectangles;
        private SpatialNode[] spatialNodes = Array.Empty<SpatialNode>();
        private int spatialRoot = -1;
        private bool spatialIndexDirty = true;

        public RoadWalkableArea()
        {
            readOnlyRectangles = rectangles.AsReadOnly();
        }

        public RoadWalkableArea(IEnumerable<Rect> xzRectangles)
            : this()
        {
            if (xzRectangles == null)
            {
                throw new ArgumentNullException(nameof(xzRectangles));
            }

            foreach (Rect rectangle in xzRectangles)
            {
                Add(rectangle);
            }
        }

        public IReadOnlyList<Rect> Rectangles => readOnlyRectangles;

        public static RoadWalkableArea FromLayout(CityLayout layout)
        {
            return FromLayout(
                layout,
                CityMountainBoundaryPlanner.Create(layout));
        }

        public static RoadWalkableArea FromLayout(
            CityLayout layout,
            CityMountainBoundaryPlan mountainPlan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (mountainPlan == null)
            {
                throw new ArgumentNullException(nameof(mountainPlan));
            }

            var area = new RoadWalkableArea(layout.CreateRoadRects());
            CityGroundTraversalPlan groundTraversal =
                CityGroundTraversalPlanner.CreatePlan(layout);
            for (int index = 0;
                 index < groundTraversal.GroundRectangles.Count;
                 index++)
            {
                area.Add(groundTraversal.GroundRectangles[index]);
            }

            for (int index = 0;
                 index < groundTraversal.ConnectorRectangles.Count;
                 index++)
            {
                area.Add(groundTraversal.ConnectorRectangles[index]);
            }

            if (layout.Park.IsEnabled)
            {
                for (int regionIndex = 0;
                     regionIndex < layout.Park.Regions.Count;
                     regionIndex++)
                {
                    area.Add(
                        layout.Park.Regions[regionIndex].WalkableBounds);
                }
            }

            if (layout.River.IsEnabled)
            {
                for (int promenadeIndex = 0;
                     promenadeIndex < layout.River.Promenades.Count;
                     promenadeIndex++)
                {
                    CityRiverPromenadeDescriptor promenade =
                        layout.River.Promenades[promenadeIndex];
                    area.Add(promenade.Bounds);
                    float seamX = promenade.WestBank
                        ? promenade.Bounds.xMin
                        : promenade.Bounds.xMax;
                    float halfConnector =
                        CityGroundTraversalPlanner.ConnectorReach;
                    area.Add(Rect.MinMaxRect(
                        seamX - halfConnector,
                        promenade.Bounds.yMin,
                        seamX + halfConnector,
                        promenade.Bounds.yMax));
                }

                for (int landingIndex = 0;
                     landingIndex < layout.River.Landings.Count;
                     landingIndex++)
                {
                    CityRiverLandingDescriptor landing =
                        layout.River.Landings[landingIndex];
                    area.Add(landing.StairBounds);
                    area.Add(landing.PlatformBounds);
                }

                if (mountainPlan.HasRiverCave)
                {
                    // The cave approaches ABUT the city promenades at the
                    // fringe line. Abutting rects do not union for a
                    // non-zero radius (a point must sit radius-inside one
                    // single rect), so without a bridging seam the walk
                    // south clamps 0.32 m short of the line — an invisible
                    // wall exactly where the new area begins. Same idiom
                    // as the promenade↔road seam strip above.
                    float caveSeamReach =
                        CityGroundTraversalPlanner.ConnectorReach;
                    Rect westApproach =
                        mountainPlan.RiverCave.WestPromenadeBounds;
                    Rect eastApproach =
                        mountainPlan.RiverCave.EastPromenadeBounds;
                    area.Add(westApproach);
                    area.Add(eastApproach);
                    area.Add(Rect.MinMaxRect(
                        westApproach.xMin,
                        westApproach.yMax - caveSeamReach,
                        westApproach.xMax,
                        westApproach.yMax + caveSeamReach));
                    area.Add(Rect.MinMaxRect(
                        eastApproach.xMin,
                        eastApproach.yMax - caveSeamReach,
                        eastApproach.xMax,
                        eastApproach.yMax + caveSeamReach));

                    // The sloped forefield shoulders flanking the banks
                    // are real collidered ground continuous with the
                    // fringe yards - without them in the mask the whole
                    // promenade extension had an invisible wall along
                    // its outer edge, sealing the walk from the
                    // embankment onto the yards' land. Each shoulder
                    // needs its own seams: to the promenade beside it
                    // and to the yard ground it abuts.
                    Rect approach =
                        mountainPlan.RiverCave.ApproachBounds;
                    Rect westShoulder = Rect.MinMaxRect(
                        approach.xMin,
                        approach.yMin,
                        mountainPlan.RiverCave.WestBankBounds.xMin,
                        approach.yMax);
                    Rect eastShoulder = Rect.MinMaxRect(
                        mountainPlan.RiverCave.EastBankBounds.xMax,
                        approach.yMin,
                        approach.xMax,
                        approach.yMax);
                    area.Add(westShoulder);
                    area.Add(eastShoulder);
                    AddVerticalSeam(
                        area,
                        westShoulder.xMax,
                        approach.yMin,
                        approach.yMax,
                        caveSeamReach);
                    AddVerticalSeam(
                        area,
                        westShoulder.xMin,
                        approach.yMin,
                        approach.yMax,
                        caveSeamReach);
                    AddVerticalSeam(
                        area,
                        eastShoulder.xMin,
                        approach.yMin,
                        approach.yMax,
                        caveSeamReach);
                    AddVerticalSeam(
                        area,
                        eastShoulder.xMax,
                        approach.yMin,
                        approach.yMax,
                        caveSeamReach);
                    area.Add(Rect.MinMaxRect(
                        westShoulder.xMin,
                        westShoulder.yMax - caveSeamReach,
                        westShoulder.xMax,
                        westShoulder.yMax + caveSeamReach));
                    area.Add(Rect.MinMaxRect(
                        eastShoulder.xMin,
                        eastShoulder.yMax - caveSeamReach,
                        eastShoulder.xMax,
                        eastShoulder.yMax + caveSeamReach));
                }
            }

            if (mountainPlan.HasTunnel)
            {
                AddTunnelWalkableCorridor(area, mountainPlan.Tunnel);
            }

            for (int accessIndex = 0;
                 accessIndex < layout.OpenAreaAccesses.Count;
                 accessIndex++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[accessIndex];
                if (area.Contains(
                        access.Center,
                        CityGroundTraversalPlanner.MaximumAgentRadius))
                {
                    area.Add(access.ApproachBounds);
                }
            }

            for (int pointIndex = 0;
                 pointIndex < layout.DistrictPointsOfInterest.Count;
                 pointIndex++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[pointIndex];
                area.Add(point.PublicBounds);
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    CityDistrictPointOfInterestAccessDescriptor access =
                        point.Accesses[accessIndex];
                    if (area.Contains(
                            access.Center,
                            CityGroundTraversalPlanner
                                .MaximumAgentRadius))
                    {
                        area.Add(access.ApproachBounds);
                    }
                }
            }

            for (int stairIndex = 0;
                 stairIndex < layout.ElevationPlan.SignatureStairs.Count;
                 stairIndex++)
            {
                CityElevationStairPlacement placement =
                    CityElevationStairPlacementPlanner.Create(
                        layout,
                        layout.ElevationPlan.SignatureStairs[stairIndex]);
                area.Add(placement.GroundCutFootprint);
            }

            return area;
        }

        private static void AddTunnelWalkableCorridor(
            RoadWalkableArea area,
            CityMountainTunnelDescriptor tunnel)
        {
            if (tunnel.WalkableDepth <= 0f)
            {
                return;
            }

            Vector3 axis = tunnel.Axis;
            axis.y = 0f;
            axis.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            float halfWidth = tunnel.OpeningWidth * 0.5f;
            float seamReach = CityGroundTraversalPlanner.ConnectorReach;
            Vector3 start = tunnel.PortalGroundCenter - axis * seamReach;
            Vector3 end = tunnel.PortalGroundCenter +
                          axis * tunnel.WalkableDepth;
            Vector3 firstLeft = start - right * halfWidth;
            Vector3 firstRight = start + right * halfWidth;
            Vector3 lastLeft = end - right * halfWidth;
            Vector3 lastRight = end + right * halfWidth;
            area.Add(Rect.MinMaxRect(
                Mathf.Min(
                    Mathf.Min(firstLeft.x, firstRight.x),
                    Mathf.Min(lastLeft.x, lastRight.x)),
                Mathf.Min(
                    Mathf.Min(firstLeft.z, firstRight.z),
                    Mathf.Min(lastLeft.z, lastRight.z)),
                Mathf.Max(
                    Mathf.Max(firstLeft.x, firstRight.x),
                    Mathf.Max(lastLeft.x, lastRight.x)),
                Mathf.Max(
                    Mathf.Max(firstLeft.z, firstRight.z),
                    Mathf.Max(lastLeft.z, lastRight.z))));
        }

        private static void AddVerticalSeam(
            RoadWalkableArea area,
            float x,
            float zMin,
            float zMax,
            float reach)
        {
            area.Add(Rect.MinMaxRect(
                x - reach,
                zMin,
                x + reach,
                zMax));
        }

        public void Add(Rect xzRectangle)
        {
            float xMin = Mathf.Min(xzRectangle.xMin, xzRectangle.xMax);
            float xMax = Mathf.Max(xzRectangle.xMin, xzRectangle.xMax);
            float zMin = Mathf.Min(xzRectangle.yMin, xzRectangle.yMax);
            float zMax = Mathf.Max(xzRectangle.yMin, xzRectangle.yMax);
            if (!IsFinite(xMin) || !IsFinite(xMax) ||
                !IsFinite(zMin) || !IsFinite(zMax) ||
                xMax <= xMin || zMax <= zMin)
            {
                throw new ArgumentException(
                    "Walkable rectangles must have finite positive dimensions.",
                    nameof(xzRectangle));
            }

            rectangles.Add(Rect.MinMaxRect(xMin, zMin, xMax, zMax));
            spatialIndexDirty = true;
        }

        public bool Contains(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            EnsureSpatialIndex();
            return spatialRoot >= 0 &&
                   Contains(
                       spatialRoot,
                       position.x,
                       position.z,
                       radius);
        }

        private bool Contains(
            int nodeIndex,
            float x,
            float z,
            float radius)
        {
            SpatialNode node = spatialNodes[nodeIndex];
            if (!node.Bounds.Contains(x, z, BoundaryEpsilon))
            {
                return false;
            }

            if (node.IsLeaf)
            {
                return Contains(
                    rectangles[node.RectangleIndex],
                    x,
                    z,
                    radius);
            }

            return Contains(node.LeftChild, x, z, radius) ||
                   Contains(node.RightChild, x, z, radius);
        }

        public Vector3 Constrain(
            Vector3 currentPosition,
            Vector3 desiredPosition,
            float radius = 0f)
        {
            ValidateRadius(radius);
            if (Contains(desiredPosition, radius))
            {
                return desiredPosition;
            }

            var xOnly = new Vector3(
                desiredPosition.x,
                desiredPosition.y,
                currentPosition.z);
            var zOnly = new Vector3(
                currentPosition.x,
                desiredPosition.y,
                desiredPosition.z);
            bool canMoveX = Contains(xOnly, radius);
            bool canMoveZ = Contains(zOnly, radius);
            if (canMoveX && canMoveZ)
            {
                return (xOnly - desiredPosition).sqrMagnitude <=
                       (zOnly - desiredPosition).sqrMagnitude
                    ? xOnly
                    : zOnly;
            }

            if (canMoveX)
            {
                return xOnly;
            }

            if (canMoveZ)
            {
                return zOnly;
            }

            var stationary = new Vector3(
                currentPosition.x,
                desiredPosition.y,
                currentPosition.z);
            if (Contains(stationary, radius))
            {
                return stationary;
            }

            return ClosestPoint(desiredPosition, radius, stationary);
        }

        public Vector3 ClosestPoint(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            return ClosestPoint(position, radius, position);
        }

        private Vector3 ClosestPoint(
            Vector3 position,
            float radius,
            Vector3 fallback)
        {
            if (!IsFinite(position.x) ||
                !IsFinite(position.y) ||
                !IsFinite(position.z))
            {
                return ClosestPointLinear(position, radius, fallback);
            }

            EnsureSpatialIndex();
            if (spatialRoot < 0)
            {
                return fallback;
            }

            bool found = false;
            float bestDistance = float.PositiveInfinity;
            Vector3 best = fallback;
            int bestRectangleIndex = int.MaxValue;
            FindClosestPoint(
                spatialRoot,
                position,
                radius,
                ref found,
                ref bestDistance,
                ref bestRectangleIndex,
                ref best);
            return found ? best : fallback;
        }

        private void FindClosestPoint(
            int nodeIndex,
            Vector3 position,
            float radius,
            ref bool found,
            ref float bestDistance,
            ref int bestRectangleIndex,
            ref Vector3 best)
        {
            SpatialNode node = spatialNodes[nodeIndex];
            float lowerBound = node.Bounds.SquaredDistance(
                position.x,
                position.z);
            if (found &&
                (lowerBound > bestDistance ||
                 (lowerBound == bestDistance &&
                  node.MinimumRectangleIndex >= bestRectangleIndex)))
            {
                return;
            }

            if (node.IsLeaf)
            {
                TryClosestPoint(
                    node.RectangleIndex,
                    position,
                    radius,
                    ref found,
                    ref bestDistance,
                    ref bestRectangleIndex,
                    ref best);
                return;
            }

            int firstChild = node.LeftChild;
            int secondChild = node.RightChild;
            SpatialNode firstNode = spatialNodes[firstChild];
            SpatialNode secondNode = spatialNodes[secondChild];
            float firstDistance = firstNode.Bounds.SquaredDistance(
                position.x,
                position.z);
            float secondDistance = secondNode.Bounds.SquaredDistance(
                position.x,
                position.z);
            if (secondDistance < firstDistance ||
                (secondDistance == firstDistance &&
                 secondNode.MinimumRectangleIndex <
                 firstNode.MinimumRectangleIndex))
            {
                firstChild = node.RightChild;
                secondChild = node.LeftChild;
            }

            FindClosestPoint(
                firstChild,
                position,
                radius,
                ref found,
                ref bestDistance,
                ref bestRectangleIndex,
                ref best);
            FindClosestPoint(
                secondChild,
                position,
                radius,
                ref found,
                ref bestDistance,
                ref bestRectangleIndex,
                ref best);
        }

        private void TryClosestPoint(
            int rectangleIndex,
            Vector3 position,
            float radius,
            ref bool found,
            ref float bestDistance,
            ref int bestRectangleIndex,
            ref Vector3 best)
        {
            Rect rectangle = rectangles[rectangleIndex];
            float xMin = rectangle.xMin + radius;
            float xMax = rectangle.xMax - radius;
            float zMin = rectangle.yMin + radius;
            float zMax = rectangle.yMax - radius;
            if (xMin > xMax || zMin > zMax)
            {
                return;
            }

            var candidate = new Vector3(
                Mathf.Clamp(position.x, xMin, xMax),
                position.y,
                Mathf.Clamp(position.z, zMin, zMax));
            float distance = (candidate - position).sqrMagnitude;
            if ((!found && distance >= bestDistance) ||
                (found &&
                 (distance > bestDistance ||
                  (distance == bestDistance &&
                   rectangleIndex >= bestRectangleIndex))))
            {
                return;
            }

            found = true;
            bestDistance = distance;
            bestRectangleIndex = rectangleIndex;
            best = candidate;
        }

        private Vector3 ClosestPointLinear(
            Vector3 position,
            float radius,
            Vector3 fallback)
        {
            bool found = false;
            float bestDistance = float.PositiveInfinity;
            Vector3 best = fallback;

            for (int index = 0; index < rectangles.Count; index++)
            {
                Rect rectangle = rectangles[index];
                float xMin = rectangle.xMin + radius;
                float xMax = rectangle.xMax - radius;
                float zMin = rectangle.yMin + radius;
                float zMax = rectangle.yMax - radius;
                if (xMin > xMax || zMin > zMax)
                {
                    continue;
                }

                var candidate = new Vector3(
                    Mathf.Clamp(position.x, xMin, xMax),
                    position.y,
                    Mathf.Clamp(position.z, zMin, zMax));
                float distance = (candidate - position).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                found = true;
                bestDistance = distance;
                best = candidate;
            }

            return found ? best : fallback;
        }

        private void EnsureSpatialIndex()
        {
            if (!spatialIndexDirty)
            {
                return;
            }

            int rectangleCount = rectangles.Count;
            if (rectangleCount == 0)
            {
                spatialNodes = Array.Empty<SpatialNode>();
                spatialRoot = -1;
                spatialIndexDirty = false;
                return;
            }

            var rectangleIndices = new int[rectangleCount];
            for (int index = 0; index < rectangleCount; index++)
            {
                rectangleIndices[index] = index;
            }

            spatialNodes = new SpatialNode[(rectangleCount * 2) - 1];
            var comparer = new RectangleCenterComparer(rectangles);
            int nextNodeIndex = 0;
            spatialRoot = BuildSpatialNode(
                rectangleIndices,
                0,
                rectangleCount,
                comparer,
                ref nextNodeIndex);
            spatialIndexDirty = false;
        }

        private int BuildSpatialNode(
            int[] rectangleIndices,
            int start,
            int count,
            RectangleCenterComparer comparer,
            ref int nextNodeIndex)
        {
            int nodeIndex = nextNodeIndex++;
            if (count == 1)
            {
                int rectangleIndex = rectangleIndices[start];
                spatialNodes[nodeIndex] = SpatialNode.CreateLeaf(
                    SpatialBounds.FromRect(rectangles[rectangleIndex]),
                    rectangleIndex);
                return nodeIndex;
            }

            GetCenterExtents(
                rectangleIndices,
                start,
                count,
                out float xMin,
                out float xMax,
                out float zMin,
                out float zMax);
            comparer.SortByX = xMax - xMin >= zMax - zMin;
            Array.Sort(rectangleIndices, start, count, comparer);

            int leftCount = count / 2;
            int leftChild = BuildSpatialNode(
                rectangleIndices,
                start,
                leftCount,
                comparer,
                ref nextNodeIndex);
            int rightChild = BuildSpatialNode(
                rectangleIndices,
                start + leftCount,
                count - leftCount,
                comparer,
                ref nextNodeIndex);
            spatialNodes[nodeIndex] = SpatialNode.CreateBranch(
                spatialNodes[leftChild],
                leftChild,
                spatialNodes[rightChild],
                rightChild);
            return nodeIndex;
        }

        private void GetCenterExtents(
            int[] rectangleIndices,
            int start,
            int count,
            out float xMin,
            out float xMax,
            out float zMin,
            out float zMax)
        {
            Rect first = rectangles[rectangleIndices[start]];
            xMin = RectangleCenterComparer.CenterX(first);
            xMax = xMin;
            zMin = RectangleCenterComparer.CenterZ(first);
            zMax = zMin;

            int end = start + count;
            for (int index = start + 1; index < end; index++)
            {
                Rect rectangle = rectangles[rectangleIndices[index]];
                float centerX = RectangleCenterComparer.CenterX(rectangle);
                float centerZ = RectangleCenterComparer.CenterZ(rectangle);
                xMin = Mathf.Min(xMin, centerX);
                xMax = Mathf.Max(xMax, centerX);
                zMin = Mathf.Min(zMin, centerZ);
                zMax = Mathf.Max(zMax, centerZ);
            }
        }

        private static bool Contains(
            Rect rectangle,
            float x,
            float z,
            float radius)
        {
            float xMin = rectangle.xMin + radius;
            float xMax = rectangle.xMax - radius;
            float zMin = rectangle.yMin + radius;
            float zMax = rectangle.yMax - radius;
            return xMin <= xMax &&
                   zMin <= zMax &&
                   x >= xMin - BoundaryEpsilon &&
                   x <= xMax + BoundaryEpsilon &&
                   z >= zMin - BoundaryEpsilon &&
                   z <= zMax + BoundaryEpsilon;
        }

        private static void ValidateRadius(float radius)
        {
            if (!IsFinite(radius) || radius < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct SpatialBounds
        {
            public SpatialBounds(
                float xMin,
                float xMax,
                float zMin,
                float zMax)
            {
                XMin = xMin;
                XMax = xMax;
                ZMin = zMin;
                ZMax = zMax;
            }

            public float XMin { get; }

            public float XMax { get; }

            public float ZMin { get; }

            public float ZMax { get; }

            public static SpatialBounds FromRect(Rect rectangle)
            {
                return new SpatialBounds(
                    rectangle.xMin,
                    rectangle.xMax,
                    rectangle.yMin,
                    rectangle.yMax);
            }

            public static SpatialBounds Encapsulate(
                SpatialBounds first,
                SpatialBounds second)
            {
                return new SpatialBounds(
                    Mathf.Min(first.XMin, second.XMin),
                    Mathf.Max(first.XMax, second.XMax),
                    Mathf.Min(first.ZMin, second.ZMin),
                    Mathf.Max(first.ZMax, second.ZMax));
            }

            public bool Contains(float x, float z, float epsilon)
            {
                return x >= XMin - epsilon &&
                       x <= XMax + epsilon &&
                       z >= ZMin - epsilon &&
                       z <= ZMax + epsilon;
            }

            public float SquaredDistance(float x, float z)
            {
                float deltaX = x < XMin
                    ? XMin - x
                    : x > XMax
                        ? x - XMax
                        : 0f;
                float deltaZ = z < ZMin
                    ? ZMin - z
                    : z > ZMax
                        ? z - ZMax
                        : 0f;
                return (deltaX * deltaX) + (deltaZ * deltaZ);
            }
        }

        private readonly struct SpatialNode
        {
            private SpatialNode(
                SpatialBounds bounds,
                int leftChild,
                int rightChild,
                int rectangleIndex,
                int minimumRectangleIndex)
            {
                Bounds = bounds;
                LeftChild = leftChild;
                RightChild = rightChild;
                RectangleIndex = rectangleIndex;
                MinimumRectangleIndex = minimumRectangleIndex;
            }

            public SpatialBounds Bounds { get; }

            public int LeftChild { get; }

            public int RightChild { get; }

            public int RectangleIndex { get; }

            public int MinimumRectangleIndex { get; }

            public bool IsLeaf => RectangleIndex >= 0;

            public static SpatialNode CreateLeaf(
                SpatialBounds bounds,
                int rectangleIndex)
            {
                return new SpatialNode(
                    bounds,
                    -1,
                    -1,
                    rectangleIndex,
                    rectangleIndex);
            }

            public static SpatialNode CreateBranch(
                SpatialNode left,
                int leftChild,
                SpatialNode right,
                int rightChild)
            {
                return new SpatialNode(
                    SpatialBounds.Encapsulate(left.Bounds, right.Bounds),
                    leftChild,
                    rightChild,
                    -1,
                    Math.Min(
                        left.MinimumRectangleIndex,
                        right.MinimumRectangleIndex));
            }
        }

        private sealed class RectangleCenterComparer : IComparer<int>
        {
            private readonly List<Rect> source;

            public RectangleCenterComparer(List<Rect> source)
            {
                this.source = source;
            }

            public bool SortByX { get; set; }

            public int Compare(int firstIndex, int secondIndex)
            {
                Rect first = source[firstIndex];
                Rect second = source[secondIndex];
                float firstCenter = SortByX
                    ? CenterX(first)
                    : CenterZ(first);
                float secondCenter = SortByX
                    ? CenterX(second)
                    : CenterZ(second);
                int comparison = firstCenter.CompareTo(secondCenter);
                return comparison != 0
                    ? comparison
                    : firstIndex.CompareTo(secondIndex);
            }

            public static float CenterX(Rect rectangle)
            {
                return (rectangle.xMin * 0.5f) +
                       (rectangle.xMax * 0.5f);
            }

            public static float CenterZ(Rect rectangle)
            {
                return (rectangle.yMin * 0.5f) +
                       (rectangle.yMax * 0.5f);
            }
        }
    }
}
