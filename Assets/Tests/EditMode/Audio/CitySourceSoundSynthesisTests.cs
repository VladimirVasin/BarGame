using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySourceSoundSynthesisTests
    {
        private const float MaximumPeak = 0.821f;
        private const float MinimumRms = 0.0035f;
        private const float MaximumRms = 0.28f;
        private const float MinimumDistinctDifference = 0.006f;
        private const float MinimumVariantDifference = 0.003f;

        [Test]
        public void Catalog_IsContiguousSpatialAndMemoryBounded()
        {
            Assert.That(
                CitySourceSoundSynthesis.SampleRate,
                Is.EqualTo(22050));
            Assert.That(
                CitySourceSoundSynthesis.Channels,
                Is.EqualTo(1));
            Assert.That(
                CitySourceSoundSynthesis.Count,
                Is.EqualTo((int)CitySourceSoundId.Count - 1));
            Assert.That(
                CitySourceSoundSynthesis.MaximumSampleCount,
                Is.EqualTo(88200));

            int loopCount = 0;
            int oneShotCount = 0;
            for (int index = 1;
                 index < (int)CitySourceSoundId.Count;
                 index++)
            {
                CitySourceSoundId id = (CitySourceSoundId)index;
                CitySourceSoundDefinition definition =
                    CitySourceSoundSynthesis.GetDefinition(id);

                Assert.That(definition.Id, Is.EqualTo(id));
                Assert.That(
                    definition.Duration,
                    Is.InRange(
                        0.1f,
                        CitySourceSoundSynthesis.MaximumDuration));
                Assert.That(
                    definition.VariantCount,
                    Is.InRange(3, 4));
                Assert.That(definition.Volume, Is.InRange(0.1f, 0.35f));
                Assert.That(definition.MinDistance, Is.GreaterThan(0f));
                Assert.That(
                    definition.MaxDistance,
                    Is.GreaterThan(definition.MinDistance));
                Assert.That(
                    definition.MaxDistance,
                    Is.LessThanOrEqualTo(18f));
                Assert.That(
                    definition.LowPassFrequency,
                    Is.InRange(
                        2000f,
                        CitySourceSoundSynthesis.SampleRate * 0.5f));

                if (definition.IsLoop)
                {
                    loopCount++;
                    Assert.That(
                        definition.Playback,
                        Is.EqualTo(CitySourceSoundPlayback.Loop));
                    Assert.That(
                        definition.Duration,
                        Is.EqualTo(
                            CitySourceSoundSynthesis.LoopDuration));
                }
                else
                {
                    oneShotCount++;
                    Assert.That(
                        definition.Playback,
                        Is.EqualTo(CitySourceSoundPlayback.OneShot));
                    Assert.That(definition.Duration, Is.LessThan(2f));
                }
            }

            Assert.That(loopCount, Is.EqualTo(5));
            Assert.That(oneShotCount, Is.EqualTo(6));
        }

        [Test]
        public void EveryVariant_IsDeterministicFiniteAudibleAndEdgeSafe()
        {
            for (int index = 1;
                 index < (int)CitySourceSoundId.Count;
                 index++)
            {
                CitySourceSoundId id = (CitySourceSoundId)index;
                CitySourceSoundDefinition definition =
                    CitySourceSoundSynthesis.GetDefinition(id);
                for (int variant = 0;
                     variant < definition.VariantCount;
                     variant++)
                {
                    float[] first =
                        CitySourceSoundSynthesis.GenerateSamples(
                            id,
                            variant);
                    float[] second =
                        CitySourceSoundSynthesis.GenerateSamples(
                            id,
                            variant);

                    Assert.That(
                        first.Length,
                        Is.EqualTo(
                            Mathf.RoundToInt(
                                CitySourceSoundSynthesis.SampleRate *
                                definition.Duration)),
                        id + " variant " + variant);
                    Assert.That(
                        first.Length,
                        Is.LessThanOrEqualTo(
                            CitySourceSoundSynthesis.MaximumSampleCount));
                    Assert.That(
                        first.Length * sizeof(float),
                        Is.LessThanOrEqualTo(
                            CitySourceSoundSynthesis.MaximumSampleCount *
                            sizeof(float)));
                    CollectionAssert.AreEqual(first, second);
                    AssertSignal(first, id, variant);

                    if (definition.IsLoop)
                    {
                        Assert.That(
                            first[first.Length - 1],
                            Is.EqualTo(first[0]),
                            id + " loop seam");
                    }
                    else
                    {
                        Assert.That(first[0], Is.Zero, id + " cue start");
                        Assert.That(
                            first[first.Length - 1],
                            Is.Zero,
                            id + " cue end");
                    }
                }
            }
        }

        [Test]
        public void Variants_ChangeTimbreWithoutChangingClipContract()
        {
            for (int index = 1;
                 index < (int)CitySourceSoundId.Count;
                 index++)
            {
                CitySourceSoundId id = (CitySourceSoundId)index;
                CitySourceSoundDefinition definition =
                    CitySourceSoundSynthesis.GetDefinition(id);
                float[] first =
                    CitySourceSoundSynthesis.GenerateSamples(id, 0);
                float[] second =
                    CitySourceSoundSynthesis.GenerateSamples(id, 1);

                Assert.That(second.Length, Is.EqualTo(first.Length));
                Assert.That(
                    MeanAbsoluteDifference(first, second),
                    Is.GreaterThan(MinimumVariantDifference),
                    id + " variants are effectively identical");
            }
        }

        [Test]
        public void SourceFamilies_HaveAudiblyDistinctSignals()
        {
            AssertPairwiseDistinct(
                new[]
                {
                    CitySourceSoundId.WaterworksPipeLoop,
                    CitySourceSoundId.DryingYardClothLoop,
                    CitySourceSoundId
                        .IndustrialWeighbridgeMechanismLoop,
                    CitySourceSoundId.LastRouteRelayLoop,
                    CitySourceSoundId.ParkFountainLoop
                });
            AssertPairwiseDistinct(
                new[]
                {
                    CitySourceSoundId.WaterworksDrip,
                    CitySourceSoundId.DryingYardRopeCreak,
                    CitySourceSoundId.DryingYardCarpetStrike,
                    CitySourceSoundId.IndustrialMetalStress,
                    CitySourceSoundId.LastRouteIncompleteChime,
                    CitySourceSoundId.ParkSwingCreak
                });
        }

        [Test]
        public void InvalidIdsAndVariants_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CitySourceSoundSynthesis.GetDefinition(
                    CitySourceSoundId.None));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CitySourceSoundSynthesis.GetDefinition(
                    CitySourceSoundId.Count));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CitySourceSoundSynthesis.GenerateSamples(
                    CitySourceSoundId.WaterworksDrip,
                    -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CitySourceSoundSynthesis.GenerateSamples(
                    CitySourceSoundId.WaterworksDrip,
                    4));
        }

        private static void AssertSignal(
            IReadOnlyList<float> samples,
            CitySourceSoundId id,
            int variant)
        {
            float peak = 0f;
            double sumSquares = 0d;
            for (int index = 0; index < samples.Count; index++)
            {
                float sample = samples[index];
                Assert.That(float.IsNaN(sample), Is.False);
                Assert.That(float.IsInfinity(sample), Is.False);
                peak = Mathf.Max(peak, Mathf.Abs(sample));
                sumSquares += sample * sample;
            }

            float rms = Mathf.Sqrt(
                (float)(sumSquares / samples.Count));
            Assert.That(
                peak,
                Is.InRange(0.025f, MaximumPeak),
                id + " variant " + variant + " peak");
            Assert.That(
                rms,
                Is.InRange(MinimumRms, MaximumRms),
                id + " variant " + variant + " RMS");
        }

        private static void AssertPairwiseDistinct(
            IReadOnlyList<CitySourceSoundId> ids)
        {
            var signals = new float[ids.Count][];
            for (int index = 0; index < ids.Count; index++)
            {
                signals[index] =
                    CitySourceSoundSynthesis.GenerateSamples(ids[index]);
            }

            for (int first = 0; first < ids.Count; first++)
            {
                for (int second = first + 1;
                     second < ids.Count;
                     second++)
                {
                    Assert.That(
                        NormalizedMeanAbsoluteDifference(
                            signals[first],
                            signals[second]),
                        Is.GreaterThan(MinimumDistinctDifference),
                        ids[first] + " and " + ids[second] +
                        " are effectively identical");
                }
            }
        }

        private static float MeanAbsoluteDifference(
            IReadOnlyList<float> first,
            IReadOnlyList<float> second)
        {
            Assert.That(second.Count, Is.EqualTo(first.Count));
            double difference = 0d;
            for (int index = 0; index < first.Count; index++)
            {
                difference += Mathf.Abs(first[index] - second[index]);
            }

            return (float)(difference / first.Count);
        }

        private static float NormalizedMeanAbsoluteDifference(
            IReadOnlyList<float> first,
            IReadOnlyList<float> second)
        {
            const int pointCount = 4096;
            double difference = 0d;
            for (int point = 0; point < pointCount; point++)
            {
                float normalized = point / (float)(pointCount - 1);
                int firstIndex = Mathf.RoundToInt(
                    normalized * (first.Count - 1));
                int secondIndex = Mathf.RoundToInt(
                    normalized * (second.Count - 1));
                difference += Mathf.Abs(
                    first[firstIndex] - second[secondIndex]);
            }

            return (float)(difference / pointCount);
        }
    }
}
