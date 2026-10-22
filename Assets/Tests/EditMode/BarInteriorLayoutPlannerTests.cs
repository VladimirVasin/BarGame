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
        public void Generate_CreatesCompactZonedInteriorInsideExterior()
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    20260727,
                    "bar-layout-test",
                    BarActivityKind.Cocktail);

            Assert.That(
                plan.RoomSize,
                Is.EqualTo(new Vector2(11.4f, 7.8f)));
            Assert.That(plan.RoomHeight, Is.EqualTo(3f));
            Assert.That(plan.WallThickness, Is.EqualTo(0.3f));
            Assert.That(
                plan.PlayerSpawn,
                Is.EqualTo(new Vector3(0f, 0.12f, -1.8f)));
            Assert.That(
                plan.ExitPosition,
                Is.EqualTo(new Vector3(0f, 0.9f, -3.55f)));
            Assert.That(
                plan.CounterSize,
                Is.EqualTo(new Vector3(5.95f, 0.86f, 1f)));
            Assert.That(plan.CounterPosition,
                Is.EqualTo(new Vector3(0.875f, 0.43f, 1.95f)));
            Assert.That(
                plan.CounterStationPosition,
                Is.EqualTo(new Vector3(-0.75f, 0.9f, 0.55f)));
            Assert.That(
                plan.CounterStationTriggerSize,
                Is.EqualTo(new Vector3(1.1f, 1.8f, 0.85f)));
            Assert.That(plan.Zones, Has.Count.EqualTo(7));
            Assert.That(plan.Paths, Has.Count.EqualTo(4));
            Assert.That(
                plan.FurnitureFootprints,
                Has.Count.EqualTo(10));
            Assert.That(plan.NpcAnchors, Has.Count.EqualTo(10));
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
            Assert.That(plan.AudioAnchors, Has.Count.EqualTo(4));
            Assert.That(
                plan.AudioAnchors.Count(anchor =>
                    anchor.Kind == BarInteriorAudioKind.CrowdBed),
                Is.EqualTo(2));
            Assert.That(
                plan.AudioAnchors.Count(anchor =>
                    anchor.Kind == BarInteriorAudioKind.BarService),
                Is.EqualTo(1));
            Assert.That(
                plan.AudioAnchors.Count(anchor =>
                    anchor.Kind == BarInteriorAudioKind.Music),
                Is.EqualTo(1));

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
            Assert.That(counter.Bounds.xMin,
                Is.EqualTo(-3f + BarCounterSeatPlanner.StoolPitch).Within(.0001f),
                "The left end must shorten by exactly one stool pitch.");
            Assert.That(counter.Bounds.xMax, Is.EqualTo(3.85f).Within(.0001f),
                "The right end must retain the tap and return placement.");
            Assert.That(
                plan.TryGetFurniture(
                    BarInteriorFurnitureKind.CounterReturn,
                    out BarInteriorFurnitureFootprint counterReturn),
                Is.True);
            Assert.That(
                counterReturn.Bounds,
                Is.EqualTo(new Rect(3.05f, 0.65f, 1.04f, 1.8f)));
            Assert.That(counterReturn.Height, Is.EqualTo(0.86f));
            Assert.That(
                plan.FurnitureFootprints.Count(
                    item => item.Kind == BarInteriorFurnitureKind.PubTable),
                Is.EqualTo(3));
            Assert.That(
                plan.FurnitureFootprints
                    .Where(item =>
                        item.Kind == BarInteriorFurnitureKind.PubTable)
                    .All(item => Mathf.Approximately(item.Height, 0.82f)),
                Is.True);
            Assert.DoesNotThrow(
                () => BarInteriorLayoutValidator.ValidateOrThrow(plan));
        }

        [Test]
        public void DrinkServiceValidator_RejectsLegacyBottleCatalog()
        {
            BarInteriorLayoutPlan layout =
                BarInteriorLayoutPlanner.Generate(
                    20260904,
                    "four-offer-menu-test",
                    BarActivityKind.Cocktail);
            BarDrinkServicePlan valid =
                BarDrinkServicePlan.FromLayout(layout);
            DrinkId[] legacyDrinks =
            {
                DrinkId.Water,
                DrinkId.DarkBeer,
                DrinkId.WhiteWine,
                DrinkId.PepperVodka
            };
            var legacySlots = new BarDrinkBottleSlotPlan[legacyDrinks.Length];
            for (int index = 0; index < legacySlots.Length; index++)
            {
                legacySlots[index] = new BarDrinkBottleSlotPlan(
                    $"legacy-slot-{index}",
                    legacyDrinks[index],
                    valid.BottleSlots[index].Pose);
            }

            var invalid = new BarDrinkServicePlan(
                valid.BarId,
                valid.StableSeed,
                valid.SeatPose,
                valid.CameraPosition,
                valid.CameraLookAt,
                valid.CameraFieldOfView,
                valid.ServiceStoolPosition,
                valid.MenuPose,
                valid.VesselCounterPose,
                valid.VesselHandPose,
                valid.BottleHandPose,
                valid.BottlePourPose,
                legacySlots);

            Assert.Throws<InvalidOperationException>(
                () => invalid.ValidateOrThrow(layout));
        }

        [TestCase(BarActivityKind.Cocktail)]
        [TestCase(BarActivityKind.BeerPong)]
        [TestCase(BarActivityKind.SplitTheG)]
        [TestCase(BarActivityKind.TinctureMatch)]
        public void Generate_KeepsJukeboxAndOnlyBeerPongOwnsActivityFurniture(
            BarActivityKind activity)
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    -8821,
                    "Test jukebox activity",
                    activity);

            Assert.That(plan.Activity, Is.EqualTo(activity));
            Assert.That(
                plan.TryGetFurniture(
                    BarInteriorFurnitureKind.ActivityFixture,
                    out BarInteriorFurnitureFootprint fixture),
                Is.EqualTo(activity == BarActivityKind.BeerPong));
            if (activity == BarActivityKind.BeerPong)
            {
                Assert.That(fixture.Activity, Is.EqualTo(activity));
                Assert.That(fixture.Bounds, Is.EqualTo(new Rect(3.85f, -1.75f, .76f, 1.83f)));
            }
            Assert.That(
                plan.FurnitureFootprints.Count(
                    item =>
                        item.Kind ==
                        BarInteriorFurnitureKind.ActivityFixture),
                Is.EqualTo(activity == BarActivityKind.BeerPong ? 1 : 0));
            BarInteriorFurnitureFootprint jukebox = plan.FurnitureFootprints.Single(
                item => item.Kind == BarInteriorFurnitureKind.Jukebox);
            Assert.That(jukebox.Bounds.xMin, Is.EqualTo(4.69f).Within(.0001f));
            Assert.That(jukebox.Bounds.yMin, Is.EqualTo(-1.14f).Within(.0001f));
            Assert.That(jukebox.Bounds.size, Is.EqualTo(new Vector2(.62f, .98f)));
            Assert.That(jukebox.BlocksMovement, Is.True);
            Assert.That(jukebox.Height, Is.EqualTo(1.75f));
            Assert.That(plan.FurnitureFootprints.Where(item => item.Id != jukebox.Id)
                .All(item => !item.Bounds.Overlaps(jukebox.Bounds)), Is.True);
            BarInteriorAudioAnchor music = plan.AudioAnchors.Single(
                anchor => anchor.Kind == BarInteriorAudioKind.Music);
            Assert.That(Vector3.Distance(music.Position, new Vector3(4.725f, .42f, -.6525f)),
                Is.LessThan(.0001f));
            BarInteriorFurnitureFootprint platform = plan.FurnitureFootprints.Single(
                item => item.Kind == BarInteriorFurnitureKind.Stage);
            Assert.That(platform.Bounds,
                Is.EqualTo(new Rect(3.05f, -3.525f, 2.1f, 1.65f)));
            Assert.That(plan.Zones.Single(zone => zone.Kind == BarInteriorZoneKind.Stage).Bounds,
                Is.EqualTo(platform.Bounds));
            Assert.That(platform.BlocksMovement, Is.False,
                "The low physical stage is a walkable floor, not an aisle obstacle.");
            Assert.That(platform.Height, Is.EqualTo(.12f));
            Assert.That(platform.Height, Is.LessThan(PlayerFactory.StepOffset));
            Assert.That(plan.FurnitureFootprints.Any(item => item.Kind == BarInteriorFurnitureKind.CoatRack),
                Is.False);
            foreach (Vector3 stageDock in new[] { BarInteriorLayoutPlanner.StageEntryPosition,
                BarInteriorLayoutPlanner.StageStandingPosition })
            {
                float radius = BarInteriorLayoutValidator.PlayerRadius;
                Assert.That(stageDock.x, Is.InRange(platform.Bounds.xMin + radius,
                    platform.Bounds.xMax - radius));
                Assert.That(stageDock.z, Is.InRange(platform.Bounds.yMin + radius,
                    platform.Bounds.yMax - radius));
                Assert.That(stageDock.y,
                    Is.EqualTo(platform.Height + PlayerFactory.GroundedRootOffset));
            }
            Assert.That(BarInteriorLayoutPlanner.StageStandingPosition.x -
                BarInteriorLayoutPlanner.StageMicrophonePosition.x,
                Is.GreaterThan(BarInteriorLayoutValidator.PlayerRadius + .03f),
                "The hero must fit behind the microphone without crossing its pole.");
            Assert.That(plan.FurnitureFootprints.Where(item => item.Id != platform.Id)
                .All(item => !item.Bounds.Overlaps(platform.Bounds)), Is.True,
                "The expanded floor must retain the BeerPong and jukebox separation.");
            BarInteriorLightAnchor stageLight = plan.LightAnchors.Single(light => light.Kind == BarInteriorLightKind.StageKey);
            Assert.That(stageLight.Position, Is.EqualTo(new Vector3(4.5f, 2.5f, -2.825f)));
            Assert.That(Vector3.Dot(stageLight.Direction, new Vector3(-.1f, -1f, 0f).normalized), Is.GreaterThan(.999f));
            Assert.That(
                plan.Paths.Any(
                    path =>
                        Contains(
                            path.Bounds,
                            plan.ActivityStationPosition)),
                Is.True);
            Assert.That(
                plan.CounterStationPosition,
                Is.EqualTo(new Vector3(-0.75f, 0.9f, 0.55f)));
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
                Is.EqualTo(4));
            Assert.That(
                plan.NpcAnchors.Count(
                    anchor =>
                        anchor.Role == BarNpcRole.CounterPatron),
                Is.EqualTo(2));
            Assert.That(
                plan.NpcAnchors.Count(
                    anchor =>
                        anchor.Role == BarNpcRole.StandingPatron),
                Is.EqualTo(3));
            Assert.That(
                plan.NpcAnchors.Count(
                    anchor =>
                        anchor.Role == BarNpcRole.Performer ||
                        anchor.Role == BarNpcRole.Walker),
                Is.Zero);
        }

        [Test]
        public void GeneratedFurnitureBoundNpcAnchorsMatchAuthoredPlaces()
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    91275,
                    "bar-furniture-bound-crowd",
                    BarActivityKind.BeerPong);
            var expected = new[]
            {
                (
                    "booth-1-near",
                    BarNpcRole.SeatedPatron,
                    new Vector3(-4.38f, 0f, -2.15f - 0.82f),
                    0f,
                    string.Empty),
                (
                    "booth-1-far",
                    BarNpcRole.SeatedPatron,
                    new Vector3(-4.38f, 0f, -2.15f + 0.82f),
                    180f,
                    string.Empty),
                (
                    "booth-2-near",
                    BarNpcRole.SeatedPatron,
                    new Vector3(-4.38f, 0f, 0.65f - 0.82f),
                    0f,
                    string.Empty),
                (
                    "booth-2-far",
                    BarNpcRole.SeatedPatron,
                    new Vector3(-4.38f, 0f, 0.65f + 0.82f),
                    180f,
                    string.Empty),
                (
                    "counter-seat-left",
                    BarNpcRole.CounterPatron,
                    new Vector3(-1.65f, 0f, 0.91f),
                    0f,
                    string.Empty),
                (
                    "counter-seat-right",
                    BarNpcRole.CounterPatron,
                    new Vector3(1.05f, 0f, 0.91f),
                    0f,
                    string.Empty),
                (
                    "social-left-front",
                    BarNpcRole.StandingPatron,
                    new Vector3(-1.75f, 0f, -3.45f),
                    0f,
                    "pub-table-left-front"),
                (
                    "social-right-front",
                    BarNpcRole.StandingPatron,
                    new Vector3(1.75f, 0f, -3.45f),
                    0f,
                    "pub-table-right-front"),
                (
                    "social-left-rear",
                    BarNpcRole.StandingPatron,
                    new Vector3(-4.85f, 0f, 2.7f),
                    90f,
                    "pub-table-left-rear")
            };
            var actual = plan.NpcAnchors
                .Where(anchor => anchor.Role != BarNpcRole.Bartender)
                .Select(anchor =>
                    (
                        anchor.Id,
                        anchor.Role,
                        anchor.Position,
                        anchor.YawDegrees,
                        anchor.RouteId))
                .ToArray();

            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void GeneratedNpcAnchors_RestOnFloor()
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    91275,
                    "bar-grounded-crowd",
                    BarActivityKind.Cocktail);

            foreach (BarNpcAnchor anchor in plan.NpcAnchors)
            {
                Assert.That(
                    anchor.Position.y,
                    Is.EqualTo(0f).Within(0.001f),
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
                        new Rect(-0.5f, -2f, 1f, 1f),
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
        public void Validator_RejectsFurnitureAcrossFreeStoolEntry()
        {
            BarInteriorLayoutPlan valid = BarInteriorLayoutPlanner.Generate(
                100, "Test bar with a blocked stool", BarActivityKind.Cocktail);
            var furniture = new List<BarInteriorFurnitureFootprint>(valid.FurnitureFootprints)
            {
                new BarInteriorFurnitureFootprint(
                    "Test table beside the counter", BarInteriorFurnitureKind.PubTable,
                    new Rect(-1.95f, -0.35f, 0.6f, 0.6f), 0.82f, true)
            };
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => BarInteriorLayoutValidator.ValidateOrThrow(
                    Copy(valid, furnitureFootprints: furniture)));
            Assert.That(error.Message, Does.Contain("approach/entry capsule"));
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
                                StringComparison.Ordinal) &&
                            !child.name.EndsWith(
                                " Collision",
                                StringComparison.Ordinal))
                    .ToArray();

                Assert.That(stools, Has.Length.EqualTo(4));
                for (int index = 0; index < stools.Length; index++)
                {
                    Vector3 stoolPosition = room.InverseTransformPoint(
                        stools[index].GetComponent<Renderer>().bounds.center);
                    float distance = Vector2.Distance(
                        new Vector2(stoolPosition.x, stoolPosition.z),
                        new Vector2(
                            plan.CounterStationPosition.x,
                            plan.CounterStationPosition.z));
                    Assert.That(
                        distance,
                        Is.GreaterThanOrEqualTo(0.879f),
                        stools[index].name);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CounterSeats_ExposeEveryUnoccupiedStoolWithSafeDocks()
        {
            BarInteriorLayoutPlan plan =
                BarInteriorLayoutPlanner.Generate(
                    105,
                    "bar-free-counter-seats",
                    BarActivityKind.Cocktail);

            IReadOnlyList<BarCounterSeatDescriptor> seats =
                BarCounterSeatPlanner.CreateAvailable(plan);

            Assert.That(seats, Has.Count.EqualTo(3));
            Assert.That(
                seats.Select(item => item.SeatTopLocalPosition.x),
                Is.EquivalentTo(new[] { -0.75f, 0.15f, 1.95f }));
            Assert.That(
                seats.Select(item => item.StableId).Distinct().Count(),
                Is.EqualTo(seats.Count));
            for (int index = 0; index < seats.Count; index++)
            {
                BarCounterSeatDescriptor seat = seats[index];
                Assert.That(
                    seat.SeatTopLocalPosition.z -
                    seat.EntryGroundLocalPosition.z,
                    Is.GreaterThanOrEqualTo(0.63f));
                Assert.That(
                    seat.EntryGroundLocalPosition.z,
                    Is.GreaterThan(
                        seat.ApproachGroundLocalPosition.z));
                Assert.That(
                    seat.ServiceLocalOffset.x,
                    Is.EqualTo(
                        seat.SeatTopLocalPosition.x -
                        plan.CounterStationPosition.x).Within(0.0001f));
                CounterSeatAccessSide expectedSide = Mathf.Approximately(
                    seat.SeatTopLocalPosition.x, .15f)
                    ? CounterSeatAccessSide.Left : CounterSeatAccessSide.Right;
                Assert.That(seat.AccessSide, Is.EqualTo(expectedSide),
                    "The side entry must use the gap away from the occupied neighbour.");
                PlayerAnimatedInteractionDefinition seatClips =
                    CounterSeatInteraction.CreateDefinition(seat.AccessSide);
                Assert.That(seatClips.EnterClipName, Is.EqualTo(expectedSide == CounterSeatAccessSide.Left
                    ? CounterSeatInteraction.EnterLeftClipName : CounterSeatInteraction.EnterRightClipName));
                Assert.That(seatClips.ExitClipName, Is.EqualTo(expectedSide == CounterSeatAccessSide.Left
                    ? CounterSeatInteraction.ExitLeftClipName : CounterSeatInteraction.ExitRightClipName));
                Assert.That(seatClips.LoopClipName, Is.EqualTo(CounterSeatInteraction.BarLoopClipName));
            }

            BarCounterSeatDescriptor rightmost = seats.Single(
                item => Mathf.Approximately(
                    item.SeatTopLocalPosition.x,
                    BarCounterSeatPlanner.RightReturnSafeSeatX));
            Assert.That(
                rightmost.EntryGroundLocalPosition.x,
                Is.EqualTo(rightmost.SeatTopLocalPosition.x)
                    .Within(0.0001f));
            Assert.That(rightmost.MirrorServiceHorizontally, Is.True);
            Assert.That(
                rightmost.SeatTopLocalPosition.z -
                rightmost.EntryGroundLocalPosition.z -
                MountainRoadCafeWorldBuilder.StoolSeatDiameter * 0.5f,
                Is.GreaterThan(0.32f),
                "The entry root must leave the hero capsule clear of " +
                "the solid stool seat.");
            BarInteriorFurnitureFootprint counterReturn =
                plan.FurnitureFootprints.Single(item =>
                    item.Kind == BarInteriorFurnitureKind.CounterReturn);
            Assert.That(
                counterReturn.Bounds.xMin -
                rightmost.EntryGroundLocalPosition.x,
                Is.GreaterThanOrEqualTo(0.50f),
                "The hero capsule must clear the counter-return body and " +
                "its visible top overhang.");
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
