using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketCityPlanningTests
    {
        private const float Tolerance = 0.001f;

        [TestCase(GameSessionState.DefaultCitySeed)]
        [TestCase(71923)]
        [TestCase(-99123)]
        public void DefaultCity_SelectsOneNearestEligibleResidentialLot(
            int seed)
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                seed);
            BuildingLot supermarket = layout.Supermarket;

            Assert.That(supermarket, Is.Not.Null);
            Assert.That(
                layout.BuildingLots.Count(lot => lot.IsSupermarket),
                Is.EqualTo(1));
            Assert.That(supermarket.HasBuilding, Is.True);
            Assert.That(supermarket.IsBar, Is.False);
            Assert.That(supermarket.IsPlayerHome, Is.False);
            Assert.That(
                supermarket.District,
                Is.EqualTo(CityDistrictKind.Residential));
            Assert.That(
                layout.PrimaryLandmarkCells.Values,
                Has.No.Member(supermarket.Cell));
            Assert.That(
                layout.TryGetDistrictPointOfInterest(
                    supermarket.Cell,
                    out _),
                Is.False);
            Assert.That(
                layout.TryGetFrontageEdge(
                    supermarket,
                    out RoadEdge supermarketFrontage),
                Is.True);
            Assert.That(
                layout.GetPathKind(supermarketFrontage),
                Is.EqualTo(CityPathKind.Street));
            Assert.That(
                ContainsInclusive(
                    layout.GetRoadRect(supermarketFrontage),
                    supermarket.ReturnPosition),
                Is.True);

            BuildingLot home = layout.PlayerHome;
            Assert.That(home, Is.Not.Null);
            float chosenDistance = GetTravelDistance(
                layout,
                home,
                supermarket);
            float nearestEligibleDistance = layout.BuildingLots
                .Where(lot =>
                    lot.HasBuilding &&
                    lot.HasRoadFrontage &&
                    lot.District == CityDistrictKind.Residential &&
                    !lot.IsBar &&
                    !lot.IsPlayerHome &&
                    !layout.PrimaryLandmarkCells.Values.Contains(
                        lot.Cell) &&
                    !layout.TryGetDistrictPointOfInterest(
                        lot.Cell,
                        out _))
                .Min(lot => GetTravelDistance(layout, home, lot));
            Assert.That(
                chosenDistance,
                Is.EqualTo(nearestEligibleDistance).Within(Tolerance));
        }

        [Test]
        public void DefaultCity_OpensTheSupermarketStreetApproach()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            RoadFencePlan fence =
                RoadFencePlanner.CreatePlan(layout);

            Assert.That(layout.Supermarket, Is.Not.Null);
            Assert.That(
                fence.SupermarketOpenings,
                Has.Count.EqualTo(1));
            RoadFenceOpeningDescriptor opening =
                fence.SupermarketOpenings[0];
            Vector3 frontage = new Vector3(
                layout.Supermarket.FrontageDirection.x,
                0f,
                layout.Supermarket.FrontageDirection.y);
            Vector3 outward = -frontage;
            Vector3 expectedCenter =
                layout.Supermarket.ReturnPosition +
                outward * (layout.RoadWidth * 0.5f);

            Assert.That(
                opening.Kind,
                Is.EqualTo(
                    RoadFenceOpeningKind.SupermarketEntrance));
            Assert.That(opening.SupermarketId, Is.EqualTo("supermarket"));
            Assert.That(opening.Center, Is.EqualTo(expectedCenter));
            Assert.That(opening.OutwardNormal, Is.EqualTo(outward));
            Assert.That(
                opening.Width,
                Is.EqualTo(
                    SupermarketEntranceGeometry.FenceOpeningWidth));
            Assert.That(
                opening.Width,
                Is.GreaterThanOrEqualTo(
                    SupermarketEntranceGeometry.WalkwayWidth));
        }

        [Test]
        public void TinyLayout_MayOmitSupermarketWhenNoOrdinaryLotRemains()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.BlocksX = 1;
            settings.BlocksZ = 1;
            settings.BarCount = 0;
            settings.ParkBlocksX = 0;
            settings.ParkBlocksZ = 0;
            settings.MinimumBarRouteDistance = 0f;

            CityLayout layout = CityLayoutGenerator.Generate(
                settings,
                4125);

            Assert.That(layout.Supermarket, Is.Null);
            Assert.That(
                layout.BuildingLots.Any(lot => lot.IsSupermarket),
                Is.False);
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        private static float GetTravelDistance(
            CityLayout layout,
            BuildingLot first,
            BuildingLot second)
        {
            Assert.That(
                layout.TryGetFrontageEdge(first, out RoadEdge firstEdge),
                Is.True);
            Assert.That(
                layout.TryGetFrontageEdge(second, out RoadEdge secondEdge),
                Is.True);
            return CityTravelDistance.BetweenAnchors(
                layout.Nodes,
                layout.RoadEdges,
                layout.GetNodeWorldPosition,
                firstEdge,
                first.ReturnPosition,
                secondEdge,
                second.ReturnPosition);
        }

        private static bool ContainsInclusive(
            Rect bounds,
            Vector3 position)
        {
            return position.x >= bounds.xMin - Tolerance &&
                   position.x <= bounds.xMax + Tolerance &&
                   position.z >= bounds.yMin - Tolerance &&
                   position.z <= bounds.yMax + Tolerance;
        }
    }
}
