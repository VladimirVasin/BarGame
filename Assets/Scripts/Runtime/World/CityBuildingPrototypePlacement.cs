using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityBuildingExteriorFit
    {
        Hidden,
        Crossing,
        Full
    }

    internal readonly struct CityBuildingPrototypePose
    {
        public CityBuildingPrototypePose(
            Vector3 position,
            Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        public Vector3 TransformPoint(Vector3 localPoint)
        {
            return Position + Rotation * localPoint;
        }
    }

    /// <summary>
    /// Pure placement contract shared by City composition, the bounded Home
    /// reconstruction and decoration planning. Prototypes stay at authored
    /// metre scale: their front anchor, rather than their footprint centre,
    /// is the stable connection to a generated lot.
    /// </summary>
    internal static class CityBuildingPrototypePlacement
    {
        public const float ExteriorBoundsPadding = 0.05f;
        public const float BoundsTolerance = 0.002f;

        public static CityBuildingPrototypePose ResolveCityPose(
            BuildingLot lot,
            CityBuildingAssetRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (lot == null || !lot.IsOrdinaryBuilding ||
                registry.District != lot.District)
            {
                throw new ArgumentException(
                    "A City prototype pose requires a matching ordinary lot.",
                    nameof(lot));
            }

            Vector3 frontAnchor = registry.transform.InverseTransformPoint(
                registry.FrontAnchor.position);
            return ResolveCityPose(lot, frontAnchor);
        }

        public static CityBuildingPrototypePose ResolveHomePose(
            BuildingLot home,
            CityBuildingPrototypePose cityPose)
        {
            Vector3 localPosition = PlayerHomeBalconyGeometry.ToHomeLocal(
                home,
                cityPose.Position);
            Vector3 localForward = PlayerHomeBalconyGeometry
                .ToHomeLocalDirection(
                    home,
                    cityPose.Rotation * Vector3.forward)
                .normalized;
            Vector3 localUp = PlayerHomeBalconyGeometry
                .ToHomeLocalDirection(
                    home,
                    cityPose.Rotation * Vector3.up)
                .normalized;
            return new CityBuildingPrototypePose(
                localPosition,
                Quaternion.LookRotation(localForward, localUp));
        }

        public static Bounds ResolveHomeBounds(
            BuildingLot home,
            CityBuildingPrototypePose cityPose,
            Bounds prototypeLocalBounds)
        {
            CityBuildingPrototypePose homePose = ResolveHomePose(
                home,
                cityPose);
            Bounds padded = prototypeLocalBounds;
            padded.Expand(new Vector3(
                ExteriorBoundsPadding * 2f,
                0f,
                ExteriorBoundsPadding * 2f));
            return TransformBounds(padded, homePose);
        }

        public static CityBuildingExteriorFit ClassifyHomeBounds(
            Bounds homeLocalBounds)
        {
            float minimum = HomeExteriorViewBuilder.ExteriorMinimumX;
            if (homeLocalBounds.max.x <= minimum + BoundsTolerance)
            {
                return CityBuildingExteriorFit.Hidden;
            }

            return homeLocalBounds.min.x >= minimum - BoundsTolerance
                ? CityBuildingExteriorFit.Full
                : CityBuildingExteriorFit.Crossing;
        }

        public static Vector3 ResolveRoofAnchor(
            BuildingLot lot,
            CityDecorationKind kind,
            float verticalClearance)
        {
            if (lot == null || !lot.IsOrdinaryBuilding)
            {
                throw new ArgumentException(
                    "A prototype roof anchor requires an ordinary lot.",
                    nameof(lot));
            }

            CityBuildingPrototypePose pose = ResolveCityPose(
                lot,
                ResolveExpectedFrontAnchor(lot.District));
            Vector3 localAnchor = ResolveRoofMount(lot.District, kind);
            return pose.TransformPoint(localAnchor) +
                   Vector3.up * verticalClearance;
        }

        public static Vector3 ResolveFacadeAnchor(
            BuildingLot lot,
            uint lateralSelector)
        {
            if (lot == null || !lot.IsOrdinaryBuilding)
            {
                throw new ArgumentException(
                    "A prototype facade anchor requires an ordinary lot.",
                    nameof(lot));
            }

            CityBuildingPrototypePose pose = ResolveCityPose(
                lot,
                ResolveExpectedFrontAnchor(lot.District));
            Vector3 localMount;
            switch (lot.District)
            {
                case CityDistrictKind.OldTown:
                    localMount = new Vector3(-3.8f, 4.2f, 6.79f);
                    break;
                case CityDistrictKind.Residential:
                    localMount = new Vector3(
                        (lateralSelector & 1u) == 0u ? -4.35f : 4.35f,
                        4.2f,
                        3.69f);
                    break;
                case CityDistrictKind.Industrial:
                    localMount = new Vector3(0f, 4.2f, 6.79f);
                    break;
                case CityDistrictKind.Nightlife:
                    localMount = new Vector3(0f, 4.2f, 6.04f);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(lot),
                        lot.District,
                        "Only ordinary urban districts own prototypes.");
            }

            return pose.TransformPoint(localMount);
        }

        public static Vector3 ResolveForward(BuildingLot lot)
        {
            return ResolveFrontageDirection(lot);
        }

        public static Bounds GetExpectedRoofAttachmentBounds(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return CreateBounds(
                        new Vector3(-6.6f, 27f, -6.35f),
                        new Vector3(6.6f, 42f, 6.35f));
                case CityDistrictKind.Residential:
                    return CreateBounds(
                        new Vector3(-5.35f, 26f, -5.35f),
                        new Vector3(5.35f, 40f, 3.4f));
                case CityDistrictKind.Industrial:
                    return CreateBounds(
                        new Vector3(-6.7f, 24f, -6.45f),
                        new Vector3(6.7f, 30f, 6.45f));
                case CityDistrictKind.Nightlife:
                    return CreateBounds(
                        new Vector3(-5f, 37f, -5.1f),
                        new Vector3(5f, 48f, 4.1f));
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        "Only ordinary urban districts own prototypes.");
            }
        }

        public static Bounds TransformBounds(
            Bounds localBounds,
            CityBuildingPrototypePose pose)
        {
            Vector3 minimum = localBounds.min;
            Vector3 maximum = localBounds.max;
            Vector3 first = pose.TransformPoint(minimum);
            Bounds result = new Bounds(first, Vector3.zero);
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        result.Encapsulate(pose.TransformPoint(new Vector3(
                            x == 0 ? minimum.x : maximum.x,
                            y == 0 ? minimum.y : maximum.y,
                            z == 0 ? minimum.z : maximum.z)));
                    }
                }
            }

            return result;
        }

        private static CityBuildingPrototypePose ResolveCityPose(
            BuildingLot lot,
            Vector3 localFrontAnchor)
        {
            Vector3 forward = ResolveFrontageDirection(lot);
            Quaternion rotation = Quaternion.LookRotation(
                forward,
                Vector3.up);
            Vector3 authoredBase = Vector3.up *
                CityFacadeGrid.MassBaseElevation;
            if (!lot.HasRoadFrontage)
            {
                return new CityBuildingPrototypePose(
                    lot.Center + authoredBase,
                    rotation);
            }

            Vector3 targetFrontAnchor = lot.DoorPosition + authoredBase;
            return new CityBuildingPrototypePose(
                targetFrontAnchor - rotation * localFrontAnchor,
                rotation);
        }

        private static Vector3 ResolveFrontageDirection(BuildingLot lot)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            Vector3 direction = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
            return direction.sqrMagnitude > 0.5f
                ? direction.normalized
                : Vector3.back;
        }

        private static Vector3 ResolveExpectedFrontAnchor(
            CityDistrictKind district)
        {
            Vector3 envelope = CityBuildingAssetProvider
                .GetExpectedEnvelope(district);
            return new Vector3(0f, 0f, envelope.z * 0.5f);
        }

        private static Vector3 ResolveRoofMount(
            CityDistrictKind district,
            CityDecorationKind kind)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    if (kind == CityDecorationKind
                            .OldTownChimneysAndDormers)
                    {
                        // The two chimney feet touch the left gable here;
                        // the dormer is intentionally bedded into its slope.
                        return new Vector3(-3.8f, 31.70f, 0f);
                    }

                    if (kind == CityDecorationKind.OldTownClockTower)
                    {
                        // The four-metre tower base intersects the gable at
                        // its edge height and reads as an integrated cupola.
                        return new Vector3(-3.8f, 32.55f, 0f);
                    }

                    break;
                case CityDistrictKind.Residential:
                    if (kind == CityDecorationKind
                            .ResidentialLaundryAndAntenna)
                    {
                        return new Vector3(-3.6f, 30.30f, -4.0f);
                    }

                    if (kind == CityDecorationKind
                            .ResidentialRooftopGreenhouse)
                    {
                        // The greenhouse is set into the rear deck and the
                        // central stair tower instead of floating above it.
                        return new Vector3(-3.0f, 30.30f, -3.75f);
                    }

                    break;
                case CityDistrictKind.Industrial:
                    if (kind == CityDecorationKind
                            .IndustrialStacksAndTanks)
                    {
                        // Minimum sawtooth height under every stack foot.
                        return new Vector3(0f, 25.25f, 0f);
                    }

                    if (kind == CityDecorationKind.IndustrialGantry)
                    {
                        // The gantry legs are deliberately bedded into the
                        // alternating shed planes at their shared minimum.
                        return new Vector3(0f, 26.75f, 0f);
                    }

                    break;
                case CityDistrictKind.Nightlife:
                    if (kind == CityDecorationKind.NightlifeBillboard)
                    {
                        // Exposed front strip of the lower flat roof, clear
                        // of the upper tower and its pyramid roof.
                        return new Vector3(-0.6f, 37.30f, 4.45f);
                    }

                    break;
            }

            throw new ArgumentException(
                $"Decoration '{kind}' has no authored {district} roof " +
                "mount.",
                nameof(kind));
        }

        private static Bounds CreateBounds(
            Vector3 minimum,
            Vector3 maximum)
        {
            var result = new Bounds();
            result.SetMinMax(minimum, maximum);
            return result;
        }
    }
}
