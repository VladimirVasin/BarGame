using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerHomeLayoutTests
    {
        [TestCase(GameSessionState.DefaultCitySeed)]
        [TestCase(73119)]
        [TestCase(-99123)]
        [TestCase(0)]
        public void Generate_DefaultCityPlacesOneDeterministicHomeNearBar(
            int seed)
        {
            CityLayout first = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                seed);
            CityLayout second = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                seed);
            BuildingLot home = first.PlayerHome;

            Assert.That(home, Is.Not.Null);
            Assert.That(
                first.BuildingLots.Count(lot => lot.IsPlayerHome),
                Is.EqualTo(1));
            Assert.That(home.IsBar, Is.False);
            Assert.That(home.IsPark, Is.False);
            Assert.That(home.HasBuilding, Is.True);
            Vector2 expectedSize = home.FrontageDirection.x != 0
                ? new Vector2(12f, 13f)
                : new Vector2(13f, 12f);
            Assert.That(home.Size, Is.EqualTo(expectedSize));
            Assert.That(
                home.Height,
                Is.EqualTo(
                        PlayerHomeBalconyGeometry
                            .ResolveBuildingHeight(
                                CityGenerationSettings.Default))
                    .Within(0.001f));
            Assert.That(
                PlayerHomeBalconyGeometry.SupportsThirdFloor(
                    home.Height),
                Is.True);
            Assert.That(
                second.PlayerHome.Cell,
                Is.EqualTo(home.Cell));
            Assert.That(
                second.PlayerHome.FrontageDirection,
                Is.EqualTo(home.FrontageDirection));
            Assert.That(
                second.PlayerHome.ReturnPosition,
                Is.EqualTo(home.ReturnPosition));
            Assert.That(
                first.TryGetFrontageEdge(
                    home,
                    out RoadEdge homeFrontage),
                Is.True);
            Assert.That(
                first.GetPathKind(homeFrontage),
                Is.EqualTo(CityPathKind.Street));
            BuildingLot sharedFrontageBar =
                first.BuildingLots.FirstOrDefault(
                    lot =>
                        lot.IsBar &&
                        RoadEdge.ForCellFrontage(
                            lot.Cell,
                            lot.FrontageDirection) ==
                        homeFrontage);
            Assert.That(
                sharedFrontageBar,
                Is.Not.Null);
            Assert.That(
                sharedFrontageBar.Cell,
                Is.EqualTo(
                    home.Cell + home.FrontageDirection));
            Assert.That(
                sharedFrontageBar.FrontageDirection,
                Is.EqualTo(-home.FrontageDirection));
            Assert.That(
                Vector3.Distance(
                    home.ReturnPosition,
                    sharedFrontageBar.ReturnPosition),
                Is.LessThanOrEqualTo(0.001f));
            Assert.That(
                homeFrontage.Contains(first.SpawnNode),
                Is.True);

            float nearestBarDistance = first.BuildingLots
                .Where(lot => lot.IsBar)
                .Min(bar =>
                    CityRoutePathfinder.Build(
                        first,
                        home.ReturnPosition,
                        new[] { bar })
                    .TotalLength);
            Assert.That(
                nearestBarDistance,
                Is.LessThanOrEqualTo(
                    CityLayoutGenerator
                        .MaximumHomeBarRouteDistance +
                    0.001f));
        }

        [TestCase(6.20f, false)]
        [TestCase(7.40f, false)]
        [TestCase(8.79f, false)]
        [TestCase(8.80f, true)]
        [TestCase(13.00f, true)]
        public void Generate_PlayerHomeRequiresAuthoredHeight(
            float maximumHeight,
            bool expectsHome)
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.MaximumBuildingHeight = maximumHeight;

            CityLayout layout =
                CityLayoutGenerator.Generate(settings, 73119);

            Assert.That(layout.PlayerHome != null, Is.EqualTo(expectsHome));
            Assert.That(
                layout.BuildingLots.Count(lot => lot.IsPlayerHome),
                Is.EqualTo(expectsHome ? 1 : 0));
            if (expectsHome)
            {
                Assert.That(
                    layout.PlayerHome.Height,
                    Is.EqualTo(
                            PlayerHomeBalconyGeometry
                                .PreferredBuildingHeight)
                        .Within(0.001f));
                Assert.That(
                    PlayerHomeBalconyGeometry.SupportsThirdFloor(
                        layout.PlayerHome.Height),
                    Is.True);
            }
        }

        [Test]
        public void Generate_OmitsPlayerHomeWhenFootprintIsUndersized()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.BlockWidth =
                settings.BuildingInset * 2f + 11.99f;

            CityLayout layout =
                CityLayoutGenerator.Generate(settings, 73119);

            Assert.That(layout.PlayerHome, Is.Null);
            Assert.That(
                layout.BuildingLots.Any(lot => lot.IsPlayerHome),
                Is.False);
        }

        [TestCase(0, 1, 13f, 12f)]
        [TestCase(1, 0, 12f, 13f)]
        public void PlayerHomeInfrastructure_AcceptsOrientedEnvelope(
            int frontageX,
            int frontageZ,
            float sizeX,
            float sizeZ)
        {
            var frontage = new Vector2Int(frontageX, frontageZ);
            var lot = new BuildingLot(
                new Vector2Int(4, 5),
                Vector3.zero,
                new Vector2(sizeX, sizeZ),
                PlayerHomeBalconyGeometry.PreferredBuildingHeight,
                Color.gray,
                "test.player-home",
                CityDistrictKind.Residential,
                CityLandUseKind.Building,
                false,
                true,
                false,
                string.Empty,
                BarActivityKind.None,
                frontage,
                new Vector3(
                    frontageX * sizeX * 0.5f,
                    0f,
                    frontageZ * sizeZ * 0.5f),
                Vector3.zero,
                Vector3.zero);

            Assert.That(
                () =>
                    CitySpecialBuildingWorldBuilder
                        .ValidatePlayerHome(lot),
                Throws.Nothing);
        }

        [TestCase(GameSessionState.DefaultCitySeed)]
        [TestCase(73119)]
        [TestCase(-99123)]
        public void PlayerHomeBalconyGeometry_CityTransformRoundTrips(
            int seed)
        {
            BuildingLot home = CityLayoutGenerator.Generate(
                    CityGenerationSettings.Default,
                    seed)
                .PlayerHome;
            var local = new Vector3(
                PlayerHomeBalconyGeometry.HomeFacadeX + 1.35f,
                0.72f,
                -1.12f);

            Vector3 city =
                PlayerHomeBalconyGeometry.ToCityWorld(home, local);
            Vector3 roundTrip =
                PlayerHomeBalconyGeometry.ToHomeLocal(home, city);

            Assert.That(
                Vector3.Distance(roundTrip, local),
                Is.LessThan(0.001f));
            Assert.That(
                city.y,
                Is.EqualTo(
                        PlayerHomeBalconyGeometry
                            .ApartmentFloorElevation +
                        local.y)
                    .Within(0.001f));
            Assert.That(
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    home,
                    PlayerHomeBalconyGeometry
                        .GetFrontageDirection(home)),
                Is.EqualTo(Vector3.right));
        }

        [Test]
        public void HomeBalconyFacade_UsesAuthoredLayoutAndKeepsAnchors()
        {
            HomeInteriorLayoutPlan interior =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconyLayoutPlan plan =
                HomeBalconyLayoutPlanner.Generate(interior);
            var root = new GameObject("Home Balcony Facade Test");

            try
            {
                Transform balcony = HomeBalconyWorldBuilder.Build(
                    root.transform,
                    interior,
                    plan);
                Transform deck =
                    balcony.Find("Home Balcony Deck");
                Transform door =
                    balcony.Find("Home Balcony Ajar Door Pivot");
                Transform window =
                    balcony.Find("Home Balcony Window Glass");
                Assert.That(deck, Is.Not.Null);
                Assert.That(door, Is.Not.Null);
                Assert.That(window, Is.Not.Null);
                Assert.That(
                    balcony.Find("Home Balcony Outer Guard"),
                    Is.Not.Null);
                Assert.That(
                    balcony.Find("Home Balcony South Guard"),
                    Is.Not.Null);
                Assert.That(
                    balcony.Find("Home Balcony North Guard"),
                    Is.Not.Null);

                string[] authoredFacadeParts =
                {
                    "Player Home Brick Plinth",
                    "Player Home Front Roof Eave",
                    "Player Home Front Eave Fascia",
                    "Player Home Recessed Entrance Door"
                };
                for (int index = 0;
                     index < authoredFacadeParts.Length;
                     index++)
                {
                    Assert.That(
                        balcony.Find(authoredFacadeParts[index]),
                        Is.Not.Null,
                        authoredFacadeParts[index]);
                }

                Renderer[] authoredFacadeGlass = balcony
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(
                        item => item.name.StartsWith(
                            "Player Home Authored Front Window Glass",
                            StringComparison.Ordinal))
                    .ToArray();
                Assert.That(authoredFacadeGlass, Has.Length.EqualTo(2));
                Material homeLitMaterial =
                    CityWindowAppearance.ResolveLitMaterial(
                        CityWindowFamily.Home);
                Renderer[] litFacadeGlass = authoredFacadeGlass
                    .Where(item => item.sharedMaterial == homeLitMaterial)
                    .ToArray();
                Assert.That(
                    litFacadeGlass,
                    Has.Length.EqualTo(1),
                    "The Home reconstruction must keep exactly the one " +
                    "authored lit facade pane.");
                Assert.That(
                    litFacadeGlass[0].name,
                    Is.EqualTo(
                        "Player Home Authored Front Window Glass 2"));
                Assert.That(
                    litFacadeGlass[0].transform.localPosition.x,
                    Is.EqualTo(5.227f).Within(0.001f));
                Assert.That(
                    litFacadeGlass[0].transform.localPosition.y,
                    Is.EqualTo(0.66f).Within(0.001f));
                Assert.That(
                    litFacadeGlass[0].transform.localPosition.z,
                    Is.EqualTo(2.15f).Within(0.001f));
                Assert.That(
                    balcony.GetComponentsInChildren<Renderer>(true)
                        .Count(item =>
                            item.sharedMaterial == homeLitMaterial),
                    Is.EqualTo(1),
                    "Balcony and door glazing must remain non-emissive.");
                Assert.That(
                    deck.localPosition,
                    Is.EqualTo(new Vector3(
                        plan.BalconyBounds.center.x,
                        -PlayerHomeBalconyGeometry
                            .BalconySlabThickness * 0.5f,
                        plan.BalconyBounds.center.y)));
                Assert.That(
                    door.localPosition,
                    Is.EqualTo(new Vector3(
                        plan.DoorCenter.x - 0.02f,
                        0f,
                        plan.DoorCenter.z -
                        plan.DoorSize.z * 0.5f)));
                Assert.That(window.localPosition.x,
                    Is.EqualTo(plan.WindowCenter.x + 0.008f)
                        .Within(0.001f));
                Assert.That(window.localPosition.y,
                    Is.EqualTo(plan.WindowCenter.y).Within(0.001f));
                Assert.That(window.localPosition.z,
                    Is.EqualTo(plan.WindowCenter.z).Within(0.001f));
                Assert.That(deck.GetComponent<Collider>(), Is.Not.Null);

                string[] obsoleteFacadeParts =
                {
                    "Home Lower Exterior Facade",
                    "Home Upper Exterior Facade",
                    "Home Exterior Roof Lip",
                    "Home Lower Facade Damp Stain"
                };
                for (int index = 0;
                     index < obsoleteFacadeParts.Length;
                     index++)
                {
                    Assert.That(
                        balcony.Find(obsoleteFacadeParts[index]),
                        Is.Null,
                        obsoleteFacadeParts[index]);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HomeInteriorPlan_ProvidesConnectedBathroomAndFixtures()
        {
            HomeInteriorLayoutPlan plan =
                HomeInteriorLayoutPlanner.Generate();

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        plan),
                Throws.Nothing);
            Assert.That(plan.RoomSize, Is.EqualTo(new Vector2(10f, 8f)));
            Assert.That(plan.RoomHeight, Is.EqualTo(3.4f));
            Assert.That(
                plan.Zones.Select(zone => zone.Kind),
                Is.EquivalentTo(
                    new[]
                    {
                        HomeInteriorZoneKind.MainRoom,
                        HomeInteriorZoneKind.Bathroom
                    }));
            Assert.That(
                plan.Paths.Select(path => path.Kind),
                Is.EquivalentTo(
                    new[]
                    {
                        HomeInteriorPathKind.Entry,
                        HomeInteriorPathKind.Main,
                        HomeInteriorPathKind.BathroomAccess
                    }));
            Assert.That(
                plan.Furniture.Select(item => item.Kind),
                Is.EquivalentTo(
                    Enum.GetValues(typeof(HomeFurnitureKind))
                        .Cast<HomeFurnitureKind>()));
            Assert.That(
                plan.Furniture.Select(item => item.Id).Distinct().Count(),
                Is.EqualTo(plan.Furniture.Count));
            Assert.That(
                plan.TryGetZone(
                    HomeInteriorZoneKind.Bathroom,
                    out HomeInteriorZone bathroom),
                Is.True);
            Assert.That(
                bathroom.Bounds,
                Is.EqualTo(plan.BathroomBounds));
            Assert.That(
                plan.BathroomBounds.xMin,
                Is.EqualTo(1.55f).Within(0.001f));
            Assert.That(
                plan.BathroomBounds.xMax,
                Is.EqualTo(4.65f).Within(0.001f));
            Assert.That(
                plan.BathroomBounds.yMin,
                Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(
                plan.BathroomBounds.yMax,
                Is.EqualTo(3.65f).Within(0.001f));
            Assert.That(
                plan.TryGetPath(
                    HomeInteriorPathKind.Main,
                    out HomeInteriorPath mainPath),
                Is.True);
            Assert.That(
                RectContains(
                    mainPath.Bounds,
                    plan.PlayerSpawn),
                Is.True);
            Assert.That(
                RectContains(
                    mainPath.Bounds,
                    plan.ExitPosition),
                Is.True);
            Assert.That(
                RectContains(
                    mainPath.Bounds,
                    plan.BathroomDoorway),
                Is.True);
            Assert.That(
                mainPath.Clearance,
                Is.GreaterThanOrEqualTo(
                    HomeInteriorLayoutValidator
                        .MinimumPathClearance));

            HomeFurnitureFootprint[] fixtures =
                plan.Furniture
                    .Where(item => item.IsFixture)
                    .ToArray();
            Assert.That(fixtures, Has.Length.EqualTo(3));
            Assert.That(
                fixtures.Select(item => item.Kind),
                Is.EquivalentTo(
                    new[]
                    {
                        HomeFurnitureKind.Toilet,
                        HomeFurnitureKind.Shower,
                        HomeFurnitureKind.Sink
                    }));
            Assert.That(
                fixtures.All(
                    item =>
                        RectContains(
                            plan.BathroomBounds,
                            item.Bounds)),
                Is.True);
            Assert.That(
                plan.Furniture
                    .Where(item => item.BlocksMovement)
                    .Any(
                        item =>
                            plan.Paths.Any(
                                path =>
                                    item.Bounds.Overlaps(
                                        path.Bounds,
                                        true))),
                Is.False);
            Assert.That(
                plan.TryGetFurniture(
                    HomeFurnitureKind.Shower,
                    out HomeFurnitureFootprint shower),
                Is.True);
            Assert.That(shower.BlocksMovement, Is.False);
        }

        [TestCase(HomeFurnitureKind.Bed)]
        [TestCase(HomeFurnitureKind.Kitchen)]
        [TestCase(HomeFurnitureKind.Sofa)]
        [TestCase(HomeFurnitureKind.Table)]
        [TestCase(HomeFurnitureKind.Bookcase)]
        [TestCase(HomeFurnitureKind.CameraCornerJunk)]
        [TestCase(HomeFurnitureKind.Toilet)]
        [TestCase(HomeFurnitureKind.Shower)]
        [TestCase(HomeFurnitureKind.Sink)]
        public void HomeInteriorValidator_RejectsMissingRequiredFurniture(
            HomeFurnitureKind missingKind)
        {
            HomeInteriorLayoutPlan source =
                HomeInteriorLayoutPlanner.Generate();
            var furniture = source.Furniture
                .Where(item => item.Kind != missingKind)
                .ToList();
            HomeInteriorLayoutPlan invalid =
                CopyPlan(source, furniture: furniture);

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        invalid),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void HomeInteriorValidator_RejectsDuplicateFurnitureId()
        {
            HomeInteriorLayoutPlan source =
                HomeInteriorLayoutPlanner.Generate();
            var furniture =
                new List<HomeFurnitureFootprint>(
                    source.Furniture);
            int sinkIndex = furniture.FindIndex(
                item => item.Kind == HomeFurnitureKind.Sink);
            HomeFurnitureFootprint sink = furniture[sinkIndex];
            furniture[sinkIndex] = new HomeFurnitureFootprint(
                furniture[0].Id,
                sink.Kind,
                sink.Bounds,
                sink.Height,
                sink.BlocksMovement);
            HomeInteriorLayoutPlan invalid =
                CopyPlan(source, furniture: furniture);

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        invalid),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("unique"));
        }

        [Test]
        public void HomeInteriorValidator_RejectsDuplicateFurnitureKind()
        {
            HomeInteriorLayoutPlan source =
                HomeInteriorLayoutPlanner.Generate();
            var furniture =
                new List<HomeFurnitureFootprint>(
                    source.Furniture);
            int sinkIndex = furniture.FindIndex(
                item => item.Kind == HomeFurnitureKind.Sink);
            HomeFurnitureFootprint sink = furniture[sinkIndex];
            furniture[sinkIndex] = new HomeFurnitureFootprint(
                "second-shower",
                HomeFurnitureKind.Shower,
                sink.Bounds,
                sink.Height,
                false);
            HomeInteriorLayoutPlan invalid =
                CopyPlan(source, furniture: furniture);

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        invalid),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("unique"));
        }

        [Test]
        public void HomeInteriorValidator_RejectsFixtureOutsideBathroom()
        {
            HomeInteriorLayoutPlan source =
                HomeInteriorLayoutPlanner.Generate();
            var furniture =
                new List<HomeFurnitureFootprint>(
                    source.Furniture);
            int sinkIndex = furniture.FindIndex(
                item => item.Kind == HomeFurnitureKind.Sink);
            HomeFurnitureFootprint sink = furniture[sinkIndex];
            furniture[sinkIndex] = new HomeFurnitureFootprint(
                sink.Id,
                sink.Kind,
                new Rect(0.55f, 1.15f, 0.60f, 0.35f),
                sink.Height,
                sink.BlocksMovement);
            HomeInteriorLayoutPlan invalid =
                CopyPlan(source, furniture: furniture);

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        invalid),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("inside the bathroom"));
        }

        [Test]
        public void HomeInteriorValidator_RejectsBlockingFurnitureOnPath()
        {
            HomeInteriorLayoutPlan source =
                HomeInteriorLayoutPlanner.Generate();
            var furniture =
                new List<HomeFurnitureFootprint>(
                    source.Furniture);
            int bookcaseIndex = furniture.FindIndex(
                item => item.Kind == HomeFurnitureKind.Bookcase);
            HomeFurnitureFootprint bookcase =
                furniture[bookcaseIndex];
            furniture[bookcaseIndex] =
                new HomeFurnitureFootprint(
                    bookcase.Id,
                    bookcase.Kind,
                    new Rect(0.15f, -0.40f, 0.50f, 0.50f),
                    bookcase.Height,
                    true);
            HomeInteriorLayoutPlan invalid =
                CopyPlan(source, furniture: furniture);

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        invalid),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("intersects path"));
        }

        [Test]
        public void HomeInteriorValidator_RejectsDisconnectedReservedPath()
        {
            HomeInteriorLayoutPlan source =
                HomeInteriorLayoutPlanner.Generate();
            var paths =
                new List<HomeInteriorPath>(source.Paths);
            int entryIndex = paths.FindIndex(
                path => path.Kind == HomeInteriorPathKind.Entry);
            paths[entryIndex] = new HomeInteriorPath(
                "entry",
                HomeInteriorPathKind.Entry,
                new Rect(-4.40f, 1.00f, 1.10f, 1.20f),
                1f);
            HomeInteriorLayoutPlan invalid =
                CopyPlan(source, paths: paths);

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        invalid),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("connected route"));
        }

        [Test]
        public void HomeInteriorValidator_RejectsUnsafeExitTrigger()
        {
            HomeInteriorLayoutPlan source =
                HomeInteriorLayoutPlanner.Generate();
            HomeInteriorLayoutPlan invalid =
                CopyPlan(
                    source,
                    exitTriggerSize:
                        new Vector3(2.20f, 0f, 0.65f));

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        invalid),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("trigger"));
        }

        [Test]
        public void HomeInteriorValidator_RejectsWalkableBoundsOutsideRoom()
        {
            HomeInteriorLayoutPlan source =
                HomeInteriorLayoutPlanner.Generate();
            HomeInteriorLayoutPlan invalid =
                CopyPlan(
                    source,
                    walkableBounds:
                        new Rect(-5.25f, -3.65f, 9.90f, 7.30f));

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        invalid),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("inside the room"));
        }

        private static HomeInteriorLayoutPlan CopyPlan(
            HomeInteriorLayoutPlan source,
            Rect? walkableBounds = null,
            Vector3? exitTriggerSize = null,
            Rect? bathroomBounds = null,
            Rect? bathroomDoorway = null,
            IList<HomeInteriorZone> zones = null,
            IList<HomeInteriorPath> paths = null,
            IList<HomeFurnitureFootprint> furniture = null)
        {
            return new HomeInteriorLayoutPlan(
                source.RoomSize,
                source.RoomHeight,
                walkableBounds ?? source.WalkableBounds,
                source.PlayerSpawn,
                source.ExitPosition,
                exitTriggerSize ?? source.ExitTriggerSize,
                source.EntryCorridor,
                bathroomBounds ?? source.BathroomBounds,
                bathroomDoorway ?? source.BathroomDoorway,
                zones ??
                    new List<HomeInteriorZone>(source.Zones),
                paths ??
                    new List<HomeInteriorPath>(source.Paths),
                furniture ??
                    new List<HomeFurnitureFootprint>(
                        source.Furniture));
        }

        private static bool RectContains(
            Rect outer,
            Rect inner)
        {
            const float tolerance = 0.001f;
            return inner.xMin >= outer.xMin - tolerance &&
                   inner.xMax <= outer.xMax + tolerance &&
                   inner.yMin >= outer.yMin - tolerance &&
                   inner.yMax <= outer.yMax + tolerance;
        }

        private static bool RectContains(
            Rect outer,
            Vector3 point)
        {
            const float tolerance = 0.001f;
            return point.x >= outer.xMin - tolerance &&
                   point.x <= outer.xMax + tolerance &&
                   point.z >= outer.yMin - tolerance &&
                   point.z <= outer.yMax + tolerance;
        }
    }
}
