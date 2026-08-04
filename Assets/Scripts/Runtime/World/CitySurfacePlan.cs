using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CitySurfaceKind
    {
        BuildableGround = 0,
        ParkGround = 1,
        OpenGround = 2,
        Beach = 3,
        LakeShore = 4,
        CemeteryGround = 5,
        Water = 6
    }

    public readonly struct CitySurfaceDescriptor :
        IEquatable<CitySurfaceDescriptor>
    {
        internal CitySurfaceDescriptor(
            string areaId,
            CityAreaFeatureKind feature,
            CitySurfaceKind kind,
            Vector2Int cell,
            Rect worldBounds,
            Color mapColor,
            bool isWalkable)
        {
            AreaId = areaId ?? string.Empty;
            Feature = feature;
            Kind = kind;
            Cell = cell;
            WorldBounds = worldBounds;
            MapColor = mapColor;
            IsWalkable = isWalkable;
        }

        public string AreaId { get; }
        public CityAreaFeatureKind Feature { get; }
        public CitySurfaceKind Kind { get; }
        public Vector2Int Cell { get; }
        public Rect WorldBounds { get; }
        public Color MapColor { get; }
        public bool IsWalkable { get; }
        public bool IsWater => Kind == CitySurfaceKind.Water;
        public Vector3 Center => new Vector3(
            WorldBounds.center.x,
            0f,
            WorldBounds.center.y);

        public bool Equals(CitySurfaceDescriptor other)
        {
            return string.Equals(
                       AreaId,
                       other.AreaId,
                       StringComparison.Ordinal) &&
                   Feature == other.Feature &&
                   Kind == other.Kind &&
                   Cell == other.Cell &&
                   WorldBounds.Equals(other.WorldBounds) &&
                   MapColor.Equals(other.MapColor) &&
                   IsWalkable == other.IsWalkable;
        }

        public override bool Equals(object obj)
        {
            return obj is CitySurfaceDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(AreaId);
                hash = (hash * 397) ^ (int)Feature;
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ Cell.GetHashCode();
                hash = (hash * 397) ^ WorldBounds.GetHashCode();
                hash = (hash * 397) ^ MapColor.GetHashCode();
                return (hash * 397) ^ IsWalkable.GetHashCode();
            }
        }

        public static bool operator ==(
            CitySurfaceDescriptor left,
            CitySurfaceDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            CitySurfaceDescriptor left,
            CitySurfaceDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct CityOpenAreaAccessDescriptor :
        IEquatable<CityOpenAreaAccessDescriptor>
    {
        internal CityOpenAreaAccessDescriptor(
            string id,
            string areaId,
            CityAreaFeatureKind feature,
            Vector2Int cell,
            Vector2Int streetSideDirection,
            RoadEdge frontageEdge,
            Vector3 center,
            Vector3 outwardNormal,
            float width,
            Rect approachBounds)
        {
            Id = id ?? string.Empty;
            AreaId = areaId ?? string.Empty;
            Feature = feature;
            Cell = cell;
            StreetSideDirection = streetSideDirection;
            FrontageEdge = frontageEdge;
            Center = center;
            OutwardNormal = outwardNormal;
            Width = width;
            ApproachBounds = approachBounds;
        }

        public string Id { get; }
        public string AreaId { get; }
        public CityAreaFeatureKind Feature { get; }
        public Vector2Int Cell { get; }

        // Direction from the open cell towards its adjacent street.
        public Vector2Int StreetSideDirection { get; }

        public RoadEdge FrontageEdge { get; }

        // Center of the opening on the exposed outer edge of the street.
        public Vector3 Center { get; }

        // Direction from the street into the open area.
        public Vector3 OutwardNormal { get; }
        public float Width { get; }

        // XZ strip overlapping both the street and the open surface.
        public Rect ApproachBounds { get; }

        public bool Equals(CityOpenAreaAccessDescriptor other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   string.Equals(
                       AreaId,
                       other.AreaId,
                       StringComparison.Ordinal) &&
                   Feature == other.Feature &&
                   Cell == other.Cell &&
                   StreetSideDirection == other.StreetSideDirection &&
                   FrontageEdge == other.FrontageEdge &&
                   Center.Equals(other.Center) &&
                   OutwardNormal.Equals(other.OutwardNormal) &&
                   Width.Equals(other.Width) &&
                   ApproachBounds.Equals(other.ApproachBounds);
        }

        public override bool Equals(object obj)
        {
            return obj is CityOpenAreaAccessDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Id);
                hash = (hash * 397) ^
                       StringComparer.Ordinal.GetHashCode(AreaId);
                hash = (hash * 397) ^ (int)Feature;
                hash = (hash * 397) ^ Cell.GetHashCode();
                hash = (hash * 397) ^
                       StreetSideDirection.GetHashCode();
                hash = (hash * 397) ^ FrontageEdge.GetHashCode();
                hash = (hash * 397) ^ Center.GetHashCode();
                hash = (hash * 397) ^ OutwardNormal.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                return (hash * 397) ^ ApproachBounds.GetHashCode();
            }
        }

        public static bool operator ==(
            CityOpenAreaAccessDescriptor left,
            CityOpenAreaAccessDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            CityOpenAreaAccessDescriptor left,
            CityOpenAreaAccessDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    public static class CitySurfacePlanner
    {
        private const float AccessWidthMargin = 0.8f;
        private const float MaximumApproachDepth = 4f;
        private const float MapPaddingRoadMultiplier = 0.75f;

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.down,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left
        };

        public static void Create(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            Vector3 origin,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
            out List<CitySurfaceDescriptor> surfaces,
            out List<CityOpenAreaAccessDescriptor> accesses,
            out Rect worldXZBounds,
            out Rect mapWorldXZBounds)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (roads == null)
            {
                throw new ArgumentNullException(nameof(roads));
            }

            if (pathKinds == null)
            {
                throw new ArgumentNullException(nameof(pathKinds));
            }

            if (!IsFinite(origin.x) ||
                !IsFinite(origin.y) ||
                !IsFinite(origin.z))
            {
                throw new ArgumentOutOfRangeException(nameof(origin));
            }

            settings.Validate();
            var roadSet = new HashSet<RoadEdge>(roads);
            ValidateRoadKinds(roads, pathKinds);
            surfaces = CreateSurfaces(
                blueprint,
                settings,
                origin,
                roadSet,
                pathKinds);
            accesses = CreateAccesses(
                blueprint,
                settings,
                origin,
                roadSet,
                pathKinds,
                surfaces);
            worldXZBounds = CalculateWorldBounds(
                settings,
                origin,
                roads,
                surfaces);
            mapWorldXZBounds = CalculateMapBounds(
                blueprint,
                settings,
                origin);
        }

        private static List<CitySurfaceDescriptor> CreateSurfaces(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            Vector3 origin,
            ISet<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            var result = new List<CitySurfaceDescriptor>(
                blueprint.Cells.Count);
            for (int index = 0; index < blueprint.Cells.Count; index++)
            {
                CityBlueprintCell cell = blueprint.Cells[index];
                CitySurfaceKind kind = ResolveSurfaceKind(cell);
                Rect bounds = CreateSurfaceBounds(
                    settings,
                    origin,
                    cell.Cell,
                    roads,
                    pathKinds);
                result.Add(new CitySurfaceDescriptor(
                    cell.Area.Id,
                    cell.Area.Feature,
                    kind,
                    cell.Cell,
                    bounds,
                    cell.Area.MapColor,
                    cell.Topology == CityCellTopologyKind.OpenLand));
            }

            return result;
        }

        private static List<CityOpenAreaAccessDescriptor> CreateAccesses(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            Vector3 origin,
            ISet<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
            IReadOnlyList<CitySurfaceDescriptor> surfaces)
        {
            var surfacesByCell =
                new Dictionary<Vector2Int, CitySurfaceDescriptor>();
            for (int index = 0; index < surfaces.Count; index++)
            {
                surfacesByCell.Add(surfaces[index].Cell, surfaces[index]);
            }

            var result = new List<CityOpenAreaAccessDescriptor>();
            for (int areaIndex = 0;
                 areaIndex < blueprint.Areas.Count;
                 areaIndex++)
            {
                CityAreaPlacement area = blueprint.Areas[areaIndex];
                if (!RequiresOpenAreaAccess(area))
                {
                    continue;
                }

                if (!TryFindAccessCandidate(
                        blueprint,
                        area,
                        roads,
                        pathKinds,
                        out AccessCandidate candidate))
                {
                    throw new InvalidOperationException(
                        $"Open area '{area.Id}' has no adjacent street access.");
                }

                CitySurfaceDescriptor surface =
                    surfacesByCell[candidate.Cell];
                result.Add(CreateAccessDescriptor(
                    area,
                    candidate,
                    surface,
                    settings,
                    origin));
            }

            return result;
        }

        private static bool RequiresOpenAreaAccess(CityAreaPlacement area)
        {
            CityAreaFeatureKind feature = area.Definition.Feature;
            if (feature != CityAreaFeatureKind.NorthWaterfront &&
                feature != CityAreaFeatureKind.Lake &&
                feature != CityAreaFeatureKind.Cemetery)
            {
                return false;
            }

            for (int index = 0; index < area.Cells.Count; index++)
            {
                if (area.GetTopology(area.Cells[index]) ==
                    CityCellTopologyKind.OpenLand)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindAccessCandidate(
            CityBlueprint blueprint,
            CityAreaPlacement area,
            ISet<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
            out AccessCandidate selected)
        {
            bool found = false;
            selected = default;
            for (int cellIndex = 0;
                 cellIndex < area.Cells.Count;
                 cellIndex++)
            {
                Vector2Int cell = area.Cells[cellIndex];
                if (area.GetTopology(cell) != CityCellTopologyKind.OpenLand)
                {
                    continue;
                }

                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int streetDirection =
                        CardinalDirections[directionIndex];
                    if (area.Definition.Feature ==
                            CityAreaFeatureKind.NorthWaterfront &&
                        streetDirection != Vector2Int.down)
                    {
                        continue;
                    }

                    RoadEdge frontage = RoadEdge.ForCellFrontage(
                        cell,
                        streetDirection);
                    if (!roads.Contains(frontage) ||
                        !pathKinds.TryGetValue(
                            frontage,
                            out CityPathKind pathKind) ||
                        pathKind != CityPathKind.Street)
                    {
                        continue;
                    }

                    var candidate = new AccessCandidate(
                        cell,
                        streetDirection,
                        frontage,
                        SquaredDistanceToCenter(
                            frontage,
                            blueprint.CenterNode));
                    if (!found || CompareCandidates(candidate, selected) < 0)
                    {
                        selected = candidate;
                        found = true;
                    }
                }
            }

            return found;
        }

        private static CityOpenAreaAccessDescriptor CreateAccessDescriptor(
            CityAreaPlacement area,
            AccessCandidate candidate,
            CitySurfaceDescriptor surface,
            CityGenerationSettings settings,
            Vector3 origin)
        {
            Vector3 first = GetNodeWorldPosition(
                settings,
                origin,
                candidate.Frontage.A);
            Vector3 second = GetNodeWorldPosition(
                settings,
                origin,
                candidate.Frontage.B);
            Vector3 outward = new Vector3(
                -candidate.StreetDirection.x,
                0f,
                -candidate.StreetDirection.y);
            Vector3 center =
                ((first + second) * 0.5f) +
                (outward * (settings.RoadWidth * 0.5f));
            float parallelSize = candidate.Frontage.IsHorizontal
                ? surface.WorldBounds.width
                : surface.WorldBounds.height;
            float width = Mathf.Min(
                settings.RoadWidth + AccessWidthMargin,
                parallelSize);
            float perpendicularSize = candidate.Frontage.IsHorizontal
                ? surface.WorldBounds.height
                : surface.WorldBounds.width;
            float approachDepth = Mathf.Min(
                MaximumApproachDepth,
                perpendicularSize);
            Rect approach = candidate.Frontage.IsHorizontal
                ? new Rect(
                    center.x - (width * 0.5f),
                    center.z - (approachDepth * 0.5f),
                    width,
                    approachDepth)
                : new Rect(
                    center.x - (approachDepth * 0.5f),
                    center.z - (width * 0.5f),
                    approachDepth,
                    width);
            return new CityOpenAreaAccessDescriptor(
                $"{area.Id}-access",
                area.Id,
                area.Definition.Feature,
                candidate.Cell,
                candidate.StreetDirection,
                candidate.Frontage,
                center,
                outward,
                width,
                approach);
        }

        private static Rect CreateSurfaceBounds(
            CityGenerationSettings settings,
            Vector3 origin,
            Vector2Int cell,
            ISet<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            Vector3 minimum = GetNodeWorldPosition(
                settings,
                origin,
                cell);
            Vector3 maximum = GetNodeWorldPosition(
                settings,
                origin,
                cell + Vector2Int.one);
            float halfRoad = settings.RoadWidth * 0.5f;
            float xMin = minimum.x +
                         (HasTravelEdge(
                              cell,
                              Vector2Int.left,
                              roads,
                              pathKinds)
                             ? halfRoad
                             : 0f);
            float xMax = maximum.x -
                         (HasTravelEdge(
                              cell,
                              Vector2Int.right,
                              roads,
                              pathKinds)
                             ? halfRoad
                             : 0f);
            float zMin = minimum.z +
                         (HasTravelEdge(
                              cell,
                              Vector2Int.down,
                              roads,
                              pathKinds)
                             ? halfRoad
                             : 0f);
            float zMax = maximum.z -
                         (HasTravelEdge(
                              cell,
                              Vector2Int.up,
                              roads,
                              pathKinds)
                             ? halfRoad
                             : 0f);
            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }

        private static bool HasTravelEdge(
            Vector2Int cell,
            Vector2Int direction,
            ISet<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            RoadEdge edge = RoadEdge.ForCellFrontage(cell, direction);
            return roads.Contains(edge) &&
                   pathKinds.TryGetValue(edge, out CityPathKind kind) &&
                   (kind == CityPathKind.Street ||
                    kind == CityPathKind.ParkPath);
        }

        private static Rect CalculateWorldBounds(
            CityGenerationSettings settings,
            Vector3 origin,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyList<CitySurfaceDescriptor> surfaces)
        {
            Rect result = surfaces[0].WorldBounds;
            for (int index = 1; index < surfaces.Count; index++)
            {
                result = Encapsulate(result, surfaces[index].WorldBounds);
            }

            float halfRoad = settings.RoadWidth * 0.5f;
            for (int index = 0; index < roads.Count; index++)
            {
                Vector3 first = GetNodeWorldPosition(
                    settings,
                    origin,
                    roads[index].A);
                Vector3 second = GetNodeWorldPosition(
                    settings,
                    origin,
                    roads[index].B);
                Rect road = Rect.MinMaxRect(
                    Mathf.Min(first.x, second.x) - halfRoad,
                    Mathf.Min(first.z, second.z) - halfRoad,
                    Mathf.Max(first.x, second.x) + halfRoad,
                    Mathf.Max(first.z, second.z) + halfRoad);
                result = Encapsulate(result, road);
            }

            return result;
        }

        private static Rect CalculateMapBounds(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            Vector3 origin)
        {
            RectInt nodes = blueprint.MapNodeBounds;
            Vector3 minimum = GetNodeWorldPosition(
                settings,
                origin,
                nodes.min);
            Vector3 maximum = GetNodeWorldPosition(
                settings,
                origin,
                nodes.max);
            float padding = settings.RoadWidth * MapPaddingRoadMultiplier;
            return Rect.MinMaxRect(
                minimum.x - padding,
                minimum.z - padding,
                maximum.x + padding,
                maximum.z + padding);
        }

        private static CitySurfaceKind ResolveSurfaceKind(
            CityBlueprintCell cell)
        {
            switch (cell.Topology)
            {
                case CityCellTopologyKind.BuildableLand:
                    return CitySurfaceKind.BuildableGround;
                case CityCellTopologyKind.ParkLand:
                    return CitySurfaceKind.ParkGround;
                case CityCellTopologyKind.Water:
                    return CitySurfaceKind.Water;
                case CityCellTopologyKind.OpenLand:
                    switch (cell.Area.Feature)
                    {
                        case CityAreaFeatureKind.NorthWaterfront:
                            return CitySurfaceKind.Beach;
                        case CityAreaFeatureKind.Lake:
                            return CitySurfaceKind.LakeShore;
                        case CityAreaFeatureKind.Cemetery:
                            return CitySurfaceKind.CemeteryGround;
                        default:
                            return CitySurfaceKind.OpenGround;
                    }
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(cell),
                        cell.Topology,
                        "Unsupported city-cell topology.");
            }
        }

        private static void ValidateRoadKinds(
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            for (int index = 0; index < roads.Count; index++)
            {
                RoadEdge edge = roads[index];
                if (!pathKinds.TryGetValue(
                        edge,
                        out CityPathKind kind) ||
                    (kind != CityPathKind.Street &&
                     kind != CityPathKind.ParkPath))
                {
                    throw new InvalidOperationException(
                        $"Road edge {edge} requires a supported path kind.");
                }
            }
        }

        private static float SquaredDistanceToCenter(
            RoadEdge edge,
            Vector2Int centerNode)
        {
            float centerX = (edge.A.x + edge.B.x) * 0.5f;
            float centerZ = (edge.A.y + edge.B.y) * 0.5f;
            float deltaX = centerX - centerNode.x;
            float deltaZ = centerZ - centerNode.y;
            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }

        private static int CompareCandidates(
            AccessCandidate left,
            AccessCandidate right)
        {
            int distanceComparison =
                left.DistanceToCenter.CompareTo(right.DistanceToCenter);
            if (distanceComparison != 0)
            {
                return distanceComparison;
            }

            int cellComparison = CityAreaPlacement.CompareCellsRowMajor(
                left.Cell,
                right.Cell);
            if (cellComparison != 0)
            {
                return cellComparison;
            }

            int directionComparison =
                CompareDirections(
                    left.StreetDirection,
                    right.StreetDirection);
            return directionComparison != 0
                ? directionComparison
                : RoadEdge.Compare(left.Frontage, right.Frontage);
        }

        private static int CompareDirections(
            Vector2Int left,
            Vector2Int right)
        {
            return DirectionOrdinal(left).CompareTo(DirectionOrdinal(right));
        }

        private static int DirectionOrdinal(Vector2Int direction)
        {
            for (int index = 0; index < CardinalDirections.Length; index++)
            {
                if (CardinalDirections[index] == direction)
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        private static Vector3 GetNodeWorldPosition(
            CityGenerationSettings settings,
            Vector3 origin,
            Vector2Int node)
        {
            return origin + new Vector3(
                node.x * settings.NodeSpacing.x,
                0f,
                node.y * settings.NodeSpacing.y);
        }

        private static Rect Encapsulate(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct AccessCandidate
        {
            public AccessCandidate(
                Vector2Int cell,
                Vector2Int streetDirection,
                RoadEdge frontage,
                float distanceToCenter)
            {
                Cell = cell;
                StreetDirection = streetDirection;
                Frontage = frontage;
                DistanceToCenter = distanceToCenter;
            }

            public Vector2Int Cell { get; }
            public Vector2Int StreetDirection { get; }
            public RoadEdge Frontage { get; }
            public float DistanceToCenter { get; }
        }
    }
}
