using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Selects a few shallow, ordinary residential frontage pockets from the
    /// real lot/access geometry. They are deliberately descriptors in the
    /// existing City-decoration plan: later roadside planning sees their
    /// occupied anchors and wind dressing sees their physical proxies.
    /// </summary>
    public static class CityCourtyardPocketPlanner
    {
        public const int MaximumPocketCount = 4;
        public const float MinimumPocketSpacing = 26f;
        public const float DoorClearance = 0.78f;
        public const float AccessClearance = 0.45f;
        public const float PointOfInterestClearance = 0.75f;
        public const float DryingYardClearance = 25f;
        public const float HomeYardClearance = 1f;
        public const float ProxyClearance = 0.28f;
        public const float MaximumGroundDelta = 0.16f;

        private const float FacadeGap = 0.06f;
        private const float RoadEdgeClearance = 0.10f;
        private const float SideClearance = 0.24f;
        private const float OtherGroundAnchorClearance = 2.8f;
        private const int MaximumCandidatesPerVariant = 64;

        private const uint OptionalVariantSalt = 0x43504F56u;
        private const uint CandidateRankSalt = 0x4350524Bu;
        private const uint CandidateSideSalt = 0x43505344u;
        private const uint PaletteSalt = 0x4350504Cu;

        private static readonly float[] LateralMagnitudes =
        {
            0.36f,
            0.36f,
            0.28f,
            0.28f,
            0.42f,
            0.42f,
            0.21f,
            0.21f
        };

        public static int ResolveOptionalVariant(int seed)
        {
            return CityCourtyardPocketGeometry.ChairRepairVariant +
                   (int)(StableHash(
                       seed,
                       0,
                       0,
                       OptionalVariantSalt) % 3u);
        }

        public static void Append(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            IList<CityDecorationDescriptor> target,
            ICollection<Vector3> occupiedGroundPositions)
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

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (occupiedGroundPositions == null)
            {
                throw new ArgumentNullException(
                    nameof(occupiedGroundPositions));
            }

            for (int index = 0; index < target.Count; index++)
            {
                if (target[index].Kind ==
                    CityDecorationKind.ResidentialCourtyardPocket)
                {
                    // The stage is idempotent. In particular, a caller that
                    // retries world composition cannot exceed the four-scene
                    // district cap or duplicate stable IDs.
                    return;
                }
            }

            int available = CityDecorationPlan.MaximumDescriptorCount -
                            target.Count;
            int desiredCount = Mathf.Min(MaximumPocketCount, available);
            if (desiredCount <= 0)
            {
                return;
            }

            var blockingBounds = new List<Bounds>();
            var proxyBuffer = new List<Bounds>(
                CityStaticCollisionBuilder.MaximumDecorationProxyCount);
            for (int index = 0; index < target.Count; index++)
            {
                CityDecorationDescriptor descriptor = target[index];
                if (descriptor.CollisionTier ==
                    CityDecorationCollisionTier.None)
                {
                    continue;
                }

                proxyBuffer.Clear();
                CityStaticCollisionBuilder.AddDecorationProxyBounds(
                    layout,
                    descriptor,
                    proxyBuffer);
                blockingBounds.AddRange(proxyBuffer);
            }

            HomeYardSitePlan? homeYard = HomeYardSitePlanner.TryCreate(
                layout,
                out HomeYardSitePlan site)
                ? site
                : (HomeYardSitePlan?)null;

            int[] variants =
            {
                CityCourtyardPocketGeometry.BalconyBasketVariant,
                CityCourtyardPocketGeometry.NardiVariant,
                CityCourtyardPocketGeometry.BicycleVariant,
                ResolveOptionalVariant(layout.Seed)
            };
            var candidatesByVariant =
                new List<PocketCandidate>[desiredCount];
            for (int index = 0; index < desiredCount; index++)
            {
                candidatesByVariant[index] = CollectCandidates(
                    layout,
                    variants[index],
                    fencePlan,
                    nightPlan,
                    target,
                    occupiedGroundPositions,
                    blockingBounds,
                    homeYard);
            }

            var selected = new List<PocketCandidate>(desiredCount);
            if (!TrySelectCompleteSet(
                    candidatesByVariant,
                    0,
                    selected))
            {
                selected.Clear();
                SelectGreedyFallback(candidatesByVariant, selected);
            }

            for (int index = 0; index < selected.Count; index++)
            {
                target.Add(selected[index].Descriptor);
                occupiedGroundPositions.Add(
                    selected[index].Descriptor.Position);
            }
        }

        internal static Rect CreateDoorClearance(BuildingLot lot)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            return Rect.MinMaxRect(
                Mathf.Min(
                    lot.DoorPosition.x,
                    lot.SidewalkArrivalPosition.x) - DoorClearance,
                Mathf.Min(
                    lot.DoorPosition.z,
                    lot.SidewalkArrivalPosition.z) - DoorClearance,
                Mathf.Max(
                    lot.DoorPosition.x,
                    lot.SidewalkArrivalPosition.x) + DoorClearance,
                Mathf.Max(
                    lot.DoorPosition.z,
                    lot.SidewalkArrivalPosition.z) + DoorClearance);
        }

        internal static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }

        internal static bool OverlapsStrict(Rect left, Rect right)
        {
            const float epsilon = 0.001f;
            return left.xMin < right.xMax - epsilon &&
                   left.xMax > right.xMin + epsilon &&
                   left.yMin < right.yMax - epsilon &&
                   left.yMax > right.yMin + epsilon;
        }

        private static List<PocketCandidate> CollectCandidates(
            CityLayout layout,
            int variant,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            IList<CityDecorationDescriptor> existingDescriptors,
            ICollection<Vector3> occupiedGroundPositions,
            IReadOnlyList<Bounds> blockingBounds,
            HomeYardSitePlan? homeYard)
        {
            var result = new List<PocketCandidate>();
            for (int lotIndex = 0;
                 lotIndex < layout.BuildingLots.Count;
                 lotIndex++)
            {
                BuildingLot lot = layout.BuildingLots[lotIndex];
                if (!lot.IsOrdinaryBuilding ||
                    lot.District != CityDistrictKind.Residential ||
                    !lot.HasRoadFrontage ||
                    !layout.HasRoad(RoadEdge.ForCellFrontage(
                        lot.Cell,
                        lot.FrontageDirection)))
                {
                    continue;
                }

                int preference = variant ==
                                     CityCourtyardPocketGeometry
                                         .BalconyBasketVariant &&
                                 !LotHasDecoration(
                                     existingDescriptors,
                                     lot.Cell,
                                     CityDecorationKind
                                         .ResidentialBalconies)
                    ? 1
                    : 0;
                for (int attempt = 0;
                     attempt < LateralMagnitudes.Length;
                     attempt++)
                {
                    if (!TryCreateCandidate(
                            layout,
                            lot,
                            variant,
                            attempt,
                            preference,
                            fencePlan,
                            nightPlan,
                            occupiedGroundPositions,
                            blockingBounds,
                            homeYard,
                            out PocketCandidate candidate))
                    {
                        continue;
                    }

                    result.Add(candidate);
                }
            }

            result.Sort(CompareCandidates);
            if (result.Count > MaximumCandidatesPerVariant)
            {
                result.RemoveRange(
                    MaximumCandidatesPerVariant,
                    result.Count - MaximumCandidatesPerVariant);
            }

            return result;
        }

        private static bool TryCreateCandidate(
            CityLayout layout,
            BuildingLot lot,
            int variant,
            int attempt,
            int preference,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<Vector3> occupiedGroundPositions,
            IReadOnlyList<Bounds> blockingBounds,
            HomeYardSitePlan? homeYard,
            out PocketCandidate candidate)
        {
            candidate = default;
            Vector3 forward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Vector3 tangent = new Vector3(
                forward.z,
                0f,
                -forward.x);
            bool frontageIsX = Mathf.Abs(forward.x) > 0.5f;
            float buildingHalfDepth = frontageIsX
                ? lot.Size.x * 0.5f
                : lot.Size.y * 0.5f;
            float centerToRoad = frontageIsX
                ? layout.NodeSpacing.x * 0.5f
                : layout.NodeSpacing.y * 0.5f;
            float availableDepth = centerToRoad -
                                   layout.RoadWidth * 0.5f -
                                   buildingHalfDepth;
            float depth = CityCourtyardPocketGeometry.GetDepth(variant);
            if (availableDepth <
                FacadeGap + depth + RoadEdgeClearance)
            {
                return false;
            }

            float parallelSpan = frontageIsX
                ? lot.Size.y
                : lot.Size.x;
            float width = CityCourtyardPocketGeometry.GetWidth(variant);
            float lateralLimit = parallelSpan * 0.5f -
                                 width * 0.5f -
                                 SideClearance;
            if (lateralLimit <= DoorClearance)
            {
                return false;
            }

            uint sideHash = StableHash(
                layout.Seed,
                lot.Cell.x,
                lot.Cell.y,
                CandidateSideSalt ^ (uint)variant);
            float baseSign = (sideHash & 1u) == 0u ? -1f : 1f;
            float sign = (attempt & 1) == 0 ? baseSign : -baseSign;
            float lateral = Mathf.Min(
                parallelSpan * LateralMagnitudes[attempt],
                lateralLimit) * sign;
            Vector3 position = lot.Center +
                               forward *
                               (buildingHalfDepth +
                                FacadeGap +
                                depth * 0.5f) +
                               tangent * lateral;
            if (!TryGroundFootprint(
                    layout,
                    lot,
                    variant,
                    forward,
                    ref position,
                    out Rect footprint))
            {
                return false;
            }

            var descriptor = new CityDecorationDescriptor(
                CreateStableId(layout.Seed, lot.Cell, variant),
                CityDecorationKind.ResidentialCourtyardPocket,
                CityDecorationAnchorKind.BuildingFrontage,
                CityDistrictKind.Residential,
                lot.Cell,
                position,
                forward,
                variant,
                ResolvePalette(layout.Seed, lot.Cell, variant),
                CityDecorationVisibilityTier.Near,
                CityDecorationCollisionCatalog.ResolveTier(
                    CityDecorationKind.ResidentialCourtyardPocket));
            if (CityDecorationValidator.IsProtectedGroundAnchor(
                    position,
                    CityDecorationValidator.ResolveProtectionRadius(
                        descriptor.Kind),
                    fencePlan,
                    nightPlan) ||
                OverlapsStrict(
                    footprint,
                    CreateDoorClearance(lot)) ||
                IsTooCloseToGroundAnchor(
                    position,
                    occupiedGroundPositions) ||
                BlocksAnyAccess(layout, footprint) ||
                BlocksPointOfInterest(layout, footprint) ||
                BlocksDryingYard(layout, footprint) ||
                BlocksHomeYard(homeYard, footprint) ||
                IntersectsBlockingProxy(footprint, blockingBounds))
            {
                return false;
            }

            var proxies = new List<Bounds>(
                CityStaticCollisionBuilder.MaximumDecorationProxyCount);
            CityCourtyardPocketGeometry.AppendCollisionBounds(
                descriptor,
                proxies);
            uint rank = StableHash(
                layout.Seed,
                lot.Cell.x,
                lot.Cell.y,
                CandidateRankSalt ^
                ((uint)variant * 31u) ^
                (uint)attempt);
            candidate = new PocketCandidate(
                descriptor,
                lot,
                footprint,
                proxies,
                preference,
                attempt,
                rank);
            return true;
        }

        private static bool TryGroundFootprint(
            CityLayout layout,
            BuildingLot lot,
            int variant,
            Vector3 forward,
            ref Vector3 position,
            out Rect footprint)
        {
            var provisional = new CityDecorationDescriptor(
                "courtyard-ground-probe",
                CityDecorationKind.ResidentialCourtyardPocket,
                CityDecorationAnchorKind.BuildingFrontage,
                CityDistrictKind.Residential,
                lot.Cell,
                position,
                forward,
                variant,
                CityDecorationPalette.ResidentialCool,
                CityDecorationVisibilityTier.Near,
                CityDecorationCollisionTier.Blocking);
            footprint = CityCourtyardPocketGeometry.CreateFootprint(
                provisional);
            Vector2[] samples =
            {
                footprint.center,
                new Vector2(footprint.xMin, footprint.yMin),
                new Vector2(footprint.xMin, footprint.yMax),
                new Vector2(footprint.xMax, footprint.yMin),
                new Vector2(footprint.xMax, footprint.yMax)
            };
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            float centerTop = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                if (!CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        samples[index],
                        out float top,
                        out CitySurfaceDescriptor surface) ||
                    surface.Kind != CitySurfaceKind.BuildableGround ||
                    surface.Cell != lot.Cell)
                {
                    return false;
                }

                if (index == 0)
                {
                    centerTop = top;
                }

                minimum = Mathf.Min(minimum, top);
                maximum = Mathf.Max(maximum, top);
            }

            if (maximum - minimum > MaximumGroundDelta)
            {
                return false;
            }

            position.y = centerTop;
            return true;
        }

        private static bool TrySelectCompleteSet(
            IReadOnlyList<List<PocketCandidate>> candidatesByVariant,
            int variantIndex,
            IList<PocketCandidate> selected)
        {
            if (variantIndex >= candidatesByVariant.Count)
            {
                return true;
            }

            List<PocketCandidate> candidates =
                candidatesByVariant[variantIndex];
            for (int index = 0; index < candidates.Count; index++)
            {
                PocketCandidate candidate = candidates[index];
                if (!ClearsSelected(candidate, selected))
                {
                    continue;
                }

                selected.Add(candidate);
                if (TrySelectCompleteSet(
                        candidatesByVariant,
                        variantIndex + 1,
                        selected))
                {
                    return true;
                }

                selected.RemoveAt(selected.Count - 1);
            }

            return false;
        }

        private static void SelectGreedyFallback(
            IReadOnlyList<List<PocketCandidate>> candidatesByVariant,
            IList<PocketCandidate> selected)
        {
            for (int variant = 0;
                 variant < candidatesByVariant.Count;
                 variant++)
            {
                List<PocketCandidate> candidates =
                    candidatesByVariant[variant];
                for (int index = 0; index < candidates.Count; index++)
                {
                    if (!ClearsSelected(candidates[index], selected))
                    {
                        continue;
                    }

                    selected.Add(candidates[index]);
                    break;
                }
            }
        }

        private static bool ClearsSelected(
            PocketCandidate candidate,
            IList<PocketCandidate> selected)
        {
            float minimumSquared =
                MinimumPocketSpacing * MinimumPocketSpacing;
            for (int index = 0; index < selected.Count; index++)
            {
                PocketCandidate existing = selected[index];
                if (candidate.Lot.Cell == existing.Lot.Cell)
                {
                    return false;
                }

                Vector3 delta = candidate.Descriptor.Position -
                                existing.Descriptor.Position;
                delta.y = 0f;
                if (delta.sqrMagnitude < minimumSquared - 0.001f ||
                    OverlapsStrict(
                        Expand(
                            candidate.Footprint,
                            ProxyClearance),
                        existing.Footprint))
                {
                    return false;
                }

                for (int proxy = 0;
                     proxy < candidate.Proxies.Count;
                     proxy++)
                {
                    Rect candidateProxy = Expand(
                        ToXZRect(candidate.Proxies[proxy]),
                        ProxyClearance);
                    for (int other = 0;
                         other < existing.Proxies.Count;
                         other++)
                    {
                        if (OverlapsStrict(
                                candidateProxy,
                                ToXZRect(existing.Proxies[other])))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool BlocksAnyAccess(
            CityLayout layout,
            Rect footprint)
        {
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                if (OverlapsStrict(
                        footprint,
                        Expand(
                            layout.OpenAreaAccesses[index]
                                .ApproachBounds,
                            AccessClearance)))
                {
                    return true;
                }
            }

            for (int pointIndex = 0;
                 pointIndex < layout.DistrictPointsOfInterest.Count;
                 pointIndex++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[pointIndex];
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    if (OverlapsStrict(
                            footprint,
                            Expand(
                                point.Accesses[accessIndex]
                                    .ApproachBounds,
                                AccessClearance)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool BlocksPointOfInterest(
            CityLayout layout,
            Rect footprint)
        {
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                if (OverlapsStrict(
                        footprint,
                        Expand(
                            layout.DistrictPointsOfInterest[index]
                                .PublicBounds,
                            PointOfInterestClearance)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool BlocksDryingYard(
            CityLayout layout,
            Rect footprint)
        {
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[index];
                if (point.Kind == CityDistrictPointOfInterestKind
                                      .ResidentialDryingYard &&
                    OverlapsStrict(
                        footprint,
                        Expand(
                            point.PublicBounds,
                            DryingYardClearance)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool BlocksHomeYard(
            HomeYardSitePlan? homeYard,
            Rect footprint)
        {
            return homeYard.HasValue &&
                   OverlapsStrict(
                       footprint,
                       Expand(
                           homeYard.Value.GroundBounds,
                           HomeYardClearance));
        }

        private static bool IntersectsBlockingProxy(
            Rect footprint,
            IReadOnlyList<Bounds> blockingBounds)
        {
            Rect expanded = Expand(footprint, ProxyClearance);
            for (int index = 0; index < blockingBounds.Count; index++)
            {
                if (OverlapsStrict(
                        expanded,
                        ToXZRect(blockingBounds[index])))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTooCloseToGroundAnchor(
            Vector3 position,
            ICollection<Vector3> occupiedGroundPositions)
        {
            float squared = OtherGroundAnchorClearance *
                            OtherGroundAnchorClearance;
            foreach (Vector3 occupied in occupiedGroundPositions)
            {
                float x = position.x - occupied.x;
                float z = position.z - occupied.z;
                if ((x * x) + (z * z) < squared)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LotHasDecoration(
            IList<CityDecorationDescriptor> descriptors,
            Vector2Int cell,
            CityDecorationKind kind)
        {
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (descriptors[index].LotCell == cell &&
                    descriptors[index].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static Rect ToXZRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static CityDecorationPalette ResolvePalette(
            int seed,
            Vector2Int cell,
            int variant)
        {
            return (StableHash(
                        seed,
                        cell.x,
                        cell.y,
                        PaletteSalt ^ (uint)variant) & 1u) == 0u
                ? CityDecorationPalette.ResidentialCool
                : CityDecorationPalette.ResidentialWarm;
        }

        private static string CreateStableId(
            int seed,
            Vector2Int cell,
            int variant)
        {
            return $"city-decor-{unchecked((uint)seed):x8}-" +
                   $"lot-{cell.x:D3}-{cell.y:D3}-" +
                   $"courtyard-{variant:D2}";
        }

        private static int CompareCandidates(
            PocketCandidate left,
            PocketCandidate right)
        {
            int preference = left.Preference.CompareTo(right.Preference);
            if (preference != 0)
            {
                return preference;
            }

            int rank = left.Rank.CompareTo(right.Rank);
            if (rank != 0)
            {
                return rank;
            }

            int row = left.Lot.Cell.y.CompareTo(right.Lot.Cell.y);
            if (row != 0)
            {
                return row;
            }

            int column = left.Lot.Cell.x.CompareTo(right.Lot.Cell.x);
            return column != 0
                ? column
                : left.Attempt.CompareTo(right.Attempt);
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

        private readonly struct PocketCandidate
        {
            public PocketCandidate(
                CityDecorationDescriptor descriptor,
                BuildingLot lot,
                Rect footprint,
                IReadOnlyList<Bounds> proxies,
                int preference,
                int attempt,
                uint rank)
            {
                Descriptor = descriptor;
                Lot = lot;
                Footprint = footprint;
                Proxies = proxies;
                Preference = preference;
                Attempt = attempt;
                Rank = rank;
            }

            public CityDecorationDescriptor Descriptor { get; }
            public BuildingLot Lot { get; }
            public Rect Footprint { get; }
            public IReadOnlyList<Bounds> Proxies { get; }
            public int Preference { get; }
            public int Attempt { get; }
            public uint Rank { get; }
        }
    }
}
