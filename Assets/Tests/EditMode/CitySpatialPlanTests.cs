using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySpatialPlanTests
    {
        [TestCase(-2.75f, -1.25f)]
        [TestCase(0f, 0f)]
        [TestCase(1.375f, 2.125f)]
        [TestCase(6f, 6f)]
        [TestCase(13.625f, 10.75f)]
        [TestCase(17f, 14f)]
        [TestCase(20.5f, 18.25f)]
        public void CoastalCoordinates_RoundTripInsideAndBeyondAuthoredAxes(float x, float z)
        {
            CitySpatialPlan plan = CreateCoastal();
            Vector2 coordinate = new Vector2(x, z);
            Vector2 result = plan.WorldToGrid(plan.GetCoordinateWorldOffset(coordinate));
            Assert.That(result.x, Is.EqualTo(x).Within(0.0001f));
            Assert.That(result.y, Is.EqualTo(z).Within(0.0001f));
        }

        [Test]
        public void CoastalAxes_KeepUrbanMetresAndCompactOnlyTheServiceEnvelope()
        {
            CitySpatialPlan plan = CreateCoastal();
            Assert.That(plan.IsCompactCoastal, Is.True);
            Assert.That(plan.IsUniform, Is.False);
            for (int z = 0; z <= 12; z++)
                for (int x = 0; x <= 13; x++)
                    Assert.That(plan.GetCoordinateWorldOffset(new Vector2Int(x, z)),
                        Is.EqualTo(new Vector2(x * 26f, z * 26f)));
            for (int x = 13; x <= 17; x++)
                Assert.That(plan.GetCoordinateWorldOffset(new Vector2Int(x, 6)).x,
                    Is.EqualTo(338f + (x - 13) * 17.5f));
            Assert.That(plan.GetCellSize(new Vector2Int(0, 0)), Is.EqualTo(new Vector2(26f, 26f)));
            Assert.That(plan.GetCellSize(new Vector2Int(-1, -1)), Is.EqualTo(new Vector2(12f, 12f)));
            Assert.That(plan.GetCellSize(new Vector2Int(13, 12)), Is.EqualTo(new Vector2(17.5f, 28f)));
            Assert.That(plan.GetNodeSpan(new RoadEdge(Vector2Int.zero, Vector2Int.right)), Is.EqualTo(26f));
            Assert.That(plan.GetCellBounds(Vector2Int.zero).xMax,
                Is.EqualTo(plan.GetCellBounds(Vector2Int.right).xMin));
            Vector2 minimum = plan.GetCoordinateWorldOffset(new Vector2Int(-1, -1));
            Vector2 maximum = plan.GetCoordinateWorldOffset(new Vector2Int(17, 13));
            Assert.That(maximum - minimum, Is.EqualTo(new Vector2(420f, 352f)));
        }

        [Test]
        public void CustomAndLegacyBlueprints_RetainUniformMetres()
        {
            CityGenerationSettings settings = CityGenerationSettings.Default;
            CitySpatialPlan legacy = CitySpatialPlan.Create(
                CityBlueprintCatalog.CreateLegacy(settings), settings);
            settings.BlockWidth = 24f;
            CitySpatialPlan custom = CitySpatialPlan.Create(CityBlueprintCatalog.Default, settings);
            Assert.That(legacy.IsUniform, Is.True);
            Assert.That(custom.IsUniform, Is.True);
            Vector2 coordinate = new Vector2(-1.5f, 14.25f);
            Assert.That(custom.GetCoordinateWorldOffset(coordinate),
                Is.EqualTo(Vector2.Scale(coordinate, settings.NodeSpacing)));
            Assert.That(custom.WorldToGrid(custom.GetCoordinateWorldOffset(coordinate)),
                Is.EqualTo(coordinate));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void UniformAxes_RejectInvalidStep(float invalidStep)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CitySpatialPlan.Uniform(new Vector2(invalidStep, 26f)));
        }

        private static CitySpatialPlan CreateCoastal()
        {
            return CitySpatialPlan.Create(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default);
        }
    }
}
