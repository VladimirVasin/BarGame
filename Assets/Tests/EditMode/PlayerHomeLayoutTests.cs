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
        [TestCase(7.40f, true)]
        [TestCase(13.00f, true)]
        public void Generate_PlayerHomeHeightHonorsCustomMaximum(
            float maximumHeight,
            bool supportsThirdFloor)
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.MaximumBuildingHeight = maximumHeight;

            CityLayout layout =
                CityLayoutGenerator.Generate(settings, 73119);

            Assert.That(
                layout.PlayerHome.Height,
                Is.EqualTo(
                        Mathf.Min(
                            PlayerHomeBalconyGeometry
                                .PreferredBuildingHeight,
                            maximumHeight))
                    .Within(0.001f));
            Assert.That(
                PlayerHomeBalconyGeometry.SupportsThirdFloor(
                    layout.PlayerHome.Height),
                Is.EqualTo(supportsThirdFloor));
            Assert.That(
                layout.BuildingLots
                    .Where(lot =>
                        lot.HasBuilding &&
                        !lot.IsOrdinaryBuilding)
                    .All(lot => lot.Height <= maximumHeight),
                Is.True,
                "The legacy maximum remains the special-building clamp.");
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
        public void CityHomeFacade_BuildsMatchingThirdFloorBalcony()
        {
            BuildingLot home = CityLayoutGenerator.Generate(
                    CityGenerationSettings.Default,
                    GameSessionState.DefaultCitySeed)
                .PlayerHome;
            var root = new GameObject("City Home Facade Test");

            try
            {
                Material emissive =
                    CityNightResources.EmissiveMaterial;
                CityWorldBuilder.BuildHomeBalconyFacade(
                    root.transform,
                    home,
                    emissive);

                Transform slab =
                    root.transform.Find("Home Balcony Slab");
                Transform door =
                    root.transform.Find("Home Balcony Door");
                Transform window =
                    root.transform.Find("Home Balcony Window");
                Assert.That(slab, Is.Not.Null);
                Assert.That(door, Is.Not.Null);
                Assert.That(window, Is.Not.Null);
                Assert.That(
                    root.transform.Find(
                        "Home Balcony Front Rail"),
                    Is.Not.Null);
                Assert.That(
                    root.transform.Find(
                        "Home Balcony Side Rail Left"),
                    Is.Not.Null);
                Assert.That(
                    root.transform.Find(
                        "Home Balcony Side Rail Right"),
                    Is.Not.Null);
                Assert.That(
                    root.transform
                        .Cast<Transform>()
                        .Count(
                            item =>
                                item.name ==
                                "Home Balcony Door Frame"),
                    Is.EqualTo(2));
                Assert.That(
                    root.transform
                        .Cast<Transform>()
                        .Count(
                            item =>
                                item.name ==
                                "Home Balcony Window Frame"),
                    Is.EqualTo(2));

                Assert.That(
                    Vector3.Distance(
                        slab.localPosition,
                        PlayerHomeBalconyGeometry
                            .GetCityBalconyCenter(home)),
                    Is.LessThan(0.001f));
                Vector3 expectedDoor =
                    PlayerHomeBalconyGeometry.ToCityWorld(
                        home,
                        new Vector3(
                            PlayerHomeBalconyGeometry
                                .HomeFacadeX +
                            0.035f,
                            PlayerHomeBalconyGeometry
                                .DoorHeight * 0.5f,
                            PlayerHomeBalconyGeometry
                                .DoorCenterZ));
                Vector3 expectedWindow =
                    PlayerHomeBalconyGeometry.ToCityWorld(
                        home,
                        new Vector3(
                            PlayerHomeBalconyGeometry
                                .HomeFacadeX +
                            0.035f,
                            PlayerHomeBalconyGeometry
                                .WindowCenterY,
                            PlayerHomeBalconyGeometry
                                .WindowCenterZ));
                Assert.That(
                    Vector3.Distance(
                        door.localPosition,
                        expectedDoor),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Distance(
                        window.localPosition,
                        expectedWindow),
                    Is.LessThan(0.001f));
                Assert.That(
                    window
                        .GetComponent<Renderer>()
                        .sharedMaterial,
                    Is.SameAs(emissive));
                Assert.That(
                    root.GetComponentsInChildren<Collider>(true),
                    Is.Empty);

                Assert.That(
                    CityWorldBuilder
                        .ShouldBuildGenericFrontWindowRow(
                            home,
                            1.5f + 2f * 2.35f),
                    Is.False);
                Assert.That(
                    CityWorldBuilder
                        .ShouldBuildGenericFrontWindowRow(
                            home,
                            1.5f + 1f * 2.35f),
                    Is.True);
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
