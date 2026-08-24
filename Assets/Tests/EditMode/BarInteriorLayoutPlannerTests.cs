using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarInteriorLayoutPlannerTests
    {
        [Test]
        public void Generate_CreatesLargeZonedInteriorWithBoundedAnchors()
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    20260727,
                    "bar-layout-test",
                    BarActivityKind.Cocktail);

            Assert.That(
                plan.RoomSize,
                Is.EqualTo(new Vector2(22f, 16f)));
            Assert.That(plan.RoomHeight, Is.EqualTo(4.8f));
            Assert.That(plan.WallThickness, Is.EqualTo(0.3f));
            Assert.That(
                plan.PlayerSpawn,
                Is.EqualTo(new Vector3(0f, 0.12f, -6.45f)));
            Assert.That(
                plan.ExitPosition,
                Is.EqualTo(new Vector3(0f, 0.9f, -7.25f)));
            Assert.That(
                plan.CounterSize,
                Is.EqualTo(new Vector3(11.2f, 1.4f, 1f)));
            Assert.That(
                plan.CounterStationPosition,
                Is.EqualTo(new Vector3(-1.15f, 0.9f, 4.75f)));
            Assert.That(
                plan.CounterStationTriggerSize,
                Is.EqualTo(new Vector3(1.1f, 1.8f, 0.85f)));
            Assert.That(plan.Zones, Has.Count.EqualTo(7));
            Assert.That(plan.Paths, Has.Count.EqualTo(4));
            Assert.That(
                plan.FurnitureFootprints,
                Has.Count.EqualTo(12));
            Assert.That(plan.NpcAnchors, Has.Count.EqualTo(12));
            Assert.That(
                BarInteriorLayoutValidator.MaximumNpcAnchors,
                Is.EqualTo(14));
            Assert.That(
                plan.NpcAnchors.Count,
                Is.LessThanOrEqualTo(
                    BarInteriorLayoutValidator.MaximumNpcAnchors));
            Assert.That(plan.LightAnchors, Has.Count.EqualTo(6));
            Assert.That(
                BarInteriorLayoutValidator.MaximumLightAnchors,
                Is.EqualTo(6));
            Assert.That(
                plan.LightAnchors.Count,
                Is.LessThanOrEqualTo(
                    BarInteriorLayoutValidator.MaximumLightAnchors));
            Assert.That(plan.AudioAnchors, Has.Count.EqualTo(2));

            foreach (BarInteriorZoneKind kind in
                     Enum.GetValues(typeof(BarInteriorZoneKind)))
            {
                Assert.That(
                    plan.TryGetZone(kind, out _),
                    Is.True,
                    $"Missing zone {kind}.");
            }

            Assert.That(
                plan.TryGetFurniture(
                    BarInteriorFurnitureKind.Counter,
                    out BarInteriorFurnitureFootprint counter),
                Is.True);
            Assert.That(
                counter.Center,
                Is.EqualTo(plan.CounterPosition));
            Assert.That(counter.Size, Is.EqualTo(plan.CounterSize));
            Assert.DoesNotThrow(
                () => BarInteriorLayoutValidator.ValidateOrThrow(plan));
        }

        [TestCase(BarActivityKind.Cocktail)]
        [TestCase(BarActivityKind.BeerPong)]
        [TestCase(BarActivityKind.SplitTheG)]
        [TestCase(BarActivityKind.TinctureMatch)]
        public void Generate_EveryActivityOwnsOneReachableFixture(
            BarActivityKind activity)
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    -8821,
                    "bar-activity-test",
                    activity);

            Assert.That(plan.Activity, Is.EqualTo(activity));
            Assert.That(
                plan.TryGetFurniture(
                    BarInteriorFurnitureKind.ActivityFixture,
                    out BarInteriorFurnitureFootprint fixture),
                Is.True);
            Assert.That(fixture.Activity, Is.EqualTo(activity));
            Assert.That(
                plan.FurnitureFootprints.Count(
                    item =>
                        item.Kind ==
                        BarInteriorFurnitureKind.ActivityFixture),
                Is.EqualTo(1));
            Assert.That(
                plan.Paths.Any(
                    path =>
                        Contains(
                            path.Bounds,
                            plan.ActivityStationPosition)),
                Is.True);
            Assert.That(
                plan.CounterStationPosition,
                Is.EqualTo(new Vector3(-1.15f, 0.9f, 4.75f)));
            Assert.That(
                plan.CounterStationTriggerSize,
                Is.EqualTo(new Vector3(1.1f, 1.8f, 0.85f)));
            Assert.That(
                plan.Paths.Any(
                    path =>
                        Contains(
                            path.Bounds,
                            plan.CounterStationPosition)),
                Is.True);
            Assert.DoesNotThrow(
                () => BarInteriorLayoutValidator.ValidateOrThrow(plan));
        }

        [Test]
        public void Generate_NoneActivityFallsBackToCocktail()
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    17,
                    string.Empty,
                    BarActivityKind.None);

            Assert.That(
                plan.Activity,
                Is.EqualTo(BarActivityKind.Cocktail));
        }

        [Test]
        public void Generate_SameInputsProduceIdenticalPlan()
        {
            BarInteriorLayoutPlan first =
                BarInteriorLayoutPlanner.Generate(
                    48125,
                    "bar-stable-03",
                    BarActivityKind.TinctureMatch);
            BarInteriorLayoutPlan second =
                BarInteriorLayoutPlanner.Generate(
                    48125,
                    "bar-stable-03",
                    BarActivityKind.TinctureMatch);

            Assert.That(second.StableSeed, Is.EqualTo(first.StableSeed));
            Assert.That(second.RoomBounds, Is.EqualTo(first.RoomBounds));
            Assert.That(
                second.WalkableBounds,
                Is.EqualTo(first.WalkableBounds));
            Assert.That(
                second.CounterStationPosition,
                Is.EqualTo(first.CounterStationPosition));
            Assert.That(
                second.CounterStationTriggerSize,
                Is.EqualTo(first.CounterStationTriggerSize));
            CollectionAssert.AreEqual(first.Zones, second.Zones);
            CollectionAssert.AreEqual(first.Paths, second.Paths);
            CollectionAssert.AreEqual(
                first.FurnitureFootprints,
                second.FurnitureFootprints);
            CollectionAssert.AreEqual(
                first.NpcAnchors,
                second.NpcAnchors);
            CollectionAssert.AreEqual(
                first.LightAnchors,
                second.LightAnchors);
            CollectionAssert.AreEqual(
                first.AudioAnchors,
                second.AudioAnchors);
        }

        [Test]
        public void StableSeed_UsesStableStringContentHash()
        {
            Assert.That(
                BarInteriorLayoutPlanner.ComputeStableSeed(
                    20260727,
                    "bar-alpha"),
                Is.EqualTo(1676406455u));
            Assert.That(
                BarInteriorLayoutPlanner.ComputeStableSeed(
                    20260727,
                    new string("bar-alpha".ToCharArray())),
                Is.EqualTo(1676406455u));
            Assert.That(
                BarInteriorLayoutPlanner.ComputeStableSeed(
                    20260727,
                    "bar-beta"),
                Is.Not.EqualTo(1676406455u));
        }

        [Test]
        public void Generate_DifferentBarIdsVaryNpcPresentationOnly()
        {
            BarInteriorLayoutPlan first =
                BarInteriorLayoutPlanner.Generate(
                    712,
                    "bar-one",
                    BarActivityKind.SplitTheG);
            BarInteriorLayoutPlan second =
                BarInteriorLayoutPlanner.Generate(
                    712,
                    "bar-two",
                    BarActivityKind.SplitTheG);

            Assert.That(second.StableSeed, Is.Not.EqualTo(first.StableSeed));
            CollectionAssert.AreEqual(first.Zones, second.Zones);
            CollectionAssert.AreEqual(first.Paths, second.Paths);
            CollectionAssert.AreEqual(
                first.FurnitureFootprints,
                second.FurnitureFootprints);
            Assert.That(
                second.NpcAnchors.Select(
                    anchor =>
                        (
                            anchor.VisualVariant,
                            anchor.AnimationPhase,
                            anchor.Position))
                    .SequenceEqual(
                        first.NpcAnchors.Select(
                            anchor =>
                                (
                                    anchor.VisualVariant,
                                    anchor.AnimationPhase,
                                    anchor.Position))),
                Is.False);
        }

        [Test]
        public void GeneratedNpcRolesMatchCrowdBudget()
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    91275,
                    "bar-crowd",
                    BarActivityKind.BeerPong);

            Assert.That(
                plan.NpcAnchors.Count(
                    anchor =>
                        anchor.Role == BarNpcRole.Bartender),
                Is.EqualTo(1));
            Assert.That(
                plan.NpcAnchors.Count(
                    anchor =>
                        anchor.Role == BarNpcRole.SeatedPatron),
                Is.EqualTo(6));
            Assert.That(
                plan.NpcAnchors.Count(
                    anchor =>
                        anchor.Role == BarNpcRole.Performer),
                Is.EqualTo(1));
            Assert.That(
                plan.NpcAnchors.Count(
                    anchor =>
                        anchor.Role == BarNpcRole.StandingPatron),
                Is.EqualTo(3));
            Assert.That(
                plan.NpcAnchors.Count(
                    anchor =>
                        anchor.Role == BarNpcRole.Walker),
                Is.EqualTo(1));
        }

        [Test]
        public void GeneratedNpcAnchors_RestOnFloorOrStage()
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    91275,
                    "bar-grounded-crowd",
                    BarActivityKind.Cocktail);

            foreach (BarNpcAnchor anchor in plan.NpcAnchors)
            {
                float expectedHeight =
                    anchor.Role == BarNpcRole.Performer
                        ? 0.32f
                        : 0f;
                Assert.That(
                    anchor.Position.y,
                    Is.EqualTo(expectedHeight).Within(0.001f),
                    anchor.Id);
            }
        }

        [Test]
        public void Validator_RejectsFurnitureAcrossReservedPath()
        {
            BarInteriorLayoutPlan valid =
                BarInteriorLayoutPlanner.Generate(
                    100,
                    "bar-invalid-furniture",
                    BarActivityKind.Cocktail);
            var furniture =
                new List<BarInteriorFurnitureFootprint>(
                    valid.FurnitureFootprints)
                {
                    new BarInteriorFurnitureFootprint(
                        "main-path-blocker",
                        BarInteriorFurnitureKind.HighTopTable,
                        new Rect(-0.5f, -4f, 1f, 1f),
                        1f,
                        true)
                };
            BarInteriorLayoutPlan invalid =
                Copy(valid, furnitureFootprints: furniture);

            Assert.Throws<InvalidOperationException>(
                () =>
                    BarInteriorLayoutValidator.ValidateOrThrow(
                        invalid));
        }

        [Test]
        public void Validator_RejectsInvalidCounterStationGeometry()
        {
            BarInteriorLayoutPlan valid =
                BarInteriorLayoutPlanner.Generate(
                    102,
                    "bar-invalid-counter-station",
                    BarActivityKind.Cocktail);

            Assert.Throws<InvalidOperationException>(
                () => BarInteriorLayoutValidator.ValidateOrThrow(
                    Copy(
                        valid,
                        counterStationPosition:
                            new Vector3(float.NaN, 0.9f, 4.75f))));
            Assert.Throws<InvalidOperationException>(
                () => BarInteriorLayoutValidator.ValidateOrThrow(
                    Copy(
                        valid,
                        counterStationTriggerSize:
                            new Vector3(0f, 1.8f, 0.85f))));
            Assert.Throws<InvalidOperationException>(
                () => BarInteriorLayoutValidator.ValidateOrThrow(
                    Copy(
                        valid,
                        counterStationPosition:
                            new Vector3(3f, 0.9f, 4.75f))));
        }

        [Test]
        public void Validator_RejectsCounterStationIntersections()
        {
            BarInteriorLayoutPlan valid =
                BarInteriorLayoutPlanner.Generate(
                    103,
                    "bar-counter-station-overlap",
                    BarActivityKind.BeerPong);

            Assert.Throws<InvalidOperationException>(
                () => BarInteriorLayoutValidator.ValidateOrThrow(
                    Copy(
                        valid,
                        counterStationTriggerSize:
                            new Vector3(1.1f, 1.8f, 2f))));
            Assert.Throws<InvalidOperationException>(
                () => BarInteriorLayoutValidator.ValidateOrThrow(
                    Copy(
                        valid,
                        activityStationPosition:
                            valid.CounterStationPosition)));
        }

        [Test]
        public void WorldBuilder_LeavesCounterStationApproachFreeOfStools()
        {
            GameObject host = new GameObject("Bar Layout Test Host");
            try
            {
                BarInteriorLayoutPlan plan =
                    BarInteriorLayoutPlanner.Generate(
                        104,
                        "bar-counter-station-stools",
                        BarActivityKind.TinctureMatch);
                Transform room = BarInteriorWorldBuilder.Build(
                    host.transform,
                    plan);
                Transform[] stools = room
                    .GetComponentsInChildren<Transform>(true)
                    .Where(
                        child =>
                            child.name.StartsWith(
                                "Bar Stool ",
                                StringComparison.Ordinal) &&
                            !child.name.EndsWith(
                                " Leg",
                                StringComparison.Ordinal))
                    .ToArray();

                Assert.That(stools, Has.Length.EqualTo(5));
                for (int index = 0; index < stools.Length; index++)
                {
                    Vector3 stoolPosition = stools[index].localPosition;
                    float distance = Vector2.Distance(
                        new Vector2(stoolPosition.x, stoolPosition.z),
                        new Vector2(
                            plan.CounterStationPosition.x,
                            plan.CounterStationPosition.z));
                    Assert.That(
                        distance,
                        Is.GreaterThanOrEqualTo(1.349f),
                        stools[index].name);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Validator_RejectsPracticalLightBudgetOverflow()
        {
            BarInteriorLayoutPlan valid =
                BarInteriorLayoutPlanner.Generate(
                    101,
                    "bar-invalid-lights",
                    BarActivityKind.Cocktail);
            var lights =
                new List<BarInteriorLightAnchor>(
                    valid.LightAnchors)
                {
                    new BarInteriorLightAnchor(
                        "overflow-light",
                        BarInteriorLightKind.StageRim,
                        new Vector3(0f, 3f, 0f),
                        Vector3.down,
                        Color.white,
                        1f,
                        4f,
                        60f)
                };
            BarInteriorLayoutPlan invalid =
                Copy(valid, lightAnchors: lights);

            Assert.Throws<InvalidOperationException>(
                () =>
                    BarInteriorLayoutValidator.ValidateOrThrow(
                        invalid));
        }

        [Test]
        public void Validator_RejectsNpcBudgetOverflow()
        {
            BarInteriorLayoutPlan valid =
                BarInteriorLayoutPlanner.Generate(
                    102,
                    "bar-invalid-npcs",
                    BarActivityKind.Cocktail);
            var npcs = new List<BarNpcAnchor>(valid.NpcAnchors);
            for (int index = 0; index < 3; index++)
            {
                npcs.Add(new BarNpcAnchor(
                    $"overflow-npc-{index}",
                    BarNpcRole.StandingPatron,
                    new Vector3(4.5f + index * 0.2f, 0.12f, -4f),
                    180f,
                    index,
                    index * 0.1f));
            }

            BarInteriorLayoutPlan invalid =
                Copy(valid, npcAnchors: npcs);

            Assert.Throws<InvalidOperationException>(
                () =>
                    BarInteriorLayoutValidator.ValidateOrThrow(
                        invalid));
        }

        private static BarInteriorLayoutPlan Copy(
            BarInteriorLayoutPlan source,
            IReadOnlyList<BarInteriorFurnitureFootprint>
                furnitureFootprints = null,
            IReadOnlyList<BarInteriorLightAnchor> lightAnchors = null,
            IReadOnlyList<BarNpcAnchor> npcAnchors = null,
            Vector3? counterStationPosition = null,
            Vector3? counterStationTriggerSize = null,
            Vector3? activityStationPosition = null)
        {
            return new BarInteriorLayoutPlan(
                source.CitySeed,
                source.StableSeed,
                source.BarId,
                source.Activity,
                source.RoomSize,
                source.RoomHeight,
                source.WallThickness,
                source.WalkableBounds,
                source.PlayerSpawn,
                source.ExitPosition,
                source.ExitApproachPosition,
                source.ExitTriggerSize,
                source.CounterPosition,
                source.CounterSize,
                counterStationPosition ??
                    source.CounterStationPosition,
                counterStationTriggerSize ??
                    source.CounterStationTriggerSize,
                activityStationPosition ??
                    source.ActivityStationPosition,
                source.ActivityStationTriggerSize,
                source.Zones,
                source.Paths,
                furnitureFootprints ?? source.FurnitureFootprints,
                npcAnchors ?? source.NpcAnchors,
                lightAnchors ?? source.LightAnchors,
                source.AudioAnchors);
        }

        private static bool Contains(Rect bounds, Vector3 point)
        {
            return point.x >= bounds.xMin &&
                   point.x <= bounds.xMax &&
                   point.z >= bounds.yMin &&
                   point.z <= bounds.yMax;
        }
    }
}
