using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySoundscapeIntegrationTests
    {
        [Test]
        public void DefaultCity_AnchorsTenCausalSoundsToPhysicalOwners()
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

            Assert.That(plan.Sources, Has.Count.EqualTo(10));
            Assert.That(plan.LoopingSources, Has.Count.EqualTo(5));
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
                Assert.That(
                    source.Cue,
                    Is.Not.EqualTo(CitySourceSoundId.ParkSwingCreak),
                    "A swing cue needs a real motion binding first.");
                Assert.That(
                    source.PhysicalOwner,
                    Is.Not.EqualTo(
                        CitySoundPhysicalOwnerKind.ParkPlayground));
                owners.Add(source.PhysicalOwner);
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    CitySourceSoundId.DryingYardCarpetStrike,
                    CitySourceSoundId.IndustrialMetalStress
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
                    CitySoundPhysicalOwnerKind.IndustrialWeighbridge,
                    CitySoundPhysicalOwnerKind.NightlifeLastRouteIsland,
                    CitySoundPhysicalOwnerKind.ParkFountainAndStatue
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

        private static BuildingLot CreateBuildingLot(float centerX)
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
                Vector3.zero);
        }
    }
}
