using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class AlpineVillagePeripheralStormTests
    {
        private static AlpineVillagePlan CreateVillage(int seed =
            GameSessionState.DefaultCitySeed)
        {
            return AlpineVillagePlanner.Create(seed);
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void TroddenNetwork_IsCalmAcrossLaneAndEveryPlannedPath()
        {
            AlpineVillagePlan village = CreateVillage();
            AlpineVillagePeripheralStormPlan storm =
                AlpineVillagePeripheralStormPlan.Create(village);
            IReadOnlyList<AlpineVillagePathDescriptor> generated =
                AlpineVillagePathPlanner.Create(village);

            Assert.That(storm.Paths.Count, Is.EqualTo(generated.Count));
            for (float distance = 0f;
                 distance <= village.Lane.Length;
                 distance += 1f)
            {
                AlpineVillageLaneSample lane = village.Lane.Sample(distance);
                AssertCalm(
                    storm,
                    new Vector2(lane.Position.x, lane.Position.z),
                    $"lane at {distance:0.0} m");
            }

            for (int pathIndex = 0;
                 pathIndex < storm.Paths.Count;
                 pathIndex++)
            {
                AlpineVillagePathDescriptor path = storm.Paths[pathIndex];
                var start = new Vector2(path.Start.x, path.Start.z);
                var end = new Vector2(path.End.x, path.End.z);
                Vector2 forward = (end - start).normalized;
                var right = new Vector2(forward.y, -forward.x);
                int steps = Mathf.Max(2, Mathf.CeilToInt(path.LengthXZ));
                for (int step = 0; step <= steps; step++)
                {
                    Vector2 center = Vector2.Lerp(
                        start,
                        end,
                        step / (float)steps);
                    foreach (float side in new[] { -0.75f, 0f, 0.75f })
                    {
                        Vector2 point = center +
                            right * (path.SurfaceHalfWidth * side);
                        AssertCalm(
                            storm,
                            point,
                            $"{path.StableId} at {step}/{steps}, {side}");
                    }
                }
            }
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void TrailExposure_RampsSmoothlyAndBecomesStrongOffRoute()
        {
            Assert.That(
                AlpineVillagePeripheralStormRules.EvaluateTrailExposure(
                    -4f),
                Is.EqualTo(0f));
            Assert.That(
                AlpineVillagePeripheralStormRules.EvaluateTrailExposure(
                    AlpineVillagePeripheralStormRules
                        .TrailCalmDistance),
                Is.EqualTo(0f));
            Assert.That(
                AlpineVillagePeripheralStormRules.EvaluateTrailExposure(
                    (AlpineVillagePeripheralStormRules
                         .TrailCalmDistance +
                     AlpineVillagePeripheralStormRules
                         .TrailFullStrengthDistance) * 0.5f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                AlpineVillagePeripheralStormRules.EvaluateTrailExposure(
                    AlpineVillagePeripheralStormRules
                        .TrailFullStrengthDistance),
                Is.EqualTo(1f));

            float previous = 0f;
            for (float distance = -1f;
                 distance <= 6.0001f;
                 distance += 0.05f)
            {
                float exposure = AlpineVillagePeripheralStormRules
                    .EvaluateTrailExposure(distance);
                Assert.That(
                    exposure,
                    Is.GreaterThanOrEqualTo(previous - 0.000001f),
                    $"The exposure falls at {distance:0.00} m.");
                previous = exposure;
            }

            AlpineVillagePlan village = CreateVillage();
            AlpineVillagePeripheralStormPlan storm =
                AlpineVillagePeripheralStormPlan.Create(village);
            bool foundStrongSide = false;
            for (float along = 10f;
                 along <= village.Lane.Length - 10f && !foundStrongSide;
                 along += 4f)
            {
                AlpineVillageLaneSample lane = village.Lane.Sample(along);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 candidate = lane.Position +
                        lane.Right * sign *
                        (lane.Width * 0.5f + 7f);
                    AlpineVillagePeripheralStormSample sample =
                        storm.Evaluate(candidate);
                    if (sample.DistanceOutsideTrodden <
                            AlpineVillagePeripheralStormRules
                                .TrailFullStrengthDistance ||
                        sample.LandmarkApertureProtection01 > 0.01f ||
                        sample.RearClosure01 > 0.01f)
                    {
                        continue;
                    }

                    Assert.That(sample.TrailExposure01, Is.EqualTo(1f));
                    Assert.That(sample.StormStrength01, Is.EqualTo(1f));
                    foundStrongSide = true;
                    break;
                }
            }

            Assert.That(
                foundStrongSide,
                Is.True,
                "No fully exposed side remained outside the landmark cone.");
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void StationToMotherAperture_ProtectsTheCompleteHouseDepth()
        {
            AlpineVillagePlan village = CreateVillage();
            AlpineVillagePeripheralStormPlan storm =
                AlpineVillagePeripheralStormPlan.Create(village);
            Vector2[] corners = CreateCorners(village.MothersHouse);

            for (int cornerIndex = 0;
                 cornerIndex < corners.Length;
                 cornerIndex++)
            {
                for (int step = 0; step <= 8; step++)
                {
                    Vector2 point = Vector2.Lerp(
                        storm.ApertureStart,
                        corners[cornerIndex],
                        step / 8f);
                    AlpineVillagePeripheralStormSample sample =
                        storm.Evaluate(point);
                    Assert.That(
                        sample.LandmarkApertureProtection01,
                        Is.EqualTo(1f).Within(0.0001f),
                        $"House corner ray {cornerIndex} loses its aperture " +
                        $"at {step}/8.");
                    Assert.That(
                        sample.StormStrength01,
                        Is.LessThanOrEqualTo(0.0001f),
                        $"Storm covers house corner ray {cornerIndex} at " +
                        $"{step}/8.");
                }
            }

            AlpineVillagePlotDescriptor house = village.MothersHouse;
            Vector2 center = ToXZ(house.GroundCenter);
            Vector2 facing = ToXZ(house.Facing).normalized;
            Vector2 frontWall = center +
                                facing * (house.FootprintSize.y * 0.5f);
            Vector2 backWall = center -
                               facing * (house.FootprintSize.y * 0.5f);
            foreach (Vector2 point in new[] { frontWall, center, backWall })
            {
                AlpineVillagePeripheralStormSample sample =
                    storm.Evaluate(point);
                Assert.That(
                    sample.LandmarkApertureProtection01,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(sample.StormStrength01, Is.LessThan(0.0001f));
            }

            Vector2 apertureRight = new Vector2(
                storm.ApertureDirection.y,
                -storm.ApertureDirection.x);
            float amount = 0.5f;
            float innerWidth = Mathf.Lerp(
                AlpineVillagePeripheralStormRules.ApertureNearHalfWidth,
                storm.ApertureFarHalfWidth,
                amount);
            Vector2 outside = storm.ApertureStart +
                              storm.ApertureDirection *
                              (storm.ApertureCoreLength * amount) +
                              apertureRight *
                              (innerWidth +
                               AlpineVillagePeripheralStormRules
                                   .ApertureEdgeFeather +
                               0.5f);
            Assert.That(
                storm.Evaluate(outside)
                    .LandmarkApertureProtection01,
                Is.EqualTo(0f),
                "The aperture widens into an unrestricted central vista.");
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void RearClosure_StartsBehindTheWallAndIsFullTowardTheRidge()
        {
            AlpineVillagePlan village = CreateVillage();
            AlpineVillagePeripheralStormPlan storm =
                AlpineVillagePeripheralStormPlan.Create(village);

            Assert.That(
                storm.Evaluate(
                        storm.RearWallCenter - storm.RearDirection * 0.1f)
                    .RearClosure01,
                Is.EqualTo(0f));
            Assert.That(
                storm.Evaluate(storm.RearWallCenter).RearClosure01,
                Is.EqualTo(0f).Within(0.000001f));

            float previous = 0f;
            for (float distance = 0.5f;
                 distance <= AlpineVillagePeripheralStormRules
                         .RearClosureFullDistance;
                 distance += 0.5f)
            {
                AlpineVillagePeripheralStormSample sample = storm.Evaluate(
                    storm.RearWallCenter +
                    storm.RearDirection * distance);
                Assert.That(
                    sample.RearClosure01,
                    Is.GreaterThan(previous),
                    $"The rear band does not grow at {distance:0.0} m.");
                previous = sample.RearClosure01;
            }

            AlpineVillagePeripheralStormSample full = storm.Evaluate(
                storm.RearWallCenter +
                storm.RearDirection *
                AlpineVillagePeripheralStormRules
                    .RearClosureFullDistance);
            Assert.That(full.RearClosure01, Is.EqualTo(1f));
            Assert.That(full.StormStrength01, Is.EqualTo(1f));

            float ridgeToeDistance = FindRidgeToeDistance(village, storm);
            Assert.That(
                ridgeToeDistance,
                Is.GreaterThan(0f),
                "The head ridge was not found behind the mother's house.");
            Assert.That(
                storm.Evaluate(
                        storm.RearWallCenter +
                        storm.RearDirection * ridgeToeDistance)
                    .RearClosure01,
                Is.EqualTo(1f).Within(0.0001f),
                "The rear band is still open where the ridge begins.");
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void Samples_AreDeterministicFiniteAndNormalized()
        {
            foreach (int seed in new[]
                     {
                         GameSessionState.DefaultCitySeed,
                         -99992,
                         3677,
                         89380
                     })
            {
                AlpineVillagePlan village = CreateVillage(seed);
                AlpineVillagePeripheralStormPlan first =
                    AlpineVillagePeripheralStormPlan.Create(village);
                AlpineVillagePeripheralStormPlan second =
                    AlpineVillagePeripheralStormPlan.Create(village);
                Rect bounds = village.TerrainMeshBounds;
                for (int x = 0; x <= 8; x++)
                {
                    for (int z = 0; z <= 8; z++)
                    {
                        var point = new Vector2(
                            Mathf.Lerp(bounds.xMin, bounds.xMax, x / 8f),
                            Mathf.Lerp(bounds.yMin, bounds.yMax, z / 8f));
                        AlpineVillagePeripheralStormSample left =
                            first.Evaluate(point);
                        AlpineVillagePeripheralStormSample right =
                            second.Evaluate(point);
                        AssertSampleIsFiniteAndNormalized(left);
                        Assert.That(
                            right.DistanceOutsideTrodden,
                            Is.EqualTo(left.DistanceOutsideTrodden));
                        Assert.That(
                            right.TrailOutward,
                            Is.EqualTo(left.TrailOutward));
                        Assert.That(
                            right.TrailExposure01,
                            Is.EqualTo(left.TrailExposure01));
                        Assert.That(
                            right.LandmarkApertureProtection01,
                            Is.EqualTo(
                                left.LandmarkApertureProtection01));
                        Assert.That(
                            right.RearClosure01,
                            Is.EqualTo(left.RearClosure01));
                        Assert.That(
                            right.StormStrength01,
                            Is.EqualTo(left.StormStrength01));
                    }
                }

                AssertSampleIsFiniteAndNormalized(first.Evaluate(
                    new Vector2(1e30f, 1e30f)));
            }
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void PresentationRules_CloseTheSidesWithoutTraversalState()
        {
            float onRouteRate = AlpineVillagePeripheralStormFieldRules
                .EvaluateEmissionRate(0.75f, 0f, false);
            float offRouteRate = AlpineVillagePeripheralStormFieldRules
                .EvaluateEmissionRate(0.75f, 1f, false);
            Assert.That(offRouteRate, Is.GreaterThan(onRouteRate));
            Assert.That(
                AlpineVillagePeripheralStormFieldRules
                    .EvaluateEmissionRate(1f, 1f, true),
                Is.EqualTo(0f),
                "The cabin ride must not drag a village-local curtain down " +
                "the cableway.");

            float protectedOpacity =
                AlpineVillagePeripheralStormFieldRules.EvaluateOpacity(
                    0f,
                    1f,
                    1f,
                    1f);
            float sideTroughOpacity =
                AlpineVillagePeripheralStormFieldRules.EvaluateOpacity(
                    1f,
                    0f,
                    0f,
                    0.5f);
            float sideCrestOpacity =
                AlpineVillagePeripheralStormFieldRules.EvaluateOpacity(
                    1f,
                    1f,
                    0f,
                    0.5f);
            float offRouteCrestOpacity =
                AlpineVillagePeripheralStormFieldRules.EvaluateOpacity(
                    1f,
                    1f,
                    1f,
                    0.5f);
            Assert.That(protectedOpacity, Is.EqualTo(0f));
            Assert.That(sideTroughOpacity, Is.GreaterThan(0.15f));
            Assert.That(sideCrestOpacity, Is.GreaterThan(sideTroughOpacity));
            Assert.That(
                offRouteCrestOpacity,
                Is.GreaterThan(sideCrestOpacity));

            float size = 8f;
            Assert.That(
                AlpineVillagePeripheralStormFieldRules
                    .EvaluateFootprintTrailExposure(2f, size),
                Is.EqualTo(0f),
                "A large sheet whose centre is outside the lane still " +
                "overlaps the protected trodden surface.");
            Assert.That(
                AlpineVillagePeripheralStormFieldRules
                    .EvaluateFootprintTrailExposure(9f, size),
                Is.EqualTo(1f));
            Assert.That(
                AlpineVillagePeripheralStormFieldRules
                    .EvaluateFootprintRearClosure(2f, size),
                Is.EqualTo(0f),
                "A rear sheet must not spill through the house wall.");

            AlpineVillagePlan village = CreateVillage();
            AlpineVillagePeripheralStormPlan spatial =
                AlpineVillagePeripheralStormPlan.Create(village);
            Vector2 apertureRight = new Vector2(
                spatial.ApertureDirection.y,
                -spatial.ApertureDirection.x);
            float along = spatial.ApertureCoreLength * 0.5f;
            float halfWidth = Mathf.Lerp(
                AlpineVillagePeripheralStormRules.ApertureNearHalfWidth,
                spatial.ApertureFarHalfWidth,
                0.5f);
            Vector2 justOutside = spatial.ApertureStart +
                                  spatial.ApertureDirection * along +
                                  apertureRight *
                                  (halfWidth +
                                   AlpineVillagePeripheralStormRules
                                       .ApertureEdgeFeather +
                                   0.2f);
            Assert.That(
                spatial.EvaluateLandmarkApertureProtection(justOutside),
                Is.EqualTo(0f));
            Assert.That(
                AlpineVillagePeripheralStormFieldRules
                    .EvaluateFootprintApertureProtection(
                        spatial,
                        justOutside,
                        size),
                Is.GreaterThan(0.95f),
                "A large sheet outside the cone still overlaps the protected " +
                "station-to-house aperture.");
        }

        private static void AssertCalm(
            AlpineVillagePeripheralStormPlan storm,
            Vector2 point,
            string label)
        {
            AlpineVillagePeripheralStormSample sample =
                storm.Evaluate(point);
            Assert.That(
                sample.DistanceOutsideTrodden,
                Is.LessThanOrEqualTo(0.0001f),
                label);
            Assert.That(sample.TrailExposure01, Is.EqualTo(0f), label);
            Assert.That(sample.RearClosure01, Is.EqualTo(0f), label);
            Assert.That(sample.StormStrength01, Is.EqualTo(0f), label);
        }

        private static Vector2[] CreateCorners(
            AlpineVillagePlotDescriptor house)
        {
            Vector2 center = ToXZ(house.GroundCenter);
            Vector2 forward = ToXZ(house.Facing).normalized;
            var right = new Vector2(forward.y, -forward.x);
            float halfWidth = house.FootprintSize.x * 0.5f;
            float halfDepth = house.FootprintSize.y * 0.5f;
            return new[]
            {
                center + right * halfWidth + forward * halfDepth,
                center + right * halfWidth - forward * halfDepth,
                center - right * halfWidth + forward * halfDepth,
                center - right * halfWidth - forward * halfDepth
            };
        }

        private static float FindRidgeToeDistance(
            AlpineVillagePlan village,
            AlpineVillagePeripheralStormPlan storm)
        {
            for (float distance = 0.25f; distance <= 45f; distance += 0.25f)
            {
                Vector2 point = storm.RearWallCenter +
                                storm.RearDirection * distance;
                if (AlpineVillageTerrainSampler.SampleRidgeRise(
                        village,
                        point) > 0.001f)
                {
                    return distance;
                }
            }

            return 0f;
        }

        private static void AssertSampleIsFiniteAndNormalized(
            AlpineVillagePeripheralStormSample sample)
        {
            AssertFinite(sample.DistanceOutsideTrodden);
            AssertFinite(sample.TrailOutward.x);
            AssertFinite(sample.TrailOutward.y);
            AssertUnit(sample.TrailExposure01);
            AssertUnit(sample.LandmarkApertureProtection01);
            AssertUnit(sample.RearClosure01);
            AssertUnit(sample.StormStrength01);
        }

        private static void AssertUnit(float value)
        {
            AssertFinite(value);
            Assert.That(value, Is.InRange(0f, 1f));
        }

        private static void AssertFinite(float value)
        {
            Assert.That(float.IsNaN(value), Is.False);
            Assert.That(float.IsInfinity(value), Is.False);
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }
}
