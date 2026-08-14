using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityOpenAreaDecorationKind
    {
        LakeWaterEdge = 0,
        LakeReeds = 1,
        LakeRock = 2,
        LakeBoat = 3,
        CemeteryFence = 4,
        CemeteryGate = 5,
        CemeteryPath = 6,
        CemeteryGrave = 7,
        CemeteryTree = 8,
        YardRingTrack = 9,
        YardDeadTree = 10,
        YardBench = 11,
        YardCarpetFrame = 12,
        YardSandpit = 13,
        YardChildToy = 14,
        YardDeadLamp = 15,
        YardBin = 16,
        YardBottle = 17
    }

    public enum CityOpenAreaDecorationStyle
    {
        LakeStone = 0,
        Reeds = 1,
        WeatheredWood = 2,
        CemeteryIron = 3,
        CemeteryPath = 4,
        GraveStone = 5,
        TreeTrunk = 6,
        DarkFoliage = 7,
        YardWornTrack = 8,
        YardTimber = 9,
        YardPipe = 10,
        YardPaint = 11
    }

    public readonly struct CityOpenAreaDecorationDescriptor :
        IEquatable<CityOpenAreaDecorationDescriptor>
    {
        public CityOpenAreaDecorationDescriptor(
            string stableId,
            CityAreaFeatureKind feature,
            CityOpenAreaDecorationKind kind,
            CityOpenAreaDecorationStyle style,
            Bounds bounds)
        {
            StableId = stableId ?? string.Empty;
            Feature = feature;
            Kind = kind;
            Style = style;
            Bounds = bounds;
        }

        public string StableId { get; }
        public CityAreaFeatureKind Feature { get; }
        public CityOpenAreaDecorationKind Kind { get; }
        public CityOpenAreaDecorationStyle Style { get; }
        public Bounds Bounds { get; }
        public bool BlocksMovement =>
            CityOpenAreaDecorationRules.BlocksMovement(Style);

        public bool Equals(CityOpenAreaDecorationDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Feature == other.Feature &&
                   Kind == other.Kind &&
                   Style == other.Style &&
                   Bounds.Equals(other.Bounds);
        }

        public override bool Equals(object obj)
        {
            return obj is CityOpenAreaDecorationDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ (int)Feature;
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ (int)Style;
                return (hash * 397) ^ Bounds.GetHashCode();
            }
        }
    }

    public sealed class CityOpenAreaDecorationPlan
    {
        public const int MaximumPartCount = 420;

        private readonly ReadOnlyCollection<
            CityOpenAreaDecorationDescriptor> descriptors;

        internal CityOpenAreaDecorationPlan(
            IList<CityOpenAreaDecorationDescriptor> source)
        {
            var copy = new List<CityOpenAreaDecorationDescriptor>(source);
            copy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            descriptors = new ReadOnlyCollection<
                CityOpenAreaDecorationDescriptor>(copy);
        }

        public IReadOnlyList<CityOpenAreaDecorationDescriptor>
            Descriptors => descriptors;
        public int Count => descriptors.Count;

        public int GetCount(CityOpenAreaDecorationKind kind)
        {
            int count = 0;
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (descriptors[index].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public static class CityOpenAreaDecorationRules
    {
        public static bool BlocksMovement(
            CityOpenAreaDecorationStyle style)
        {
            switch (style)
            {
                case CityOpenAreaDecorationStyle.Reeds:
                case CityOpenAreaDecorationStyle.CemeteryPath:
                case CityOpenAreaDecorationStyle.DarkFoliage:
                // The worn ring is a flat trace in the ground and the
                // dropped toy is ankle-high; neither may stop a body.
                case CityOpenAreaDecorationStyle.YardWornTrack:
                case CityOpenAreaDecorationStyle.YardPaint:
                    return false;
                default:
                    return true;
            }
        }
    }

    public static class CityOpenAreaDecorationPlanner
    {
        private const float WaterEdgeThickness = 0.42f;
        private const float FenceThickness = 0.16f;
        private const float FencePostSpacing = 3.2f;
        private const float AccessClearance = 0.45f;
        private const float SpatialChunkSize = 48f;

        // The yard between the hero's building and its neighbour. The
        // typed fringe precincts are a different thing and stay bare.
        private const string HomeYardId = "home-yard";
        private const float YardMinimumRingRadius = 3.5f;
        private const float YardRingWidth = 1.1f;
        private const float YardRingEdgeMargin = 2.2f;
        private const float YardEdgeOffset = 3.4f;

        // The gap between the hero's building and its neighbour is narrow,
        // so the circuit is smaller than an open precinct would allow and
        // keeps a hand's width off both walls.
        private const float HomeYardRingRadius = 4.6f;
        private const float HomeYardRingMargin = 1.1f;
        private const float HomeYardWallMargin = 0.6f;
        private const float YardSlotLateralOffset = 2.1f;
        private const float YardCircuitClearance = 1.4f;
        private const int YardRingSegments = 24;
        private const uint YardSalt = 0x59415244u;

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.up
        };

        public static CityOpenAreaDecorationPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var descriptors =
                new List<CityOpenAreaDecorationDescriptor>(260);
            BuildLake(layout, descriptors);
            BuildCemetery(layout, descriptors);
            BuildHomeYard(layout, descriptors);
            var plan = new CityOpenAreaDecorationPlan(descriptors);
            ValidateOrThrow(layout, plan);
            return plan;
        }

        public static void ValidateOrThrow(
            CityLayout layout,
            CityOpenAreaDecorationPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Count > CityOpenAreaDecorationPlan.MaximumPartCount)
            {
                throw new InvalidOperationException(
                    "Open-area decoration exceeds its bounded part count.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var water = new List<Rect>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                if (layout.Surfaces[index].IsWater)
                {
                    water.Add(layout.Surfaces[index].WorldBounds);
                }
            }

            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (string.IsNullOrWhiteSpace(descriptor.StableId) ||
                    !ids.Add(descriptor.StableId) ||
                    !IsPositiveFinite(descriptor.Bounds.size))
                {
                    throw new InvalidOperationException(
                        "Open-area decoration descriptors require unique " +
                        "IDs and finite positive bounds.");
                }

                Rect footprint = ToXZRect(descriptor.Bounds);
                for (int waterIndex = 0;
                     waterIndex < water.Count;
                     waterIndex++)
                {
                    if (OverlapsStrict(footprint, water[waterIndex]))
                    {
                        throw new InvalidOperationException(
                            $"Open-area decoration '{descriptor.StableId}' " +
                            "overlaps non-walkable water.");
                    }
                }

                if (!descriptor.BlocksMovement)
                {
                    continue;
                }

                for (int accessIndex = 0;
                     accessIndex < layout.OpenAreaAccesses.Count;
                     accessIndex++)
                {
                    CityOpenAreaAccessDescriptor access =
                        layout.OpenAreaAccesses[accessIndex];
                    if (access.Feature != descriptor.Feature)
                    {
                        continue;
                    }

                    if (OverlapsStrict(
                            footprint,
                            Expand(access.ApproachBounds, AccessClearance)))
                    {
                        throw new InvalidOperationException(
                            $"Open-area decoration '{descriptor.StableId}' " +
                            "blocks its canonical street approach.");
                    }
                }
            }
        }

        private static void BuildLake(
            CityLayout layout,
            ICollection<CityOpenAreaDecorationDescriptor> target)
        {
            var lakeByCell =
                new Dictionary<Vector2Int, CitySurfaceDescriptor>();
            var shore = new List<CitySurfaceDescriptor>();
            var water = new List<CitySurfaceDescriptor>();
            var shoreAnchors = new List<ShoreAnchor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Feature != CityAreaFeatureKind.Lake)
                {
                    continue;
                }

                lakeByCell.Add(surface.Cell, surface);
                if (surface.IsWater)
                {
                    water.Add(surface);
                }
                else if (surface.Kind == CitySurfaceKind.LakeShore)
                {
                    shore.Add(surface);
                }
            }

            if (shore.Count == 0 || water.Count == 0)
            {
                return;
            }

            int edgeIndex = 0;
            for (int waterIndex = 0; waterIndex < water.Count; waterIndex++)
            {
                CitySurfaceDescriptor waterSurface = water[waterIndex];
                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int direction =
                        CardinalDirections[directionIndex];
                    if (!lakeByCell.TryGetValue(
                            waterSurface.Cell + direction,
                            out CitySurfaceDescriptor neighbour) ||
                        neighbour.Kind != CitySurfaceKind.LakeShore)
                    {
                        continue;
                    }

                    target.Add(new CityOpenAreaDecorationDescriptor(
                        $"lake-edge-{edgeIndex++:D2}",
                        CityAreaFeatureKind.Lake,
                        CityOpenAreaDecorationKind.LakeWaterEdge,
                        CityOpenAreaDecorationStyle.LakeStone,
                        CreateWaterEdgeBounds(
                            waterSurface.WorldBounds,
                            direction,
                            neighbour.DatumY +
                            CityElevationPlan.GroundTopOffset)));
                    shoreAnchors.Add(CreateShoreAnchor(
                        neighbour,
                        waterSurface,
                        -direction));
                }
            }

            TryGetAccess(
                layout,
                CityAreaFeatureKind.Lake,
                out CityOpenAreaAccessDescriptor access);
            for (int index = 0; index < shoreAnchors.Count; index++)
            {
                ShoreAnchor anchor = shoreAnchors[index];
                CitySurfaceDescriptor surface = anchor.Surface;
                uint hash = StableHash(
                    layout.Seed,
                    surface.Cell.x,
                    surface.Cell.y,
                    0x4C414B45u);
                float parallelJitter =
                    ((hash & 0xFFFFu) / 65535f - 0.5f) * 5f;
                Vector3 position = anchor.Position;
                if (anchor.DirectionToWater.x != 0)
                {
                    position.z = Mathf.Clamp(
                        position.z + parallelJitter,
                        surface.WorldBounds.yMin + 1.2f,
                        surface.WorldBounds.yMax - 1.2f);
                }
                else
                {
                    position.x = Mathf.Clamp(
                        position.x + parallelJitter,
                        surface.WorldBounds.xMin + 1.2f,
                        surface.WorldBounds.xMax - 1.2f);
                }

                if ((index & 1) == 0)
                {
                    AddLakeReeds(
                        target,
                        index,
                        position,
                        access);
                }
                else
                {
                    Bounds rock = CreateGroundedBounds(
                        position,
                        new Vector3(1.35f, 0.58f, 0.95f));
                    if (IsClearOfAccess(rock, access))
                    {
                        target.Add(new CityOpenAreaDecorationDescriptor(
                            $"lake-rock-{index:D2}",
                            CityAreaFeatureKind.Lake,
                            CityOpenAreaDecorationKind.LakeRock,
                            CityOpenAreaDecorationStyle.LakeStone,
                            rock));
                    }
                }
            }

            ShoreAnchor boatAnchor = shoreAnchors[0];
            float bestDistance = float.NegativeInfinity;
            for (int index = 0; index < shoreAnchors.Count; index++)
            {
                float distance =
                    (shoreAnchors[index].Position - access.Center)
                    .sqrMagnitude;
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    boatAnchor = shoreAnchors[index];
                }
            }

            Vector3 boatSize = boatAnchor.DirectionToWater.x != 0
                ? new Vector3(1.25f, 0.48f, 3.8f)
                : new Vector3(3.8f, 0.48f, 1.25f);
            Bounds boat = CreateGroundedBounds(
                boatAnchor.Position,
                boatSize);
            if (IsClearOfAccess(boat, access))
            {
                target.Add(new CityOpenAreaDecorationDescriptor(
                    "lake-weathered-boat",
                    CityAreaFeatureKind.Lake,
                    CityOpenAreaDecorationKind.LakeBoat,
                    CityOpenAreaDecorationStyle.WeatheredWood,
                    boat));
            }
        }

        private static ShoreAnchor CreateShoreAnchor(
            CitySurfaceDescriptor shore,
            CitySurfaceDescriptor water,
            Vector2Int directionToWater)
        {
            const float waterGap = 1.15f;
            Vector3 position = shore.Center;
            if (directionToWater.x > 0)
            {
                position.x = water.WorldBounds.xMin - waterGap;
                position.z = water.WorldBounds.center.y;
            }
            else if (directionToWater.x < 0)
            {
                position.x = water.WorldBounds.xMax + waterGap;
                position.z = water.WorldBounds.center.y;
            }
            else if (directionToWater.y > 0)
            {
                position.x = water.WorldBounds.center.x;
                position.z = water.WorldBounds.yMin - waterGap;
            }
            else
            {
                position.x = water.WorldBounds.center.x;
                position.z = water.WorldBounds.yMax + waterGap;
            }

            position.y = shore.DatumY +
                         CityElevationPlan.GroundTopOffset;
            return new ShoreAnchor(shore, directionToWater, position);
        }

        private static Bounds CreateWaterEdgeBounds(
            Rect water,
            Vector2Int shoreDirection,
            float groundTopY)
        {
            const float edgeHeight = 0.34f;
            const float cornerClearance = 0.28f;
            Vector3 size;
            Vector3 center;
            if (shoreDirection.x != 0)
            {
                size = new Vector3(
                    WaterEdgeThickness,
                    edgeHeight,
                    Mathf.Max(0.2f, water.height - cornerClearance));
                center = new Vector3(
                    shoreDirection.x < 0
                        ? water.xMin - WaterEdgeThickness * 0.5f
                        : water.xMax + WaterEdgeThickness * 0.5f,
                    groundTopY + edgeHeight * 0.5f,
                    water.center.y);
            }
            else
            {
                size = new Vector3(
                    Mathf.Max(0.2f, water.width - cornerClearance),
                    edgeHeight,
                    WaterEdgeThickness);
                center = new Vector3(
                    water.center.x,
                    groundTopY + edgeHeight * 0.5f,
                    shoreDirection.y < 0
                        ? water.yMin - WaterEdgeThickness * 0.5f
                        : water.yMax + WaterEdgeThickness * 0.5f);
            }

            return new Bounds(center, size);
        }

        private static void AddLakeReeds(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            int clusterIndex,
            Vector3 groundPosition,
            CityOpenAreaAccessDescriptor access)
        {
            for (int reed = 0; reed < 3; reed++)
            {
                float height = 0.62f + reed * 0.14f;
                Vector3 offset = new Vector3(
                    (reed - 1) * 0.24f,
                    0f,
                    reed == 1 ? 0.18f : 0f);
                Bounds bounds = CreateGroundedBounds(
                    groundPosition + offset,
                    new Vector3(0.12f, height, 0.12f));
                if (!IsClearOfAccess(bounds, access))
                {
                    continue;
                }

                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"lake-reeds-{clusterIndex:D2}-{reed}",
                    CityAreaFeatureKind.Lake,
                    CityOpenAreaDecorationKind.LakeReeds,
                    CityOpenAreaDecorationStyle.Reeds,
                    bounds));
            }
        }

        private static void BuildCemetery(
            CityLayout layout,
            ICollection<CityOpenAreaDecorationDescriptor> target)
        {
            var surfaces = new List<CitySurfaceDescriptor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Feature == CityAreaFeatureKind.Cemetery &&
                    surface.Kind == CitySurfaceKind.CemeteryGround)
                {
                    surfaces.Add(surface);
                }
            }

            if (surfaces.Count == 0 ||
                !TryGetAccess(
                    layout,
                    CityAreaFeatureKind.Cemetery,
                    out CityOpenAreaAccessDescriptor access))
            {
                return;
            }

            string cemeteryAreaId = surfaces[0].AreaId;
            int minimumCellX = int.MaxValue;
            int maximumCellX = int.MinValue;
            int minimumCellZ = int.MaxValue;
            int maximumCellZ = int.MinValue;
            for (int index = 0; index < surfaces.Count; index++)
            {
                if (!string.Equals(
                        surfaces[index].AreaId,
                        cemeteryAreaId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                Vector2Int cell = surfaces[index].Cell;
                minimumCellX = Mathf.Min(minimumCellX, cell.x);
                maximumCellX = Mathf.Max(maximumCellX, cell.x);
                minimumCellZ = Mathf.Min(minimumCellZ, cell.y);
                maximumCellZ = Mathf.Max(maximumCellZ, cell.y);
            }

            int expectedCellCount =
                (maximumCellX - minimumCellX + 1) *
                (maximumCellZ - minimumCellZ + 1);
            if (surfaces.Count != expectedCellCount)
            {
                return;
            }

            Rect grounds = surfaces[0].WorldBounds;
            for (int index = 1; index < surfaces.Count; index++)
            {
                grounds = Rect.MinMaxRect(
                    Mathf.Min(grounds.xMin, surfaces[index].WorldBounds.xMin),
                    Mathf.Min(grounds.yMin, surfaces[index].WorldBounds.yMin),
                    Mathf.Max(grounds.xMax, surfaces[index].WorldBounds.xMax),
                    Mathf.Max(grounds.yMax, surfaces[index].WorldBounds.yMax));
            }

            Rect path = CreateCemeteryPath(grounds, access);
            float groundTopY = surfaces[0].DatumY +
                               CityElevationPlan.GroundTopOffset;
            target.Add(new CityOpenAreaDecorationDescriptor(
                "cemetery-entry-path",
                CityAreaFeatureKind.Cemetery,
                CityOpenAreaDecorationKind.CemeteryPath,
                CityOpenAreaDecorationStyle.CemeteryPath,
                new Bounds(
                    new Vector3(path.center.x, groundTopY + 0.035f, path.center.y),
                    new Vector3(path.width, 0.07f, path.height))));

            AddCemeteryFence(target, grounds, access, groundTopY);
            AddCemeteryGraves(target, grounds, path, access, groundTopY);
            AddCemeteryTrees(target, grounds, path, access, groundTopY);
        }

        private static Rect CreateCemeteryPath(
            Rect grounds,
            CityOpenAreaAccessDescriptor access)
        {
            Vector3 inward = access.OutwardNormal.normalized;
            bool alongX = Mathf.Abs(inward.x) > Mathf.Abs(inward.z);
            float available = alongX ? grounds.width : grounds.height;
            float length = Mathf.Min(18f, Mathf.Max(5f, available * 0.42f));
            Vector3 start = access.Center - inward * 1.2f;
            Vector3 center = start + inward * (length * 0.5f);
            if (alongX)
            {
                center.x = Mathf.Clamp(
                    center.x,
                    grounds.xMin + length * 0.5f,
                    grounds.xMax - length * 0.5f);
                center.z = Mathf.Clamp(center.z, grounds.yMin, grounds.yMax);
                return new Rect(
                    center.x - length * 0.5f,
                    center.z - 1.45f,
                    length,
                    2.9f);
            }

            center.x = Mathf.Clamp(center.x, grounds.xMin, grounds.xMax);
            center.z = Mathf.Clamp(
                center.z,
                grounds.yMin + length * 0.5f,
                grounds.yMax - length * 0.5f);
            return new Rect(
                center.x - 1.45f,
                center.z - length * 0.5f,
                2.9f,
                length);
        }

        private static void AddCemeteryFence(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            Rect bounds,
            CityOpenAreaAccessDescriptor access,
            float groundTopY)
        {
            Vector3 inward = access.OutwardNormal.normalized;
            bool gateOnWest = inward.x > 0.5f;
            bool gateOnEast = inward.x < -0.5f;
            bool gateOnSouth = inward.z > 0.5f;
            bool gateOnNorth = inward.z < -0.5f;
            float gateCenter = Mathf.Abs(inward.x) > 0.5f
                ? access.Center.z
                : access.Center.x;
            float gateHalfWidth = access.Width * 0.5f +
                                  AccessClearance +
                                  0.35f;
            float gateMinimum = gateCenter - gateHalfWidth;
            float gateMaximum = gateCenter + gateHalfWidth;
            int id = 0;

            AddFenceSide(
                target,
                ref id,
                false,
                bounds.xMin,
                bounds.yMin,
                bounds.yMax,
                gateOnWest,
                gateMinimum,
                gateMaximum,
                groundTopY);
            AddFenceSide(
                target,
                ref id,
                false,
                bounds.xMax,
                bounds.yMin,
                bounds.yMax,
                gateOnEast,
                gateMinimum,
                gateMaximum,
                groundTopY);
            AddFenceSide(
                target,
                ref id,
                true,
                bounds.yMin,
                bounds.xMin,
                bounds.xMax,
                gateOnSouth,
                gateMinimum,
                gateMaximum,
                groundTopY);
            AddFenceSide(
                target,
                ref id,
                true,
                bounds.yMax,
                bounds.xMin,
                bounds.xMax,
                gateOnNorth,
                gateMinimum,
                gateMaximum,
                groundTopY);

            if (gateOnWest || gateOnEast)
            {
                float x = gateOnWest ? bounds.xMin : bounds.xMax;
                AddGatePillar(
                    target,
                    "cemetery-gate-a",
                    x,
                    gateMinimum,
                    groundTopY);
                AddGatePillar(
                    target,
                    "cemetery-gate-b",
                    x,
                    gateMaximum,
                    groundTopY);
            }
            else
            {
                float z = gateOnSouth ? bounds.yMin : bounds.yMax;
                AddGatePillar(
                    target,
                    "cemetery-gate-a",
                    gateMinimum,
                    z,
                    groundTopY);
                AddGatePillar(
                    target,
                    "cemetery-gate-b",
                    gateMaximum,
                    z,
                    groundTopY);
            }
        }

        private static void AddFenceSide(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            ref int id,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            bool hasGate,
            float gateMinimum,
            float gateMaximum,
            float groundTopY)
        {
            if (!hasGate)
            {
                AddFenceRun(
                    target,
                    ref id,
                    horizontal,
                    fixedCoordinate,
                    minimum,
                    maximum,
                    groundTopY);
                return;
            }

            AddFenceRun(
                target,
                ref id,
                horizontal,
                fixedCoordinate,
                minimum,
                Mathf.Clamp(gateMinimum, minimum, maximum),
                groundTopY);
            AddFenceRun(
                target,
                ref id,
                horizontal,
                fixedCoordinate,
                Mathf.Clamp(gateMaximum, minimum, maximum),
                maximum,
                groundTopY);
        }

        private static void AddFenceRun(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            ref int id,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float groundTopY)
        {
            float length = maximum - minimum;
            if (length <= 0.1f)
            {
                return;
            }

            for (int rail = 0; rail < 2; rail++)
            {
                float height = rail == 0 ? 0.52f : 1.02f;
                AddFenceRailPieces(
                    target,
                    ref id,
                    horizontal,
                    fixedCoordinate,
                    minimum,
                    maximum,
                    height,
                    groundTopY);
            }

            int postCount = Mathf.Max(
                1,
                Mathf.CeilToInt(length / FencePostSpacing));
            for (int post = 0; post <= postCount; post++)
            {
                float coordinate = Mathf.Lerp(
                    minimum,
                    maximum,
                    post / (float)postCount);
                Vector3 position = horizontal
                    ? new Vector3(
                        coordinate,
                        groundTopY + 0.66f,
                        fixedCoordinate)
                    : new Vector3(
                        fixedCoordinate,
                        groundTopY + 0.66f,
                        coordinate);
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"cemetery-fence-post-{id++:D3}",
                    CityAreaFeatureKind.Cemetery,
                    CityOpenAreaDecorationKind.CemeteryFence,
                    CityOpenAreaDecorationStyle.CemeteryIron,
                    new Bounds(
                        position,
                        new Vector3(0.18f, 1.48f, 0.18f))));
            }
        }

        private static void AddFenceRailPieces(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            ref int id,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float height,
            float groundTopY)
        {
            float pieceMinimum = minimum;
            while (pieceMinimum < maximum - 0.1f)
            {
                float boundary =
                    (Mathf.Floor(pieceMinimum / SpatialChunkSize) + 1f) *
                    SpatialChunkSize;
                float pieceMaximum = Mathf.Min(maximum, boundary);
                float length = pieceMaximum - pieceMinimum;
                float center = (pieceMinimum + pieceMaximum) * 0.5f;
                Vector3 size = horizontal
                    ? new Vector3(length, 0.12f, FenceThickness)
                    : new Vector3(FenceThickness, 0.12f, length);
                Vector3 position = horizontal
                    ? new Vector3(
                        center,
                        groundTopY + height,
                        fixedCoordinate)
                    : new Vector3(
                        fixedCoordinate,
                        groundTopY + height,
                        center);
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"cemetery-fence-rail-{id++:D3}",
                    CityAreaFeatureKind.Cemetery,
                    CityOpenAreaDecorationKind.CemeteryFence,
                    CityOpenAreaDecorationStyle.CemeteryIron,
                    new Bounds(position, size)));
                pieceMinimum = pieceMaximum;
            }
        }

        private static void AddGatePillar(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            string id,
            float x,
            float z,
            float groundTopY)
        {
            target.Add(new CityOpenAreaDecorationDescriptor(
                id,
                CityAreaFeatureKind.Cemetery,
                CityOpenAreaDecorationKind.CemeteryGate,
                CityOpenAreaDecorationStyle.GraveStone,
                CreateGroundedBounds(
                    new Vector3(x, groundTopY, z),
                    new Vector3(0.58f, 1.8f, 0.58f))));
        }

        private static void AddCemeteryGraves(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            Rect grounds,
            Rect path,
            CityOpenAreaAccessDescriptor access,
            float groundTopY)
        {
            int id = 0;
            Rect clearPath = Expand(path, 0.9f);
            for (float z = grounds.yMin + 4.2f;
                 z <= grounds.yMax - 3.2f;
                 z += 6.0f)
            {
                for (float x = grounds.xMin + 4.0f;
                     x <= grounds.xMax - 3.2f;
                     x += 6.5f)
                {
                    Bounds slab = CreateGroundedBounds(
                        new Vector3(x, groundTopY, z + 0.35f),
                        new Vector3(1.2f, 0.16f, 2.25f));
                    if (OverlapsStrict(ToXZRect(slab), clearPath) ||
                        !IsClearOfAccess(slab, access))
                    {
                        continue;
                    }

                    Bounds headstone = CreateGroundedBounds(
                        new Vector3(x, groundTopY, z - 0.78f),
                        new Vector3(0.84f, 1.05f, 0.30f));
                    target.Add(new CityOpenAreaDecorationDescriptor(
                        $"cemetery-grave-{id:D2}-slab",
                        CityAreaFeatureKind.Cemetery,
                        CityOpenAreaDecorationKind.CemeteryGrave,
                        CityOpenAreaDecorationStyle.GraveStone,
                        slab));
                    target.Add(new CityOpenAreaDecorationDescriptor(
                        $"cemetery-grave-{id:D2}-head",
                        CityAreaFeatureKind.Cemetery,
                        CityOpenAreaDecorationKind.CemeteryGrave,
                        CityOpenAreaDecorationStyle.GraveStone,
                        headstone));
                    id++;
                }
            }
        }

        private static void AddCemeteryTrees(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            Rect grounds,
            Rect path,
            CityOpenAreaAccessDescriptor access,
            float groundTopY)
        {
            Vector3[] positions =
            {
                new Vector3(grounds.xMin + 2.2f, groundTopY, grounds.yMin + 2.2f),
                new Vector3(grounds.xMax - 2.2f, groundTopY, grounds.yMin + 2.2f),
                new Vector3(grounds.xMin + 2.2f, groundTopY, grounds.yMax - 2.2f),
                new Vector3(grounds.xMax - 2.2f, groundTopY, grounds.yMax - 2.2f)
            };
            for (int index = 0; index < positions.Length; index++)
            {
                Bounds trunk = CreateGroundedBounds(
                    positions[index],
                    new Vector3(0.42f, 2.4f, 0.42f));
                if (OverlapsStrict(ToXZRect(trunk), Expand(path, 0.5f)) ||
                    !IsClearOfAccess(trunk, access))
                {
                    continue;
                }

                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"cemetery-tree-{index}-trunk",
                    CityAreaFeatureKind.Cemetery,
                    CityOpenAreaDecorationKind.CemeteryTree,
                    CityOpenAreaDecorationStyle.TreeTrunk,
                    trunk));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"cemetery-tree-{index}-crown-a",
                    CityAreaFeatureKind.Cemetery,
                    CityOpenAreaDecorationKind.CemeteryTree,
                    CityOpenAreaDecorationStyle.DarkFoliage,
                    new Bounds(
                        positions[index] + Vector3.up * 2.8f,
                        new Vector3(1.45f, 3.6f, 1.45f))));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"cemetery-tree-{index}-crown-b",
                    CityAreaFeatureKind.Cemetery,
                    CityOpenAreaDecorationKind.CemeteryTree,
                    CityOpenAreaDecorationStyle.DarkFoliage,
                    new Bounds(
                        positions[index] + Vector3.up * 4.55f,
                        new Vector3(0.85f, 1.7f, 0.85f))));
            }
        }

        /// <summary>
        /// Dresses the yard the hero actually lives against: the roadless
        /// strip of ground between his building and the neighbouring one.
        /// This is the "двор" in the ordinary sense — the gap between two
        /// houses that belongs to nobody and is crossed by everybody.
        ///
        /// The typed <see cref="CityAreaFeatureKind.Yard"/> precincts on
        /// the city fringe are a different thing and stay bare.
        /// </summary>
        private static void BuildHomeYard(
            CityLayout layout,
            ICollection<CityOpenAreaDecorationDescriptor> target)
        {
            BuildingLot home = layout.PlayerHome;
            if (home == null)
            {
                return;
            }

            if (!TryFindHomeYardGround(
                    layout,
                    home,
                    out Rect ground,
                    out float groundTopY))
            {
                return;
            }

            float radius = Mathf.Min(
                HomeYardRingRadius,
                Mathf.Min(ground.width, ground.height) * 0.5f -
                HomeYardRingMargin);
            if (radius < YardMinimumRingRadius)
            {
                return;
            }

            var center = new Vector3(
                ground.center.x,
                groundTopY,
                ground.center.y);

            // No street access descriptor governs this ground, so nothing
            // is culled against an approach; the door side is protected by
            // the ground rectangle itself, which never reaches the
            // frontage.
            CityOpenAreaAccessDescriptor access = default;
            AddYardRing(target, center, radius, access);
            AddYardDeadTree(target, center, access);
            AddYardEdgeObjects(
                target,
                layout,
                ground,
                center,
                radius,
                access);
        }

        /// <summary>
        /// The usable gap beside the home: bounded by the two buildings
        /// along the roadless side, and by the shared lot ground across it.
        /// </summary>
        private static bool TryFindHomeYardGround(
            CityLayout layout,
            BuildingLot home,
            out Rect ground,
            out float groundTopY)
        {
            ground = default;
            groundTopY = 0f;
            if (!TryGetCellSurface(layout, home.Cell, out CitySurfaceDescriptor homeSurface))
            {
                return false;
            }

            bool found = false;
            float bestWidth = 0f;
            for (int index = 0; index < CardinalDirections.Length; index++)
            {
                Vector2Int direction = CardinalDirections[index];
                if (direction.y != 0)
                {
                    // Only the sides of the building, never its frontage.
                    continue;
                }

                if (layout.HasRoad(
                        RoadEdge.ForCellFrontage(home.Cell, direction)))
                {
                    continue;
                }

                Vector2Int neighbourCell = home.Cell + direction;
                if (!TryGetCellSurface(
                        layout,
                        neighbourCell,
                        out CitySurfaceDescriptor neighbourSurface))
                {
                    continue;
                }

                BuildingLot neighbour = null;
                for (int lotIndex = 0;
                     lotIndex < layout.BuildingLots.Count;
                     lotIndex++)
                {
                    if (layout.BuildingLots[lotIndex].Cell == neighbourCell)
                    {
                        neighbour = layout.BuildingLots[lotIndex];
                        break;
                    }
                }

                float homeFace = direction.x < 0
                    ? home.Center.x - home.Size.x * 0.5f
                    : home.Center.x + home.Size.x * 0.5f;
                float neighbourFace;
                if (neighbour != null && neighbour.HasBuilding)
                {
                    neighbourFace = direction.x < 0
                        ? neighbour.Center.x + neighbour.Size.x * 0.5f
                        : neighbour.Center.x - neighbour.Size.x * 0.5f;
                }
                else
                {
                    neighbourFace = direction.x < 0
                        ? neighbourSurface.WorldBounds.xMin
                        : neighbourSurface.WorldBounds.xMax;
                }

                float minimumX = Mathf.Min(homeFace, neighbourFace) +
                                 HomeYardWallMargin;
                float maximumX = Mathf.Max(homeFace, neighbourFace) -
                                 HomeYardWallMargin;
                float minimumZ = Mathf.Max(
                                     homeSurface.WorldBounds.yMin,
                                     neighbourSurface.WorldBounds.yMin) +
                                 HomeYardWallMargin;
                float maximumZ = Mathf.Min(
                                     homeSurface.WorldBounds.yMax,
                                     neighbourSurface.WorldBounds.yMax) -
                                 HomeYardWallMargin;
                float width = maximumX - minimumX;
                float depth = maximumZ - minimumZ;
                if (width <= 0f || depth <= 0f || width <= bestWidth)
                {
                    continue;
                }

                ground = Rect.MinMaxRect(
                    minimumX,
                    minimumZ,
                    maximumX,
                    maximumZ);
                groundTopY = homeSurface.DatumY +
                             CityElevationPlan.GroundTopOffset;
                bestWidth = width;
                found = true;
            }

            return found;
        }

        private static bool TryGetCellSurface(
            CityLayout layout,
            Vector2Int cell,
            out CitySurfaceDescriptor surface)
        {
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                if (layout.Surfaces[index].Cell == cell)
                {
                    surface = layout.Surfaces[index];
                    return true;
                }
            }

            surface = default;
            return false;
        }

        /// <summary>
        /// The worn circuit, laid as flat chords so it reads as a trodden
        /// trace rather than as a built path. Each chord is short, so no
        /// segment ever spans a spatial batching chunk.
        /// </summary>
        private static void AddYardRing(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            Vector3 center,
            float radius,
            CityOpenAreaAccessDescriptor access)
        {
            for (int step = 0; step < YardRingSegments; step++)
            {
                float angle = Mathf.PI * 2f *
                              ((step + 0.5f) / YardRingSegments);
                var position = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y,
                    center.z + Mathf.Sin(angle) * radius);
                float chord = Mathf.PI * 2f * radius / YardRingSegments;
                bool alongX = Mathf.Abs(Mathf.Sin(angle)) >
                              Mathf.Abs(Mathf.Cos(angle));
                Vector3 size = alongX
                    ? new Vector3(chord + 0.25f, 0.06f, YardRingWidth)
                    : new Vector3(YardRingWidth, 0.06f, chord + 0.25f);
                Bounds bounds = CreateGroundedBounds(position, size);
                if (!IsClearOfAccess(bounds, access))
                {
                    continue;
                }

                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-ring-{step:D2}",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardRingTrack,
                    CityOpenAreaDecorationStyle.YardWornTrack,
                    bounds));
            }
        }

        /// <summary>
        /// A dead trunk with two broken limbs: the only tall mass in the
        /// yard and the axis the circuit is drawn around. No crown, so the
        /// silhouette stays bare against the fog.
        /// </summary>
        private static void AddYardDeadTree(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            Vector3 center,
            CityOpenAreaAccessDescriptor access)
        {
            Bounds trunk = CreateGroundedBounds(
                center,
                new Vector3(0.46f, 4.1f, 0.46f));
            if (!IsClearOfAccess(trunk, access))
            {
                return;
            }

            target.Add(new CityOpenAreaDecorationDescriptor(
                $"{HomeYardId}-tree-trunk",
                CityAreaFeatureKind.Yard,
                CityOpenAreaDecorationKind.YardDeadTree,
                CityOpenAreaDecorationStyle.TreeTrunk,
                trunk));
            target.Add(new CityOpenAreaDecorationDescriptor(
                $"{HomeYardId}-tree-limb-a",
                CityAreaFeatureKind.Yard,
                CityOpenAreaDecorationKind.YardDeadTree,
                CityOpenAreaDecorationStyle.TreeTrunk,
                new Bounds(
                    center + new Vector3(0.52f, 3.05f, 0.06f),
                    new Vector3(1.30f, 0.20f, 0.20f))));
            target.Add(new CityOpenAreaDecorationDescriptor(
                $"{HomeYardId}-tree-limb-b",
                CityAreaFeatureKind.Yard,
                CityOpenAreaDecorationKind.YardDeadTree,
                CityOpenAreaDecorationStyle.TreeTrunk,
                new Bounds(
                    center + new Vector3(-0.10f, 3.62f, -0.58f),
                    new Vector3(0.17f, 0.17f, 1.05f))));
        }

        /// <summary>
        /// The edge objects. Every one of them is a specific trace of a
        /// person who is not here: a bench somebody repaired, a carpet
        /// frame nobody beats carpets on, an empty sandpit, one dropped
        /// toy, a dead lamp, a bin and a single bottle.
        /// </summary>
        private static void AddYardEdgeObjects(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            CityLayout layout,
            Rect grounds,
            Vector3 center,
            float radius,
            CityOpenAreaAccessDescriptor access)
        {
            uint hash = StableHash(
                layout.Seed,
                Mathf.RoundToInt(grounds.center.x),
                Mathf.RoundToInt(grounds.center.y),
                YardSalt);
            int firstSlot = (int)(hash & 3u);

            // Bench: faces the circuit, so the empty seat and the circling
            // are read in one frame.
            if (TryResolveYardSlot(
                    center,
                    grounds,
                    radius,
                    firstSlot,
                    access,
                    new Vector3(1.85f, 0.52f, 0.52f),
                    out Vector3 benchPosition))
            {
                Bounds benchSeat = CreateGroundedBounds(
                    benchPosition + Vector3.up * 0.42f,
                    new Vector3(1.85f, 0.10f, 0.52f));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-bench-seat",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardBench,
                    CityOpenAreaDecorationStyle.YardTimber,
                    benchSeat));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-bench-leg-a",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardBench,
                    CityOpenAreaDecorationStyle.YardTimber,
                    CreateGroundedBounds(
                        benchPosition + new Vector3(-0.74f, 0f, 0f),
                        new Vector3(0.14f, 0.42f, 0.46f))));
                // The replaced leg is the repair the bible asks for: a
                // painted pipe under a timber seat.
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-bench-leg-b-repair",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardBench,
                    CityOpenAreaDecorationStyle.YardPipe,
                    CreateGroundedBounds(
                        benchPosition + new Vector3(0.74f, 0f, 0f),
                        new Vector3(0.10f, 0.42f, 0.10f))));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-bench-bottle",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardBottle,
                    CityOpenAreaDecorationStyle.YardTimber,
                    CreateGroundedBounds(
                        benchPosition + new Vector3(1.18f, 0f, 0.28f),
                        new Vector3(0.09f, 0.27f, 0.09f))));
            }

            // Carpet-beating frame: the second vertical, deliberately
            // transparent so it never closes the yard in.
            if (TryResolveYardSlot(
                    center,
                    grounds,
                    radius,
                    firstSlot + 1,
                    access,
                    new Vector3(2.55f, 1.69f, 0.30f),
                    out Vector3 framePosition))
            {
                Bounds frameHeader = new Bounds(
                    framePosition + Vector3.up * 1.62f,
                    new Vector3(2.55f, 0.14f, 0.14f));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-carpet-frame-header",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardCarpetFrame,
                    CityOpenAreaDecorationStyle.YardPipe,
                    frameHeader));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-carpet-frame-post-a",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardCarpetFrame,
                    CityOpenAreaDecorationStyle.YardPipe,
                    CreateGroundedBounds(
                        framePosition + new Vector3(-1.22f, 0f, 0f),
                        new Vector3(0.13f, 1.69f, 0.13f))));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-carpet-frame-post-b",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardCarpetFrame,
                    CityOpenAreaDecorationStyle.YardPipe,
                    CreateGroundedBounds(
                        framePosition + new Vector3(1.22f, 0f, 0f),
                        new Vector3(0.13f, 1.69f, 0.13f))));
            }

            // Sandpit with no sand, and the one object in the yard that
            // was a child's. It is the only saturated colour here.
            if (TryResolveYardSlot(
                    center,
                    grounds,
                    radius,
                    firstSlot + 2,
                    access,
                    new Vector3(2.20f, 0.24f, 2.20f),
                    out Vector3 sandpitPosition))
            {
                Bounds sandpitEdge = CreateGroundedBounds(
                    sandpitPosition,
                    new Vector3(2.20f, 0.24f, 0.18f));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-sandpit-edge-a",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardSandpit,
                    CityOpenAreaDecorationStyle.YardTimber,
                    sandpitEdge));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-sandpit-edge-b",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardSandpit,
                    CityOpenAreaDecorationStyle.YardTimber,
                    CreateGroundedBounds(
                        sandpitPosition + new Vector3(0f, 0f, 2.02f),
                        new Vector3(2.20f, 0.24f, 0.18f))));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-sandpit-edge-c",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardSandpit,
                    CityOpenAreaDecorationStyle.YardTimber,
                    CreateGroundedBounds(
                        sandpitPosition + new Vector3(-1.01f, 0f, 1.01f),
                        new Vector3(0.18f, 0.24f, 1.84f))));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-sandpit-edge-d",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardSandpit,
                    CityOpenAreaDecorationStyle.YardTimber,
                    CreateGroundedBounds(
                        sandpitPosition + new Vector3(1.01f, 0f, 1.01f),
                        new Vector3(0.18f, 0.24f, 1.84f))));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-sandpit-toy",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardChildToy,
                    CityOpenAreaDecorationStyle.YardPaint,
                    CreateGroundedBounds(
                        sandpitPosition + new Vector3(0.34f, 0f, 1.46f),
                        new Vector3(0.24f, 0.16f, 0.20f))));
            }

            // A lamp post that has not worked in years: the yard owns no
            // light, so this is a silhouette, never an emitter.
            if (TryResolveYardSlot(
                    center,
                    grounds,
                    radius,
                    firstSlot + 3,
                    access,
                    new Vector3(0.70f, 3.35f, 0.40f),
                    out Vector3 lampPosition))
            {
                Bounds lampPost = CreateGroundedBounds(
                    lampPosition,
                    new Vector3(0.17f, 3.35f, 0.17f));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-dead-lamp-post",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardDeadLamp,
                    CityOpenAreaDecorationStyle.YardPipe,
                    lampPost));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-dead-lamp-head",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardDeadLamp,
                    CityOpenAreaDecorationStyle.YardPipe,
                    new Bounds(
                        lampPosition + new Vector3(0.16f, 3.34f, 0f),
                        new Vector3(0.54f, 0.22f, 0.34f))));
            }

            // The bin stands against a wall at one end, clear of the
            // circuit like everything else here.
            if (TryResolveYardSlot(
                    center,
                    grounds,
                    radius,
                    firstSlot + 2,
                    access,
                    new Vector3(1.02f, 1.02f, 0.72f),
                    out Vector3 binPosition))
            {
                Bounds binBody = CreateGroundedBounds(
                    binPosition,
                    new Vector3(1.02f, 1.02f, 0.72f));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-bin-body",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardBin,
                    CityOpenAreaDecorationStyle.YardPipe,
                    binBody));
                target.Add(new CityOpenAreaDecorationDescriptor(
                    $"{HomeYardId}-bin-lid",
                    CityAreaFeatureKind.Yard,
                    CityOpenAreaDecorationKind.YardBin,
                    CityOpenAreaDecorationStyle.YardPipe,
                    new Bounds(
                        binPosition + new Vector3(0.06f, 1.07f, 0f),
                        new Vector3(1.06f, 0.10f, 0.76f))));
            }
        }

        private static bool TryResolveYardSlot(
            Vector3 center,
            Rect grounds,
            float radius,
            int slot,
            CityOpenAreaAccessDescriptor access,
            Vector3 footprint,
            out Vector3 position)
        {
            bool longIsX = grounds.width >= grounds.height;
            Vector3 along = longIsX ? Vector3.right : Vector3.forward;
            Vector3 across = longIsX ? Vector3.forward : Vector3.right;
            float reach = radius + YardEdgeOffset;
            float lateral = Mathf.Min(
                YardSlotLateralOffset,
                (longIsX ? grounds.height : grounds.width) * 0.5f -
                YardRingEdgeMargin);

            for (int attempt = 0; attempt < 4; attempt++)
            {
                int index = (slot + attempt) & 3;
                float endSign = (index & 1) == 0 ? 1f : -1f;
                float sideSign = (index & 2) == 0 ? 1f : -1f;
                Vector3 candidate = center +
                                    along * (reach * endSign) +
                                    across * (lateral * sideSign);
                candidate.y = center.y;
                candidate.x = Mathf.Clamp(
                    candidate.x,
                    grounds.xMin + footprint.x * 0.5f,
                    grounds.xMax - footprint.x * 0.5f);
                candidate.z = Mathf.Clamp(
                    candidate.z,
                    grounds.yMin + footprint.z * 0.5f,
                    grounds.yMax - footprint.z * 0.5f);

                // Never let a clamp drag an object back onto the circuit.
                float fromCircuit = Vector2.Distance(
                    new Vector2(center.x, center.z),
                    new Vector2(candidate.x, candidate.z));
                if (fromCircuit < radius + YardCircuitClearance)
                {
                    continue;
                }

                if (IsClearOfAccess(
                        CreateGroundedBounds(candidate, footprint),
                        access))
                {
                    position = candidate;
                    return true;
                }
            }

            position = default;
            return false;
        }

        private static bool TryGetAccess(
            CityLayout layout,
            CityAreaFeatureKind feature,
            out CityOpenAreaAccessDescriptor access)
        {
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                if (layout.OpenAreaAccesses[index].Feature == feature)
                {
                    access = layout.OpenAreaAccesses[index];
                    return true;
                }
            }

            access = default;
            return false;
        }

        private static bool IsClearOfAccess(
            Bounds bounds,
            CityOpenAreaAccessDescriptor access)
        {
            if (string.IsNullOrEmpty(access.Id))
            {
                return true;
            }

            return !OverlapsStrict(
                ToXZRect(bounds),
                Expand(access.ApproachBounds, AccessClearance));
        }

        private static Bounds CreateGroundedBounds(
            Vector3 groundPosition,
            Vector3 size)
        {
            return new Bounds(
                new Vector3(
                    groundPosition.x,
                    groundPosition.y + size.y * 0.5f,
                    groundPosition.z),
                size);
        }

        private static Rect ToXZRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            const float epsilon = 0.001f;
            return left.xMin < right.xMax - epsilon &&
                   left.xMax > right.xMin + epsilon &&
                   left.yMin < right.yMax - epsilon &&
                   left.yMax > right.yMin + epsilon;
        }

        private static bool IsPositiveFinite(Vector3 value)
        {
            return value.x > 0f && value.y > 0f && value.z > 0f &&
                   IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static uint StableHash(
            int seed,
            int x,
            int z,
            uint salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ salt;
                value = (value ^ (uint)x) * 16777619u;
                value = (value ^ (uint)z) * 16777619u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }

        private readonly struct ShoreAnchor
        {
            public ShoreAnchor(
                CitySurfaceDescriptor surface,
                Vector2Int directionToWater,
                Vector3 position)
            {
                Surface = surface;
                DirectionToWater = directionToWater;
                Position = position;
            }

            public CitySurfaceDescriptor Surface { get; }
            public Vector2Int DirectionToWater { get; }
            public Vector3 Position { get; }
        }
    }
}
