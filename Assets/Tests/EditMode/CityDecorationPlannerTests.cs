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
        public void LandmarkLots_KeepCoreDressingOffTheLandmarkSurface()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout,
                out _,
                out _);
            var districts = new[]
            {
                CityDistrictKind.OldTown,
                CityDistrictKind.Residential,
                CityDistrictKind.Industrial,
                CityDistrictKind.Nightlife
            };

            for (int districtIndex = 0;
                 districtIndex < districts.Length;
                 districtIndex++)
            {
                CityDistrictKind district = districts[districtIndex];
                Assert.That(
                    layout.TryGetPrimaryLandmarkCell(
                        district,
                        out Vector2Int landmarkCell),
                    Is.True);
                int facadeCore = 0;
                int roofCore = 0;
                int landmark = 0;
                for (int index = 0;
                     index < plan.Descriptors.Count;
                     index++)
                {
                    CityDecorationDescriptor descriptor =
                        plan.Descriptors[index];
                    if (descriptor.LotCell != landmarkCell ||
                        descriptor.District != district)
                    {
                        continue;
                    }

                    switch (descriptor.AnchorKind)
                    {
                        case CityDecorationAnchorKind.BuildingFacade:
                            facadeCore++;
                            break;
                        case CityDecorationAnchorKind.BuildingRoof:
                            roofCore++;
                            break;
                        case CityDecorationAnchorKind.UrbanLandmark:
                            landmark++;
                            break;
                    }
                }

                bool facadeLandmark =
                    district == CityDistrictKind.Nightlife;
                Assert.That(
                    facadeCore,
                    Is.EqualTo(facadeLandmark ? 0 : 1),
                    district.ToString());
                Assert.That(
                    roofCore,
                    Is.EqualTo(facadeLandmark ? 1 : 0),
                    district.ToString());
                Assert.That(landmark, Is.EqualTo(1), district.ToString());
            }

            Assert.That(
                layout.TryGetPrimaryLandmarkCell(
                    CityDistrictKind.Nightlife,
                    out Vector2Int nightlifeLandmarkCell),
                Is.True);
            var conflictingDescriptors =
                new List<CityDecorationDescriptor>(
                    plan.Descriptors.Count);
            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.LotCell == nightlifeLandmarkCell &&
                    descriptor.AnchorKind ==
                        CityDecorationAnchorKind.BuildingRoof)
                {
                    descriptor = new CityDecorationDescriptor(
                        descriptor.StableId,
                        CityDecorationKind.NightlifeFireEscape,
                        CityDecorationAnchorKind.BuildingFacade,
                        descriptor.District,
                        descriptor.LotCell,
                        descriptor.Position,
                        descriptor.Forward,
                        descriptor.Variant,
                        descriptor.Palette,
                        CityDecorationVisibilityTier.MidRange,
                        descriptor.CollisionTier);
                }

                conflictingDescriptors.Add(descriptor);
            }

            var conflictingPlan = new CityDecorationPlan(
                plan.Seed,
                conflictingDescriptors);
            InvalidOperationException exception = Assert.Throws<
                InvalidOperationException>(
                () => conflictingPlan.ValidateOrThrow(layout));
            Assert.That(
                exception.Message,
                Does.Contain("landmark surface"));
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
        public void ResidentialCourtyardPockets_AreFourDistinctClearAndWindAware()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            CityDecorationPlan first = CityDecorationPlanner.CreatePlan(
                layout,
                fence,
                night);
            CityDecorationPlan second = CityDecorationPlanner.CreatePlan(
                layout,
                fence,
                night);
            List<CityDecorationDescriptor> pockets = CollectKind(
                first,
                CityDecorationKind.ResidentialCourtyardPocket);
            List<CityDecorationDescriptor> repeated = CollectKind(
                second,
                CityDecorationKind.ResidentialCourtyardPocket);

            Assert.That(
                pockets,
                Has.Count.EqualTo(
                    CityCourtyardPocketPlanner.MaximumPocketCount));
            CollectionAssert.AreEqual(pockets, repeated);

            var variants = new HashSet<int>();
            var lotCells = new HashSet<Vector2Int>();
            var positions = new List<Vector3>();
            var pocketProxies = new List<Bounds>();
            HomeYardSitePlan? homeYard = HomeYardSitePlanner.TryCreate(
                layout,
                out HomeYardSitePlan site)
                ? site
                : (HomeYardSitePlan?)null;
            for (int index = 0; index < pockets.Count; index++)
            {
                CityDecorationDescriptor pocket = pockets[index];
                Assert.That(
                    pocket.District,
                    Is.EqualTo(CityDistrictKind.Residential));
                Assert.That(
                    pocket.AnchorKind,
                    Is.EqualTo(
                        CityDecorationAnchorKind.BuildingFrontage));
                Assert.That(
                    pocket.CollisionTier,
                    Is.EqualTo(CityDecorationCollisionTier.Blocking));
                Assert.That(variants.Add(pocket.Variant), Is.True);
                Assert.That(lotCells.Add(pocket.LotCell), Is.True);
                Assert.That(
                    pocket.TryResolveLot(layout, out BuildingLot lot),
                    Is.True);
                Assert.That(lot.IsOrdinaryBuilding, Is.True);
                Assert.That(
                    CityCourtyardPocketGeometry.GetDepth(
                        pocket.Variant),
                    Is.LessThanOrEqualTo(
                        CityCourtyardPocketGeometry.MaximumDepth));

                Rect footprint =
                    CityCourtyardPocketGeometry.CreateFootprint(pocket);
                Assert.That(
                    CityCourtyardPocketPlanner.OverlapsStrict(
                        footprint,
                        CityCourtyardPocketPlanner
                            .CreateDoorClearance(lot)),
                    Is.False,
                    pocket.StableId);
                Assert.That(
                    CityDecorationValidator.IsProtectedGroundAnchor(
                        pocket.Position,
                        CityDecorationValidator.ResolveProtectionRadius(
                            pocket.Kind),
                        fence,
                        night),
                    Is.False,
                    pocket.StableId);
                AssertCourtyardFootprintGrounded(
                    layout,
                    lot,
                    pocket,
                    footprint);
                AssertCourtyardClearOfAccesses(
                    layout,
                    footprint,
                    pocket.StableId);
                AssertCourtyardClearOfPointsOfInterest(
                    layout,
                    footprint,
                    pocket.StableId);
                if (homeYard.HasValue)
                {
                    Assert.That(
                        CityCourtyardPocketPlanner.OverlapsStrict(
                            footprint,
                            CityCourtyardPocketPlanner.Expand(
                                homeYard.Value.GroundBounds,
                                CityCourtyardPocketPlanner
                                    .HomeYardClearance)),
                        Is.False,
                        pocket.StableId);
                }

                var proxies = new List<Bounds>();
                CityStaticCollisionBuilder.AddDecorationProxyBounds(
                    layout,
                    pocket,
                    proxies);
                Assert.That(proxies, Has.Count.InRange(1, 4));
                for (int proxyIndex = 0;
                     proxyIndex < proxies.Count;
                     proxyIndex++)
                {
                    Assert.That(
                        Contains(
                            footprint,
                            ToXZRect(proxies[proxyIndex])),
                        Is.True,
                        pocket.StableId);
                    pocketProxies.Add(proxies[proxyIndex]);
                }

                positions.Add(pocket.Position);
            }

            Assert.That(
                variants,
                Does.Contain(
                    CityCourtyardPocketGeometry.NardiVariant));
            Assert.That(
                variants,
                Does.Contain(
                    CityCourtyardPocketGeometry.BicycleVariant));
            Assert.That(
                variants,
                Does.Contain(
                    CityCourtyardPocketGeometry.BalconyBasketVariant));
            Assert.That(
                variants,
                Does.Contain(
                    CityCourtyardPocketPlanner.ResolveOptionalVariant(
                        layout.Seed)));
            AssertMinimumPlanarSpacing(
                positions,
                CityCourtyardPocketPlanner.MinimumPocketSpacing,
                "residential courtyard pockets");
            AssertCourtyardClearOfOtherDecoration(
                layout,
                first,
                pockets);

            CityMountainBoundaryPlan mountainPlan =
                CityMountainBoundaryPlanner.Create(layout);
            CityWindDressingPlan windPlan =
                CityWindDressingPlanner.Create(
                    layout,
                    first,
                    CitySeacoastPlanner.Create(layout),
                    CityCemeteryPlanner.Create(layout),
                    CityFringeYardPlanner.Create(
                        layout,
                        mountainPlan));
            int residentialSupportCount = 0;
            for (int index = 0;
                 index < windPlan.Supports.Count;
                 index++)
            {
                CityWindDressingSupportDescriptor support =
                    windPlan.Supports[index];
                if (support.Zone != CityWindDressingZone.Residential)
                {
                    continue;
                }

                residentialSupportCount++;
                Bounds supportBounds = AxisAlignedBounds(support.Box);
                for (int proxyIndex = 0;
                     proxyIndex < pocketProxies.Count;
                     proxyIndex++)
                {
                    Assert.That(
                        OverlapsStrict(
                            supportBounds,
                            pocketProxies[proxyIndex]),
                        Is.False,
                        $"Wind support '{support.StableId}' intersects " +
                        $"courtyard proxy {proxyIndex}.");
                }
            }

            Assert.That(
                residentialSupportCount,
                Is.GreaterThan(0),
                "Courtyard pockets must adapt existing laundry, not erase it.");
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
        public void ShippedCity_PutsTheWaterNetworkOnTheGround()
        {
            CityDecorationPlan plan = CreateShippedPlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout);

            int drains = 0;
            int standpipes = 0;
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.Kind ==
                    CityDecorationKind.RoadsideDrainAndCover)
                {
                    drains++;
                }
                else if (descriptor.Kind ==
                         CityDecorationKind.RoadsideCappedStandpipe)
                {
                    standpipes++;
                }
                else
                {
                    continue;
                }

                Assert.That(
                    descriptor.AnchorKind,
                    Is.EqualTo(CityDecorationAnchorKind.Roadside));
                Assert.That(
                    CityDecorationCollisionCatalog.ResolveTier(
                        descriptor.Kind),
                    Is.EqualTo(CityDecorationCollisionTier.None),
                    "Flush ironwork must never take a collider.");
            }

            // The city dies of its water: it has to show some. Drains
            // are ordinary municipal frequency, the capped columns are
            // deliberately rare — a failure, not street furniture.
            Assert.That(
                drains,
                Is.GreaterThan(8),
                "A walk should cross a drain without noticing one.");
            Assert.That(standpipes, Is.GreaterThan(0));
            Assert.That(
                standpipes,
                Is.LessThan(drains),
                "A capped standpipe on every corner reads as a style.");
            Assert.That(layout, Is.Not.Null);
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
                { CityDecorationKind.ResidentialCourtyardPocket, CityDecorationCollisionTier.Blocking },
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
                { CityDecorationKind.ParkPlayground, CityDecorationCollisionTier.Blocking },
                { CityDecorationKind.RoadsideDrainAndCover, CityDecorationCollisionTier.None },
                { CityDecorationKind.RoadsideCappedStandpipe, CityDecorationCollisionTier.None },
                { CityDecorationKind.LotGroundDownpipeOutfall, CityDecorationCollisionTier.None }
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
            CityDecorationPlan plan = CreateShippedPlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout);
            Assert.That(
                HomeYardSitePlanner.TryCreate(
                    layout,
                    out HomeYardSitePlan site),
                Is.True);
            Assert.That(
                HomeYardUtilityPlanner.TryCreatePhoneBooth(
                    layout,
                    site,
                    out HomeYardUtilityAnchor boothAnchor),
                Is.True);
            Assert.That(
                HomeYardUtilityPlanner.TryCreateDumpster(
                    layout,
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

        private static List<CityDecorationDescriptor> CollectKind(
            CityDecorationPlan plan,
            CityDecorationKind kind)
        {
            var result = new List<CityDecorationDescriptor>();
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                if (plan.Descriptors[index].Kind == kind)
                {
                    result.Add(plan.Descriptors[index]);
                }
            }

            return result;
        }

        private static void AssertCourtyardFootprintGrounded(
            CityLayout layout,
            BuildingLot lot,
            CityDecorationDescriptor pocket,
            Rect footprint)
        {
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
            for (int index = 0; index < samples.Length; index++)
            {
                Assert.That(
                    CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        samples[index],
                        out float top,
                        out CitySurfaceDescriptor surface),
                    Is.True,
                    pocket.StableId);
                Assert.That(surface.Cell, Is.EqualTo(lot.Cell));
                Assert.That(
                    surface.Kind,
                    Is.EqualTo(CitySurfaceKind.BuildableGround));
                if (index == 0)
                {
                    Assert.That(
                        pocket.Position.y,
                        Is.EqualTo(top).Within(0.001f),
                        pocket.StableId);
                }

                minimum = Mathf.Min(minimum, top);
                maximum = Mathf.Max(maximum, top);
            }

            Assert.That(
                maximum - minimum,
                Is.LessThanOrEqualTo(
                    CityCourtyardPocketPlanner.MaximumGroundDelta +
                    0.001f),
                pocket.StableId);
        }

        private static void AssertCourtyardClearOfAccesses(
            CityLayout layout,
            Rect footprint,
            string stableId)
        {
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                Assert.That(
                    CityCourtyardPocketPlanner.OverlapsStrict(
                        footprint,
                        CityCourtyardPocketPlanner.Expand(
                            layout.OpenAreaAccesses[index]
                                .ApproachBounds,
                            CityCourtyardPocketPlanner
                                .AccessClearance)),
                    Is.False,
                    stableId);
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
                    Assert.That(
                        CityCourtyardPocketPlanner.OverlapsStrict(
                            footprint,
                            CityCourtyardPocketPlanner.Expand(
                                point.Accesses[accessIndex]
                                    .ApproachBounds,
                                CityCourtyardPocketPlanner
                                    .AccessClearance)),
                        Is.False,
                        stableId);
                }
            }
        }

        private static void AssertCourtyardClearOfPointsOfInterest(
            CityLayout layout,
            Rect footprint,
            string stableId)
        {
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[index];
                Assert.That(
                    CityCourtyardPocketPlanner.OverlapsStrict(
                        footprint,
                        CityCourtyardPocketPlanner.Expand(
                            point.PublicBounds,
                            CityCourtyardPocketPlanner
                                .PointOfInterestClearance)),
                    Is.False,
                    stableId);
                if (point.Kind == CityDistrictPointOfInterestKind
                                      .ResidentialDryingYard)
                {
                    Assert.That(
                        CityCourtyardPocketPlanner.OverlapsStrict(
                            footprint,
                            CityCourtyardPocketPlanner.Expand(
                                point.PublicBounds,
                                CityCourtyardPocketPlanner
                                    .DryingYardClearance)),
                        Is.False,
                        stableId);
                }
            }
        }

        private static void AssertCourtyardClearOfOtherDecoration(
            CityLayout layout,
            CityDecorationPlan plan,
            IReadOnlyList<CityDecorationDescriptor> pockets)
        {
            var otherProxies = new List<Bounds>();
            var proxyBuffer = new List<Bounds>();
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.Kind ==
                        CityDecorationKind.ResidentialCourtyardPocket ||
                    descriptor.CollisionTier ==
                        CityDecorationCollisionTier.None)
                {
                    continue;
                }

                proxyBuffer.Clear();
                CityStaticCollisionBuilder.AddDecorationProxyBounds(
                    layout,
                    descriptor,
                    proxyBuffer);
                otherProxies.AddRange(proxyBuffer);
            }

            for (int pocketIndex = 0;
                 pocketIndex < pockets.Count;
                 pocketIndex++)
            {
                Rect footprint = CityCourtyardPocketPlanner.Expand(
                    CityCourtyardPocketGeometry.CreateFootprint(
                        pockets[pocketIndex]),
                    CityCourtyardPocketPlanner.ProxyClearance);
                for (int proxyIndex = 0;
                     proxyIndex < otherProxies.Count;
                     proxyIndex++)
                {
                    Assert.That(
                        CityCourtyardPocketPlanner.OverlapsStrict(
                            footprint,
                            ToXZRect(otherProxies[proxyIndex])),
                        Is.False,
                        pockets[pocketIndex].StableId);
                }
            }
        }

        private static Rect ToXZRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            const float tolerance = 0.001f;
            return inner.xMin >= outer.xMin - tolerance &&
                   inner.xMax <= outer.xMax + tolerance &&
                   inner.yMin >= outer.yMin - tolerance &&
                   inner.yMax <= outer.yMax + tolerance;
        }

        private static Bounds AxisAlignedBounds(RuntimeOrientedBox box)
        {
            Vector3 axisX = box.Rotation *
                new Vector3(box.Size.x * 0.5f, 0f, 0f);
            Vector3 axisY = box.Rotation *
                new Vector3(0f, box.Size.y * 0.5f, 0f);
            Vector3 axisZ = box.Rotation *
                new Vector3(0f, 0f, box.Size.z * 0.5f);
            var extents = new Vector3(
                Mathf.Abs(axisX.x) +
                Mathf.Abs(axisY.x) +
                Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) +
                Mathf.Abs(axisY.y) +
                Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) +
                Mathf.Abs(axisY.z) +
                Mathf.Abs(axisZ.z));
            return new Bounds(box.Center, extents * 2f);
        }

        private static bool OverlapsStrict(Bounds left, Bounds right)
        {
            const float epsilon = 0.001f;
            return left.min.x < right.max.x - epsilon &&
                   left.max.x > right.min.x + epsilon &&
                   left.min.y < right.max.y - epsilon &&
                   left.max.y > right.min.y + epsilon &&
                   left.min.z < right.max.z - epsilon &&
                   left.max.z > right.min.z + epsilon;
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

        /// <summary>
        /// The shipped city. The legacy blueprint the other cases run
        /// on carries no home yard at every seed, so anything that
        /// asserts on the yard has to ask for the real one.
        /// </summary>
        private static CityDecorationPlan CreateShippedPlan(
            int seed,
            out CityLayout layout)
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                seed);
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            return CityDecorationPlanner.CreatePlan(
                layout,
                fence,
                night);
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
