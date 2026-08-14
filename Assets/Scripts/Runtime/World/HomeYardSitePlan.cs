using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The shared geometry contract for the small yard beside the player's
    /// home. It keeps decoration, staged actors and facade fixtures on the
    /// same deterministically selected strip of ground.
    /// </summary>
    public readonly struct HomeYardSitePlan : IEquatable<HomeYardSitePlan>
    {
        internal HomeYardSitePlan(
            BuildingLot home,
            BuildingLot neighbour,
            Vector2Int directionFromHomeToNeighbour,
            Rect groundBounds,
            float groundY,
            Vector3 ringCenter,
            float ringRadius)
        {
            Home = home ?? throw new ArgumentNullException(nameof(home));
            Neighbour = neighbour;
            DirectionFromHomeToNeighbour =
                directionFromHomeToNeighbour;
            GroundBounds = groundBounds;
            GroundY = groundY;
            RingCenter = new Vector3(
                ringCenter.x,
                groundY,
                ringCenter.z);
            RingRadius = ringRadius;
        }

        public BuildingLot Home { get; }
        public BuildingLot Neighbour { get; }
        public Vector2Int HomeCell => Home != null
            ? Home.Cell
            : default;
        public Vector2Int NeighbourCell =>
            Home != null
                ? Home.Cell + DirectionFromHomeToNeighbour
                : default;
        public Vector2Int DirectionFromHomeToNeighbour { get; }
        public Rect GroundBounds { get; }
        public float GroundY { get; }
        public Vector3 RingCenter { get; }
        public float RingRadius { get; }
        public bool HasNeighbourBuilding =>
            Neighbour != null && Neighbour.HasBuilding;

        /// <summary>
        /// Normal from the neighbouring wall into the yard.
        /// </summary>
        public Vector3 NeighbourFacadeNormal => new Vector3(
            -DirectionFromHomeToNeighbour.x,
            0f,
            -DirectionFromHomeToNeighbour.y);

        public bool Equals(HomeYardSitePlan other)
        {
            return LotsEqual(Home, other.Home) &&
                   LotsEqual(Neighbour, other.Neighbour) &&
                   DirectionFromHomeToNeighbour ==
                   other.DirectionFromHomeToNeighbour &&
                   GroundBounds.Equals(other.GroundBounds) &&
                   GroundY.Equals(other.GroundY) &&
                   RingCenter.Equals(other.RingCenter) &&
                   RingRadius.Equals(other.RingRadius);
        }

        public override bool Equals(object obj)
        {
            return obj is HomeYardSitePlan other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = GetLotHashCode(Home);
                hash = (hash * 397) ^ GetLotHashCode(Neighbour);
                hash = (hash * 397) ^
                       DirectionFromHomeToNeighbour.GetHashCode();
                hash = (hash * 397) ^ GroundBounds.GetHashCode();
                hash = (hash * 397) ^ GroundY.GetHashCode();
                hash = (hash * 397) ^ RingCenter.GetHashCode();
                return (hash * 397) ^ RingRadius.GetHashCode();
            }
        }

        public static bool operator ==(
            HomeYardSitePlan left,
            HomeYardSitePlan right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            HomeYardSitePlan left,
            HomeYardSitePlan right)
        {
            return !left.Equals(right);
        }

        private static bool LotsEqual(BuildingLot left, BuildingLot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.Cell == right.Cell &&
                   left.Center.Equals(right.Center) &&
                   left.Size.Equals(right.Size) &&
                   left.Height.Equals(right.Height) &&
                   left.HasBuilding == right.HasBuilding;
        }

        private static int GetLotHashCode(BuildingLot lot)
        {
            if (lot == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = lot.Cell.GetHashCode();
                hash = (hash * 397) ^ lot.Center.GetHashCode();
                hash = (hash * 397) ^ lot.Size.GetHashCode();
                hash = (hash * 397) ^ lot.Height.GetHashCode();
                return (hash * 397) ^ lot.HasBuilding.GetHashCode();
            }
        }
    }

    public static class HomeYardSitePlanner
    {
        public const float MinimumRingRadius = 3.5f;
        public const float PreferredRingRadius = 4.6f;
        public const float RingBoundsMargin = 1.1f;
        public const float WallMargin = 0.6f;

        private static readonly Vector2Int[] SideDirections =
        {
            Vector2Int.left,
            Vector2Int.right
        };

        public static HomeYardSitePlan? Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return TryCreate(layout, out HomeYardSitePlan site)
                ? site
                : (HomeYardSitePlan?)null;
        }

        public static bool TryCreate(
            CityLayout layout,
            out HomeYardSitePlan site)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            site = default;
            BuildingLot home = layout.PlayerHome;
            if (home == null ||
                !TryGetCellSurface(
                    layout,
                    home.Cell,
                    out CitySurfaceDescriptor homeSurface))
            {
                return false;
            }

            bool found = false;
            float bestWidth = 0f;
            Rect bestGround = default;
            BuildingLot bestNeighbour = null;
            Vector2Int bestDirection = default;
            for (int index = 0; index < SideDirections.Length; index++)
            {
                Vector2Int direction = SideDirections[index];
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

                BuildingLot neighbour = FindLot(layout, neighbourCell);
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
                                 WallMargin;
                float maximumX = Mathf.Max(homeFace, neighbourFace) -
                                 WallMargin;
                float minimumZ = Mathf.Max(
                                     homeSurface.WorldBounds.yMin,
                                     neighbourSurface.WorldBounds.yMin) +
                                 WallMargin;
                float maximumZ = Mathf.Min(
                                     homeSurface.WorldBounds.yMax,
                                     neighbourSurface.WorldBounds.yMax) -
                                 WallMargin;
                float width = maximumX - minimumX;
                float depth = maximumZ - minimumZ;
                if (width <= 0f || depth <= 0f || width <= bestWidth)
                {
                    continue;
                }

                bestGround = Rect.MinMaxRect(
                    minimumX,
                    minimumZ,
                    maximumX,
                    maximumZ);
                bestNeighbour = neighbour;
                bestDirection = direction;
                bestWidth = width;
                found = true;
            }

            if (!found)
            {
                return false;
            }

            float radius = Mathf.Min(
                PreferredRingRadius,
                Mathf.Min(bestGround.width, bestGround.height) * 0.5f -
                RingBoundsMargin);
            if (radius < MinimumRingRadius)
            {
                return false;
            }

            float groundY = homeSurface.DatumY +
                            CityElevationPlan.GroundTopOffset;
            var center = new Vector3(
                bestGround.center.x,
                groundY,
                bestGround.center.y);
            site = new HomeYardSitePlan(
                home,
                bestNeighbour,
                bestDirection,
                bestGround,
                groundY,
                center,
                radius);
            return true;
        }

        private static BuildingLot FindLot(
            CityLayout layout,
            Vector2Int cell)
        {
            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                if (layout.BuildingLots[index].Cell == cell)
                {
                    return layout.BuildingLots[index];
                }
            }

            return null;
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
    }

    /// <summary>
    /// Immutable authored-light contract for the yard. It contains no scene
    /// objects, so an always-on or time-driven builder can consume the same
    /// deterministic placement without rediscovering the facade.
    /// </summary>
    public readonly struct HomeYardSpotlightDescriptor :
        IEquatable<HomeYardSpotlightDescriptor>
    {
        internal HomeYardSpotlightDescriptor(
            Vector2Int neighbourCell,
            Vector3 mountPosition,
            Vector3 targetPosition,
            Vector3 facadeNormal,
            Quaternion rotation,
            Color color,
            float intensity,
            float range,
            float spotAngle,
            float innerSpotAngle)
        {
            NeighbourCell = neighbourCell;
            MountPosition = mountPosition;
            TargetPosition = targetPosition;
            FacadeNormal = facadeNormal;
            Rotation = rotation;
            Color = color;
            Intensity = intensity;
            Range = range;
            SpotAngle = spotAngle;
            InnerSpotAngle = innerSpotAngle;
        }

        public Vector2Int NeighbourCell { get; }
        public string StableId => HomeYardSpotlightPlanner.StableId;
        public Vector3 MountPosition { get; }
        public Vector3 TargetPosition { get; }
        public Vector3 AimPosition => TargetPosition;
        public Vector3 FacadeNormal { get; }
        public Quaternion Rotation { get; }
        public Color Color { get; }
        public float Intensity { get; }
        public float Range { get; }
        public float SpotAngle { get; }
        public float InnerSpotAngle { get; }

        public bool Equals(HomeYardSpotlightDescriptor other)
        {
            return NeighbourCell == other.NeighbourCell &&
                   MountPosition.Equals(other.MountPosition) &&
                   TargetPosition.Equals(other.TargetPosition) &&
                   FacadeNormal.Equals(other.FacadeNormal) &&
                   Rotation.Equals(other.Rotation) &&
                   Color.Equals(other.Color) &&
                   Intensity.Equals(other.Intensity) &&
                   Range.Equals(other.Range) &&
                   SpotAngle.Equals(other.SpotAngle) &&
                   InnerSpotAngle.Equals(other.InnerSpotAngle);
        }

        public override bool Equals(object obj)
        {
            return obj is HomeYardSpotlightDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = NeighbourCell.GetHashCode();
                hash = (hash * 397) ^ MountPosition.GetHashCode();
                hash = (hash * 397) ^ TargetPosition.GetHashCode();
                hash = (hash * 397) ^ FacadeNormal.GetHashCode();
                hash = (hash * 397) ^ Rotation.GetHashCode();
                hash = (hash * 397) ^ Color.GetHashCode();
                hash = (hash * 397) ^ Intensity.GetHashCode();
                hash = (hash * 397) ^ Range.GetHashCode();
                hash = (hash * 397) ^ SpotAngle.GetHashCode();
                return (hash * 397) ^ InnerSpotAngle.GetHashCode();
            }
        }

        public static bool operator ==(
            HomeYardSpotlightDescriptor left,
            HomeYardSpotlightDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            HomeYardSpotlightDescriptor left,
            HomeYardSpotlightDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    public static class HomeYardSpotlightPlanner
    {
        public const string StableId = "home-yard-spotlight";
        public const float MountProudOffset = 0.18f;
        public const float FacadeEdgeClearance = 0.75f;
        public const float MinimumMountHeight = 3.2f;
        public const float MountHeightFraction = 0.55f;
        public const float RoofClearance = 0.75f;
        public const float AimHeight = 0.85f;
        // Twenty ordinary street practicals compressed into one deliberately
        // unreal key: the yard must read as a noir stage even at noon after
        // inverse-square falloff across the full circuit.
        public const float Intensity = 240f;

        // Covers the chair's 0.14 m line wander plus its 0.31 m half-track.
        internal const float RadialCoverageMargin = 0.45f;
        internal const float ChairCoverageHeight = 1.9f;
        internal const int CoverageSampleCount = 32;
        internal const float RangeMargin = 3f;
        internal const float RangeMultiplier = 1.5f;
        internal const float ConeFeatherAngle = 6f;
        private const float ConeHalfAngleMargin = 4f;
        private const float MinimumInnerSpotAngle = 40f;

        public static readonly Color SpotlightColor =
            new Color(0.92f, 0.97f, 1f, 1f);

        public static HomeYardSpotlightDescriptor? Create(
            HomeYardSitePlan site)
        {
            return TryCreate(site, out HomeYardSpotlightDescriptor descriptor)
                ? descriptor
                : (HomeYardSpotlightDescriptor?)null;
        }

        public static bool TryCreate(
            HomeYardSitePlan site,
            out HomeYardSpotlightDescriptor descriptor)
        {
            descriptor = default;
            BuildingLot neighbour = site.Neighbour;
            if (neighbour == null ||
                !neighbour.HasBuilding ||
                neighbour.Height <= RoofClearance)
            {
                return false;
            }

            Vector3 normal = site.NeighbourFacadeNormal.normalized;
            if (normal.sqrMagnitude < 0.5f)
            {
                return false;
            }

            bool normalRunsAlongX = Mathf.Abs(normal.x) > 0.5f;
            float normalHalfSize = normalRunsAlongX
                ? neighbour.Size.x * 0.5f
                : neighbour.Size.y * 0.5f;
            float facadeHalfSpan = normalRunsAlongX
                ? neighbour.Size.y * 0.5f
                : neighbour.Size.x * 0.5f;
            var tangent = new Vector3(-normal.z, 0f, normal.x);
            float tangentLimit = Mathf.Max(
                0f,
                facadeHalfSpan - FacadeEdgeClearance);
            float desiredTangentOffset = Vector3.Dot(
                site.RingCenter - neighbour.Center,
                tangent);
            float tangentOffset = Mathf.Clamp(
                desiredTangentOffset,
                -tangentLimit,
                tangentLimit);
            float desiredHeight = Mathf.Max(
                MinimumMountHeight,
                neighbour.Height * MountHeightFraction);
            float mountHeight = Mathf.Min(
                desiredHeight,
                neighbour.Height - RoofClearance);
            Vector3 mountPosition =
                neighbour.Center +
                normal * (normalHalfSize + MountProudOffset) +
                tangent * tangentOffset +
                Vector3.up * mountHeight;
            Vector3 aimPosition =
                site.RingCenter + Vector3.up * AimHeight;
            Vector3 aimVector = aimPosition - mountPosition;
            if (aimVector.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            Vector3 aimDirection = aimVector.normalized;
            float coverageRadius =
                site.RingRadius + RadialCoverageMargin;
            float range = 0f;
            float halfAngle = 0f;
            for (int index = 0; index < CoverageSampleCount; index++)
            {
                float angle = Mathf.PI * 2f * index /
                              CoverageSampleCount;
                var radial = new Vector3(
                    Mathf.Cos(angle) * coverageRadius,
                    0f,
                    Mathf.Sin(angle) * coverageRadius);
                AccumulateCoverage(
                    mountPosition,
                    aimDirection,
                    site.RingCenter + radial,
                    ref range,
                    ref halfAngle);
                AccumulateCoverage(
                    mountPosition,
                    aimDirection,
                    site.RingCenter + radial +
                    Vector3.up * ChairCoverageHeight,
                    ref range,
                    ref halfAngle);
            }

            range = Mathf.Max(
                range + RangeMargin,
                range * RangeMultiplier);
            float spotAngle = Mathf.Clamp(
                (halfAngle + ConeHalfAngleMargin) * 2f,
                2f,
                179f);
            float innerSpotAngle = Mathf.Clamp(
                Mathf.Max(
                    MinimumInnerSpotAngle,
                    spotAngle - ConeFeatherAngle),
                1f,
                spotAngle - 0.1f);
            descriptor = new HomeYardSpotlightDescriptor(
                site.NeighbourCell,
                mountPosition,
                aimPosition,
                normal,
                Quaternion.LookRotation(aimDirection, Vector3.up),
                SpotlightColor,
                Intensity,
                range,
                spotAngle,
                innerSpotAngle);
            return true;
        }

        private static void AccumulateCoverage(
            Vector3 mountPosition,
            Vector3 aimDirection,
            Vector3 sample,
            ref float range,
            ref float halfAngle)
        {
            Vector3 delta = sample - mountPosition;
            range = Mathf.Max(range, delta.magnitude);
            if (delta.sqrMagnitude > 0.0001f)
            {
                halfAngle = Mathf.Max(
                    halfAngle,
                    Vector3.Angle(aimDirection, delta));
            }
        }
    }
}
