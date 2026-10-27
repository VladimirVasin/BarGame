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

        public static CityBuildingPrototypePose ResolveExpectedCityPose(BuildingLot lot) =>
            ResolveCityPose(lot, ResolveExpectedFrontAnchor(lot));

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
                ResolveExpectedFrontAnchor(lot));
            Vector3 localAnchor = ResolveVariantRoofMount(lot, kind);
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
                ResolveExpectedFrontAnchor(lot));
            Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(
                lot.District, lot.BuildingVariant);
            float facadeDepth = envelope.z * .5f -
                (lot.District == CityDistrictKind.Residential ? 1.2f : 0f);
            return pose.TransformPoint(new Vector3(
                (lateralSelector & 1u) == 0u ? -envelope.x * .22f : envelope.x * .22f,
                4.2f, facadeDepth + .04f));
        }

        public static Vector3 ResolveForward(BuildingLot lot)
        {
            return ResolveFrontageDirection(lot);
        }

        public static Bounds GetExpectedRoofAttachmentBounds(
            CityDistrictKind district)
        {
            return GetExpectedRoofAttachmentBounds(district, 0);
        }

        public static Bounds GetExpectedRoofAttachmentBounds(CityDistrictKind district, int variantIndex)
        {
            Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(district, variantIndex);
            float halfWidth = envelope.x * .5f;
            float halfDepth = envelope.z * .5f;
            float front = halfDepth - (district == CityDistrictKind.Residential ? 1.2f : 0f);
            float bodyTop = CityBuildingAssetProvider.GetExpectedBodyTop(district, variantIndex);
            return CreateBounds(new Vector3(-halfWidth + .4f, bodyTop,
                    variantIndex == 2 ? .4f : -halfDepth + .4f),
                new Vector3(halfWidth - .4f, envelope.y, front - .4f));
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

            Vector3 direction = lot.FacadeForward;
            return direction.sqrMagnitude > 0.5f
                ? direction.normalized
                : Vector3.back;
        }

        private static Vector3 ResolveExpectedFrontAnchor(BuildingLot lot)
        {
            Vector3 envelope = CityBuildingAssetProvider
                .GetExpectedEnvelope(lot.District, lot.BuildingVariant);
            return new Vector3(0f, 0f, envelope.z * 0.5f);
        }

        private static Vector3 ResolveVariantRoofMount(BuildingLot lot, CityDecorationKind kind)
        {
            Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(
                lot.District, lot.BuildingVariant);
            float bodyTop = CityBuildingAssetProvider.GetExpectedBodyTop(
                lot.District, lot.BuildingVariant);
            // Residential roofs retain an unobstructed front deck. The small
            // stairhead occupies only the back-left corner; objects never float
            // at a height inherited from a retired high-rise prototype.
            if (lot.District == CityDistrictKind.Residential)
                return new Vector3(0f, bodyTop + .18f, envelope.z * .15f);
            float roofRise = envelope.y - bodyTop;
            float mountHeight = bodyTop + roofRise * .55f;
            return new Vector3(0f, mountHeight,
                lot.BuildingVariant == 2 ? envelope.z * .25f : 0f);
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
