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

        [Test]
        public void HomeInteriorPlan_IsBoundedAndLeavesEntryClear()
        {
            HomeInteriorLayoutPlan plan =
                HomeInteriorLayoutPlanner.Generate();

            Assert.That(
                () =>
                    HomeInteriorLayoutValidator.ValidateOrThrow(
                        plan),
                Throws.Nothing);
            Assert.That(plan.Furniture, Has.Count.EqualTo(5));
            Assert.That(
                plan.WalkableBounds.Contains(
                    new Vector2(
                        plan.PlayerSpawn.x,
                        plan.PlayerSpawn.z)),
                Is.True);
            Assert.That(
                plan.Furniture.Any(
                    item =>
                        item.Bounds.Overlaps(
                            plan.EntryCorridor,
                            true)),
                Is.False);
        }
    }
}
