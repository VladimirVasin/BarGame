using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class AlpineVillageSoundscapeTests
    {
        [Test]
        [Category("AlpineVillage")]
        public void DefaultPlan_BuildsFiveDeterministicCausalSpatialVoices()
        {
            AlpineVillagePlan village = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
            AlpineVillageSoundscapePlan first =
                AlpineVillageSoundscapePlanner.Create(village);
            AlpineVillageSoundscapePlan second =
                AlpineVillageSoundscapePlanner.Create(
                    AlpineVillagePlanner.Create(
                        GameSessionState.DefaultCitySeed));

            Assert.That(first.Anchors, Has.Count.EqualTo(5));
            Assert.That(first.LoopingAnchors, Has.Count.EqualTo(4));
            // The dog behind the fence is the only scheduled one-shot left:
            // the firewood settling in the mine cart went out of the village
            // with the adit.
            Assert.That(first.ScheduledAnchors, Has.Count.EqualTo(1));
            Assert.That(
                AlpineVillageSoundSynthesis.Count,
                Is.EqualTo(first.Anchors.Count));

            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            var ownerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < first.Anchors.Count; index++)
            {
                AlpineVillageSoundAnchorDescriptor anchor =
                    first.Anchors[index];
                AlpineVillageSoundAnchorDescriptor repeated =
                    second.GetRequiredAnchor(anchor.Kind);
                AlpineVillageSoundDefinition definition =
                    AlpineVillageSoundSynthesis.GetDefinition(anchor.Kind);

                Assert.That(stableIds.Add(anchor.StableId), Is.True);
                Assert.That(
                    string.IsNullOrWhiteSpace(
                        anchor.PhysicalOwnerStableId),
                    Is.False,
                    anchor.StableId + " has no visible causal owner.");
                ownerIds.Add(anchor.PhysicalOwnerStableId);
                Assert.That(
                    Vector3.Distance(
                        anchor.WorldPosition,
                        repeated.WorldPosition),
                    Is.LessThan(0.0001f),
                    anchor.StableId + " moved between identical plans.");
                Assert.That(
                    Vector3.Distance(
                        anchor.OwnerPosition,
                        repeated.OwnerPosition),
                    Is.LessThan(0.0001f));
                Assert.That(
                    village.TerrainBounds.Contains(
                        new Vector2(
                            anchor.WorldPosition.x,
                            anchor.WorldPosition.z)),
                    Is.True,
                    anchor.StableId + " is outside the village world.");
                Assert.That(
                    definition.IsLoop,
                    Is.EqualTo(anchor.IsLooping));
                Assert.That(
                    definition.ScheduleInterval.IsScheduled,
                    Is.EqualTo(anchor.IsScheduled));

                float[] samples =
                    AlpineVillageSoundSynthesis.GenerateSamples(
                        anchor.Kind,
                        0);
                float[] repeatedSamples =
                    AlpineVillageSoundSynthesis.GenerateSamples(
                        anchor.Kind,
                        0);
                Assert.That(samples.Length, Is.EqualTo(repeatedSamples.Length));
                float peak = 0f;
                for (int sample = 0; sample < samples.Length; sample++)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(samples[sample]));
                }

                for (int probe = 0; probe <= 24; probe++)
                {
                    int sample = Mathf.RoundToInt(
                        (samples.Length - 1) * (probe / 24f));
                    Assert.That(
                        repeatedSamples[sample],
                        Is.EqualTo(samples[sample]),
                        $"{anchor.Kind} is not deterministic at {sample}.");
                }

                Assert.That(peak, Is.GreaterThan(0.01f));
                Assert.That(
                    peak,
                    Is.LessThanOrEqualTo(
                        AlpineVillageSoundSynthesis.MaximumAmplitude +
                        0.0001f));
                if (definition.IsLoop)
                {
                    Assert.That(
                        samples[samples.Length - 1],
                        Is.EqualTo(samples[0]));
                }
                else
                {
                    Assert.That(samples[0], Is.Zero);
                    Assert.That(samples[samples.Length - 1], Is.Zero);
                }
            }

            Assert.That(
                ownerIds.Contains(
                    AlpineVillageSoundscapePlanner
                        .StationMechanismOwnerStableId),
                Is.True);
            Assert.That(
                ownerIds.Contains(
                    AlpineVillageSoundscapePlanner.CableGateOwnerStableId),
                Is.True);
            Assert.That(
                ownerIds.Contains(
                    AlpineVillageSoundscapePlanner.GarlandOwnerStableId),
                Is.True);
            Assert.That(
                ownerIds.Contains(
                    AlpineVillageSoundscapePlanner.SourceBowlOwnerStableId),
                Is.True);

            AlpineVillageSoundAnchorDescriptor dog = first.GetRequiredAnchor(
                AlpineVillageSoundKind.DogBehindFence);
            float dogDepth = Vector2.Distance(
                new Vector2(dog.WorldPosition.x, dog.WorldPosition.z),
                new Vector2(dog.OwnerPosition.x, dog.OwnerPosition.z));
            Assert.That(
                dogDepth,
                Is.EqualTo(
                    AlpineVillageSoundscapePlanner.DogDepthBehindGate)
                    .Within(0.001f),
                "The dog must sound from behind the visible cable gate.");

            AlpineVillageSoundScheduleCursor firstDog =
                AlpineVillageSoundSchedulePlanner.Start(
                    first,
                    AlpineVillageSoundscapePlanner.DogAnchorId,
                    0d);
            AlpineVillageSoundScheduleCursor repeatedDog =
                AlpineVillageSoundSchedulePlanner.Start(
                    second,
                    AlpineVillageSoundscapePlanner.DogAnchorId,
                    0d);
            CitySoundScheduleInterval dogInterval =
                AlpineVillageSoundSynthesis.GetDefinition(
                    AlpineVillageSoundKind.DogBehindFence)
                    .ScheduleInterval;
            Assert.That(
                firstDog.NextEventTimeSeconds,
                Is.EqualTo(repeatedDog.NextEventTimeSeconds));
            Assert.That(
                firstDog.NextEventTimeSeconds,
                Is.InRange(
                    (double)dogInterval.MinimumSeconds,
                    (double)dogInterval.MaximumSeconds));
            AlpineVillageSoundScheduleCursor nextDog =
                AlpineVillageSoundSchedulePlanner.AdvanceAfterFiring(
                    first,
                    firstDog,
                    firstDog.NextEventTimeSeconds + 1000d);
            Assert.That(nextDog.EventOrdinal, Is.EqualTo(1u));
            Assert.That(
                nextDog.NextEventTimeSeconds,
                Is.GreaterThan(firstDog.NextEventTimeSeconds + 1000d),
                "A hitch must not create catch-up bark debt.");

            var root = new GameObject("Village soundscape test root");
            try
            {
                var semanticOwners =
                    new Dictionary<string, Transform>(StringComparer.Ordinal);
                for (int index = 0; index < first.Anchors.Count; index++)
                {
                    AlpineVillageSoundAnchorDescriptor anchor =
                        first.Anchors[index];
                    var owner = new GameObject(
                        "Visible owner - " +
                        anchor.PhysicalOwnerStableId);
                    owner.transform.SetParent(root.transform, false);
                    owner.transform.position = anchor.OwnerPosition;
                    semanticOwners.Add(
                        anchor.PhysicalOwnerStableId,
                        owner.transform);
                }

                AlpineVillageSoundscape soundscape =
                    AlpineVillageSoundscape.Create(
                        root.transform,
                        first,
                        semanticOwners);
                Assert.That(soundscape.IsInitialized, Is.True);
                Assert.That(soundscape.WarmthGrade, Is.Zero);
                Assert.That(
                    soundscape.Sources,
                    Has.Count.EqualTo(first.Anchors.Count));

                int expectedRuntimeClips = 0;
                var initialVolumes = new float[soundscape.Sources.Count];
                for (int index = 0; index < soundscape.Sources.Count; index++)
                {
                    AudioSource source = soundscape.Sources[index];
                    AlpineVillageSoundAnchorDescriptor anchor =
                        first.Anchors[index];
                    AlpineVillageSoundDefinition definition =
                        AlpineVillageSoundSynthesis.GetDefinition(anchor.Kind);
                    expectedRuntimeClips += definition.IsLoop
                        ? 1
                        : definition.VariantCount;
                    initialVolumes[index] = source.volume;

                    Assert.That(source.playOnAwake, Is.False);
                    Assert.That(source.spatialBlend, Is.EqualTo(1f));
                    Assert.That(source.loop, Is.EqualTo(definition.IsLoop));
                    Assert.That(source.clip, Is.Not.Null);
                    Assert.That(
                        Vector3.Distance(
                            source.transform.position,
                            anchor.WorldPosition),
                        Is.LessThan(0.0001f));
                    Assert.That(
                        source.minDistance,
                        Is.EqualTo(definition.MinimumDistance));
                    Assert.That(
                        source.maxDistance,
                        Is.EqualTo(definition.AudibleRadius));
                    Assert.That(
                        source.volume,
                        Is.EqualTo(definition.Volume).Within(0.0001f));
                }

                Assert.That(
                    soundscape.RuntimeClips,
                    Has.Count.EqualTo(expectedRuntimeClips));

                soundscape.SetWarmthGrade(1f);
                Assert.That(soundscape.WarmthGrade, Is.EqualTo(1f));
                for (int index = 0; index < soundscape.Sources.Count; index++)
                {
                    Assert.That(
                        soundscape.Sources[index].volume,
                        Is.LessThan(initialVolumes[index]),
                        first.Anchors[index].StableId +
                        " ignored the shared dimming grade.");
                }

                // The dog is the village's only scheduled one-shot now: the
                // firewood settling in the mine cart went out with the adit.
                float throughFirstEvent =
                    (float)firstDog.NextEventTimeSeconds + 0.1f;
                soundscape.Advance(throughFirstEvent);
                Assert.That(soundscape.PlayedEventCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
