using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityDecorationPlannerTests
    {
        [Test]
        public void SameSeed_CreatesIdenticalOrderedPlan()
        {
            CityDecorationPlan first = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out _,
                out _,
                out _);
            CityDecorationPlan second = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out _,
                out _,
                out _);

            CollectionAssert.AreEqual(
                first.Descriptors,
                second.Descriptors);
            Assert.That(first.Count, Is.LessThanOrEqualTo(
                CityDecorationPlan.MaximumDescriptorCount));
            Assert.That(first.Count, Is.GreaterThan(120));
        }

        [Test]
        public void DifferentSeed_ChangesOptionalVisualChoices()
        {
            CityDecorationPlan first = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out _,
                out _,
                out _);
            CityDecorationPlan second = CreatePlan(
                GameSessionState.DefaultCitySeed + 1,
                out _,
                out _,
                out _);

            Assert.That(
                HaveSameVisualSignatures(
                    first.Descriptors,
                    second.Descriptors),
                Is.False);
        }

        [Test]
        public void DefaultCity_CoversEveryOrdinaryLotAndRequiredLandmarks()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout,
                out _,
                out _);

            var coreCounts = new Dictionary<Vector2Int, int>();
            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.AnchorKind !=
                        CityDecorationAnchorKind.BuildingRoof &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.BuildingFacade)
                {
                    continue;
                }

                coreCounts[descriptor.LotCell] =
                    coreCounts.TryGetValue(
                        descriptor.LotCell,
                        out int count)
                        ? count + 1
                        : 1;
            }

            int ordinaryLotCount = 0;
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

                ordinaryLotCount++;
                Assert.That(
                    coreCounts.TryGetValue(lot.Cell, out int count)
                        ? count
                        : 0,
                    Is.EqualTo(1),
                    lot.Cell.ToString());
            }

            Assert.That(coreCounts, Has.Count.EqualTo(ordinaryLotCount));
            Assert.That(
                plan.GetCount(CityDecorationAnchorKind.UrbanLandmark),
                Is.EqualTo(4));
            Assert.That(
                plan.GetCount(CityDecorationAnchorKind.ParkLandmark),
                Is.EqualTo(2));
            Assert.That(
                plan.GetCount(CityDecorationKind.ParkFountainAndStatue),
                Is.EqualTo(1));
            Assert.That(
                plan.GetCount(CityDecorationKind.ParkBandstand),
                Is.EqualTo(1));
            foreach (CityDecorationKind kind in
                     Enum.GetValues(typeof(CityDecorationKind)))
            {
                if (kind == CityDecorationKind.RoadsideBusShelter)
                {
                    Assert.That(
                        plan.GetCount(kind),
                        Is.Zero,
                        "Bus shelters are route-owned infrastructure, not random decoration.");
                    continue;
                }

                Assert.That(
                    plan.GetCount(kind),
                    Is.GreaterThan(0),
                    $"The default city is missing '{kind}'.");
            }
        }

        [Test]
        public void GroundDetails_RespectEntrancesAndNightFixtures()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out _,
                out RoadFencePlan fence,
                out CityNightFixturePlan night);

            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.AnchorKind !=
                        CityDecorationAnchorKind.BuildingFrontage &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.Roadside &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.ParkFeature &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.ParkLandmark)
                {
                    continue;
                }

                float radius =
                    CityDecorationValidator.ResolveProtectionRadius(
                        descriptor.Kind);
                Assert.That(
                    CityDecorationValidator.IsProtectedGroundAnchor(
                        descriptor.Position,
                        radius,
                        fence,
                        night),
                    Is.False,
                    descriptor.StableId);
            }
        }

        [Test]
        public void PlansAcrossSeeds_ExerciseCompleteRecipeCatalog()
        {
            var kinds = new HashSet<CityDecorationKind>();
            for (int seedOffset = 0; seedOffset < 8; seedOffset++)
            {
                CityDecorationPlan plan = CreatePlan(
                    GameSessionState.DefaultCitySeed + seedOffset,
                    out _,
                    out _,
                    out _);
                for (int index = 0;
                     index < plan.Descriptors.Count;
                     index++)
                {
                    kinds.Add(plan.Descriptors[index].Kind);
                }
            }

            Array expected = Enum.GetValues(typeof(CityDecorationKind));
            Assert.That(kinds.Count, Is.EqualTo(expected.Length - 1));
            foreach (CityDecorationKind kind in expected)
            {
                if (kind == CityDecorationKind.RoadsideBusShelter)
                {
                    Assert.That(
                        kinds.Contains(kind),
                        Is.False,
                        "Bus shelters are route-owned infrastructure, " +
                        "never ambient decoration.");
                    continue;
                }

                Assert.That(kinds, Does.Contain(kind), kind.ToString());
            }
        }

        [Test]
        public void DescriptorData_IsStableFiniteAndNonInteractive()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout,
                out RoadFencePlan fence,
                out CityNightFixturePlan night);

            string previousId = null;
            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                Assert.That(descriptor.StableId, Is.Not.Empty);
                if (previousId != null)
                {
                    Assert.That(
                        string.CompareOrdinal(
                            previousId,
                            descriptor.StableId),
                        Is.LessThan(0));
                }

                previousId = descriptor.StableId;
                Assert.That(IsFinite(descriptor.Position), Is.True);
                Assert.That(IsFinite(descriptor.Forward), Is.True);
                Assert.That(
                    descriptor.CollisionTier,
                    Is.EqualTo(
                        CityDecorationCollisionCatalog.ResolveTier(
                            descriptor.Kind)));
                Assert.That(descriptor.Forward.y, Is.EqualTo(0f));
                Assert.That(
                    descriptor.Forward.sqrMagnitude,
                    Is.EqualTo(1f).Within(0.002f));
                if (descriptor.HasLotAnchor &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.UrbanLandmark)
                {
                    Assert.That(
                        descriptor.TryResolveLot(layout, out BuildingLot lot),
                        Is.True);
                    Assert.That(
                        lot.IsBar ||
                        lot.IsPlayerHome ||
                        lot.IsSupermarket,
                        Is.False);
                }
            }

            Assert.DoesNotThrow(() =>
                plan.ValidateOrThrow(layout, fence, night));
        }

        [Test]
        public void CollisionCatalog_DefinesEveryVisualFamily()
        {
            var expected = new Dictionary<
                CityDecorationKind,
                CityDecorationCollisionTier>
            {
                { CityDecorationKind.OldTownChimneysAndDormers, CityDecorationCollisionTier.None },
                { CityDecorationKind.OldTownScaffolding, CityDecorationCollisionTier.Detail },
                { CityDecorationKind.OldTownStreetMarket, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.OldTownClockTower, CityDecorationCollisionTier.None },
                { CityDecorationKind.ResidentialBalconies, CityDecorationCollisionTier.None },
                { CityDecorationKind.ResidentialLaundryAndAntenna, CityDecorationCollisionTier.None },
                { CityDecorationKind.ResidentialDiscardedFurniture, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.ResidentialRooftopGreenhouse, CityDecorationCollisionTier.None },
                { CityDecorationKind.IndustrialStacksAndTanks, CityDecorationCollisionTier.None },
                { CityDecorationKind.IndustrialPipeRack, CityDecorationCollisionTier.Detail },
                { CityDecorationKind.IndustrialCargo, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.IndustrialGantry, CityDecorationCollisionTier.None },
                { CityDecorationKind.NightlifeBillboard, CityDecorationCollisionTier.None },
                { CityDecorationKind.NightlifeFireEscape, CityDecorationCollisionTier.Detail },
                { CityDecorationKind.NightlifeVendingAndQueue, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.NightlifeCinema, CityDecorationCollisionTier.None },
                { CityDecorationKind.RoadsideDumpsterAndUtility, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.RoadsidePhoneBooth, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.RoadsideBusShelter, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.RoadsideRoadworkAndBicycle, CityDecorationCollisionTier.Detail },
                { CityDecorationKind.ParkFountainAndStatue, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.ParkBandstand, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.ParkChessTables, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.ParkPlayground, CityDecorationCollisionTier.Blocking }
            };

            Array kinds = Enum.GetValues(typeof(CityDecorationKind));
            Assert.That(expected, Has.Count.EqualTo(kinds.Length));
            foreach (CityDecorationKind kind in kinds)
            {
                Assert.That(
                    CityDecorationCollisionCatalog.ResolveTier(kind),
                    Is.EqualTo(expected[kind]),
                    kind.ToString());
            }
        }

        [Test]
        public void DetailWorld_UsesBoundedChunkProxiesButHomeViewUsesNone()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout,
                out _,
                out _);
            int physicalDescriptorCount = 0;
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                if (plan.Descriptors[index].CollisionTier !=
                    CityDecorationCollisionTier.None)
                {
                    physicalDescriptorCount++;
                }
            }

            GameObject parent = new GameObject("City Detail Test Parent");
            GameObject homeParent = new GameObject("Home Detail Test Parent");
            try
            {
                GameObject cityDetails = CityDecorationWorldBuilder.Build(
                    parent.transform,
                    layout,
                    plan);
                BoxCollider[] proxies =
                    cityDetails.GetComponentsInChildren<BoxCollider>(true);
                Assert.That(
                    proxies.Length,
                    Is.InRange(
                        physicalDescriptorCount,
                        physicalDescriptorCount *
                        CityStaticCollisionBuilder
                            .MaximumDecorationProxyCount));
                for (int index = 0; index < proxies.Length; index++)
                {
                    Assert.That(
                        proxies[index].transform.name,
                        Does.StartWith("City Detail Chunk "));
                    Assert.That(proxies[index].size.x, Is.GreaterThan(0f));
                    Assert.That(proxies[index].size.y, Is.GreaterThan(0f));
                    Assert.That(proxies[index].size.z, Is.GreaterThan(0f));
                }

                HomeExteriorContextPlan context =
                    HomeExteriorContextPlanner.Generate(
                        GameSessionState.DefaultCitySeed);
                GameObject homeDetails =
                    CityDecorationWorldBuilder.BuildHomeExterior(
                        homeParent.transform,
                        context,
                        context.NearbyDecorations);
                Assert.That(
                    homeDetails.GetComponentsInChildren<BoxCollider>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(homeParent);
            }
        }

        [Test]
        public void StreetUtilities_RepeatRegularlyButNeverCrowd()
        {
            for (int seedOffset = 0; seedOffset < 3; seedOffset++)
            {
                CityDecorationPlan plan = CreatePlan(
                    GameSessionState.DefaultCitySeed + seedOffset,
                    out _,
                    out _,
                    out _);
                var booths = new List<Vector3>();
                var dumpsters = new List<Vector3>();
                for (int index = 0;
                     index < plan.Descriptors.Count;
                     index++)
                {
                    CityDecorationDescriptor descriptor =
                        plan.Descriptors[index];
                    if (descriptor.Kind ==
                        CityDecorationKind.RoadsidePhoneBooth)
                    {
                        booths.Add(descriptor.Position);
                    }
                    else if (descriptor.Kind ==
                             CityDecorationKind
                                 .RoadsideDumpsterAndUtility)
                    {
                        dumpsters.Add(descriptor.Position);
                    }
                }

                Assert.That(
                    booths,
                    Has.Count.InRange(4, 40),
                    "Phone booths must repeat like infrastructure.");
                Assert.That(
                    dumpsters,
                    Has.Count.InRange(6, 60),
                    "Dumpsters must repeat like infrastructure.");
                AssertMinimumPlanarSpacing(
                    booths,
                    CityDecorationPlanner.PhoneBoothMinimumSpacing,
                    "phone booths");
                AssertMinimumPlanarSpacing(
                    dumpsters,
                    CityDecorationPlanner.DumpsterMinimumSpacing,
                    "dumpsters");
            }
        }

        [Test]
        public void BarSideYard_LeansPhoneBoothAndDumpsterOnTheBarWall()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout,
                out _,
                out _);
            Assert.That(
                HomeYardSitePlanner.TryCreate(
                    layout,
                    out HomeYardSitePlan site),
                Is.True);
            Assert.That(
                HomeYardUtilityPlanner.TryCreatePhoneBooth(
                    site,
                    out HomeYardUtilityAnchor boothAnchor),
                Is.True);
            Assert.That(
                HomeYardUtilityPlanner.TryCreateDumpster(
                    site,
                    out HomeYardUtilityAnchor dumpsterAnchor),
                Is.True);

            CityDecorationDescriptor booth = FindBySuffix(
                plan,
                "-homeyard-booth");
            CityDecorationDescriptor dumpster = FindBySuffix(
                plan,
                "-homeyard-dumpster");
            Assert.That(
                booth.Kind,
                Is.EqualTo(CityDecorationKind.RoadsidePhoneBooth));
            Assert.That(
                dumpster.Kind,
                Is.EqualTo(
                    CityDecorationKind.RoadsideDumpsterAndUtility));
            Assert.That(booth.Position, Is.EqualTo(boothAnchor.Position));
            Assert.That(booth.Forward, Is.EqualTo(boothAnchor.Forward));
            Assert.That(
                dumpster.Position,
                Is.EqualTo(dumpsterAnchor.Position));
            Assert.That(
                dumpster.Forward,
                Is.EqualTo(dumpsterAnchor.Forward));
            Assert.That(
                booth.District,
                Is.EqualTo(site.Anchor.District));
            Assert.That(
                dumpster.District,
                Is.EqualTo(site.Anchor.District));

            // Both lean on the bar wall: the back face sits a
            // whisker proud of the facade plane and the service side
            // opens into the yard.
            Vector2Int direction = site.DirectionFromAnchorToNeighbour;
            float wallX = direction.x > 0
                ? site.GroundBounds.xMin - HomeYardSitePlanner.WallMargin
                : site.GroundBounds.xMax + HomeYardSitePlanner.WallMargin;
            var intoYard = new Vector3(direction.x, 0f, direction.y);
            Assert.That(booth.Forward, Is.EqualTo(intoYard));
            Assert.That(dumpster.Forward, Is.EqualTo(intoYard));
            AssertLeansOnWall(
                booth.Position,
                direction.x,
                wallX,
                HomeYardUtilityPlanner.BoothBackHalfDepth);
            AssertLeansOnWall(
                dumpster.Position,
                direction.x,
                wallX,
                HomeYardUtilityPlanner.DumpsterBackHalfDepth);

            // Neither footprint may crowd the worn circuit the chair
            // rides, nor each other.
            AssertClearOfRing(site, boothAnchor.Footprint);
            AssertClearOfRing(site, dumpsterAnchor.Footprint);
            Assert.That(
                boothAnchor.Footprint.Overlaps(
                    dumpsterAnchor.Footprint),
                Is.False);
        }

        private static CityDecorationDescriptor FindBySuffix(
            CityDecorationPlan plan,
            string suffix)
        {
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                if (plan.Descriptors[index].StableId.EndsWith(
                        suffix,
                        StringComparison.Ordinal))
                {
                    return plan.Descriptors[index];
                }
            }

            Assert.Fail($"No descriptor with suffix '{suffix}'.");
            return default;
        }

        private static void AssertLeansOnWall(
            Vector3 position,
            int directionX,
            float wallX,
            float backHalfDepth)
        {
            float backX = position.x - directionX * backHalfDepth;
            Assert.That(
                Mathf.Abs(backX - wallX),
                Is.LessThanOrEqualTo(
                    HomeYardUtilityPlanner.WallProudOffset + 0.001f));
        }

        private static void AssertClearOfRing(
            HomeYardSitePlan site,
            Rect footprint)
        {
            float nearestX = Mathf.Max(
                Mathf.Abs(site.RingCenter.x -
                          Mathf.Clamp(
                              site.RingCenter.x,
                              footprint.xMin,
                              footprint.xMax)),
                0f);
            float nearestZ = Mathf.Max(
                Mathf.Abs(site.RingCenter.z -
                          Mathf.Clamp(
                              site.RingCenter.z,
                              footprint.yMin,
                              footprint.yMax)),
                0f);
            Assert.That(
                Mathf.Sqrt(
                    (nearestX * nearestX) + (nearestZ * nearestZ)),
                Is.GreaterThanOrEqualTo(
                    site.RingRadius +
                    HomeYardUtilityPlanner.CircuitClearance -
                    0.001f));
        }

        private static void AssertMinimumPlanarSpacing(
            IReadOnlyList<Vector3> positions,
            float minimumSpacing,
            string label)
        {
            float squared = minimumSpacing * minimumSpacing;
            for (int first = 0; first < positions.Count; first++)
            {
                for (int second = first + 1;
                     second < positions.Count;
                     second++)
                {
                    float x = positions[first].x - positions[second].x;
                    float z = positions[first].z - positions[second].z;
                    Assert.That(
                        (x * x) + (z * z),
                        Is.GreaterThanOrEqualTo(squared - 0.01f),
                        $"Two {label} crowd one corner.");
                }
            }
        }

        private static CityDecorationPlan CreatePlan(
            int seed,
            out CityLayout layout,
            out RoadFencePlan fence,
            out CityNightFixturePlan night)
        {
            layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                seed);
            fence = RoadFencePlanner.CreatePlan(layout);
            night = CityNightFixturePlanner.CreatePlan(layout);
            return CityDecorationPlanner.CreatePlan(
                layout,
                fence,
                night);
        }

        private static bool HaveSameVisualSignatures(
            IReadOnlyList<CityDecorationDescriptor> first,
            IReadOnlyList<CityDecorationDescriptor> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            for (int index = 0; index < first.Count; index++)
            {
                CityDecorationDescriptor firstDescriptor = first[index];
                CityDecorationDescriptor secondDescriptor = second[index];
                if (firstDescriptor.Kind != secondDescriptor.Kind ||
                    firstDescriptor.AnchorKind !=
                    secondDescriptor.AnchorKind ||
                    firstDescriptor.LotCell != secondDescriptor.LotCell ||
                    firstDescriptor.Variant != secondDescriptor.Variant ||
                    firstDescriptor.Palette != secondDescriptor.Palette)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
