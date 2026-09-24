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

            float[] hearth = MothersHouseInteriorSoundSynthesis.GenerateHearthSamples(
                GameSessionState.DefaultCitySeed);
            float[] stove = MothersHouseInteriorSoundSynthesis.GenerateHearthSamples(
                GameSessionState.DefaultCitySeed, enclosedStove: true);
            AssertLoop(hearth, MothersHouseInteriorSoundSynthesis.SampleRate,
                MothersHouseInteriorSoundSynthesis.HearthLoopDuration);
            AssertLoop(stove, MothersHouseInteriorSoundSynthesis.SampleRate,
                MothersHouseInteriorSoundSynthesis.HearthLoopDuration);
            CollectionAssert.AreEqual(stove,
                MothersHouseInteriorSoundSynthesis.GenerateHearthSamples(
                    GameSessionState.DefaultCitySeed, enclosedStove: true));

            double hearthBrightness = NormalizedDifferenceEnergy(hearth);
            double stoveBrightness = NormalizedDifferenceEnergy(stove);
            double hearthVariation = NormalizedEnvelopeVariation(hearth);
            double stoveVariation = NormalizedEnvelopeVariation(stove);
            TestContext.WriteLine($"Hearth/stove brightness: {hearthBrightness:F5}/{stoveBrightness:F5}; " +
                $"100 ms envelope variation: {hearthVariation:F5}/{stoveVariation:F5}");
            Assert.That(stoveBrightness, Is.LessThan(hearthBrightness * .4d),
                "The enclosed stove must lose the open hearth's sharp high-frequency crackle.");
            Assert.That(stoveVariation, Is.LessThan(hearthVariation),
                "The stove's loudness must change more steadily over the duration of a wood crackle.");
            Assert.That(Rms(stove), Is.GreaterThanOrEqualTo(Rms(hearth) * .9d),
                "A calmer stove must retain its audible fire bed rather than merely becoming quieter.");
        }

        [Test]
        public void TransientCues_AreFiniteQuietNonSilentAndEdgeSafe()
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

            AssertCue(
                HomeSoundscapeSynthesis
                    .GenerateBathroomLightCrackleSamples());
        }

        [Test]
        public void BathroomLightCrackle_IsRepeatableAndExpectedLength()
        {
            float[] first =
                HomeSoundscapeSynthesis
                    .GenerateBathroomLightCrackleSamples();
            float[] second =
                HomeSoundscapeSynthesis
                    .GenerateBathroomLightCrackleSamples();

            CollectionAssert.AreEqual(first, second);
            Assert.That(
                first.Length,
                Is.EqualTo(
                    Mathf.RoundToInt(
                        HomeSoundscapeSynthesis.SampleRate *
                        HomeSoundscapeSynthesis
                            .BathroomLightCrackleDuration)));
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

        private static double Rms(IReadOnlyList<float> samples)
        {
            double squares = 0d;
            for (int i = 0; i < samples.Count; i++) squares += (double)samples[i] * samples[i];
            return Math.Sqrt(squares / samples.Count);
        }

        private static double NormalizedDifferenceEnergy(IReadOnlyList<float> samples)
        {
            // Adjacent-sample changes weight the upper spectrum. Normalizing
            // by signal energy keeps a volume reduction from passing as warmth.
            double changes = 0d;
            double squares = 0d;
            for (int i = 1; i < samples.Count; i++)
            {
                double change = samples[i] - samples[i - 1];
                changes += change * change;
                squares += (double)samples[i] * samples[i];
            }
            return changes / squares;
        }

        private static double NormalizedEnvelopeVariation(IReadOnlyList<float> samples)
        {
            // A 100 ms window follows the wood crackle's envelope. At 20 ms,
            // filtering reduces independent noise samples and can increase
            // random RMS fluctuations despite making the fire sound calmer.
            int window = Mathf.RoundToInt(MothersHouseInteriorSoundSynthesis.SampleRate * .1f);
            double previous = 0d;
            double variation = 0d;
            double total = 0d;
            for (int start = 0; start + window <= samples.Count; start += window)
            {
                double squares = 0d;
                for (int i = start; i < start + window; i++)
                    squares += (double)samples[i] * samples[i];
                double level = Math.Sqrt(squares / window);
                if (start > 0) variation += Math.Abs(level - previous);
                total += level;
                previous = level;
            }
            return variation / total;
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
