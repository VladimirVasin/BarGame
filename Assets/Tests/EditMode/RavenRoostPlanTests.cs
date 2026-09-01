using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Holds the three pure outdoor roost planners to their contract:
    /// deterministic output across a full rebuild, honest pairwise
    /// spacing measured on the real accepted anchors, per-area count
    /// bands, every canon exclusion enforced as geometry on BOTH
    /// perches, ring-resolved companion perches that genuinely stand
    /// on the area's own teleport ground, and authored perch heights
    /// that come from the named plan datums (LowerY, WestY, part
    /// tops, rail height, portal floor) rather than from a resolver
    /// that knows nothing about decks.
    ///
    /// The accepted id LIST is deliberately not pinned: the greedy
    /// selection with silent drop-outs is unverifiable before a real
    /// run, so one test only WRITES the measured ids and the pairwise
    /// distance matrix for the default seeds — the lead reads that
    /// output and pins the list afterwards, implement-run-then-pin.
    /// </summary>
    public sealed class RavenRoostPlanTests
    {
        private const int CitySeed = GameSessionState.DefaultCitySeed;

        /// <summary>The seed every mountain-road EditMode test
        /// drives (the CityMapAreaPresentation precedent), so the
        /// measured roosts describe the same road those tests pin.
        /// </summary>
        private const int RoadSeed = 58021;

        private const int VillageSeed =
            GameSessionState.DefaultCitySeed;

        private const float Tolerance = 0.001f;

        /// <summary>Roosts whose companion perch B comes from the
        /// seeded ground ring. Every other roost authors B against a
        /// plan datum and must never touch the resolver — the
        /// teleport grounds have no answer over water or decks.
        /// </summary>
        private static readonly string[] CityRingRoostIds =
        {
            "city-roost-park-fountain",
            "city-roost-tunnel-forecourt",
            "city-roost-park-bandstand",
            "city-roost-clock-plaza",
            "city-roost-plain-kerb-a",
            "city-roost-plain-kerb-b",
            "city-roost-plain-kerb-c",
            "city-roost-plain-kerb-d"
        };

        private static readonly string[] RoadRingRoostIds =
        {
            "road-roost-exit-portal",
            "road-roost-culvert"
        };

        private static readonly string[] VillageRingRoostIds =
        {
            "village-roost-adit",
            "village-roost-woodpile",
            "village-roost-lane-fence"
        };

        private GameObject cityHost;
        private CityLayout cityLayout;
        private CityWorldResult cityWorld;
        private CityMapCityTeleportGround cityGround;
        private IReadOnlyList<RavenRoostDescriptor> cityRoosts;

        private MountainRoadPlan roadPlan;
        private CityMapMountainRoadTeleportGround roadGround;
        private IReadOnlyList<RavenRoostDescriptor> roadRoosts;

        private AlpineVillagePlan villagePlan;
        private CityMapAlpineVillageTeleportGround villageGround;
        private IReadOnlyList<RavenRoostDescriptor> villageRoosts;
        private AlpineVillageSoundAnchorDescriptor villageDog;

        [OneTimeSetUp]
        public void BuildAllThreeAreasOnce()
        {
            // The city world build is the expensive step; one build
            // serves every test here, and the determinism test does
            // its own second build to have something to compare.
            cityLayout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                CitySeed);
            cityHost = new GameObject("Roost Plan Test City World");
            cityWorld = CityWorldBuilder.Build(
                cityHost.transform,
                cityLayout,
                CityGenerationSettings.Default);
            cityGround = new CityMapCityTeleportGround(cityLayout);
            cityRoosts = CityRavenRoostPlanner.Create(
                cityLayout,
                cityWorld,
                cityGround,
                CitySeed);

            roadPlan = MountainRoadPlanner.Create(RoadSeed);
            roadGround =
                new CityMapMountainRoadTeleportGround(roadPlan);
            roadRoosts = MountainRoadRavenRoostPlanner.Create(
                roadPlan,
                roadGround,
                RoadSeed);

            villagePlan = AlpineVillagePlanner.Create(VillageSeed);
            villageGround =
                new CityMapAlpineVillageTeleportGround(villagePlan);
            villageRoosts = AlpineVillageRavenRoostPlanner.Create(
                villagePlan,
                villageGround,
                VillageSeed);
            villageDog = AlpineVillageSoundscapePlanner
                .Create(villagePlan)
                .GetRequiredAnchor(
                    AlpineVillageSoundKind.DogBehindFence);
        }

        [OneTimeTearDown]
        public void TearDownTheCityWorld()
        {
            if (cityHost != null)
            {
                Object.DestroyImmediate(cityHost);
            }
        }

        [Test]
        public void Planners_AreDeterministicAcrossARebuild()
        {
            // The city: a genuinely fresh layout AND world from the
            // same seed must reproduce every descriptor element-wise
            // — the controller re-derives bird seeds from these ids
            // and positions on every scene load.
            CityLayout layoutAgain = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                CitySeed);
            var hostAgain = new GameObject(
                "Roost Plan Test City World Again");
            try
            {
                CityWorldResult worldAgain = CityWorldBuilder.Build(
                    hostAgain.transform,
                    layoutAgain,
                    CityGenerationSettings.Default);
                AssertSamePlan(
                    cityRoosts,
                    CityRavenRoostPlanner.Create(
                        layoutAgain,
                        worldAgain,
                        new CityMapCityTeleportGround(layoutAgain),
                        CitySeed),
                    "City");
            }
            finally
            {
                Object.DestroyImmediate(hostAgain);
            }

            MountainRoadPlan roadAgain =
                MountainRoadPlanner.Create(RoadSeed);
            AssertSamePlan(
                roadRoosts,
                MountainRoadRavenRoostPlanner.Create(
                    roadAgain,
                    new CityMapMountainRoadTeleportGround(roadAgain),
                    RoadSeed),
                "MountainRoad");

            AlpineVillagePlan villageAgain =
                AlpineVillagePlanner.Create(VillageSeed);
            AssertSamePlan(
                villageRoosts,
                AlpineVillageRavenRoostPlanner.Create(
                    villageAgain,
                    new CityMapAlpineVillageTeleportGround(
                        villageAgain),
                    VillageSeed),
                "AlpineVillage");
        }

        [Test]
        public void Counts_StayInsideTheAuthoredPerAreaBands()
        {
            Assert.That(
                cityRoosts.Count,
                Is.InRange(10, 14),
                "The city brief is ten to fourteen pairs.");
            Assert.That(
                roadRoosts.Count,
                Is.InRange(3, 4),
                "The road degrades to three roosts, never fewer.");
            Assert.That(
                villageRoosts.Count,
                Is.EqualTo(1),
                "The village fields exactly one roost: the adit's own edge " +
                "and the woodpile beside it went out of the village with " +
                "the adit, and the lane fence is what is left.");

            AssertWellFormed(cityRoosts, "City");
            AssertWellFormed(roadRoosts, "MountainRoad");
            AssertWellFormed(villageRoosts, "AlpineVillage");
        }

        [Test]
        public void DefaultSeeds_KeepTheMeasuredRoostRosters()
        {
            // Pinned from the measured planner runs (2026-08-31,
            // default seeds; re-pinned after the 45 m density pass).
            // The greedy pass with every exclusion active accepted
            // exactly these roosts in this order. Dropped candidates,
            // all by their own rules: the tunnel forecourt (both
            // street-side and yard-side probes fail the
            // ground/clearance gates), the second river landing and
            // the clock plaza (spacing/clearance on this seed), the
            // village woodpile (the adit's spacing circle). A change
            // here is a change to the world the player explores —
            // deliberate or a regression.
            AssertRoster(
                cityRoosts,
                new[]
                {
                    "city-roost-park-fountain",
                    "city-roost-river-landing",
                    "city-roost-mol-head",
                    "city-roost-east-barge",
                    "city-roost-road-bridge",
                    "city-roost-park-bandstand",
                    "city-roost-plain-kerb-a",
                    "city-roost-plain-kerb-b",
                    "city-roost-plain-kerb-c",
                    "city-roost-plain-kerb-d"
                },
                "City");
            AssertRoster(
                roadRoosts,
                new[]
                {
                    "road-roost-gorge-bridge",
                    "road-roost-exit-portal",
                    "road-roost-summit-brink"
                },
                "MountainRoad");
            AssertRoster(
                villageRoosts,
                new[]
                {
                    "village-roost-lane-fence"
                },
                "AlpineVillage");
        }

        /// <summary>
        /// The temporary lenient form of the roster pin: every id in
        /// the expected list must still appear among the accepted
        /// roosts, in the same relative order, with new ids free to
        /// stand between them. It exists only for the density slice's
        /// implement-run-then-pin window; the lead replaces it with
        /// AssertRoster once the denser plan has a measured run.
        /// </summary>
        private static void AssertContainsInOrder(
            IReadOnlyList<RavenRoostDescriptor> roosts,
            string[] expectedSubset,
            string area)
        {
            int cursor = 0;
            for (int index = 0;
                 index < roosts.Count &&
                 cursor < expectedSubset.Length;
                 index++)
            {
                if (roosts[index].StableId ==
                    expectedSubset[cursor])
                {
                    cursor++;
                }
            }

            Assert.That(
                cursor,
                Is.EqualTo(expectedSubset.Length),
                area + " lost or reordered a previously pinned " +
                "roost; first missing in order: " +
                (cursor < expectedSubset.Length
                    ? expectedSubset[cursor]
                    : "(none)") + ".");
        }

        private static void AssertRoster(
            IReadOnlyList<RavenRoostDescriptor> roosts,
            string[] expected,
            string area)
        {
            Assert.That(
                roosts.Count,
                Is.EqualTo(expected.Length),
                area + " roster size drifted from the pinned run.");
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.That(
                    roosts[index].StableId,
                    Is.EqualTo(expected[index]),
                    area + " roost " + index);
            }
        }

        [Test]
        public void Spacing_RealPairwisePlanarDistancesHonourEachStep()
        {
            AssertSpacing(
                cityRoosts,
                CityRavenRoostPlanner.MinimumRoostSpacingMeters,
                "City");
            AssertSpacing(
                roadRoosts,
                MountainRoadRavenRoostPlanner
                    .MinimumRoostSpacingMeters,
                "MountainRoad");
            AssertSpacing(
                villageRoosts,
                AlpineVillageRavenRoostPlanner
                    .MinimumRoostSpacingMeters,
                "AlpineVillage");
        }

        [Test]
        public void City_ForbiddenGroundsAreClearOfBothPerches()
        {
            Assert.That(cityWorld.CemeteryPlan, Is.Not.Null,
                "The default city must carry a cemetery to keep " +
                "the grave pair unique against.");
            Assert.That(cityWorld.SeacoastPlan, Is.Not.Null,
                "The default city must carry the dressed coast.");

            for (int index = 0; index < cityRoosts.Count; index++)
            {
                RavenRoostDescriptor roost = cityRoosts[index];
                foreach (Vector2 point in PerchPointsXZ(roost))
                {
                    string label = roost.StableId + " at " + point;
                    Assert.That(
                        ContainsInclusive(
                            cityWorld.CemeteryPlan.Grounds,
                            point),
                        Is.False,
                        label + " stands in the cemetery precinct.");
                    if (cityWorld.ChurchPlan != null)
                    {
                        Assert.That(
                            ContainsInclusive(
                                cityWorld.ChurchPlan.ApproachBounds,
                                point),
                            Is.False,
                            label + " stands on the church approach.");
                    }

                    if (cityWorld.ChurchCourtyardPlan != null)
                    {
                        Assert.That(
                            ContainsInclusive(
                                cityWorld.ChurchCourtyardPlan
                                    .ForecourtBounds,
                                point),
                            Is.False,
                            label + " stands in the church " +
                            "forecourt.");
                        Assert.That(
                            ContainsInclusive(
                                cityWorld.ChurchCourtyardPlan
                                    .GardenBounds,
                                point),
                            Is.False,
                            label + " stands in the church garden.");
                    }

                    for (int poi = 0;
                         poi < cityLayout
                             .DistrictPointsOfInterest.Count;
                         poi++)
                    {
                        // The 12 m inflation IS the raven voice's
                        // audible radius: the waterworks court's
                        // «ни отдельного звука» sentence as geometry.
                        Assert.That(
                            ContainsInclusive(
                                Inflate(
                                    cityLayout
                                        .DistrictPointsOfInterest[
                                            poi].PublicBounds,
                                    CityRavenRoostPlanner
                                        .PointOfInterestClearanceMeters),
                                point),
                            Is.False,
                            label + " stands inside a point of " +
                            "interest's audible clearance.");
                    }

                    Assert.That(
                        ContainsInclusive(
                            cityWorld.SeacoastPlan.Frame.CenterZone,
                            point),
                        Is.False,
                        label + " stands in the boat station's " +
                        "closed centre zone.");

                    IReadOnlyList<CityFringeYardDescriptor> yards =
                        cityWorld.FringeYardPlan.Yards;
                    for (int yard = 0; yard < yards.Count; yard++)
                    {
                        IReadOnlyList<CityFringeYardPartDescriptor>
                            parts = yards[yard].Parts;
                        for (int part = 0;
                             part < parts.Count;
                             part++)
                        {
                            if (!IsFringeWorkScene(parts[part].Kind))
                            {
                                continue;
                            }

                            Assert.That(
                                Vector2.Distance(
                                    point,
                                    XZ(parts[part].Center)),
                                Is.GreaterThanOrEqualTo(
                                    CityRavenRoostPlanner
                                        .FringeWorkSceneClearanceMeters -
                                    Tolerance),
                                label + " crowds a fringe work " +
                                "scene.");
                        }
                    }

                    IReadOnlyList<CityDecorationDescriptor>
                        decorations =
                            cityWorld.DecorationPlan.Descriptors;
                    for (int deco = 0;
                         deco < decorations.Count;
                         deco++)
                    {
                        if (decorations[deco].Kind !=
                            CityDecorationKind.ParkChessTables)
                        {
                            continue;
                        }

                        Assert.That(
                            Vector2.Distance(
                                point,
                                XZ(decorations[deco].Position)),
                            Is.GreaterThanOrEqualTo(
                                CityRavenRoostPlanner
                                    .ChessTablesClearanceMeters -
                                Tolerance),
                            label + " crowds the chess tables.");
                    }
                }
            }
        }

        [Test]
        public void City_AuthoredPerchHeightsComeFromTheNamedDatums()
        {
            if (TryFind(
                    cityRoosts,
                    "city-roost-river-landing",
                    out RavenRoostDescriptor landingRoost))
            {
                Assert.That(
                    TrySelectLanding(
                        cityLayout,
                        out CityRiverLandingDescriptor landing),
                    Is.True);
                Assert.That(
                    landingRoost.PerchA.Position.y,
                    Is.EqualTo(landing.LowerY).Within(Tolerance),
                    "The landing anchor bird stands on the plan's " +
                    "LowerY datum, never on a resolver's quay.");
                Assert.That(
                    landingRoost.PerchB.Position.y,
                    Is.EqualTo(landing.LowerY).Within(Tolerance),
                    "The landing companion shares the platform " +
                    "datum.");
            }

            if (TryFind(
                    cityRoosts,
                    "city-roost-road-bridge",
                    out RavenRoostDescriptor bridgeRoost))
            {
                Assert.That(
                    TrySelectLanding(
                        cityLayout,
                        out CityRiverLandingDescriptor landing),
                    Is.True);
                Assert.That(
                    TrySelectOtherRoadBridge(
                        cityLayout,
                        landing.BridgeId,
                        out CityRiverBridgeDescriptor bridge),
                    Is.True);
                Assert.That(
                    bridgeRoost.PerchA.Position.y,
                    Is.EqualTo(bridge.WestY).Within(Tolerance),
                    "The bridge kerb bird stands on the deck's " +
                    "WestY datum.");
                Assert.That(
                    bridgeRoost.PerchB.Position.y,
                    Is.EqualTo(bridge.WestY).Within(Tolerance),
                    "The bridge companion shares the deck datum.");
            }

            if (TryFind(
                    cityRoosts,
                    "city-roost-mol-head",
                    out RavenRoostDescriptor molRoost))
            {
                CitySeacoastPlan coast = cityWorld.SeacoastPlan;
                Assert.That(
                    TrySelectPartByWaterline(
                        coast,
                        CitySeacoastPartKind.MolParapet,
                        true,
                        out CitySeacoastPartDescriptor parapet),
                    Is.True);
                Assert.That(
                    molRoost.PerchA.Position.y,
                    Is.EqualTo(
                        parapet.Center.y + parapet.Size.y * 0.5f)
                        .Within(Tolerance),
                    "The mol anchor bird stands on the head " +
                    "parapet's own coping top.");
                Assert.That(
                    XZ(molRoost.PerchA.Position),
                    Is.EqualTo(XZ(parapet.Center)),
                    "The mol anchor sits on the head parapet's " +
                    "centre line.");
                Assert.That(
                    TrySelectPartByWaterline(
                        coast,
                        CitySeacoastPartKind.MolDeck,
                        true,
                        out CitySeacoastPartDescriptor headDeck),
                    Is.True);
                Assert.That(
                    molRoost.PerchB.Position.y,
                    Is.EqualTo(
                        headDeck.Center.y + headDeck.Size.y * 0.5f)
                        .Within(Tolerance),
                    "The mol companion stands on the head deck's " +
                    "own top.");
            }

            if (TryFind(
                    cityRoosts,
                    "city-roost-east-barge",
                    out RavenRoostDescriptor bargeRoost))
            {
                CitySeacoastPlan coast = cityWorld.SeacoastPlan;
                Assert.That(
                    TrySelectBargeDeck(
                        coast,
                        out CitySeacoastPartDescriptor deck),
                    Is.True);
                Assert.That(
                    ContainsInclusive(
                        coast.Frame.EastZone,
                        XZ(deck.Center)),
                    Is.True,
                    "The barge hull must lie in the wild east " +
                    "zone — the centre zone tableau is closed.");
                float gunwaleTop =
                    deck.Center.y + deck.Size.y * 0.5f;
                Assert.That(
                    bargeRoost.PerchA.Position.y,
                    Is.EqualTo(gunwaleTop).Within(Tolerance),
                    "The barge anchor bird stands on the hull's " +
                    "own gunwale top.");
                Assert.That(
                    bargeRoost.PerchB.Position.y,
                    Is.EqualTo(gunwaleTop).Within(Tolerance),
                    "The barge companion shares the gunwale top.");
            }
        }

        [Test]
        public void Road_AuthoredHeightsAndTheBrinkSeatClearanceHold()
        {
            if (TryFind(
                    roadRoosts,
                    "road-roost-gorge-bridge",
                    out RavenRoostDescriptor gorgeRoost))
            {
                Assert.That(
                    gorgeRoost.PerchA.Position.y,
                    Is.EqualTo(
                        roadPlan.Bridge.Start.y +
                        roadPlan.Bridge.RailHeight)
                        .Within(Tolerance),
                    "The gorge bird stands on the rail top: deck " +
                    "start plus the descriptor's own rail height.");
            }

            if (TryFind(
                    roadRoosts,
                    "road-roost-exit-portal",
                    out RavenRoostDescriptor portalRoost))
            {
                Assert.That(
                    portalRoost.PerchA.Position.y,
                    Is.EqualTo(
                        roadPlan.Tunnel.PortalGroundCenter.y)
                        .Within(Tolerance),
                    "The portal bird keeps the portal floor's own " +
                    "plan height.");
            }

            if (TryFind(
                    roadRoosts,
                    "road-roost-summit-brink",
                    out RavenRoostDescriptor brinkRoost))
            {
                Assert.That(
                    TrySelectBrinkPart(
                        roadPlan,
                        out MountainRoadSitePartDescriptor part),
                    Is.True);
                Assert.That(
                    brinkRoost.PerchA.Position.y,
                    Is.EqualTo(part.Center.y + part.Size.y * 0.5f)
                        .Within(Tolerance),
                    "The brink bird stands on the dressed-stone " +
                    "coping's own top.");
                // The adversarial fix: 3 m sat inside the 3.5 m
                // flush radius, so every walk to the bench would
                // have scattered the showcase pair.
                Assert.That(
                    Vector2.Distance(
                        XZ(brinkRoost.PerchA.Position),
                        XZ(roadPlan.Terminal.Site.BrinkSeat
                            .SeatTopCenter)),
                    Is.GreaterThanOrEqualTo(
                        MountainRoadRavenRoostPlanner
                            .BrinkSeatClearanceMeters - Tolerance),
                    "The brink perch must clear the bench by the " +
                    "flush radius plus approach slack.");
                Assert.That(
                    brinkRoost.PerchB.Position.y,
                    Is.EqualTo(roadPlan.Terminal.Site.TerraceTopY)
                        .Within(Tolerance),
                    "The brink companion stands on the terrace " +
                    "floor's own datum.");
            }
        }

        [Test]
        public void Village_ForbiddenGroundsAreClearOfBothPerches()
        {
            AlpineVillagePlotDescriptor chapel = FindPlot(
                villagePlan,
                AlpineVillagePlotKind.Chapel);
            Assert.That(chapel, Is.Not.Null,
                "The village always builds its chapel — the crime " +
                "scene the roosts must clear.");
            MountainRoadTerminalRect pad =
                villagePlan.Station.PadArea;
            Vector2 dogXZ = XZ(villageDog.OwnerPosition);

            for (int index = 0;
                 index < villageRoosts.Count;
                 index++)
            {
                RavenRoostDescriptor roost = villageRoosts[index];
                foreach (Vector2 point in PerchPointsXZ(roost))
                {
                    string label = roost.StableId + " at " + point;
                    // The chapel gets the waterworks treatment:
                    // place plus the birds' whole audible radius.
                    Assert.That(
                        ContainsInclusive(
                            Inflate(
                                chapel.BoundsXZ,
                                AlpineVillageRavenRoostPlanner
                                    .ChapelClearanceMeters),
                            point),
                        Is.False,
                        label + " stands inside the chapel's " +
                        "audible clearance.");
                    Assert.That(
                        ContainsInclusive(
                            villagePlan.MothersHouse.BoundsXZ,
                            point),
                        Is.False,
                        label + " stands on the mother's house " +
                        "plot.");
                    // Probed at the pad's own height, exactly as
                    // the planner probes it: the containment test
                    // projects onto the pad's axes.
                    Assert.That(
                        pad.ContainsXZ(new Vector3(
                            point.x,
                            pad.Center.y,
                            point.y)),
                        Is.False,
                        label + " stands on the station pad.");
                    Assert.That(
                        Vector2.Distance(point, dogXZ),
                        Is.GreaterThanOrEqualTo(
                            AlpineVillageRavenRoostPlanner
                                .DogClearanceMeters - Tolerance),
                        label + " crowds the dog's yard.");
                }
            }
        }

        [Test]
        public void RingCompanionPerches_StandOnTheAreasOwnGround()
        {
            AssertRingCompanions(
                cityRoosts, CityRingRoostIds, cityGround, "City");
            AssertRingCompanions(
                roadRoosts,
                RoadRingRoostIds,
                roadGround,
                "MountainRoad");
            AssertRingCompanions(
                villageRoosts,
                VillageRingRoostIds,
                villageGround,
                "AlpineVillage");
        }

        /// <summary>
        /// Not an assertion — a measurement. The greedy selection is
        /// unverifiable before a run, so this test prints what the
        /// default seeds actually accepted and how far apart the
        /// anchors really stand; the lead reads this output and pins
        /// the id list afterwards.
        /// </summary>
        [Test]
        public void Log_MeasuredRoostIdsAndPairwiseDistances()
        {
            WriteArea("City seed " + CitySeed, cityRoosts);
            WriteArea("MountainRoad seed " + RoadSeed, roadRoosts);
            WriteArea(
                "AlpineVillage seed " + VillageSeed,
                villageRoosts);
        }

        private static void WriteArea(
            string title,
            IReadOnlyList<RavenRoostDescriptor> roosts)
        {
            TestContext.WriteLine(
                title + ": " + roosts.Count + " roosts");
            for (int index = 0; index < roosts.Count; index++)
            {
                Vector3 a = roosts[index].PerchA.Position;
                Vector3 b = roosts[index].PerchB.Position;
                TestContext.WriteLine(
                    "  " + roosts[index].StableId +
                    "  A=(" + a.x.ToString("F2") + ", " +
                    a.y.ToString("F2") + ", " +
                    a.z.ToString("F2") + ")" +
                    "  B=(" + b.x.ToString("F2") + ", " +
                    b.y.ToString("F2") + ", " +
                    b.z.ToString("F2") + ")");
            }

            for (int first = 0; first < roosts.Count; first++)
            {
                for (int second = first + 1;
                     second < roosts.Count;
                     second++)
                {
                    float distance = Vector2.Distance(
                        XZ(roosts[first].PerchA.Position),
                        XZ(roosts[second].PerchA.Position));
                    TestContext.WriteLine(
                        "  " + roosts[first].StableId + " <-> " +
                        roosts[second].StableId + " = " +
                        distance.ToString("F1") + " m");
                }
            }
        }

        private static void AssertSamePlan(
            IReadOnlyList<RavenRoostDescriptor> expected,
            IReadOnlyList<RavenRoostDescriptor> actual,
            string area)
        {
            Assert.That(
                actual.Count,
                Is.EqualTo(expected.Count),
                area + " roost count drifted across a rebuild.");
            for (int index = 0; index < expected.Count; index++)
            {
                string label = area + " roost " + index;
                Assert.That(
                    actual[index].StableId,
                    Is.EqualTo(expected[index].StableId),
                    label);
                Assert.That(
                    actual[index].PerchA.Position,
                    Is.EqualTo(expected[index].PerchA.Position),
                    label + " perch A position");
                Assert.That(
                    actual[index].PerchA.YawDegrees,
                    Is.EqualTo(expected[index].PerchA.YawDegrees),
                    label + " perch A yaw");
                Assert.That(
                    actual[index].PerchB.Position,
                    Is.EqualTo(expected[index].PerchB.Position),
                    label + " perch B position");
                Assert.That(
                    actual[index].PerchB.YawDegrees,
                    Is.EqualTo(expected[index].PerchB.YawDegrees),
                    label + " perch B yaw");
            }
        }

        private static void AssertWellFormed(
            IReadOnlyList<RavenRoostDescriptor> roosts,
            string area)
        {
            var seen = new HashSet<string>();
            for (int index = 0; index < roosts.Count; index++)
            {
                RavenRoostDescriptor roost = roosts[index];
                Assert.That(
                    seen.Add(roost.StableId),
                    Is.True,
                    area + " repeats id " + roost.StableId);
                Assert.That(roost.PerchA.IsPresent, Is.True);
                Assert.That(roost.PerchB.IsPresent, Is.True);
                // The perch PlotId is the roost's registry key:
                // seeds and idle offsets derive from it.
                Assert.That(
                    roost.PerchA.PlotId,
                    Is.EqualTo(roost.StableId));
                Assert.That(
                    roost.PerchB.PlotId,
                    Is.EqualTo(roost.StableId));
                Assert.That(
                    roost.HomeReference,
                    Is.EqualTo(roost.PerchA.Position),
                    "The pair has one home: perch A.");
            }
        }

        private static void AssertSpacing(
            IReadOnlyList<RavenRoostDescriptor> roosts,
            float minimumMeters,
            string area)
        {
            for (int first = 0; first < roosts.Count; first++)
            {
                for (int second = first + 1;
                     second < roosts.Count;
                     second++)
                {
                    Assert.That(
                        Vector2.Distance(
                            XZ(roosts[first].PerchA.Position),
                            XZ(roosts[second].PerchA.Position)),
                        Is.GreaterThanOrEqualTo(
                            minimumMeters - Tolerance),
                        area + ": " + roosts[first].StableId +
                        " and " + roosts[second].StableId +
                        " stand closer than the step.");
                }
            }
        }

        private static void AssertRingCompanions(
            IReadOnlyList<RavenRoostDescriptor> roosts,
            string[] ringIds,
            ICityMapTeleportGround ground,
            string area)
        {
            for (int index = 0; index < ringIds.Length; index++)
            {
                if (!TryFind(
                        roosts,
                        ringIds[index],
                        out RavenRoostDescriptor roost))
                {
                    // Dropped by spacing or exclusion — the count
                    // band test owns how many may drop.
                    continue;
                }

                Vector3 a = roost.PerchA.Position;
                Vector3 b = roost.PerchB.Position;
                string label = area + " " + roost.StableId;
                float planar = Vector2.Distance(XZ(a), XZ(b));
                Assert.That(
                    planar,
                    Is.InRange(
                        CemeteryRavenPlan
                            .GroundPerchBandMinimumMeters,
                        RavenRoostPlan
                            .GroundPerchFallbackMaximumMeters),
                    label + " companion sits outside the pair " +
                    "band.");

                // The companion must stand on ground the area's
                // own authority accepts, at the surface height
                // (the wrappers ADD the capsule skin offset the
                // planner removed).
                Assert.That(
                    ground.TryResolveStandingPosition(
                        XZ(b),
                        out Vector3 standing),
                    Is.True,
                    label + " companion is not standable ground.");
                Assert.That(
                    standing.x,
                    Is.EqualTo(b.x).Within(Tolerance),
                    label + " companion x clamped away.");
                Assert.That(
                    standing.z,
                    Is.EqualTo(b.z).Within(Tolerance),
                    label + " companion z clamped away.");
                Assert.That(
                    b.y,
                    Is.EqualTo(
                        standing.y -
                        PlayerFactory.GroundedRootOffset)
                        .Within(Tolerance),
                    label + " companion height is not the " +
                    "surface's own.");
            }
        }

        private static bool TryFind(
            IReadOnlyList<RavenRoostDescriptor> roosts,
            string stableId,
            out RavenRoostDescriptor found)
        {
            for (int index = 0; index < roosts.Count; index++)
            {
                if (roosts[index].StableId == stableId)
                {
                    found = roosts[index];
                    return true;
                }
            }

            found = default;
            return false;
        }

        private static Vector2[] PerchPointsXZ(
            in RavenRoostDescriptor roost)
        {
            return new[]
            {
                XZ(roost.PerchA.Position),
                XZ(roost.PerchB.Position)
            };
        }

        private static bool TrySelectLanding(
            CityLayout layout,
            out CityRiverLandingDescriptor landing)
        {
            landing = default;
            if (!layout.River.IsEnabled)
            {
                return false;
            }

            bool found = false;
            for (int index = 0;
                 index < layout.River.Landings.Count;
                 index++)
            {
                CityRiverLandingDescriptor candidate =
                    layout.River.Landings[index];
                if (!found ||
                    string.CompareOrdinal(
                        candidate.Id,
                        landing.Id) < 0)
                {
                    found = true;
                    landing = candidate;
                }
            }

            return found;
        }

        private static bool TrySelectOtherRoadBridge(
            CityLayout layout,
            string landingBridgeId,
            out CityRiverBridgeDescriptor bridge)
        {
            bridge = default;
            bool found = false;
            for (int index = 0;
                 index < layout.River.Bridges.Count;
                 index++)
            {
                CityRiverBridgeDescriptor candidate =
                    layout.River.Bridges[index];
                if (!candidate.Definition.CarriesRoadTraffic ||
                    string.Equals(
                        candidate.Definition.Id,
                        landingBridgeId,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!found ||
                    string.CompareOrdinal(
                        candidate.Definition.Id,
                        bridge.Definition.Id) < 0)
                {
                    found = true;
                    bridge = candidate;
                }
            }

            return found;
        }

        /// <summary>The planner's own head/root rule, restated: the
        /// part of a kind farthest from the waterline is the head,
        /// ordinal-smaller stable id breaking float ties.</summary>
        private static bool TrySelectPartByWaterline(
            CitySeacoastPlan coast,
            CitySeacoastPartKind kind,
            bool farthest,
            out CitySeacoastPartDescriptor selected)
        {
            selected = default;
            bool found = false;
            float bestDistance = 0f;
            float waterlineZ = coast.Frame.WaterlineZ;
            for (int index = 0; index < coast.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part =
                    coast.Parts[index];
                if (part.Kind != kind)
                {
                    continue;
                }

                float distance = Mathf.Abs(
                    part.Center.z - waterlineZ);
                bool strictlyBetter = farthest
                    ? distance > bestDistance + 0.0005f
                    : distance < bestDistance - 0.0005f;
                bool tied = found &&
                            Mathf.Abs(distance - bestDistance) <=
                            0.0005f &&
                            string.CompareOrdinal(
                                part.StableId,
                                selected.StableId) < 0;
                if (!found || strictlyBetter || tied)
                {
                    found = true;
                    selected = part;
                    bestDistance = distance;
                }
            }

            return found;
        }

        /// <summary>The barge deck is the widest-footprint Barge
        /// box, the planner's own selection restated.</summary>
        private static bool TrySelectBargeDeck(
            CitySeacoastPlan coast,
            out CitySeacoastPartDescriptor deck)
        {
            deck = default;
            bool found = false;
            float bestArea = float.NegativeInfinity;
            for (int index = 0; index < coast.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part =
                    coast.Parts[index];
                if (part.Kind != CitySeacoastPartKind.Barge)
                {
                    continue;
                }

                float area = part.Size.x * part.Size.z;
                bool better = !found ||
                              area > bestArea + 0.0001f ||
                              (area > bestArea - 0.0001f &&
                               string.CompareOrdinal(
                                   part.StableId,
                                   deck.StableId) < 0);
                if (better)
                {
                    found = true;
                    deck = part;
                    bestArea = area;
                }
            }

            return found;
        }

        private static bool TrySelectBrinkPart(
            MountainRoadPlan plan,
            out MountainRoadSitePartDescriptor part)
        {
            part = default;
            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            if (site == null)
            {
                return false;
            }

            Vector2 seatXZ = XZ(site.BrinkSeat.SeatTopCenter);
            bool found = false;
            for (int index = 0; index < site.Parts.Count; index++)
            {
                MountainRoadSitePartDescriptor candidate =
                    site.Parts[index];
                if (candidate.Group !=
                    MountainRoadSiteGroup.Brink ||
                    candidate.Style !=
                    MountainRoadSiteStyle.DressedStone)
                {
                    continue;
                }

                if (Vector2.Distance(
                        XZ(candidate.Center),
                        seatXZ) <
                    MountainRoadRavenRoostPlanner
                        .BrinkSeatClearanceMeters)
                {
                    continue;
                }

                if (!found ||
                    string.CompareOrdinal(
                        candidate.StableId,
                        part.StableId) < 0)
                {
                    found = true;
                    part = candidate;
                }
            }

            return found;
        }

        private static AlpineVillagePlotDescriptor FindPlot(
            AlpineVillagePlan plan,
            AlpineVillagePlotKind kind)
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (plan.Plots[index].Kind == kind)
                {
                    return plan.Plots[index];
                }
            }

            return null;
        }

        private static bool IsFringeWorkScene(
            CityFringeYardPartKind kind)
        {
            return kind == CityFringeYardPartKind.MasonCart;
        }

        private static bool ContainsInclusive(
            Rect rect,
            Vector2 point)
        {
            return point.x >= rect.xMin &&
                   point.x <= rect.xMax &&
                   point.y >= rect.yMin &&
                   point.y <= rect.yMax;
        }

        private static Rect Inflate(Rect rect, float amount)
        {
            return Rect.MinMaxRect(
                rect.xMin - amount,
                rect.yMin - amount,
                rect.xMax + amount,
                rect.yMax + amount);
        }

        private static Vector2 XZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }
}
