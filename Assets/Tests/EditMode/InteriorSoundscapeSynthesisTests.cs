using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InteriorSoundscapeSynthesisTests
    {
        private const float MaximumPeak = 0.86f;
        private const float MaximumRms = 0.24f;
        private const float MinimumRms = 0.001f;
        private const float MaximumLoopSeam = 0.025f;
        private const float MaximumCueEdge = 0.025f;

        [Test]
        public void LoopBeds_AreFiniteQuietNonSilentAndLoopSafe()
        {
            float[] stairwellVentilation =
                StairwellSoundscapeSynthesis
                    .GenerateVentilationLoopSamples();
            float[] stairwellElectrical =
                StairwellSoundscapeSynthesis
                    .GenerateElectricalBuzzLoopSamples();
            float[] homeClosedRefrigerator =
                HomeSoundscapeSynthesis
                    .GenerateClosedRefrigeratorLoopSamples();
            float[] homeOpenRefrigerator =
                HomeSoundscapeSynthesis
                    .GenerateOpenRefrigeratorLoopSamples();
            float[] homeNightAir =
                HomeSoundscapeSynthesis
                    .GenerateBalconyNightAirLoopSamples();

            AssertLoop(
                stairwellVentilation,
                StairwellSoundscapeSynthesis.SampleRate,
                StairwellSoundscapeSynthesis.LoopDuration);
            AssertLoop(
                stairwellElectrical,
                StairwellSoundscapeSynthesis.SampleRate,
                StairwellSoundscapeSynthesis.LoopDuration);
            AssertLoop(
                homeClosedRefrigerator,
                HomeSoundscapeSynthesis.SampleRate,
                HomeSoundscapeSynthesis.LoopDuration);
            AssertLoop(
                homeOpenRefrigerator,
                HomeSoundscapeSynthesis.SampleRate,
                HomeSoundscapeSynthesis.LoopDuration);
            AssertLoop(
                homeNightAir,
                HomeSoundscapeSynthesis.SampleRate,
                HomeSoundscapeSynthesis.LoopDuration);
        }

        [Test]
        public void RareCues_AreFiniteQuietNonSilentAndEdgeSafe()
        {
            foreach (
                StairwellSoundscapeCueKind kind in
                Enum.GetValues(
                    typeof(StairwellSoundscapeCueKind)))
            {
                AssertCue(
                    StairwellSoundscapeSynthesis
                        .GenerateCueSamples(kind));
            }

            foreach (
                HomeSoundscapeCueKind kind in
                Enum.GetValues(typeof(HomeSoundscapeCueKind)))
            {
                AssertCue(
                    HomeSoundscapeSynthesis
                        .GenerateCueSamples(kind));
            }
        }

        [Test]
        public void GeneratedSignals_AreRepeatableAndSceneDistinct()
        {
            float[] stairwellVentilation =
                StairwellSoundscapeSynthesis
                    .GenerateVentilationLoopSamples();
            float[] stairwellElectrical =
                StairwellSoundscapeSynthesis
                    .GenerateElectricalBuzzLoopSamples();
            float[] homeClosedRefrigerator =
                HomeSoundscapeSynthesis
                    .GenerateClosedRefrigeratorLoopSamples();
            float[] homeOpenRefrigerator =
                HomeSoundscapeSynthesis
                    .GenerateOpenRefrigeratorLoopSamples();
            float[] homeNightAir =
                HomeSoundscapeSynthesis
                    .GenerateBalconyNightAirLoopSamples();

            CollectionAssert.AreEqual(
                stairwellVentilation,
                StairwellSoundscapeSynthesis
                    .GenerateVentilationLoopSamples());
            CollectionAssert.AreEqual(
                homeClosedRefrigerator,
                HomeSoundscapeSynthesis
                    .GenerateClosedRefrigeratorLoopSamples());
            CollectionAssert.AreEqual(
                homeOpenRefrigerator,
                HomeSoundscapeSynthesis
                    .GenerateOpenRefrigeratorLoopSamples());

            var signals = new[]
            {
                stairwellVentilation,
                stairwellElectrical,
                homeClosedRefrigerator,
                homeOpenRefrigerator,
                homeNightAir
            };
            for (int first = 0;
                 first < signals.Length;
                 first++)
            {
                for (int second = first + 1;
                     second < signals.Length;
                     second++)
                {
                    Assert.That(
                        MeanAbsoluteDifference(
                            signals[first],
                            signals[second]),
                        Is.GreaterThan(0.012f));
                    Assert.That(
                        FingerprintDistance(
                            signals[first],
                            signals[second]),
                        Is.GreaterThan(0.018f));
                }
            }
        }

        private static void AssertLoop(
            IReadOnlyList<float> samples,
            int sampleRate,
            float duration)
        {
            Assert.That(
                samples.Count,
                Is.EqualTo(
                    Mathf.RoundToInt(sampleRate * duration)));
            AssertSignal(samples);
            Assert.That(
                Mathf.Abs(samples[0] - samples[samples.Count - 1]),
                Is.LessThanOrEqualTo(MaximumLoopSeam));
        }

        private static void AssertCue(
            IReadOnlyList<float> samples)
        {
            Assert.That(samples.Count, Is.GreaterThan(0));
            AssertSignal(samples);
            Assert.That(
                Mathf.Abs(samples[0]),
                Is.LessThanOrEqualTo(MaximumCueEdge));
            Assert.That(
                Mathf.Abs(samples[samples.Count - 1]),
                Is.LessThanOrEqualTo(MaximumCueEdge));
        }

        private static void AssertSignal(
            IReadOnlyList<float> samples)
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
            Assert.That(peak, Is.LessThanOrEqualTo(MaximumPeak));
            Assert.That(rms, Is.InRange(MinimumRms, MaximumRms));
        }

        private static float MeanAbsoluteDifference(
            IReadOnlyList<float> first,
            IReadOnlyList<float> second)
        {
            Assert.That(second.Count, Is.EqualTo(first.Count));
            double total = 0d;
            for (int index = 0; index < first.Count; index++)
            {
                total += Mathf.Abs(first[index] - second[index]);
            }

            return (float)(total / first.Count);
        }

        private static float FingerprintDistance(
            IReadOnlyList<float> first,
            IReadOnlyList<float> second)
        {
            int[] loopCycles =
            {
                2,
                5,
                31,
                47,
                79,
                81,
                113,
                181,
                237,
                376,
                400,
                641,
                752,
                800
            };
            double sumSquares = 0d;
            for (int index = 0;
                 index < loopCycles.Length;
                 index++)
            {
                float difference =
                    SpectralMagnitude(
                        first,
                        loopCycles[index]) -
                    SpectralMagnitude(
                        second,
                        loopCycles[index]);
                sumSquares += difference * difference;
            }

            return Mathf.Sqrt((float)sumSquares);
        }

        private static float SpectralMagnitude(
            IReadOnlyList<float> samples,
            int loopCycles)
        {
            double angle =
                Math.PI * 2d * loopCycles / samples.Count;
            double cosineStep = Math.Cos(angle);
            double sineStep = Math.Sin(angle);
            double cosine = 1d;
            double sine = 0d;
            double real = 0d;
            double imaginary = 0d;

            for (int index = 0; index < samples.Count; index++)
            {
                float sample = samples[index];
                real += sample * cosine;
                imaginary -= sample * sine;
                double nextCosine =
                    cosine * cosineStep -
                    sine * sineStep;
                sine =
                    sine * cosineStep +
                    cosine * sineStep;
                cosine = nextCosine;
            }

            return
                (float)(
                    Math.Sqrt(
                        real * real +
                        imaginary * imaginary) *
                    2d /
                    samples.Count);
        }
    }
}
