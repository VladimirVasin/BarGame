using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityDecorationValidator
    {
        public const int MaximumVariant = 15;
        public const float OpeningClearance = 1.25f;
        public const float LampClearance = 1.0f;
        public const float SignalClearance = 1.25f;

        private const float Tolerance = 0.001f;

        public static void ValidateOrThrow(
            CityDecorationPlan plan,
            CityLayout layout)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            layout.ValidateOrThrow();
            if (plan.Seed != layout.Seed)
            {
                throw new InvalidOperationException(
                    "The decoration plan seed does not match the city layout.");
            }

            if (plan.Count > CityDecorationPlan.MaximumDescriptorCount)
            {
                throw new InvalidOperationException(
                    $"A city decoration plan cannot exceed " +
                    $"{CityDecorationPlan.MaximumDescriptorCount} descriptors.");
            }

            var lotsByCell = new Dictionary<Vector2Int, BuildingLot>(
                layout.BuildingLots.Count);
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                lotsByCell.Add(lot.Cell, lot);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var coreCounts = new Dictionary<Vector2Int, int>();
            var landmarkCounts =
                new Dictionary<CityDistrictKind, int>();
            string previousId = null;
            int parkLandmarkCount = 0;
            int fountainCount = 0;
            int bandstandCount = 0;

            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                ValidateDescriptorData(descriptor, ids);
                if (previousId != null &&
                    string.Compare(
                        previousId,
                        descriptor.StableId,
                        StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException(
                        "Decoration descriptors must have a stable " +
                        "strictly increasing ID order.");
                }

                previousId = descriptor.StableId;
                ValidateKindContract(descriptor);
                ValidateAnchor(
                    descriptor,
                    layout,
                    lotsByCell);

                if (IsOrdinaryLotVisualKind(descriptor.Kind))
                {
                    Increment(coreCounts, descriptor.LotCell);
                }

                if (descriptor.AnchorKind ==
                    CityDecorationAnchorKind.UrbanLandmark)
                {
                    Increment(landmarkCounts, descriptor.District);
                }
                else if (descriptor.AnchorKind ==
                         CityDecorationAnchorKind.ParkLandmark)
                {
                    parkLandmarkCount++;
                    if (descriptor.Kind ==
                        CityDecorationKind.ParkFountainAndStatue)
                    {
                        fountainCount++;
                    }
                    else if (descriptor.Kind ==
                             CityDecorationKind.ParkBandstand)
                    {
                        bandstandCount++;
                    }
                }
            }

            ValidateOrdinaryLotCoverage(layout, coreCounts);
            ValidateLandmarkCoverage(
                layout,
                landmarkCounts,
                parkLandmarkCount,
                fountainCount,
                bandstandCount);
        }

        public static void ValidateOrThrow(
            CityDecorationPlan plan,
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan)
        {
            if (fencePlan == null)
            {
                throw new ArgumentNullException(nameof(fencePlan));
            }

            if (nightPlan == null)
            {
                throw new ArgumentNullException(nameof(nightPlan));
            }

            ValidateOrThrow(plan, layout);
            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (!RequiresGroundProtection(descriptor.AnchorKind))
                {
                    continue;
                }

                float radius = ResolveProtectionRadius(descriptor.Kind);
                if (IsProtectedGroundAnchor(
                        descriptor.Position,
                        radius,
                        fencePlan,
                        nightPlan))
                {
                    throw new InvalidOperationException(
                        $"Decoration '{descriptor.StableId}' overlaps a " +
                        "reserved entrance, gate or night fixture.");
                }
            }
        }

        internal static bool IsProtectedGroundAnchor(
            Vector3 position,
            float objectRadius,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan)
        {
            for (int index = 0;
                 index < fencePlan.Openings.Count;
                 index++)
            {
                RoadFenceOpeningDescriptor opening =
                    fencePlan.Openings[index];
                float clearance =
                    (opening.Width * 0.5f) +
                    OpeningClearance +
                    objectRadius;
                if (PlanarSquaredDistance(position, opening.Center) <
                    clearance * clearance)
                {
                    return true;
                }
            }

            for (int index = 0;
                 index < nightPlan.StreetLamps.Count;
                 index++)
            {
                float clearance = LampClearance + objectRadius;
                if (PlanarSquaredDistance(
                        position,
                        nightPlan.StreetLamps[index].Position) <
                    clearance * clearance)
                {
                    return true;
                }
            }

            for (int index = 0;
                 index < nightPlan.TrafficSignals.Count;
                 index++)
            {
                float clearance = SignalClearance + objectRadius;
                if (PlanarSquaredDistance(
                        position,
                        nightPlan.TrafficSignals[index].Position) <
                    clearance * clearance)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsOrdinaryLotVisualKind(
            CityDecorationKind kind)
        {
            return kind ==
                       CityDecorationKind.OldTownChimneysAndDormers ||
                   kind == CityDecorationKind.OldTownScaffolding ||
                   kind == CityDecorationKind.ResidentialBalconies ||
                   kind ==
                       CityDecorationKind.ResidentialLaundryAndAntenna ||
                   kind ==
                       CityDecorationKind.IndustrialStacksAndTanks ||
                   kind == CityDecorationKind.IndustrialPipeRack ||
                   kind == CityDecorationKind.NightlifeBillboard ||
                   kind == CityDecorationKind.NightlifeFireEscape;
        }

        private static void ValidateDescriptorData(
            CityDecorationDescriptor descriptor,
            ISet<string> ids)
        {
            if (string.IsNullOrWhiteSpace(descriptor.StableId) ||
                !ids.Add(descriptor.StableId))
            {
                throw new InvalidOperationException(
                    "Every decoration requires a unique non-empty stable ID.");
            }

            RequireDefined(descriptor.Kind, "decoration kind");
            RequireDefined(descriptor.AnchorKind, "anchor kind");
            RequireDefined(descriptor.District, "district");
            RequireDefined(descriptor.Palette, "palette");
            RequireDefined(descriptor.VisibilityTier, "visibility tier");
            RequireDefined(descriptor.CollisionTier, "collision tier");
            RequireFinite(descriptor.Position, descriptor.StableId);
            RequireFinite(descriptor.Forward, descriptor.StableId);
            Vector3 planarForward = new Vector3(
                descriptor.Forward.x,
                0f,
                descriptor.Forward.z);
            if (Mathf.Abs(descriptor.Forward.y) > Tolerance ||
                Mathf.Abs(planarForward.sqrMagnitude - 1f) > 0.002f)
            {
                throw new InvalidOperationException(
                    $"Decoration '{descriptor.StableId}' requires a " +
                    "normalized horizontal forward vector.");
            }

            if (descriptor.Variant < 0 ||
                descriptor.Variant > MaximumVariant)
            {
                throw new InvalidOperationException(
                    $"Decoration '{descriptor.StableId}' has an invalid variant.");
            }
        }

        private static void ValidateAnchor(
            CityDecorationDescriptor descriptor,
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, BuildingLot> lotsByCell)
        {
            if (descriptor.HasLotAnchor)
            {
                if (!lotsByCell.TryGetValue(
                        descriptor.LotCell,
                        out BuildingLot lot) ||
                    !lot.HasBuilding ||
                    lot.District != descriptor.District)
                {
                    throw new InvalidOperationException(
                        $"Decoration '{descriptor.StableId}' references " +
                        "an invalid building lot.");
                }

                if (descriptor.AnchorKind !=
                        CityDecorationAnchorKind.UrbanLandmark &&
                    (lot.IsBar ||
                     lot.IsPlayerHome ||
                     lot.IsSupermarket))
                {
                    throw new InvalidOperationException(
                        $"Ordinary decoration '{descriptor.StableId}' " +
                        "cannot replace a special-building visual.");
                }

                if (descriptor.District ==
                    CityDistrictKind.CentralPark)
                {
                    throw new InvalidOperationException(
                        "Building decoration cannot belong to the park.");
                }

                return;
            }

            if (descriptor.LotCell != CityDecorationDescriptor.NoLot)
            {
                throw new InvalidOperationException(
                    $"Non-lot decoration '{descriptor.StableId}' must use " +
                    "the NoLot cell sentinel.");
            }

            if (descriptor.AnchorKind ==
                    CityDecorationAnchorKind.ParkFeature ||
                descriptor.AnchorKind ==
                    CityDecorationAnchorKind.ParkLandmark)
            {
                if (!layout.Park.IsEnabled ||
                    descriptor.District !=
                    CityDistrictKind.CentralPark ||
                    !IsInsideParkRegion(
                        layout.Park,
                        descriptor.Position))
                {
                    throw new InvalidOperationException(
                        $"Park decoration '{descriptor.StableId}' lies " +
                        "outside the planned park.");
                }

                return;
            }

            if (descriptor.AnchorKind !=
                    CityDecorationAnchorKind.Roadside ||
                descriptor.District ==
                    CityDistrictKind.CentralPark)
            {
                throw new InvalidOperationException(
                    $"Decoration '{descriptor.StableId}' has an invalid " +
                    "non-lot anchor.");
            }
        }

        private static bool IsInsideParkRegion(
            CityParkPlan park,
            Vector3 position)
        {
            for (int index = 0; index < park.Regions.Count; index++)
            {
                if (Contains(park.Regions[index].WalkableBounds, position))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateKindContract(
            CityDecorationDescriptor descriptor)
        {
            CityDistrictKind expectedDistrict;
            CityDecorationAnchorKind expectedAnchor;
            switch (descriptor.Kind)
            {
                case CityDecorationKind.OldTownChimneysAndDormers:
                    expectedDistrict = CityDistrictKind.OldTown;
                    expectedAnchor = CityDecorationAnchorKind.BuildingRoof;
                    break;
                case CityDecorationKind.OldTownScaffolding:
                    expectedDistrict = CityDistrictKind.OldTown;
                    expectedAnchor = CityDecorationAnchorKind.BuildingFacade;
                    break;
                case CityDecorationKind.OldTownStreetMarket:
                    expectedDistrict = CityDistrictKind.OldTown;
                    expectedAnchor = CityDecorationAnchorKind.BuildingFrontage;
                    break;
                case CityDecorationKind.OldTownClockTower:
                    expectedDistrict = CityDistrictKind.OldTown;
                    expectedAnchor = CityDecorationAnchorKind.UrbanLandmark;
                    break;
                case CityDecorationKind.ResidentialBalconies:
                    expectedDistrict = CityDistrictKind.Residential;
                    expectedAnchor = CityDecorationAnchorKind.BuildingFacade;
                    break;
                case CityDecorationKind.ResidentialLaundryAndAntenna:
                    expectedDistrict = CityDistrictKind.Residential;
                    expectedAnchor = CityDecorationAnchorKind.BuildingRoof;
                    break;
                case CityDecorationKind.ResidentialDiscardedFurniture:
                    expectedDistrict = CityDistrictKind.Residential;
                    expectedAnchor = CityDecorationAnchorKind.BuildingFrontage;
                    break;
                case CityDecorationKind.ResidentialRooftopGreenhouse:
                    expectedDistrict = CityDistrictKind.Residential;
                    expectedAnchor = CityDecorationAnchorKind.UrbanLandmark;
                    break;
                case CityDecorationKind.IndustrialStacksAndTanks:
                    expectedDistrict = CityDistrictKind.Industrial;
                    expectedAnchor = CityDecorationAnchorKind.BuildingRoof;
                    break;
                case CityDecorationKind.IndustrialPipeRack:
                    expectedDistrict = CityDistrictKind.Industrial;
                    expectedAnchor = CityDecorationAnchorKind.BuildingFacade;
                    break;
                case CityDecorationKind.IndustrialCargo:
                    expectedDistrict = CityDistrictKind.Industrial;
                    expectedAnchor = CityDecorationAnchorKind.BuildingFrontage;
                    break;
                case CityDecorationKind.IndustrialGantry:
                    expectedDistrict = CityDistrictKind.Industrial;
                    expectedAnchor = CityDecorationAnchorKind.UrbanLandmark;
                    break;
                case CityDecorationKind.NightlifeBillboard:
                    expectedDistrict = CityDistrictKind.Nightlife;
                    expectedAnchor = CityDecorationAnchorKind.BuildingRoof;
                    break;
                case CityDecorationKind.NightlifeFireEscape:
                    expectedDistrict = CityDistrictKind.Nightlife;
                    expectedAnchor = CityDecorationAnchorKind.BuildingFacade;
                    break;
                case CityDecorationKind.NightlifeVendingAndQueue:
                    expectedDistrict = CityDistrictKind.Nightlife;
                    expectedAnchor = CityDecorationAnchorKind.BuildingFrontage;
                    break;
                case CityDecorationKind.NightlifeCinema:
                    expectedDistrict = CityDistrictKind.Nightlife;
                    expectedAnchor = CityDecorationAnchorKind.UrbanLandmark;
                    break;
                case CityDecorationKind.RoadsideDumpsterAndUtility:
                case CityDecorationKind.RoadsidePhoneBooth:
                case CityDecorationKind.RoadsideBusShelter:
                case CityDecorationKind.RoadsideRoadworkAndBicycle:
                    expectedDistrict = descriptor.District;
                    expectedAnchor = CityDecorationAnchorKind.Roadside;
                    break;
                case CityDecorationKind.ParkFountainAndStatue:
                case CityDecorationKind.ParkBandstand:
                    expectedDistrict = CityDistrictKind.CentralPark;
                    expectedAnchor = CityDecorationAnchorKind.ParkLandmark;
                    break;
                case CityDecorationKind.ParkChessTables:
                case CityDecorationKind.ParkPlayground:
                    expectedDistrict = CityDistrictKind.CentralPark;
                    expectedAnchor = CityDecorationAnchorKind.ParkFeature;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported decoration kind '{descriptor.Kind}'.");
            }

            if (descriptor.District != expectedDistrict ||
                descriptor.AnchorKind != expectedAnchor)
            {
                throw new InvalidOperationException(
                    $"Decoration '{descriptor.StableId}' does not match " +
                    "its kind's district/anchor contract.");
            }
        }

        private static void ValidateOrdinaryLotCoverage(
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, int> coreCounts)
        {
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.HasBuilding ||
                    lot.IsBar ||
                    lot.IsPlayerHome ||
                    lot.IsSupermarket)
                {
                    continue;
                }

                int count = coreCounts.TryGetValue(
                    lot.Cell,
                    out int actual)
                    ? actual
                    : 0;
                if (count != 1)
                {
                    throw new InvalidOperationException(
                        $"Ordinary lot {lot.Cell} requires exactly one " +
                        "district visual descriptor.");
                }
            }
        }

        private static void ValidateLandmarkCoverage(
            CityLayout layout,
            IReadOnlyDictionary<CityDistrictKind, int> landmarkCounts,
            int parkLandmarkCount,
            int fountainCount,
            int bandstandCount)
        {
            var validatedArchetypes = new HashSet<CityDistrictKind>();
            for (int index = 0; index < layout.Districts.Count; index++)
            {
                CityDistrictKind district = layout.Districts[index].Kind;
                if (district == CityDistrictKind.CentralPark ||
                    !validatedArchetypes.Add(district))
                {
                    continue;
                }

                int count = landmarkCounts.TryGetValue(
                    district,
                    out int actual)
                    ? actual
                    : 0;
                if (count != 1)
                {
                    throw new InvalidOperationException(
                        $"Urban district '{district}' requires exactly " +
                        "one landmark.");
                }
            }

            int expectedParkLandmarks = layout.Park.IsEnabled ? 2 : 0;
            if (parkLandmarkCount != expectedParkLandmarks ||
                fountainCount != (layout.Park.IsEnabled ? 1 : 0) ||
                bandstandCount != (layout.Park.IsEnabled ? 1 : 0))
            {
                throw new InvalidOperationException(
                    "An enabled park requires one fountain/statue and " +
                    "one bandstand landmark.");
            }
        }

        private static bool RequiresGroundProtection(
            CityDecorationAnchorKind kind)
        {
            return kind == CityDecorationAnchorKind.BuildingFrontage ||
                   kind == CityDecorationAnchorKind.Roadside ||
                   kind == CityDecorationAnchorKind.ParkFeature ||
                   kind == CityDecorationAnchorKind.ParkLandmark;
        }

        internal static float ResolveProtectionRadius(
            CityDecorationKind kind)
        {
            switch (kind)
            {
                case CityDecorationKind.OldTownStreetMarket:
                    return 2.8f;
                case CityDecorationKind.ResidentialDiscardedFurniture:
                    return 2.5f;
                case CityDecorationKind.IndustrialCargo:
                    return 3.5f;
                case CityDecorationKind.NightlifeVendingAndQueue:
                    return 2.8f;
                case CityDecorationKind.RoadsideDumpsterAndUtility:
                    return 2.2f;
                case CityDecorationKind.RoadsidePhoneBooth:
                    return 1.25f;
                case CityDecorationKind.RoadsideBusShelter:
                    return 3.1f;
                case CityDecorationKind.RoadsideRoadworkAndBicycle:
                    return 2.4f;
                case CityDecorationKind.ParkFountainAndStatue:
                    return 3.4f;
                case CityDecorationKind.ParkBandstand:
                    return 5f;
                case CityDecorationKind.ParkChessTables:
                    return 2.8f;
                case CityDecorationKind.ParkPlayground:
                    // The frame is small; what needs the room is the
                    // bench pushed clear of the swing arc behind it.
                    return 3.9f;
                default:
                    return 0.75f;
            }
        }

        private static void RequireDefined<T>(T value, string label)
            where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new InvalidOperationException(
                    $"Decoration has an unsupported {label} '{value}'.");
            }
        }

        private static void RequireFinite(Vector3 value, string id)
        {
            if (!IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z))
            {
                throw new InvalidOperationException(
                    $"Decoration '{id}' contains a non-finite transform.");
            }
        }

        private static bool Contains(Rect bounds, Vector3 point)
        {
            return point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.z >= bounds.yMin - Tolerance &&
                   point.z <= bounds.yMax + Tolerance;
        }

        private static float PlanarSquaredDistance(
            Vector3 first,
            Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return (x * x) + (z * z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Increment<T>(
            IDictionary<T, int> counts,
            T key)
        {
            counts[key] = counts.TryGetValue(key, out int count)
                ? count + 1
                : 1;
        }
    }
}
