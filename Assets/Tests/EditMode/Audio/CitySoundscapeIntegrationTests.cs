using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySoundscapeIntegrationTests
    {
        [Test]
        public void DefaultCity_AnchorsCausalSoundsAndLeavesCanneryTimingToItsController()
        {
            CityLayout layout = CreateDefaultLayout();
            CityDecorationPlan decorationPlan =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    RoadFencePlanner.CreatePlan(layout),
                    CityNightFixturePlanner.CreatePlan(layout));

            CitySoundscapePlan plan =
                CitySoundscapeAnchorPlanner.Create(
                    layout,
                    decorationPlan);

            Assert.That(plan.Sources, Has.Count.EqualTo(9));
            Assert.That(plan.LoopingSources, Has.Count.EqualTo(4));
            Assert.That(plan.ScheduledSources, Has.Count.EqualTo(3));
            Assert.That(plan.TriggeredSources, Has.Count.EqualTo(2));

            var owners = new HashSet<CitySoundPhysicalOwnerKind>();
            for (int index = 0; index < plan.Sources.Count; index++)
            {
                CitySoundSourceDescriptor source = plan.Sources[index];
                Assert.That(
                    source.PhysicalOwnerBounds.Contains(
                        source.WorldPosition),
                    Is.True,
                    source.StableId +
                    " must sit inside the visible fixture that emits it.");
                owners.Add(source.PhysicalOwner);
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    CitySourceSoundId.DryingYardCarpetStrike,
                    CitySourceSoundId.IndustrialMetalStress,
                    CitySourceSoundId.ParkSwingCreak
                },
                GetCues(plan.TriggeredSources));
            for (int index = 0;
                 index < plan.TriggeredSources.Count;
                 index++)
            {
                CitySoundSourceDescriptor triggered =
                    plan.TriggeredSources[index];
                Assert.That(triggered.ScheduleInterval.IsNone, Is.True);
                CollectionAssert.DoesNotContain(
                    plan.ScheduledSources,
                    triggered,
                    triggered.StableId +
                    " is event-driven and must not enter the scheduler.");
                Assert.Throws<System.ArgumentException>(() =>
                    CitySoundSchedulePlanner.Start(
                        plan,
                        triggered.StableId,
                        0d));
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                    CitySoundPhysicalOwnerKind.ResidentialDryingYard,
                    CitySoundPhysicalOwnerKind.NightlifeLastRouteIsland,
                    CitySoundPhysicalOwnerKind.ParkFountainAndStatue,
                    CitySoundPhysicalOwnerKind.ParkPlayground
                },
                owners);
        }

        [Test]
        public void Occlusion_UsesZeroOneAndMultipleBuildingMassTiers()
        {
            var source = new Vector3(-10f, 1f, 0f);
            var listener = new Vector3(10f, 1f, 0f);

            CitySoundOcclusionSample clear = CitySoundOcclusion.Evaluate(
                source,
                listener,
                new List<BuildingLot>());
            Assert.That(clear.BlockerCount, Is.Zero);
            Assert.That(clear.VolumeMultiplier, Is.EqualTo(1f));
            Assert.That(
                clear.MaximumCutoffFrequency,
                Is.EqualTo(float.MaxValue));

            BuildingLot first = CreateBuildingLot(-3f);
            CitySoundOcclusionSample one = CitySoundOcclusion.Evaluate(
                source,
                listener,
                new[] { first });
            Assert.That(one.BlockerCount, Is.EqualTo(1));
            Assert.That(
                one.VolumeMultiplier,
                Is.EqualTo(CitySoundOcclusion.OneBlockerVolume));
            Assert.That(
                one.MaximumCutoffFrequency,
                Is.EqualTo(CitySoundOcclusion.OneBlockerCutoff));

            BuildingLot second = CreateBuildingLot(3f);
            CitySoundOcclusionSample multiple =
                CitySoundOcclusion.Evaluate(
                    source,
                    listener,
                    new[] { first, second });
            Assert.That(multiple.BlockerCount, Is.EqualTo(2));
            Assert.That(
                multiple.VolumeMultiplier,
                Is.EqualTo(CitySoundOcclusion.MultipleBlockerVolume));
            Assert.That(
                multiple.MaximumCutoffFrequency,
                Is.EqualTo(CitySoundOcclusion.MultipleBlockerCutoff));
            AssertSameOcclusion(CitySoundOcclusion.Capture(Array.Empty<BuildingLot>()).Evaluate(source, listener), clear);
            AssertSameOcclusion(CitySoundOcclusion.Capture(new[] { first }).Evaluate(source, listener), one);
            AssertSameOcclusion(CitySoundOcclusion.Capture(new[] { first, second }).Evaluate(source, listener), multiple);
        }

        [TestCase(0, 0f)]
        [TestCase(0, 27f)]
        [TestCase(3, 27f)]
        public void OcclusionSnapshot_MatchesLiveGeometryAcrossMovingEndpoints(int variant, float yaw)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            var lots = new[] { CreateBuildingLot(-3f, variant, forward), null, CreateBuildingLot(3f, variant, forward) };
            CitySoundOcclusion.Geometry geometry = CitySoundOcclusion.Capture(lots);
            Assert.That(geometry.BuildingCount, Is.EqualTo(2));
            var points = new[] { new Vector3(-30f, 1f, 0f), new Vector3(30f, 1f, 0f),
                new Vector3(0f, 1f, -30f), new Vector3(0f, 1f, 30f), Vector3.zero, new Vector3(3f, 1f, 0f) };
            foreach (Vector3 source in points)
            foreach (Vector3 listener in points)
            {
                CitySoundOcclusionSample expected = CitySoundOcclusion.Evaluate(source, listener, lots);
                AssertSameOcclusion(geometry.Evaluate(source, listener), expected);
                AssertSameOcclusion(geometry.Evaluate(source + Vector3.up * 20f, listener + Vector3.up * 100f), expected);
            }
        }

        [Test]
        public void OcclusionSnapshot_OwnsFiniteGeometryAndDoesNotAllocateWhileEndpointsMove()
        {
            var lots = new List<BuildingLot> { CreateBuildingLot(-3f, 3, new Vector3(1f, 0f, 1f)), CreateBuildingLot(3f) };
            Vector3 source = new Vector3(-30f, 1f, 0f), listener = new Vector3(30f, 1f, 0f);
            CitySoundOcclusionSample expected = CitySoundOcclusion.Evaluate(source, listener, lots);
            CitySoundOcclusion.Geometry geometry = CitySoundOcclusion.Capture(lots);
            lots.Clear();
            Assert.That(geometry.BuildingCount, Is.EqualTo(2));
            Assert.That(geometry.PolygonCount, Is.GreaterThan(0));
            Assert.That(geometry.VertexCount, Is.GreaterThanOrEqualTo(geometry.PolygonCount * 3));
            AssertSameOcclusion(geometry.Evaluate(source, listener), expected);
            for (int i = 0; i < 16; i++) geometry.Evaluate(source + Vector3.forward * i, listener);

            // Unity Mono may report zero from thread allocation totals. Prove
            // this current-thread profiler observes a known allocation first.
            using var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame |
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            Assert.That(recorder.Valid, Is.True);
            recorder.Start();
            GC.KeepAlive(new byte[16]);
            recorder.Stop();
            if (recorder.Count > 0) recorder.GetSample(0);
            recorder.Reset();
            recorder.Start();
            var control = new byte[16384];
            recorder.Stop();
            ProfilerRecorderSample positive = recorder.Count > 0 ? recorder.GetSample(0) : default;
            GC.KeepAlive(control);
            Assert.That(positive.Count, Is.EqualTo(1));
            recorder.Reset(); recorder.Start();
            int blockers = 0;
            for (int i = 0; i < 256; i++)
                blockers += geometry.Evaluate(source + Vector3.forward * (i % 16), listener - Vector3.forward * (i % 8)).BlockerCount;
            recorder.Stop();
            ProfilerRecorderSample measured = recorder.Count > 0 ? recorder.GetSample(0) : default;
            Assert.That(measured.Count, Is.Zero, "A retained building snapshot must allocate nothing for moving sound/listener endpoints.");
            GC.KeepAlive(blockers);
        }

        private static void AssertSameOcclusion(CitySoundOcclusionSample actual, CitySoundOcclusionSample expected)
        {
            Assert.That(actual.BlockerCount, Is.EqualTo(expected.BlockerCount));
            Assert.That(actual.VolumeMultiplier, Is.EqualTo(expected.VolumeMultiplier));
            Assert.That(actual.MaximumCutoffFrequency, Is.EqualTo(expected.MaximumCutoffFrequency));
        }

        [Test]
        public void Director_OwnsOneBoundedFullySpatialVoicePool()
        {
            CityLayout layout = CreateDefaultLayout();
            CityDecorationPlan decorationPlan =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    RoadFencePlanner.CreatePlan(layout),
                    CityNightFixturePlanner.CreatePlan(layout));
            CitySoundscapePlan plan =
                CitySoundscapeAnchorPlanner.Create(
                    layout,
                    decorationPlan);
            var root = new GameObject("City soundscape test root");
            var listener = new GameObject("City soundscape test listener");

            try
            {
                CitySoundscapeDirector director =
                    CitySoundscapeDirector.Create(
                        root.transform,
                        plan,
                        listener.transform,
                        layout,
                        Array.Empty<DryingYardBabushkaPresentation>(),
                        null,
                        Array.Empty<CityPlaygroundSwing>(),
                        () => 0f);

                Assert.That(director.IsInitialized, Is.True);
                Assert.That(
                    CitySoundscapeDirector.OwnedSourceCount,
                    Is.EqualTo(9));
                IReadOnlyList<AudioSource> loops =
                    director.GetLoopSources();
                IReadOnlyList<AudioSource> details =
                    director.GetDetailSources();
                Assert.That(loops.Count, Is.EqualTo(5));
                Assert.That(details.Count, Is.EqualTo(4));

                for (int index = 0; index < loops.Count; index++)
                {
                    AudioSource source = loops[index];
                    CitySoundSourceDescriptor descriptor =
                        director.GetLoopDescriptor(index);
                    Assert.That(source.spatialBlend, Is.EqualTo(1f));
                    Assert.That(source.loop, Is.True);
                    Assert.That(source.playOnAwake, Is.False);
                    Assert.That(source.clip, Is.Not.Null);
                    Assert.That(
                        source.transform.position,
                        Is.EqualTo(descriptor.WorldPosition));
                    Assert.That(
                        source.maxDistance,
                        Is.EqualTo(descriptor.AudibleRadius));
                }

                for (int index = 0; index < details.Count; index++)
                {
                    Assert.That(
                        details[index].spatialBlend,
                        Is.EqualTo(1f));
                    Assert.That(details[index].loop, Is.False);
                    Assert.That(details[index].playOnAwake, Is.False);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(listener);
            }
        }

        [Test]
        public void Director_RebasesSchedulesWhenAbsoluteTimeMovesBackward()
        {
            GameSessionState.BeginNewGame();
            CityLayout layout = CreateDefaultLayout();
            CityDecorationPlan decorationPlan =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    RoadFencePlanner.CreatePlan(layout),
                    CityNightFixturePlanner.CreatePlan(layout));
            CitySoundscapePlan plan =
                CitySoundscapeAnchorPlanner.Create(
                    layout,
                    decorationPlan);
            var root = new GameObject("City sound rewind test root");
            var listener = new GameObject("City sound rewind listener");
            listener.transform.position =
                plan.ScheduledSources[0].WorldPosition;

            try
            {
                CitySoundscapeDirector director =
                    CitySoundscapeDirector.Create(
                        root.transform,
                        plan,
                        listener.transform,
                        layout,
                        Array.Empty<DryingYardBabushkaPresentation>(),
                        null,
                        Array.Empty<CityPlaygroundSwing>(),
                        () => 1f);
                var wind = new WindSample(0f, 1f);

                director.Advance(0f, 10000d, true, wind, 1f);
                int eventsBeforeRewind = director.PlayedEventCount;
                Assert.That(eventsBeforeRewind, Is.GreaterThan(0));

                director.Advance(0f, 1000d, true, wind, 1f);
                director.Advance(0f, 1070d, true, wind, 1f);

                Assert.That(
                    director.PlayedEventCount,
                    Is.GreaterThan(eventsBeforeRewind));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(listener);
                GameSessionState.BeginNewGame();
            }
        }

        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        private static CitySourceSoundId[] GetCues(
            IReadOnlyList<CitySoundSourceDescriptor> sources)
        {
            var cues = new CitySourceSoundId[sources.Count];
            for (int index = 0; index < sources.Count; index++)
            {
                cues[index] = sources[index].Cue;
            }

            return cues;
        }

        private static BuildingLot CreateBuildingLot(float centerX, int variant = 0, Vector3? facadeForward = null)
        {
            return new BuildingLot(
                Vector2Int.zero,
                new Vector3(centerX, 0f, 0f),
                new Vector2(2f, 2f),
                10f,
                Color.gray,
                "sound-occlusion-test",
                CityDistrictKind.OldTown,
                CityLandUseKind.Building,
                false,
                false,
                false,
                string.Empty,
                BarActivityKind.None,
                Vector2Int.right,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                buildingVariant: variant,
                facadeForward: facadeForward);
        }
    }
}
