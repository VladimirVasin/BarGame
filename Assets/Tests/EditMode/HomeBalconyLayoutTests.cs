using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeBalconyLayoutTests
    {
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
    }
}
