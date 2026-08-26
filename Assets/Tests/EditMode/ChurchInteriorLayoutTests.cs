using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ChurchInteriorLayoutTests
    {
        [Test]
        public void Generate_CreatesReachableNaveAndSealedSanctuary()
        {
            ChurchInteriorLayoutPlan plan =
                ChurchInteriorLayoutPlanner.Generate(20260826);

            Assert.That(
                plan.RoomSize,
                Is.EqualTo(new Vector2(23f, 44f)));
            Assert.That(plan.RoomHeight, Is.EqualTo(14.25f));
            AssertBoundsNear(
                plan.ModelLocalBounds,
                new Vector3(-11.41f, -0.24f, -22.01f),
                new Vector3(11.41f, 14f, 22.01f));
            Assert.That(
                plan.ModelResourcePath,
                Is.EqualTo("Church/ChurchInterior3D"));
            Assert.That(
                plan.Zones,
                Has.Count.EqualTo(
                    ChurchInteriorLayoutValidator.RequiredZoneCount));
            Assert.That(
                plan.Paths,
                Has.Count.EqualTo(
                    ChurchInteriorLayoutValidator.RequiredPathCount));
            Assert.That(
                plan.Fixtures,
                Has.Count.EqualTo(
                    ChurchInteriorLayoutValidator.RequiredFixtureCount));
            Assert.That(plan.PlayerSpawn.z, Is.EqualTo(-18.8f));
            Assert.That(plan.ExitPosition.z, Is.EqualTo(-21f));
            Assert.That(plan.ExitTriggerSize.x, Is.EqualTo(2.8f));

            foreach (ChurchInteriorZoneKind kind in
                     Enum.GetValues(typeof(ChurchInteriorZoneKind)))
            {
                Assert.That(
                    plan.TryGetZone(kind, out _),
                    Is.True,
                    $"Missing zone {kind}.");
            }

            Assert.That(
                plan.TryGetZone(
                    ChurchInteriorZoneKind.Sanctuary,
                    out ChurchInteriorZonePlan sanctuary),
                Is.True);
            Assert.That(sanctuary.IsAccessible, Is.False);
            Assert.That(
                plan.WalkableBounds.yMax,
                Is.EqualTo(
                    ChurchInteriorLayoutPlanner
                        .SanctuaryBoundaryZ)
                    .Within(0.001f));

            ChurchInteriorFixturePlan rail = SingleFixture(
                plan,
                ChurchInteriorFixtureKind.AltarRail);
            Assert.That(
                rail.Bounds,
                Is.EqualTo(new Rect(-10.8f, 12.2f, 21.6f, 0.4f)));
            Assert.That(rail.BlocksMovement, Is.True);
            Assert.DoesNotThrow(
                () => ChurchInteriorLayoutValidator
                    .ValidateOrThrow(plan));
        }

        [Test]
        public void Generate_PinsCatholicFurnitureAndClearRoutes()
        {
            ChurchInteriorLayoutPlan plan =
                ChurchInteriorLayoutPlanner.Generate(17);

            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.Pier,
                4);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.Pew,
                12);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.Confessional,
                2);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.VotiveCandleStand,
                2);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.BaptismalFont,
                1);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.ChoirLoftSupport,
                ChurchInteriorLayoutValidator
                    .RequiredChoirLoftSupportCount);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.AltarTable,
                1);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.HighAltar,
                1);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.Crucifix,
                1);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.ChoirLoft,
                1);
            AssertFixtureCount(
                plan,
                ChurchInteriorFixtureKind.Organ,
                1);

            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.Pier,
                new[]
                {
                    new Vector2(-5.5f, -3.5f),
                    new Vector2(5.5f, -3.5f),
                    new Vector2(-5.5f, 5.5f),
                    new Vector2(5.5f, 5.5f)
                },
                new Vector2(1.4f, 1.4f),
                0f,
                9.6f);

            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.Pew,
                new[]
                {
                    new Vector2(-2.9f, -12f),
                    new Vector2(2.9f, -12f),
                    new Vector2(-2.9f, -10.45f),
                    new Vector2(2.9f, -10.45f),
                    new Vector2(-2.9f, -8.9f),
                    new Vector2(2.9f, -8.9f),
                    new Vector2(-2.9f, -7.35f),
                    new Vector2(2.9f, -7.35f),
                    new Vector2(-2.9f, -5.8f),
                    new Vector2(2.9f, -5.8f),
                    new Vector2(-2.9f, -4.25f),
                    new Vector2(2.9f, -4.25f)
                },
                new Vector2(3.8f, 0.72f),
                0f,
                1.5f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.AltarRail,
                new[] { new Vector2(0f, 12.4f) },
                new Vector2(21.6f, 0.4f),
                0f,
                0.92f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.AltarTable,
                new[] { new Vector2(0f, 15.7f) },
                new Vector2(2.75f, 1.55f),
                0f,
                1.14f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.HighAltar,
                new[] { new Vector2(0f, 18.0f) },
                new Vector2(4.2f, 2.5f),
                0f,
                6.2f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.Crucifix,
                new[] { new Vector2(0f, 20.65f) },
                new Vector2(2.5f, 0.35f),
                3.5f,
                4.7f);
            Assert.That(
                SingleFixture(plan, ChurchInteriorFixtureKind.Crucifix)
                    .BlocksMovement,
                Is.False);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.Confessional,
                new[]
                {
                    new Vector2(-9.7f, 7.3f),
                    new Vector2(9.7f, 7.3f)
                },
                new Vector2(1.8f, 3.3f),
                0f,
                3.15f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.VotiveCandleStand,
                new[]
                {
                    new Vector2(-8.8f, 10.5f),
                    new Vector2(8.8f, 10.5f)
                },
                new Vector2(0.8f, 0.8f),
                0f,
                1.35f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.BaptismalFont,
                new[] { new Vector2(-8.8f, -16.8f) },
                new Vector2(1.1f, 1.1f),
                0f,
                1.11f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.ChoirLoftSupport,
                new[]
                {
                    new Vector2(-8f, -18.2f),
                    new Vector2(-5.3f, -18.2f),
                    new Vector2(5.3f, -18.2f),
                    new Vector2(8f, -18.2f)
                },
                new Vector2(0.32f, 0.32f),
                0f,
                4.4f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.ChoirLoft,
                new[] { new Vector2(0f, -18.4f) },
                new Vector2(17f, 4.2f),
                4.4f,
                0.4f);
            AssertFixtureContract(
                plan,
                ChurchInteriorFixtureKind.Organ,
                new[] { new Vector2(0f, -20.3f) },
                new Vector2(12f, 1.6f),
                4.8f,
                6.9f);

            Assert.That(
                SingleFixture(
                    plan,
                    ChurchInteriorFixtureKind.BaptismalFont)
                    .Bounds.center,
                Is.EqualTo(new Vector2(-8.8f, -16.8f)));
            Assert.That(
                SingleFixture(
                    plan,
                    ChurchInteriorFixtureKind.ChoirLoft)
                    .BaseHeight,
                Is.EqualTo(4.4f));
            Assert.That(
                SingleFixture(
                    plan,
                    ChurchInteriorFixtureKind.Organ)
                    .BaseHeight,
                Is.EqualTo(4.8f));

            for (int pathIndex = 0;
                 pathIndex < plan.Paths.Count;
                 pathIndex++)
            {
                ChurchInteriorPathPlan path = plan.Paths[pathIndex];
                Assert.That(
                    path.MinimumClearance,
                    Is.GreaterThanOrEqualTo(
                        ChurchInteriorLayoutValidator
                            .MinimumRouteClearance));
                for (int fixtureIndex = 0;
                     fixtureIndex < plan.Fixtures.Count;
                     fixtureIndex++)
                {
                    ChurchInteriorFixturePlan fixture =
                        plan.Fixtures[fixtureIndex];
                    if (fixture.BlocksMovement &&
                        fixture.BaseHeight <
                        ChurchInteriorLayoutValidator
                            .PlayerRouteClearHeight)
                    {
                        Assert.That(
                            path.Bounds.Overlaps(fixture.Bounds),
                            Is.False,
                            $"{fixture.Id} blocks {path.Id}.");
                    }
                }
            }
        }

        [Test]
        public void InteriorPrefab_MatchesTheDataFirstLayoutContract()
        {
            ChurchInteriorLayoutPlan plan =
                ChurchInteriorLayoutPlanner.Generate(20260826);
            GameObject prefab = ChurchResources.LoadInteriorPrefab();

            Assert.That(prefab, Is.Not.Null);
            ChurchAssetRegistry registry =
                prefab.GetComponent<ChurchAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Kind, Is.EqualTo(ChurchAssetKind.Interior));
            Assert.That(
                registry.DesignId,
                Is.EqualTo(
                    "provincial_catholic_gothic_basilica_v1"));
            Assert.That(registry.BuildSignature, Is.Not.Empty);
            Assert.That(
                registry.Dimensions.Width,
                Is.EqualTo(plan.RoomSize.x).Within(0.001f));
            Assert.That(
                registry.Dimensions.Length,
                Is.EqualTo(plan.RoomSize.y).Within(0.001f));
            AssertBoundsNear(
                registry.LocalBounds,
                plan.ModelLocalBounds.min,
                plan.ModelLocalBounds.max);
            Assert.That(
                registry.LocalBounds.max.y,
                Is.EqualTo(14f).Within(0.01f));
            AssertAnchorXZ(
                registry,
                registry.SpawnAnchor,
                new Vector2(plan.PlayerSpawn.x, plan.PlayerSpawn.z));
            AssertAnchorXZ(
                registry,
                registry.ExitAnchor,
                new Vector2(plan.ExitPosition.x, plan.ExitPosition.z));
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The prefab must stay passive; the layout builds collision.");
        }

        [Test]
        public void Generate_KeepsSpawnToNaveRouteOpenThroughNarthex()
        {
            ChurchInteriorLayoutPlan plan =
                ChurchInteriorLayoutPlanner.Generate(20260826);
            Assert.That(
                plan.TryGetPath(
                    ChurchInteriorPathKind.MainNave,
                    out ChurchInteriorPathPlan mainNave),
                Is.True);
            Assert.That(
                plan.TryGetPath(
                    ChurchInteriorPathKind.NarthexCrossing,
                    out ChurchInteriorPathPlan narthexCrossing),
                Is.True);

            Assert.That(
                mainNave.Bounds.Contains(
                    new Vector2(
                        plan.PlayerSpawn.x,
                        plan.PlayerSpawn.z)),
                Is.True,
                "The spawn must stand inside the protected main nave.");
            Assert.That(
                mainNave.Bounds.Overlaps(narthexCrossing.Bounds),
                Is.True,
                "The main nave must remain open through the narthex.");
            Assert.That(
                mainNave.Bounds.width,
                Is.GreaterThanOrEqualTo(mainNave.MinimumClearance));
            Assert.That(
                narthexCrossing.Bounds.height,
                Is.GreaterThanOrEqualTo(
                    narthexCrossing.MinimumClearance));

            AssertGroundRouteClear(plan, mainNave);
            AssertGroundRouteClear(plan, narthexCrossing);
        }

        [Test]
        public void DoorDirections_AppendChurchWithoutRenumberingLegacy()
        {
            Assert.That((int)DoorTransitionDirection.EnterBar, Is.EqualTo(0));
            Assert.That((int)DoorTransitionDirection.ExitBar, Is.EqualTo(1));
            Assert.That(
                (int)DoorTransitionDirection.EnterBuilding,
                Is.EqualTo(2));
            Assert.That(
                (int)DoorTransitionDirection.ExitBuilding,
                Is.EqualTo(3));
            Assert.That(
                (int)DoorTransitionDirection.EnterApartment,
                Is.EqualTo(4));
            Assert.That(
                (int)DoorTransitionDirection.ExitApartment,
                Is.EqualTo(5));
            Assert.That(
                (int)DoorTransitionDirection.EnterChurch,
                Is.EqualTo(6));
            Assert.That(
                (int)DoorTransitionDirection.ExitChurch,
                Is.EqualTo(7));
        }

        private static ChurchInteriorFixturePlan SingleFixture(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorFixtureKind kind)
        {
            return plan.Fixtures.Single(
                fixture => fixture.Kind == kind);
        }

        private static void AssertAnchorXZ(
            ChurchAssetRegistry registry,
            Transform anchor,
            Vector2 expected)
        {
            Assert.That(anchor, Is.Not.Null);
            Vector3 local = registry.transform.InverseTransformPoint(
                anchor.position);
            Assert.That(
                local.x,
                Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(
                local.z,
                Is.EqualTo(expected.y).Within(0.01f));
        }

        private static void AssertBoundsNear(
            Bounds actual,
            Vector3 expectedMin,
            Vector3 expectedMax)
        {
            AssertVectorNear(actual.min, expectedMin);
            AssertVectorNear(actual.max, expectedMax);
        }

        private static void AssertVectorNear(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.01f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.01f));
        }

        private static void AssertGroundRouteClear(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorPathPlan path)
        {
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                ChurchInteriorFixturePlan fixture = plan.Fixtures[index];
                if (!fixture.BlocksMovement ||
                    !path.Bounds.Overlaps(fixture.Bounds))
                {
                    continue;
                }

                Assert.That(
                    fixture.BaseHeight,
                    Is.GreaterThanOrEqualTo(
                        ChurchInteriorLayoutValidator
                            .PlayerRouteClearHeight),
                    $"{fixture.Id} blocks {path.Id} below player height.");
            }
        }

        private static ChurchInteriorFixturePlan[] Fixtures(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorFixtureKind kind)
        {
            return plan.Fixtures
                .Where(fixture => fixture.Kind == kind)
                .ToArray();
        }

        private static void AssertFixtureContract(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorFixtureKind kind,
            Vector2[] expectedCenters,
            Vector2 expectedFootprint,
            float expectedBaseHeight,
            float expectedHeight)
        {
            ChurchInteriorFixturePlan[] fixtures =
                Fixtures(plan, kind);
            Assert.That(fixtures, Has.Length.EqualTo(expectedCenters.Length));
            for (int index = 0; index < fixtures.Length; index++)
            {
                ChurchInteriorFixturePlan fixture = fixtures[index];
                Assert.That(
                    fixture.Bounds.center.x,
                    Is.EqualTo(expectedCenters[index].x).Within(0.001f),
                    fixture.Id);
                Assert.That(
                    fixture.Bounds.center.y,
                    Is.EqualTo(expectedCenters[index].y).Within(0.001f),
                    fixture.Id);
                Assert.That(
                    fixture.Bounds.size,
                    Is.EqualTo(expectedFootprint),
                    fixture.Id);
                Assert.That(
                    fixture.BaseHeight,
                    Is.EqualTo(expectedBaseHeight),
                    fixture.Id);
                Assert.That(
                    fixture.Height,
                    Is.EqualTo(expectedHeight),
                    fixture.Id);
            }
        }

        private static void AssertFixtureCount(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorFixtureKind kind,
            int expected)
        {
            Assert.That(
                plan.Fixtures.Count(fixture => fixture.Kind == kind),
                Is.EqualTo(expected));
        }
    }
}
