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
        CemeteryTree = 8
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
        DarkFoliage = 7
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
                    return false;
                default:
                    return true;
            }
        }
    }

    public static class CityOpenAreaDecorationPlanner
    {
        private const float GroundTopY = -0.08f;
        private const float WaterEdgeThickness = 0.42f;
        private const float FenceThickness = 0.16f;
        private const float FencePostSpacing = 3.2f;
        private const float AccessClearance = 0.45f;
        private const float SpatialChunkSize = 48f;

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
                            direction)));
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

            position.y = GroundTopY;
            return new ShoreAnchor(shore, directionToWater, position);
        }

        private static Bounds CreateWaterEdgeBounds(
            Rect water,
            Vector2Int shoreDirection)
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
                    GroundTopY + edgeHeight * 0.5f,
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
                    GroundTopY + edgeHeight * 0.5f,
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
            target.Add(new CityOpenAreaDecorationDescriptor(
                "cemetery-entry-path",
                CityAreaFeatureKind.Cemetery,
                CityOpenAreaDecorationKind.CemeteryPath,
                CityOpenAreaDecorationStyle.CemeteryPath,
                new Bounds(
                    new Vector3(path.center.x, GroundTopY + 0.035f, path.center.y),
                    new Vector3(path.width, 0.07f, path.height))));

            AddCemeteryFence(target, grounds, access);
            AddCemeteryGraves(target, grounds, path, access);
            AddCemeteryTrees(target, grounds, path, access);
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
            CityOpenAreaAccessDescriptor access)
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
                gateMaximum);
            AddFenceSide(
                target,
                ref id,
                false,
                bounds.xMax,
                bounds.yMin,
                bounds.yMax,
                gateOnEast,
                gateMinimum,
                gateMaximum);
            AddFenceSide(
                target,
                ref id,
                true,
                bounds.yMin,
                bounds.xMin,
                bounds.xMax,
                gateOnSouth,
                gateMinimum,
                gateMaximum);
            AddFenceSide(
                target,
                ref id,
                true,
                bounds.yMax,
                bounds.xMin,
                bounds.xMax,
                gateOnNorth,
                gateMinimum,
                gateMaximum);

            if (gateOnWest || gateOnEast)
            {
                float x = gateOnWest ? bounds.xMin : bounds.xMax;
                AddGatePillar(target, "cemetery-gate-a", x, gateMinimum);
                AddGatePillar(target, "cemetery-gate-b", x, gateMaximum);
            }
            else
            {
                float z = gateOnSouth ? bounds.yMin : bounds.yMax;
                AddGatePillar(target, "cemetery-gate-a", gateMinimum, z);
                AddGatePillar(target, "cemetery-gate-b", gateMaximum, z);
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
            float gateMaximum)
        {
            if (!hasGate)
            {
                AddFenceRun(
                    target,
                    ref id,
                    horizontal,
                    fixedCoordinate,
                    minimum,
                    maximum);
                return;
            }

            AddFenceRun(
                target,
                ref id,
                horizontal,
                fixedCoordinate,
                minimum,
                Mathf.Clamp(gateMinimum, minimum, maximum));
            AddFenceRun(
                target,
                ref id,
                horizontal,
                fixedCoordinate,
                Mathf.Clamp(gateMaximum, minimum, maximum),
                maximum);
        }

        private static void AddFenceRun(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            ref int id,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum)
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
                    height);
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
                    ? new Vector3(coordinate, 0.66f, fixedCoordinate)
                    : new Vector3(fixedCoordinate, 0.66f, coordinate);
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
            float height)
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
                    ? new Vector3(center, height, fixedCoordinate)
                    : new Vector3(fixedCoordinate, height, center);
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
            float z)
        {
            target.Add(new CityOpenAreaDecorationDescriptor(
                id,
                CityAreaFeatureKind.Cemetery,
                CityOpenAreaDecorationKind.CemeteryGate,
                CityOpenAreaDecorationStyle.GraveStone,
                CreateGroundedBounds(
                    new Vector3(x, GroundTopY, z),
                    new Vector3(0.58f, 1.8f, 0.58f))));
        }

        private static void AddCemeteryGraves(
            ICollection<CityOpenAreaDecorationDescriptor> target,
            Rect grounds,
            Rect path,
            CityOpenAreaAccessDescriptor access)
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
                        new Vector3(x, GroundTopY, z + 0.35f),
                        new Vector3(1.2f, 0.16f, 2.25f));
                    if (OverlapsStrict(ToXZRect(slab), clearPath) ||
                        !IsClearOfAccess(slab, access))
                    {
                        continue;
                    }

                    Bounds headstone = CreateGroundedBounds(
                        new Vector3(x, GroundTopY, z - 0.78f),
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
            CityOpenAreaAccessDescriptor access)
        {
            Vector3[] positions =
            {
                new Vector3(grounds.xMin + 2.2f, GroundTopY, grounds.yMin + 2.2f),
                new Vector3(grounds.xMax - 2.2f, GroundTopY, grounds.yMin + 2.2f),
                new Vector3(grounds.xMin + 2.2f, GroundTopY, grounds.yMax - 2.2f),
                new Vector3(grounds.xMax - 2.2f, GroundTopY, grounds.yMax - 2.2f)
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
                    GroundTopY + size.y * 0.5f,
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
