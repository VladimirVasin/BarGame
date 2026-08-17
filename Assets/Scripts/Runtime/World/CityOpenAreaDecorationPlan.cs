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
        TreeTrunk = 6,
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
        // The cemetery now budgets its own dedicated plan; this covers
        // the lake shoreline and the authored bar-side yard only.
        public const int MaximumPartCount = 260;

        private readonly ReadOnlyCollection<
            CityOpenAreaDecorationDescriptor> descriptors;

        internal CityOpenAreaDecorationPlan(
            IList<CityOpenAreaDecorationDescriptor> source,
            HomeYardSitePlan? homeYardSite,
            HomeYardSpotlightDescriptor? yardSpotlight)
        {
            var copy = new List<CityOpenAreaDecorationDescriptor>(source);
            copy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            descriptors = new ReadOnlyCollection<
                CityOpenAreaDecorationDescriptor>(copy);
            HomeYardSite = homeYardSite;
            YardSpotlight = yardSpotlight;
        }

        public IReadOnlyList<CityOpenAreaDecorationDescriptor>
            Descriptors => descriptors;
        public int Count => descriptors.Count;
        public HomeYardSitePlan? HomeYardSite { get; }
        public HomeYardSpotlightDescriptor? YardSpotlight { get; }

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
                // The dropped toy is ankle-high; it may not stop a body.
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
        private const float AccessClearance = 0.45f;

        // The yard immediately left of the selected bar. The
        // typed fringe precincts are a different thing and stay bare.
        private const string HomeYardId = "home-yard";
        private const float YardRingEdgeMargin = 2.2f;
        private const float YardEdgeOffset = 3.4f;

        private const float YardSlotLateralOffset = 2.1f;
        private const float YardCircuitClearance =
            HomeYardUtilityPlanner.CircuitClearance;
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
            HomeYardSitePlan? homeYardSite =
                HomeYardSitePlanner.Create(layout);
            BuildHomeYard(layout, descriptors, homeYardSite);
            HomeYardSpotlightDescriptor? yardSpotlight =
                homeYardSite.HasValue
                    ? HomeYardSpotlightPlanner.Create(homeYardSite.Value)
                    : null;
            var plan = new CityOpenAreaDecorationPlan(
                descriptors,
                homeYardSite,
                yardSpotlight);
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

            ValidateHomeYardContracts(layout, plan);

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

        private static void ValidateHomeYardContracts(
            CityLayout layout,
            CityOpenAreaDecorationPlan plan)
        {
            bool hasSite = plan.HomeYardSite.HasValue;
            bool hasSpotlight = plan.YardSpotlight.HasValue;
            if (hasSpotlight && !hasSite)
            {
                throw new InvalidOperationException(
                    "A home-yard spotlight requires a home-yard site.");
            }

            if (!hasSite)
            {
                return;
            }

            HomeYardSitePlan site = plan.HomeYardSite.Value;
            if (layout.PlayerHome == null ||
                site.Home == null ||
                !ReferenceEquals(site.Home, layout.PlayerHome) ||
                site.HomeCell != layout.PlayerHome.Cell)
            {
                throw new InvalidOperationException(
                    "The yard site must retain the layout's player home " +
                    "as its narrative owner.");
            }

            BuildingLot expectedAnchor = null;
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot candidate = layout.BuildingLots[index];
                if (candidate != null &&
                    candidate.Cell == site.AnchorCell)
                {
                    expectedAnchor = candidate;
                    break;
                }
            }

            if (site.Anchor == null ||
                !site.Anchor.IsBar ||
                !ReferenceEquals(site.Anchor, expectedAnchor) ||
                site.AnchorCell != site.Anchor.Cell ||
                site.AnchorCell !=
                    site.HomeCell + site.Home.FrontageDirection ||
                site.Anchor.FrontageDirection !=
                    -site.Home.FrontageDirection ||
                !layout.HasRoad(
                    RoadEdge.ForCellFrontage(
                        site.HomeCell,
                        site.Home.FrontageDirection)))
            {
                throw new InvalidOperationException(
                    "The yard site must reference the bar across the " +
                    "player home's shared street frontage.");
            }

            if (site.DirectionFromAnchorToNeighbour != Vector2Int.left)
            {
                throw new InvalidOperationException(
                    "The yard must occupy the roadless ground directly " +
                    "left of its bar.");
            }

            if (!IsPositiveFinite(site.GroundBounds.size) ||
                !IsFinite(site.GroundBounds.position) ||
                !IsFinite(site.GroundY) ||
                !IsFinite(site.RingCenter) ||
                !IsFinite(site.RingRadius) ||
                site.RingRadius < HomeYardSitePlanner.MinimumRingRadius)
            {
                throw new InvalidOperationException(
                    "The home-yard site requires finite ground and ring geometry.");
            }

            var expectedRingCenter = new Vector3(
                site.GroundBounds.center.x,
                site.GroundY,
                site.GroundBounds.center.y);
            if ((site.RingCenter - expectedRingCenter).sqrMagnitude >
                0.000001f)
            {
                throw new InvalidOperationException(
                    "The home-yard ring must be centred on its ground.");
            }

            BuildingLot expectedNeighbour = null;
            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                BuildingLot candidate = layout.BuildingLots[index];
                if (candidate != null &&
                    candidate.Cell == site.NeighbourCell)
                {
                    expectedNeighbour = candidate;
                    break;
                }
            }

            if (!ReferenceEquals(site.Neighbour, expectedNeighbour))
            {
                throw new InvalidOperationException(
                    "The home-yard neighbour does not match its layout cell.");
            }

            if (!hasSpotlight)
            {
                return;
            }

            HomeYardSpotlightDescriptor spotlight =
                plan.YardSpotlight.Value;
            if (expectedNeighbour == null ||
                !expectedNeighbour.HasBuilding ||
                spotlight.NeighbourCell != site.NeighbourCell)
            {
                throw new InvalidOperationException(
                    "The home-yard spotlight requires the selected real " +
                    "neighbour building.");
            }

            if (string.IsNullOrWhiteSpace(spotlight.StableId) ||
                !string.Equals(
                    spotlight.StableId,
                    HomeYardSpotlightPlanner.StableId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The home-yard spotlight requires its stable ID.");
            }

            float rotationMagnitudeSquared =
                spotlight.Rotation.x * spotlight.Rotation.x +
                spotlight.Rotation.y * spotlight.Rotation.y +
                spotlight.Rotation.z * spotlight.Rotation.z +
                spotlight.Rotation.w * spotlight.Rotation.w;
            if (!IsFinite(spotlight.MountPosition) ||
                !IsFinite(spotlight.TargetPosition) ||
                !IsFinite(spotlight.FacadeNormal) ||
                !IsFinite(spotlight.Color) ||
                !IsFinite(spotlight.Rotation) ||
                !IsFinite(rotationMagnitudeSquared) ||
                rotationMagnitudeSquared < 0.5f)
            {
                throw new InvalidOperationException(
                    "The home-yard spotlight transform and color must be finite.");
            }

            if (!IsFinite(spotlight.Intensity) ||
                !IsFinite(spotlight.Range) ||
                spotlight.Intensity < HomeYardSpotlightPlanner.Intensity ||
                spotlight.Range <= HomeYardSpotlightPlanner.RangeMargin ||
                !IsFinite(spotlight.InnerSpotAngle) ||
                !IsFinite(spotlight.SpotAngle) ||
                spotlight.InnerSpotAngle <= 0f ||
                spotlight.InnerSpotAngle >= spotlight.SpotAngle ||
                spotlight.SpotAngle >= 180f)
            {
                throw new InvalidOperationException(
                    "The home-yard spotlight requires its theatrical " +
                    "intensity, protected range margin and nested cone " +
                    "angles below 180 degrees.");
            }

            Vector3 expectedNormal =
                site.NeighbourFacadeNormal.normalized;
            Vector3 actualNormal = spotlight.FacadeNormal.normalized;
            if (expectedNormal.sqrMagnitude < 0.5f ||
                actualNormal.sqrMagnitude < 0.5f ||
                Vector3.Dot(expectedNormal, actualNormal) < 0.999f)
            {
                throw new InvalidOperationException(
                    "The home-yard spotlight must face out from the selected wall.");
            }

            Vector3 targetDelta =
                spotlight.TargetPosition - spotlight.MountPosition;
            Vector3 forward = spotlight.Rotation * Vector3.forward;
            if (targetDelta.sqrMagnitude < 0.0001f ||
                forward.sqrMagnitude < 0.5f ||
                Vector3.Dot(
                    targetDelta.normalized,
                    forward.normalized) < 0.999f)
            {
                throw new InvalidOperationException(
                    "The home-yard spotlight rotation must aim at its target.");
            }

            Vector3 expectedTarget =
                site.RingCenter +
                Vector3.up * HomeYardSpotlightPlanner.AimHeight;
            if ((spotlight.TargetPosition - expectedTarget).sqrMagnitude >
                0.000001f)
            {
                throw new InvalidOperationException(
                    "The home-yard spotlight must target the ring.");
            }

            ValidateHomeYardSpotlightCoverage(site, spotlight, forward);
        }

        private static void ValidateHomeYardSpotlightCoverage(
            HomeYardSitePlan site,
            HomeYardSpotlightDescriptor spotlight,
            Vector3 forward)
        {
            float coverageRadius =
                site.RingRadius +
                HomeYardSpotlightPlanner.RadialCoverageMargin;
            float hotHalfAngle = spotlight.InnerSpotAngle * 0.5f;
            for (int index = 0;
                 index < HomeYardSpotlightPlanner.CoverageSampleCount;
                 index++)
            {
                float angle = Mathf.PI * 2f * index /
                              HomeYardSpotlightPlanner.CoverageSampleCount;
                var radial = new Vector3(
                    Mathf.Cos(angle) * coverageRadius,
                    0f,
                    Mathf.Sin(angle) * coverageRadius);
                ValidateHomeYardSpotlightSample(
                    spotlight,
                    forward,
                    site.RingCenter + radial,
                    hotHalfAngle);
                ValidateHomeYardSpotlightSample(
                    spotlight,
                    forward,
                    site.RingCenter + radial +
                    Vector3.up *
                    HomeYardSpotlightPlanner.ChairCoverageHeight,
                    hotHalfAngle);
            }
        }

        private static void ValidateHomeYardSpotlightSample(
            HomeYardSpotlightDescriptor spotlight,
            Vector3 forward,
            Vector3 sample,
            float hotHalfAngle)
        {
            const float tolerance = 0.001f;
            Vector3 delta = sample - spotlight.MountPosition;
            float protectedRange = Mathf.Min(
                spotlight.Range - HomeYardSpotlightPlanner.RangeMargin,
                spotlight.Range / HomeYardSpotlightPlanner.RangeMultiplier);
            if (delta.magnitude > protectedRange + tolerance ||
                Vector3.Angle(forward, delta) > hotHalfAngle + tolerance)
            {
                throw new InvalidOperationException(
                    "The home-yard spotlight hot core must cover the full " +
                    "wheelchair circuit inside its protected range.");
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
            ICollection<CityOpenAreaDecorationDescriptor> target,
            HomeYardSitePlan? plannedSite)
        {
            if (!plannedSite.HasValue)
            {
                return;
            }

            HomeYardSitePlan site = plannedSite.Value;
            Rect ground = site.GroundBounds;
            Vector3 center = site.RingCenter;
            float radius = site.RingRadius;

            // No street access descriptor governs this ground, so nothing
            // is culled against an approach; the door side is protected by
            // the ground rectangle itself, which never reaches the
            // frontage.
            CityOpenAreaAccessDescriptor access = default;

            // The leaning utilities are authored by the shared yard
            // contract and drawn by the city decoration pass; the yard
            // dressing only has to keep its slots off their ground.
            var reservedGround = new List<Rect>(2);
            if (HomeYardUtilityPlanner.TryCreatePhoneBooth(
                    site,
                    out HomeYardUtilityAnchor booth))
            {
                reservedGround.Add(
                    Expand(booth.Footprint, AccessClearance));
            }

            if (HomeYardUtilityPlanner.TryCreateDumpster(
                    site,
                    out HomeYardUtilityAnchor dumpster))
            {
                reservedGround.Add(
                    Expand(dumpster.Footprint, AccessClearance));
            }

            // The rider's circuit is invisible ground: the chair rides
            // bare earth around the dead tree, and only the yard site
            // contract knows the circle. Nothing is drawn for it.
            AddYardDeadTree(target, center, access);
            AddYardEdgeObjects(
                target,
                layout,
                ground,
                center,
                radius,
                access,
                reservedGround);
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
            CityOpenAreaAccessDescriptor access,
            IReadOnlyList<Rect> reservedGround)
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
                    reservedGround,
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
                    reservedGround,
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
                    reservedGround,
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
                    reservedGround,
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
                    reservedGround,
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
            IReadOnlyList<Rect> reservedGround,
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

                if (!IsClearOfAccess(
                        CreateGroundedBounds(candidate, footprint),
                        access))
                {
                    continue;
                }

                if (OverlapsReservedGround(
                        CreateGroundedBounds(candidate, footprint),
                        reservedGround))
                {
                    continue;
                }

                position = candidate;
                return true;
            }

            position = default;
            return false;
        }

        private static bool OverlapsReservedGround(
            Bounds bounds,
            IReadOnlyList<Rect> reservedGround)
        {
            if (reservedGround == null)
            {
                return false;
            }

            Rect footprint = ToXZRect(bounds);
            for (int index = 0; index < reservedGround.Count; index++)
            {
                if (OverlapsStrict(footprint, reservedGround[index]))
                {
                    return true;
                }
            }

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

        private static bool IsPositiveFinite(Vector2 value)
        {
            return value.x > 0f && value.y > 0f &&
                   IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r) &&
                   IsFinite(value.g) &&
                   IsFinite(value.b) &&
                   IsFinite(value.a);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
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
