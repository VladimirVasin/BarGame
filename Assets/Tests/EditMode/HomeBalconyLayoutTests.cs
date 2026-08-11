using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeBalconyLayoutTests
    {
        private const float PositionTolerance = 0.0001f;

        [Test]
        public void Generate_CreatesConnectedThirdFloorBalcony()
        {
            HomeInteriorLayoutPlan interior =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconyLayoutPlan balcony =
                HomeBalconyLayoutPlanner.Generate(interior);

            Assert.That(
                () =>
                    HomeBalconyLayoutValidator
                        .ValidateOrThrow(
                            interior,
                            balcony),
                Throws.Nothing);
            Assert.That(
                balcony.BalconyBounds.xMin,
                Is.LessThan(interior.RoomBounds.xMax));
            Assert.That(
                balcony.BalconyBounds.xMax,
                Is.GreaterThan(
                    interior.RoomBounds.xMax + 2f));
            Assert.That(
                balcony.StreetGroundY,
                Is.EqualTo(
                    -PlayerHomeBalconyGeometry
                        .ApartmentFloorElevation));
            Assert.That(
                balcony.DoorSize.z,
                Is.GreaterThanOrEqualTo(
                    HomeInteriorLayoutValidator
                        .MinimumPathClearance));

            var walkable =
                new RoadWalkableArea(
                    balcony.WalkableRectangles);
            float radius =
                HomeInteriorLayoutValidator
                    .PlayerClearanceRadius;
            Assert.That(
                walkable.Contains(
                    new Vector3(4.20f, 0f, -0.50f),
                    radius),
                Is.True);
            Assert.That(
                walkable.Contains(
                    new Vector3(5.10f, 0f, -0.50f),
                    radius),
                Is.True);
            Assert.That(
                walkable.Contains(
                    new Vector3(6.60f, 0f, -1.45f),
                    radius),
                Is.True);
            Assert.That(
                walkable.Contains(
                    new Vector3(7.45f, 0f, -1.45f),
                    radius),
                Is.False);
        }

        [Test]
        public void Validator_RejectsDisconnectedDoorway()
        {
            HomeInteriorLayoutPlan interior =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconyLayoutPlan source =
                HomeBalconyLayoutPlanner.Generate(interior);
            var invalid =
                new HomeBalconyLayoutPlan(
                    source.InteriorAccessPath,
                    new Rect(5.20f, -1.10f, 0.30f, 1.20f),
                    source.BalconyBounds,
                    source.DoorCenter,
                    source.DoorSize,
                    source.WindowCenter,
                    source.WindowSize,
                    new List<Rect>(
                        source.WalkableRectangles));

            Assert.That(
                () =>
                    HomeBalconyLayoutValidator
                        .ValidateOrThrow(
                            interior,
                            invalid),
                Throws.TypeOf<
                    System.InvalidOperationException>()
                    .With.Message.Contains("connected"));
        }

        [TestCase(GameSessionState.DefaultCitySeed)]
        [TestCase(73119)]
        [TestCase(-99123)]
        public void ExteriorContext_IsDeterministicAndMatchesHome(
            int seed)
        {
            HomeExteriorContextPlan first =
                HomeExteriorContextPlanner.Generate(seed);
            HomeExteriorContextPlan second =
                HomeExteriorContextPlanner.Generate(seed);

            Assert.That(first.PlayerHome, Is.Not.Null);
            Assert.That(
                first.FrontageEdge,
                Is.EqualTo(second.FrontageEdge));
            Assert.That(
                first.PlayerHome.Cell,
                Is.EqualTo(second.PlayerHome.Cell));
            Assert.That(
                first.PlayerHome.FrontageDirection,
                Is.EqualTo(
                    second.PlayerHome.FrontageDirection));
            Assert.That(
                first.NearbyRoads,
                Is.EqualTo(second.NearbyRoads));
            Assert.That(
                first.NearbyLots.Select(
                    lot => lot.Cell),
                Is.EqualTo(
                    second.NearbyLots.Select(
                        lot => lot.Cell)));
            Assert.That(
                first.NearbyStreetLamps,
                Is.EqualTo(
                    second.NearbyStreetLamps));
            Assert.That(
                first.NearbyDistrictPointsOfInterest.Select(
                    descriptor => descriptor.Id),
                Is.EqualTo(
                    second.NearbyDistrictPointsOfInterest.Select(
                        descriptor => descriptor.Id)));
            Assert.That(
                first.NearbyDecorations,
                Is.EqualTo(second.NearbyDecorations));
            Assert.That(
                first.NearbyRoads,
                Does.Contain(first.FrontageEdge));
            Assert.That(
                first.NearbyLots.Any(
                    lot => lot.IsPlayerHome),
                Is.True);
        }

        [TestCase(GameSessionState.DefaultCitySeed)]
        [TestCase(73119)]
        public void SharedTransform_RoundTripsCityCoordinates(
            int seed)
        {
            HomeExteriorContextPlan context =
                HomeExteriorContextPlanner.Generate(seed);
            BuildingLot home = context.PlayerHome;
            Vector3 cityPosition =
                home.ReturnPosition +
                new Vector3(2.15f, 3.25f, -1.70f);

            Vector3 local =
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    home,
                    cityPosition);
            Vector3 roundTrip =
                PlayerHomeBalconyGeometry.ToCityWorld(
                    home,
                    local);

            Assert.That(
                Vector3.Distance(
                    cityPosition,
                    roundTrip),
                Is.LessThan(0.001f));
        }

        [Test]
        public void
            ExteriorPedestrians_DefaultThreeWayBusApronsRemainAxisAligned()
        {
            int seed = GameSessionState.DefaultCitySeed;
            HomeExteriorContextPlan context =
                HomeExteriorContextPlanner.Generate(seed);
            Vector2Int[] threeWayBusIntersections =
                CityBusIntersectionSelector
                    .Select(context.Layout)
                    .Where(node =>
                        context.Layout.RoadEdges.Count(edge =>
                            edge.Contains(node)) == 3)
                    .ToArray();

            Assert.That(
                threeWayBusIntersections,
                Is.Not.Empty,
                "Regression setup requires a selected three-way Road v2.1 " +
                "bus apron in the production city.");

            CityPedestrianPlan exterior =
                HomeExteriorPedestrianPlanner.Create(context, seed);
            for (int index = 0; index < exterior.Links.Count; index++)
            {
                CityPedestrianLink link = exterior.Links[index];
                Vector3 first =
                    exterior.Nodes[link.FirstNodeIndex].Position;
                Vector3 second =
                    exterior.Nodes[link.SecondNodeIndex].Position;
                Assert.That(
                    Mathf.Abs(first.x - second.x) <= PositionTolerance ||
                    Mathf.Abs(first.z - second.z) <= PositionTolerance,
                    Is.True,
                    $"Home pedestrian link '{link.Id}' is not " +
                    "axis-aligned.");
            }
        }

        [Test]
        public void
            ExteriorPedestrians_TransformCityGraphAndFilterSpawnAnchors()
        {
            int seed = GameSessionState.DefaultCitySeed;
            HomeExteriorContextPlan context =
                HomeExteriorContextPlanner.Generate(seed);
            CityStreetSurfacePlan streetSurfaces =
                CityStreetSurfacePlanner.Create(context.Layout);
            CityPedestrianPlan source =
                CityPedestrianPlanner.Create(
                    context.Layout,
                    seed,
                    streetSurfaces);
            CityPedestrianPlan exterior =
                HomeExteriorPedestrianPlanner.Create(
                    context,
                    seed);

            Assert.That(exterior.LayoutSeed, Is.EqualTo(source.LayoutSeed));
            Assert.That(
                exterior.PopulationSeed,
                Is.EqualTo(source.PopulationSeed));
            Assert.That(exterior.StableSeed, Is.EqualTo(source.StableSeed));
            Assert.That(exterior.AgentRadius, Is.EqualTo(source.AgentRadius));
            Assert.That(exterior.Nodes.Count, Is.EqualTo(source.Nodes.Count));
            Assert.That(exterior.Links.Count, Is.EqualTo(source.Links.Count));
            Assert.That(exterior.Count, Is.GreaterThan(0));

            for (int index = 0; index < source.Nodes.Count; index++)
            {
                CityPedestrianNode sourceNode = source.Nodes[index];
                CityPedestrianNode exteriorNode = exterior.Nodes[index];
                Assert.That(exteriorNode.Id, Is.EqualTo(sourceNode.Id));
                Assert.That(
                    exteriorNode.IsCrosswalkEntry,
                    Is.EqualTo(sourceNode.IsCrosswalkEntry));
                Assert.That(
                    Vector3.Distance(
                        exteriorNode.Position,
                        PlayerHomeBalconyGeometry.ToHomeLocal(
                            context.PlayerHome,
                            sourceNode.Position)),
                    Is.LessThan(0.001f),
                    $"Pedestrian node '{sourceNode.Id}' was not transformed " +
                    "into Home coordinates.");
            }

            for (int index = 0; index < source.Links.Count; index++)
            {
                CityPedestrianLink sourceLink = source.Links[index];
                CityPedestrianLink exteriorLink = exterior.Links[index];
                Assert.That(exteriorLink.Id, Is.EqualTo(sourceLink.Id));
                Assert.That(exteriorLink.Kind, Is.EqualTo(sourceLink.Kind));
                Assert.That(
                    exteriorLink.FirstNodeIndex,
                    Is.EqualTo(sourceLink.FirstNodeIndex));
                Assert.That(
                    exteriorLink.SecondNodeIndex,
                    Is.EqualTo(sourceLink.SecondNodeIndex));
            }

            var sourceAnchors = source.SpawnAnchors.ToDictionary(
                anchor => anchor.Id);
            for (int index = 0;
                 index < exterior.SpawnAnchors.Count;
                 index++)
            {
                CityPedestrianSpawnAnchor anchor =
                    exterior.SpawnAnchors[index];
                Assert.That(
                    sourceAnchors.TryGetValue(
                        anchor.Id,
                        out CityPedestrianSpawnAnchor sourceAnchor),
                    Is.True);
                Assert.That(
                    Vector3.Distance(
                        anchor.Position,
                        PlayerHomeBalconyGeometry.ToHomeLocal(
                            context.PlayerHome,
                            sourceAnchor.Position)),
                    Is.LessThan(0.001f));
                Assert.That(
                    anchor.FirstNodeIndex,
                    Is.EqualTo(sourceAnchor.FirstNodeIndex));
                Assert.That(
                    anchor.SecondNodeIndex,
                    Is.EqualTo(sourceAnchor.SecondNodeIndex));
                Assert.That(
                    anchor.Position.x - exterior.AgentRadius,
                    Is.GreaterThanOrEqualTo(
                        HomeExteriorViewBuilder.ExteriorMinimumX -
                        0.0001f),
                    $"Spawn anchor '{anchor.Id}' crosses the Home facade.");
                float deltaX = sourceAnchor.Position.x -
                    context.PlayerHome.DoorPosition.x;
                float deltaZ = sourceAnchor.Position.z -
                    context.PlayerHome.DoorPosition.z;
                Assert.That(
                    (deltaX * deltaX) + (deltaZ * deltaZ),
                    Is.LessThanOrEqualTo(
                        HomeExteriorPedestrianPlanner.SpawnContextRadius *
                        HomeExteriorPedestrianPlanner.SpawnContextRadius),
                    $"Spawn anchor '{anchor.Id}' is outside the bounded " +
                    "Home approach context.");
            }

            RoadWalkableArea walkable =
                CityPedestrianPlanner.CreateWalkableArea(exterior);
            for (int index = 0; index < exterior.Links.Count; index++)
            {
                CityPedestrianLink link = exterior.Links[index];
                Vector3 first =
                    exterior.Nodes[link.FirstNodeIndex].Position;
                Vector3 second =
                    exterior.Nodes[link.SecondNodeIndex].Position;
                for (int sample = 0; sample <= 4; sample++)
                {
                    Assert.That(
                        walkable.Contains(
                            Vector3.Lerp(first, second, sample / 4f),
                            exterior.AgentRadius),
                        Is.True,
                        $"Transformed pedestrian link '{link.Id}' leaves " +
                        "its Home-local navigation area.");
                }
            }
        }

        [Test]
        public void ExteriorHalfSpace_RejectsInteriorAndClipsCrossingBounds()
        {
            var interiorOnly =
                new Bounds(
                    Vector3.zero,
                    new Vector3(2f, 3f, 4f));
            Assert.That(
                HomeExteriorViewBuilder
                    .TryClipToExteriorHalfSpace(
                        interiorOnly,
                        out _),
                Is.False);

            var crossing =
                new Bounds(
                    new Vector3(5f, 1f, -2f),
                    new Vector3(4f, 2f, 6f));
            Assert.That(
                HomeExteriorViewBuilder
                    .TryClipToExteriorHalfSpace(
                        crossing,
                        out Bounds clipped),
                Is.True);
            Assert.That(
                clipped.min.x,
                Is.EqualTo(
                        HomeExteriorViewBuilder
                            .ExteriorMinimumX)
                    .Within(0.001f));
            Assert.That(
                clipped.max.x,
                Is.EqualTo(crossing.max.x)
                    .Within(0.001f));
            Assert.That(
                clipped.min.y,
                Is.EqualTo(crossing.min.y)
                    .Within(0.001f));
            Assert.That(
                clipped.max.z,
                Is.EqualTo(crossing.max.z)
                    .Within(0.001f));

            var exteriorOnly =
                new Bounds(
                    new Vector3(12f, 1f, 3f),
                    new Vector3(2f, 2f, 2f));
            Assert.That(
                HomeExteriorViewBuilder
                    .TryClipToExteriorHalfSpace(
                        exteriorOnly,
                        out Bounds unchanged),
                Is.True);
            Assert.That(
                unchanged.center,
                Is.EqualTo(exteriorOnly.center));
            Assert.That(
                unchanged.size,
                Is.EqualTo(exteriorOnly.size));
        }

        private static bool Contains(Rect bounds, Vector3 position)
        {
            return position.x >= bounds.xMin &&
                   position.x <= bounds.xMax &&
                   position.z >= bounds.yMin &&
                   position.z <= bounds.yMax;
        }
    }
}
