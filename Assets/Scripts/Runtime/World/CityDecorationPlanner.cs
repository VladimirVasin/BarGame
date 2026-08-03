using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityDecorationPlanner
    {
        public const float MinimumFrontageDepth = 1.25f;
        public const float MinimumFrontageSpan = 5.2f;
        public const int MaximumRoadsideClusters = 24;

        private const uint CoreKindSalt = 0x434F5245u;
        private const uint CoreVariantSalt = 0x43564152u;
        private const uint CorePaletteSalt = 0x4350414Cu;
        private const uint FrontageSelectionSalt = 0x4653454Cu;
        private const uint FrontageSideSalt = 0x46534944u;
        private const uint FrontageVariantSalt = 0x46564152u;
        private const uint FrontagePaletteSalt = 0x4650414Cu;
        private const uint LandmarkLotSalt = 0x4C4D4C54u;
        private const uint LandmarkVariantSalt = 0x4C4D5652u;
        private const uint RoadsideRankSalt = 0x5252414Eu;
        private const uint RoadsideKindSalt = 0x524B494Eu;
        private const uint RoadsideSideSalt = 0x52534944u;
        private const uint RoadsideVariantSalt = 0x52564152u;
        private const uint ParkLandmarkVariantSalt = 0x504C5652u;
        private const uint ParkFeatureVariantSalt = 0x50465652u;

        public static CityDecorationPlan CreatePlan(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (fencePlan == null)
            {
                throw new ArgumentNullException(nameof(fencePlan));
            }

            if (nightPlan == null)
            {
                throw new ArgumentNullException(nameof(nightPlan));
            }

            layout.ValidateOrThrow();
            List<BuildingLot> ordinaryLots = CollectOrdinaryLots(layout);
            int mandatoryCount = checked(
                ordinaryLots.Count +
                CountUrbanDistricts(layout) +
                (layout.Park.IsEnabled ? 2 : 0));
            if (mandatoryCount >
                CityDecorationPlan.MaximumDescriptorCount)
            {
                throw new InvalidOperationException(
                    "The city contains more mandatory lot visuals and " +
                    "landmarks than the decoration budget permits.");
            }

            var descriptors = new List<CityDecorationDescriptor>(
                Mathf.Min(
                    CityDecorationPlan.MaximumDescriptorCount,
                    mandatoryCount + ordinaryLots.Count));
            var occupiedGroundPositions = new List<Vector3>();

            AddOrdinaryLotVisuals(layout, ordinaryLots, descriptors);
            AddUrbanLandmarks(layout, descriptors);
            AddParkLandmarks(
                layout,
                fencePlan,
                nightPlan,
                descriptors,
                occupiedGroundPositions);
            AddFrontageClusters(
                layout,
                ordinaryLots,
                fencePlan,
                nightPlan,
                descriptors,
                occupiedGroundPositions);
            AddParkFeatures(
                layout,
                fencePlan,
                nightPlan,
                descriptors,
                occupiedGroundPositions);
            AddRoadsideClusters(
                layout,
                ordinaryLots,
                fencePlan,
                nightPlan,
                descriptors,
                occupiedGroundPositions);

            var plan = new CityDecorationPlan(layout.Seed, descriptors);
            CityDecorationValidator.ValidateOrThrow(
                plan,
                layout,
                fencePlan,
                nightPlan);
            return plan;
        }

        private static List<BuildingLot> CollectOrdinaryLots(
            CityLayout layout)
        {
            var result = new List<BuildingLot>(
                layout.BuildingLots.Count);
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (lot.HasBuilding &&
                    !lot.IsBar &&
                    !lot.IsPlayerHome &&
                    !lot.IsSupermarket)
                {
                    result.Add(lot);
                }
            }

            result.Sort(CompareLotsRowMajor);
            return result;
        }

        private static int CountUrbanDistricts(CityLayout layout)
        {
            int count = 0;
            for (int index = 0; index < layout.Districts.Count; index++)
            {
                if (layout.Districts[index].Kind !=
                    CityDistrictKind.CentralPark)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AddOrdinaryLotVisuals(
            CityLayout layout,
            IReadOnlyList<BuildingLot> lots,
            ICollection<CityDecorationDescriptor> target)
        {
            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                uint kindHash = StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    CoreKindSalt);
                CityDecorationKind kind = ResolveCoreKind(
                    lot.District,
                    (kindHash & 1u) != 0u);
                CityDecorationAnchorKind anchorKind =
                    ResolveCoreAnchorKind(kind);
                Vector3 forward = ResolveLotForward(
                    layout.Seed,
                    lot,
                    CoreKindSalt);
                Vector3 position = anchorKind ==
                    CityDecorationAnchorKind.BuildingRoof
                    ? lot.Center +
                      (Vector3.up * (lot.Height + 0.18f))
                    : CreateFacadeAnchor(lot, forward);
                uint paletteHash = StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    CorePaletteSalt);

                target.Add(new CityDecorationDescriptor(
                    CreateLotId(layout.Seed, lot.Cell, "core"),
                    kind,
                    anchorKind,
                    lot.District,
                    lot.Cell,
                    position,
                    forward,
                    HashToVariant(StableHash(
                        layout.Seed,
                        lot.Cell.x,
                        lot.Cell.y,
                        CoreVariantSalt)),
                    ResolveDistrictPalette(lot.District, paletteHash),
                    anchorKind == CityDecorationAnchorKind.BuildingRoof
                        ? CityDecorationVisibilityTier.Landmark
                        : CityDecorationVisibilityTier.MidRange,
                    CityDecorationCollisionTier.None));
            }
        }

        private static void AddUrbanLandmarks(
            CityLayout layout,
            ICollection<CityDecorationDescriptor> target)
        {
            for (int index = 0; index < layout.Districts.Count; index++)
            {
                CityDistrictKind district = layout.Districts[index].Kind;
                if (district == CityDistrictKind.CentralPark)
                {
                    continue;
                }

                BuildingLot lot = SelectLandmarkLot(layout, district);
                CityDecorationKind kind = ResolveLandmarkKind(district);
                Vector3 forward = ResolveLotForward(
                    layout.Seed,
                    lot,
                    LandmarkLotSalt);
                Vector3 position = kind ==
                    CityDecorationKind.NightlifeCinema
                    ? CreateFacadeAnchor(lot, forward)
                    : lot.Center +
                      (Vector3.up * (lot.Height + 0.22f));
                uint variantHash = StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    LandmarkVariantSalt);

                target.Add(new CityDecorationDescriptor(
                    CreateDistrictId(layout.Seed, district, "landmark"),
                    kind,
                    CityDecorationAnchorKind.UrbanLandmark,
                    district,
                    lot.Cell,
                    position,
                    forward,
                    HashToVariant(variantHash),
                    ResolveDistrictPalette(district, variantHash),
                    CityDecorationVisibilityTier.Landmark,
                    CityDecorationCollisionTier.None));
            }
        }

        private static BuildingLot SelectLandmarkLot(
            CityLayout layout,
            CityDistrictKind district)
        {
            if (layout.TryGetPrimaryLandmarkCell(
                    district,
                    out Vector2Int reservedCell))
            {
                for (int index = 0;
                     index < layout.BuildingLots.Count;
                     index++)
                {
                    BuildingLot reservedLot = layout.BuildingLots[index];
                    if (reservedLot.Cell == reservedCell &&
                        reservedLot.HasBuilding &&
                        reservedLot.District == district)
                    {
                        return reservedLot;
                    }
                }

                throw new InvalidOperationException(
                    $"District '{district}' primary landmark reservation " +
                    $"{reservedCell} does not resolve to a building lot.");
            }

            var candidates = new List<RankedLot>();
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.HasBuilding || lot.District != district)
                {
                    continue;
                }

                uint rank = StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    (uint)district,
                    LandmarkLotSalt);
                candidates.Add(new RankedLot(
                    lot,
                    lot.IsBar ||
                    lot.IsPlayerHome ||
                    lot.IsSupermarket
                        ? 1
                        : 0,
                    rank));
            }

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"District '{district}' has no building for its landmark.");
            }

            candidates.Sort(CompareRankedLots);
            return candidates[0].Lot;
        }

        private static void AddFrontageClusters(
            CityLayout layout,
            IReadOnlyList<BuildingLot> lots,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<CityDecorationDescriptor> target,
            ICollection<Vector3> occupiedGroundPositions)
        {
            for (int index = 0;
                 index < lots.Count &&
                 target.Count < CityDecorationPlan.MaximumDescriptorCount;
                 index++)
            {
                BuildingLot lot = lots[index];
                uint selectionHash = StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    FrontageSelectionSalt);
                if ((selectionHash % 100u) >= 56u)
                {
                    continue;
                }

                float side = (StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    FrontageSideSalt) & 1u) == 0u
                    ? -0.26f
                    : 0.26f;
                CityDecorationKind kind =
                    ResolveFrontageKind(lot.District);
                if (!TryCreateClearFrontageAnchor(
                        layout,
                        lot,
                        side,
                        CityDecorationValidator
                            .ResolveProtectionRadius(kind),
                        fencePlan,
                        nightPlan,
                        occupiedGroundPositions,
                        out Vector3 position,
                        out Vector3 forward))
                {
                    continue;
                }

                uint variantHash = StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    FrontageVariantSalt);
                target.Add(new CityDecorationDescriptor(
                    CreateLotId(layout.Seed, lot.Cell, "frontage"),
                    kind,
                    CityDecorationAnchorKind.BuildingFrontage,
                    lot.District,
                    lot.Cell,
                    position,
                    forward,
                    HashToVariant(variantHash),
                    ResolveDistrictPalette(
                        lot.District,
                        StableHash(
                            layout.Seed,
                            lot.Cell.x,
                            lot.Cell.y,
                            FrontagePaletteSalt)),
                    CityDecorationVisibilityTier.Near,
                    CityDecorationCollisionTier.None));
                occupiedGroundPositions.Add(position);
            }
        }

        private static void AddRoadsideClusters(
            CityLayout layout,
            IReadOnlyList<BuildingLot> lots,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<CityDecorationDescriptor> target,
            ICollection<Vector3> occupiedGroundPositions)
        {
            var candidates = new List<RankedLot>(lots.Count);
            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                candidates.Add(new RankedLot(
                    lot,
                    0,
                    StableHash(
                        layout.Seed,
                        lot.Cell.x,
                        lot.Cell.y,
                        RoadsideRankSalt)));
            }

            candidates.Sort(CompareRankedLots);
            int targetCount = Mathf.Min(
                MaximumRoadsideClusters,
                Mathf.Max(4, lots.Count / 6));
            int added = 0;
            for (int index = 0;
                 index < candidates.Count &&
                 added < targetCount &&
                 target.Count < CityDecorationPlan.MaximumDescriptorCount;
                 index++)
            {
                BuildingLot lot = candidates[index].Lot;
                float side = (StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    RoadsideSideSalt) & 1u) == 0u
                    ? -0.36f
                    : 0.36f;
                uint kindHash = StableHash(
                    layout.Seed,
                    lot.Cell.x,
                    lot.Cell.y,
                    RoadsideKindSalt);
                CityDecorationKind kind =
                    ResolveRoadsideKind(kindHash);
                if (!TryCreateClearFrontageAnchor(
                        layout,
                        lot,
                        side,
                        CityDecorationValidator
                            .ResolveProtectionRadius(kind),
                        fencePlan,
                        nightPlan,
                        occupiedGroundPositions,
                        out Vector3 position,
                        out Vector3 forward))
                {
                    continue;
                }

                target.Add(new CityDecorationDescriptor(
                    CreateLotId(layout.Seed, lot.Cell, "roadside"),
                    kind,
                    CityDecorationAnchorKind.Roadside,
                    lot.District,
                    CityDecorationDescriptor.NoLot,
                    position,
                    forward,
                    HashToVariant(StableHash(
                        layout.Seed,
                        lot.Cell.x,
                        lot.Cell.y,
                        RoadsideVariantSalt)),
                    CityDecorationPalette.StreetUtility,
                    kind ==
                        CityDecorationKind.RoadsideRoadworkAndBicycle
                        ? CityDecorationVisibilityTier.Near
                        : CityDecorationVisibilityTier.MidRange,
                    CityDecorationCollisionTier.None));
                occupiedGroundPositions.Add(position);
                added++;
            }
        }

        private static void AddParkLandmarks(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<CityDecorationDescriptor> target,
            ICollection<Vector3> occupiedGroundPositions)
        {
            if (!layout.Park.IsEnabled)
            {
                return;
            }

            AddParkDecoration(
                layout,
                fencePlan,
                nightPlan,
                target,
                occupiedGroundPositions,
                CityDecorationKind.ParkFountainAndStatue,
                CityDecorationAnchorKind.ParkLandmark,
                -1,
                1,
                CityDecorationValidator.ResolveProtectionRadius(
                    CityDecorationKind.ParkFountainAndStatue),
                CityDecorationPalette.ParkStone,
                CityDecorationVisibilityTier.Landmark,
                CityDecorationCollisionTier.None,
                ParkLandmarkVariantSalt);
            AddParkDecoration(
                layout,
                fencePlan,
                nightPlan,
                target,
                occupiedGroundPositions,
                CityDecorationKind.ParkBandstand,
                CityDecorationAnchorKind.ParkLandmark,
                1,
                -1,
                CityDecorationValidator.ResolveProtectionRadius(
                    CityDecorationKind.ParkBandstand),
                CityDecorationPalette.ParkPaint,
                CityDecorationVisibilityTier.Landmark,
                CityDecorationCollisionTier.None,
                ParkLandmarkVariantSalt ^ 0x9E3779B9u);
        }

        private static void AddParkFeatures(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<CityDecorationDescriptor> target,
            ICollection<Vector3> occupiedGroundPositions)
        {
            if (!layout.Park.IsEnabled)
            {
                return;
            }

            if (target.Count < CityDecorationPlan.MaximumDescriptorCount)
            {
                TryAddParkDecoration(
                    layout,
                    fencePlan,
                    nightPlan,
                    target,
                    occupiedGroundPositions,
                    CityDecorationKind.ParkChessTables,
                    CityDecorationAnchorKind.ParkFeature,
                    -1,
                    -1,
                    CityDecorationValidator.ResolveProtectionRadius(
                        CityDecorationKind.ParkChessTables),
                    CityDecorationPalette.ParkStone,
                    CityDecorationVisibilityTier.Near,
                    CityDecorationCollisionTier.None,
                    ParkFeatureVariantSalt);
            }

            if (target.Count < CityDecorationPlan.MaximumDescriptorCount)
            {
                TryAddParkDecoration(
                    layout,
                    fencePlan,
                    nightPlan,
                    target,
                    occupiedGroundPositions,
                    CityDecorationKind.ParkPlayground,
                    CityDecorationAnchorKind.ParkFeature,
                    1,
                    1,
                    CityDecorationValidator.ResolveProtectionRadius(
                        CityDecorationKind.ParkPlayground),
                    CityDecorationPalette.ParkPaint,
                    CityDecorationVisibilityTier.MidRange,
                    CityDecorationCollisionTier.None,
                    ParkFeatureVariantSalt ^ 0x85EBCA6Bu);
            }
        }

        private static void AddParkDecoration(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<CityDecorationDescriptor> target,
            ICollection<Vector3> occupiedGroundPositions,
            CityDecorationKind kind,
            CityDecorationAnchorKind anchorKind,
            int xSign,
            int zSign,
            float radius,
            CityDecorationPalette palette,
            CityDecorationVisibilityTier visibilityTier,
            CityDecorationCollisionTier collisionTier,
            uint variantSalt)
        {
            if (!TryAddParkDecoration(
                    layout,
                    fencePlan,
                    nightPlan,
                    target,
                    occupiedGroundPositions,
                    kind,
                    anchorKind,
                    xSign,
                    zSign,
                    radius,
                    palette,
                    visibilityTier,
                    collisionTier,
                    variantSalt))
            {
                throw new InvalidOperationException(
                    $"The park has no protected placement for '{kind}'.");
            }
        }

        private static bool TryAddParkDecoration(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<CityDecorationDescriptor> target,
            ICollection<Vector3> occupiedGroundPositions,
            CityDecorationKind kind,
            CityDecorationAnchorKind anchorKind,
            int xSign,
            int zSign,
            float radius,
            CityDecorationPalette palette,
            CityDecorationVisibilityTier visibilityTier,
            CityDecorationCollisionTier collisionTier,
            uint variantSalt)
        {
            if (!TryFindParkPosition(
                    layout,
                    fencePlan,
                    nightPlan,
                    occupiedGroundPositions,
                    xSign,
                    zSign,
                    radius,
                    out Vector3 position))
            {
                return false;
            }

            Vector3 forward = layout.Park.Center - position;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f
                ? forward.normalized
                : Vector3.forward;
            target.Add(new CityDecorationDescriptor(
                CreateParkId(layout.Seed, kind),
                kind,
                anchorKind,
                CityDistrictKind.CentralPark,
                CityDecorationDescriptor.NoLot,
                position,
                forward,
                HashToVariant(StableHash(
                    layout.Seed,
                    (int)kind,
                    0,
                    variantSalt)),
                palette,
                visibilityTier,
                collisionTier));
            occupiedGroundPositions.Add(position);
            return true;
        }

        private static bool TryFindParkPosition(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<Vector3> occupiedGroundPositions,
            int xSign,
            int zSign,
            float radius,
            out Vector3 position)
        {
            Rect bounds = layout.Park.WalkableBounds;
            float margin = radius + 0.75f;
            float[] fractions = { 0.30f, 0.38f, 0.22f, 0.44f, 0.16f };
            for (int xIndex = 0; xIndex < fractions.Length; xIndex++)
            {
                for (int zIndex = 0; zIndex < fractions.Length; zIndex++)
                {
                    Vector3 candidate = layout.Park.Center + new Vector3(
                        xSign * bounds.width * fractions[xIndex],
                        0f,
                        zSign * bounds.height * fractions[zIndex]);
                    if (candidate.x < bounds.xMin + margin ||
                        candidate.x > bounds.xMax - margin ||
                        candidate.z < bounds.yMin + margin ||
                        candidate.z > bounds.yMax - margin ||
                        !IsParkCandidateClear(
                            layout,
                            candidate,
                            radius,
                            fencePlan,
                            nightPlan,
                            occupiedGroundPositions))
                    {
                        continue;
                    }

                    position = candidate;
                    return true;
                }
            }

            position = default;
            return false;
        }

        private static bool IsParkCandidateClear(
            CityLayout layout,
            Vector3 position,
            float radius,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<Vector3> occupiedGroundPositions)
        {
            float pathClearance = Mathf.Min(
                5.4f + radius,
                Mathf.Min(
                    layout.Park.WalkableBounds.width,
                    layout.Park.WalkableBounds.height) * 0.18f);
            Vector3 centerOffset = position - layout.Park.Center;
            if (Mathf.Abs(centerOffset.x) < pathClearance ||
                Mathf.Abs(centerOffset.z) < pathClearance ||
                CityDecorationValidator.IsProtectedGroundAnchor(
                    position,
                    radius,
                    fencePlan,
                    nightPlan) ||
                !IsSeparated(position, occupiedGroundPositions, radius + 3f))
            {
                return false;
            }

            for (int index = 0;
                 index < layout.Park.TreePositions.Count;
                 index++)
            {
                if (PlanarSquaredDistance(
                        position,
                        layout.Park.TreePositions[index]) <
                    (radius + 1.8f) * (radius + 1.8f))
                {
                    return false;
                }
            }

            for (int index = 0;
                 index < layout.Park.BenchPositions.Count;
                 index++)
            {
                if (PlanarSquaredDistance(
                        position,
                        layout.Park.BenchPositions[index]) <
                    (radius + 1.25f) * (radius + 1.25f))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryCreateClearFrontageAnchor(
            CityLayout layout,
            BuildingLot lot,
            float lateralFraction,
            float objectRadius,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<Vector3> occupiedGroundPositions,
            out Vector3 position,
            out Vector3 forward)
        {
            float[] attempts =
            {
                lateralFraction,
                -lateralFraction,
                0f
            };
            for (int index = 0; index < attempts.Length; index++)
            {
                if (!TryCreateFrontageAnchor(
                        layout,
                        lot,
                        attempts[index],
                        out position,
                        out forward) ||
                    CityDecorationValidator.IsProtectedGroundAnchor(
                        position,
                        objectRadius,
                        fencePlan,
                        nightPlan) ||
                    !IsSeparated(
                        position,
                        occupiedGroundPositions,
                        objectRadius + 1.35f))
                {
                    continue;
                }

                return true;
            }

            position = default;
            forward = default;
            return false;
        }

        private static bool TryCreateFrontageAnchor(
            CityLayout layout,
            BuildingLot lot,
            float lateralFraction,
            out Vector3 position,
            out Vector3 forward)
        {
            forward = ResolveLotForward(
                layout.Seed,
                lot,
                FrontageSideSalt);
            bool frontageIsX = Mathf.Abs(forward.x) > 0.5f;
            float buildingHalfDepth = frontageIsX
                ? lot.Size.x * 0.5f
                : lot.Size.y * 0.5f;
            float centerToRoad = frontageIsX
                ? layout.NodeSpacing.x * 0.5f
                : layout.NodeSpacing.y * 0.5f;
            float clearance =
                centerToRoad -
                (layout.RoadWidth * 0.5f) -
                buildingHalfDepth;
            float parallelSpan = frontageIsX ? lot.Size.y : lot.Size.x;
            if (clearance < MinimumFrontageDepth ||
                parallelSpan < MinimumFrontageSpan)
            {
                position = default;
                return false;
            }

            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            float lateralLimit = Mathf.Max(
                0f,
                (parallelSpan * 0.5f) - 1.5f);
            float lateral = Mathf.Clamp(
                parallelSpan * lateralFraction,
                -lateralLimit,
                lateralLimit);
            float depth = Mathf.Min(0.82f, clearance * 0.5f);
            position =
                lot.Center +
                (forward * (buildingHalfDepth + depth)) +
                (right * lateral);
            position.y = 0f;
            return true;
        }

        private static Vector3 CreateFacadeAnchor(
            BuildingLot lot,
            Vector3 forward)
        {
            float halfDepth = Mathf.Abs(forward.x) > 0.5f
                ? lot.Size.x * 0.5f
                : lot.Size.y * 0.5f;
            return lot.Center +
                   (forward * (halfDepth + 0.04f)) +
                   (Vector3.up * Mathf.Min(4.2f, lot.Height * 0.58f));
        }

        private static Vector3 ResolveLotForward(
            int seed,
            BuildingLot lot,
            uint fallbackSalt)
        {
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
            if (forward.sqrMagnitude > 0.5f)
            {
                return forward.normalized;
            }

            switch (StableHash(
                        seed,
                        lot.Cell.x,
                        lot.Cell.y,
                        fallbackSalt) % 4u)
            {
                case 0u:
                    return Vector3.right;
                case 1u:
                    return Vector3.forward;
                case 2u:
                    return Vector3.left;
                default:
                    return Vector3.back;
            }
        }

        private static CityDecorationKind ResolveCoreKind(
            CityDistrictKind district,
            bool alternate)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return alternate
                        ? CityDecorationKind.OldTownScaffolding
                        : CityDecorationKind.OldTownChimneysAndDormers;
                case CityDistrictKind.Residential:
                    return alternate
                        ? CityDecorationKind.ResidentialLaundryAndAntenna
                        : CityDecorationKind.ResidentialBalconies;
                case CityDistrictKind.Industrial:
                    return alternate
                        ? CityDecorationKind.IndustrialPipeRack
                        : CityDecorationKind.IndustrialStacksAndTanks;
                case CityDistrictKind.Nightlife:
                    return alternate
                        ? CityDecorationKind.NightlifeFireEscape
                        : CityDecorationKind.NightlifeBillboard;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported urban district '{district}'.");
            }
        }

        private static CityDecorationAnchorKind ResolveCoreAnchorKind(
            CityDecorationKind kind)
        {
            switch (kind)
            {
                case CityDecorationKind.OldTownChimneysAndDormers:
                case CityDecorationKind.ResidentialLaundryAndAntenna:
                case CityDecorationKind.IndustrialStacksAndTanks:
                case CityDecorationKind.NightlifeBillboard:
                    return CityDecorationAnchorKind.BuildingRoof;
                default:
                    return CityDecorationAnchorKind.BuildingFacade;
            }
        }

        private static CityDecorationKind ResolveFrontageKind(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return CityDecorationKind.OldTownStreetMarket;
                case CityDistrictKind.Residential:
                    return CityDecorationKind.ResidentialDiscardedFurniture;
                case CityDistrictKind.Industrial:
                    return CityDecorationKind.IndustrialCargo;
                case CityDistrictKind.Nightlife:
                    return CityDecorationKind.NightlifeVendingAndQueue;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported frontage district '{district}'.");
            }
        }

        private static CityDecorationKind ResolveLandmarkKind(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return CityDecorationKind.OldTownClockTower;
                case CityDistrictKind.Residential:
                    return CityDecorationKind.ResidentialRooftopGreenhouse;
                case CityDistrictKind.Industrial:
                    return CityDecorationKind.IndustrialGantry;
                case CityDistrictKind.Nightlife:
                    return CityDecorationKind.NightlifeCinema;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported landmark district '{district}'.");
            }
        }

        private static CityDecorationKind ResolveRoadsideKind(uint hash)
        {
            switch (hash % 4u)
            {
                case 0u:
                    return CityDecorationKind.RoadsideDumpsterAndUtility;
                case 1u:
                    return CityDecorationKind.RoadsidePhoneBooth;
                case 2u:
                    return CityDecorationKind.RoadsideBusShelter;
                default:
                    return CityDecorationKind.RoadsideRoadworkAndBicycle;
            }
        }

        private static CityDecorationPalette ResolveDistrictPalette(
            CityDistrictKind district,
            uint hash)
        {
            bool alternate = (hash & 1u) != 0u;
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return alternate
                        ? CityDecorationPalette.OldTownStone
                        : CityDecorationPalette.OldTownBrick;
                case CityDistrictKind.Residential:
                    return alternate
                        ? CityDecorationPalette.ResidentialWarm
                        : CityDecorationPalette.ResidentialCool;
                case CityDistrictKind.Industrial:
                    return alternate
                        ? CityDecorationPalette.IndustrialRust
                        : CityDecorationPalette.IndustrialSteel;
                case CityDistrictKind.Nightlife:
                    return alternate
                        ? CityDecorationPalette.NightlifeCyan
                        : CityDecorationPalette.NightlifeMagenta;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported decoration palette district '{district}'.");
            }
        }

        private static bool IsSeparated(
            Vector3 position,
            ICollection<Vector3> occupied,
            float minimumDistance)
        {
            float squared = minimumDistance * minimumDistance;
            foreach (Vector3 other in occupied)
            {
                if (PlanarSquaredDistance(position, other) < squared)
                {
                    return false;
                }
            }

            return true;
        }

        private static float PlanarSquaredDistance(
            Vector3 first,
            Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return (x * x) + (z * z);
        }

        private static int HashToVariant(uint hash)
        {
            return (int)(hash % 4u);
        }

        private static string CreateLotId(
            int seed,
            Vector2Int cell,
            string role)
        {
            return $"city-decor-{unchecked((uint)seed):x8}-" +
                   $"lot-{cell.x:D3}-{cell.y:D3}-{role}";
        }

        private static string CreateDistrictId(
            int seed,
            CityDistrictKind district,
            string role)
        {
            return $"city-decor-{unchecked((uint)seed):x8}-" +
                   $"district-{(int)district:D2}-{role}";
        }

        private static string CreateParkId(
            int seed,
            CityDecorationKind kind)
        {
            return $"city-decor-{unchecked((uint)seed):x8}-" +
                   $"park-{(int)kind:D2}";
        }

        private static int CompareLotsRowMajor(
            BuildingLot left,
            BuildingLot right)
        {
            int rowComparison = left.Cell.y.CompareTo(right.Cell.y);
            return rowComparison != 0
                ? rowComparison
                : left.Cell.x.CompareTo(right.Cell.x);
        }

        private static int CompareRankedLots(
            RankedLot left,
            RankedLot right)
        {
            int preferenceComparison =
                left.Preference.CompareTo(right.Preference);
            if (preferenceComparison != 0)
            {
                return preferenceComparison;
            }

            int rankComparison = left.Rank.CompareTo(right.Rank);
            return rankComparison != 0
                ? rankComparison
                : CompareLotsRowMajor(left.Lot, right.Lot);
        }

        private static uint StableHash(
            int seed,
            int x,
            int y,
            uint salt)
        {
            uint hash = StableHash(
                unchecked((uint)seed),
                unchecked((uint)x));
            hash = StableHash(hash, unchecked((uint)y));
            return StableHash(hash, salt);
        }

        private static uint StableHash(
            int seed,
            int x,
            int y,
            uint category,
            uint salt)
        {
            uint hash = StableHash(seed, x, y, salt);
            return StableHash(hash, category);
        }

        private static uint StableHash(uint first, uint second)
        {
            uint hash = first ^ 0x9E3779B9u;
            hash ^= second + 0x85EBCA6Bu + (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }

        private readonly struct RankedLot
        {
            public RankedLot(
                BuildingLot lot,
                int preference,
                uint rank)
            {
                Lot = lot;
                Preference = preference;
                Rank = rank;
            }

            public BuildingLot Lot { get; }
            public int Preference { get; }
            public uint Rank { get; }
        }
    }
}
