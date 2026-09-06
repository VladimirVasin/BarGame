using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class RetroSfxLibraryTests
    {
        [Test]
        public void Catalog_ContainsEveryRequiredEffectAndCategory()
        {
            Assert.That(
                RetroSfxLibrary.Count,
                Is.EqualTo((int)RetroSfxId.Count - 1));

            var categoryCounts =
                new int[(int)RetroSfxCategory.Count];
            for (int index = 1;
                 index < (int)RetroSfxId.Count;
                 index++)
            {
                RetroSfxId id = (RetroSfxId)index;
                RetroSfxDefinition definition =
                    RetroSfxLibrary.GetDefinition(id);

                Assert.That(definition.Id, Is.EqualTo(id));
                Assert.That(
                    definition.Category,
                    Is.Not.EqualTo(RetroSfxCategory.None));
                // Short one-shots, not music. Most sit under half a
                // second; the toilet flush is the one long world
                // effect and still has to stay a sound, not a track.
                Assert.That(definition.Duration, Is.InRange(0.04f, 3f));
                Assert.That(definition.Volume, Is.InRange(0.01f, 1f));
                Assert.That(definition.MaxVoices, Is.InRange(1, 3));
                Assert.That(definition.SampleHold, Is.InRange(1, 4));
                Assert.That(
                    definition.QuantizationSteps,
                    Is.InRange(256, 4096));
                Assert.That(
                    definition.LowPassFrequency,
                    Is.LessThan(RetroSfxLibrary.SampleRate * 0.5f));
                categoryCounts[(int)definition.Category]++;
            }

            Assert.That(
                categoryCounts[(int)RetroSfxCategory.Ui],
                Is.GreaterThan(0));
            Assert.That(
                categoryCounts[(int)RetroSfxCategory.World],
                Is.GreaterThan(0));
            Assert.That(
                categoryCounts[(int)RetroSfxCategory.Bar],
                Is.GreaterThan(0));
        }

        [TestCase(RetroSfxId.UiMove)]
        [TestCase(RetroSfxId.UiConfirm)]
        [TestCase(RetroSfxId.UiCancel)]
        [TestCase(RetroSfxId.MapOpen)]
        [TestCase(RetroSfxId.Footstep)]
        [TestCase(RetroSfxId.Door)]
        [TestCase(RetroSfxId.DoorCreak)]
        [TestCase(RetroSfxId.Pour)]
        [TestCase(RetroSfxId.Clink)]
        [TestCase(RetroSfxId.Shake)]
        [TestCase(RetroSfxId.Good)]
        [TestCase(RetroSfxId.Bad)]
        [TestCase(RetroSfxId.BeerPongThrow)]
        [TestCase(RetroSfxId.BeerPongBounce)]
        [TestCase(RetroSfxId.BeerPongRim)]
        [TestCase(RetroSfxId.BeerPongSink)]
        [TestCase(RetroSfxId.DrinkGulp)]
        [TestCase(RetroSfxId.ShotSwap)]
        [TestCase(RetroSfxId.ShotMatch)]
        [TestCase(RetroSfxId.MoonshineBurst)]
        [TestCase(RetroSfxId.RefrigeratorSeal)]
        [TestCase(RetroSfxId.RefrigeratorHinge)]
        [TestCase(RetroSfxId.RefrigeratorThunk)]
        [TestCase(RetroSfxId.Hiccup)]
        [TestCase(RetroSfxId.Retch)]
        [TestCase(RetroSfxId.VomitGush)]
        [TestCase(RetroSfxId.VomitSplat)]
        [TestCase(RetroSfxId.VomitCough)]
        public void GenerateSamples_IsDeterministicFiniteAndAudible(
            RetroSfxId id)
        {
            RetroSfxDefinition definition =
                RetroSfxLibrary.GetDefinition(id);
            float[] first = RetroSfxLibrary.GenerateSamples(id);
            float[] second = RetroSfxLibrary.GenerateSamples(id);

            Assert.That(
                first.Length,
                Is.EqualTo(
                    Mathf.CeilToInt(
                        definition.Duration *
                        RetroSfxLibrary.SampleRate)));
            CollectionAssert.AreEqual(first, second);

            float peak = 0f;
            double energy = 0d;
            for (int index = 0; index < first.Length; index++)
            {
                float sample = first[index];
                Assert.That(float.IsNaN(sample), Is.False);
                Assert.That(float.IsInfinity(sample), Is.False);
                peak = Mathf.Max(peak, Mathf.Abs(sample));
                energy += sample * sample;
            }

            Assert.That(peak, Is.GreaterThan(0.04f));
            Assert.That(peak, Is.LessThanOrEqualTo(0.981f));
            Assert.That(energy, Is.GreaterThan(0.01d));
        }

        [Test]
        public void DoorCreak_IsDistinctWorldCueForOpeningMotion()
        {
            RetroSfxDefinition latch =
                RetroSfxLibrary.GetDefinition(RetroSfxId.Door);
            RetroSfxDefinition creak =
                RetroSfxLibrary.GetDefinition(RetroSfxId.DoorCreak);

            Assert.That(
                creak.Category,
                Is.EqualTo(RetroSfxCategory.World));
            Assert.That(creak.Duration, Is.GreaterThan(latch.Duration));
            Assert.That(creak.Duration, Is.LessThanOrEqualTo(0.5f));
            Assert.That(
                RetroSfxLibrary.GenerateSamples(RetroSfxId.DoorCreak),
                Is.Not.EqualTo(
                    RetroSfxLibrary.GenerateSamples(RetroSfxId.Door)));
        }

        [Test]
        public void RefrigeratorCues_AreDistinctSpatialWorldEffects()
        {
            RetroSfxId[] cues =
            {
                RetroSfxId.RefrigeratorSeal,
                RetroSfxId.RefrigeratorHinge,
                RetroSfxId.RefrigeratorThunk
            };
            for (int index = 0; index < cues.Length; index++)
            {
                RetroSfxDefinition definition =
                    RetroSfxLibrary.GetDefinition(cues[index]);
                Assert.That(
                    definition.Category,
                    Is.EqualTo(RetroSfxCategory.World));
                Assert.That(
                    definition.SpatialBlend,
                    Is.GreaterThanOrEqualTo(0.9f));
            }

            Assert.That(
                RetroSfxLibrary.GenerateSamples(
                    RetroSfxId.RefrigeratorSeal),
                Is.Not.EqualTo(
                    RetroSfxLibrary.GenerateSamples(
                        RetroSfxId.RefrigeratorHinge)));
            Assert.That(
                RetroSfxLibrary.GenerateSamples(
                    RetroSfxId.RefrigeratorHinge),
                Is.Not.EqualTo(
                    RetroSfxLibrary.GenerateSamples(
                        RetroSfxId.RefrigeratorThunk)));
        }

        [Test]
        public void VomitCues_AreDistinctSpatialWorldEffects()
        {
            RetroSfxId[] cues =
            {
                RetroSfxId.Retch,
                RetroSfxId.VomitGush,
                RetroSfxId.VomitSplat,
                RetroSfxId.VomitCough
            };
            float[] expectedDurations = { 0.62f, 0.7f, 0.18f, 0.55f };
            // Loud enough to be heard from the third-person camera: the
            // first cut sat at a quarter volume and the user heard
            // nothing. The splat stays under the voice.
            float[] minimumVolumes = { 0.5f, 0.45f, 0.3f, 0.45f };
            for (int index = 0; index < cues.Length; index++)
            {
                RetroSfxDefinition definition =
                    RetroSfxLibrary.GetDefinition(cues[index]);
                Assert.That(
                    definition.Category,
                    Is.EqualTo(RetroSfxCategory.World),
                    cues[index].ToString());
                Assert.That(
                    definition.SpatialBlend,
                    Is.GreaterThanOrEqualTo(0.9f),
                    cues[index].ToString());
                Assert.That(
                    definition.Duration,
                    Is.EqualTo(expectedDurations[index]).Within(1e-4f),
                    cues[index].ToString());
                Assert.That(
                    definition.Volume,
                    Is.GreaterThanOrEqualTo(minimumVolumes[index]),
                    cues[index].ToString());
            }

            // Impacts arrive in clusters; the retch and the stream are
            // one voice each so a dropped frame cannot double them.
            Assert.That(
                RetroSfxLibrary.GetDefinition(RetroSfxId.VomitSplat)
                    .MaxVoices,
                Is.EqualTo(2));

            float[] retch =
                RetroSfxLibrary.GenerateSamples(RetroSfxId.Retch);
            float[] gush =
                RetroSfxLibrary.GenerateSamples(RetroSfxId.VomitGush);
            float[] splat =
                RetroSfxLibrary.GenerateSamples(RetroSfxId.VomitSplat);
            float[] hiccup =
                RetroSfxLibrary.GenerateSamples(RetroSfxId.Hiccup);
            float[] cough =
                RetroSfxLibrary.GenerateSamples(RetroSfxId.VomitCough);
            Assert.That(cough, Is.Not.EqualTo(retch));
            Assert.That(cough, Is.Not.EqualTo(gush));
            Assert.That(retch, Is.Not.EqualTo(gush));
            Assert.That(gush, Is.Not.EqualTo(splat));
            Assert.That(retch, Is.Not.EqualTo(splat));
            Assert.That(retch, Is.Not.EqualTo(hiccup));
            Assert.That(gush, Is.Not.EqualTo(hiccup));
            Assert.That(splat, Is.Not.EqualTo(hiccup));
        }

        [Test]
        public void ServicePoolBudget_IsSmallAndCategoryBounded()
        {
            Assert.That(RetroAudioService.UiPoolSize, Is.EqualTo(4));
            Assert.That(RetroAudioService.WorldPoolSize, Is.EqualTo(5));
            Assert.That(RetroAudioService.BarPoolSize, Is.EqualTo(5));
            Assert.That(
                RetroAudioService.TotalPoolSize,
                Is.EqualTo(14));
        }

        [Test]
        public void InvalidEffectId_IsRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => RetroSfxLibrary.GetDefinition(
                    RetroSfxId.None));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => RetroSfxLibrary.GenerateSamples(
                    RetroSfxId.Count));
        }

        [TestCase(RetroAmbienceKind.City)]
        [TestCase(RetroAmbienceKind.Bar)]
        [TestCase(RetroAmbienceKind.Home)]
        [TestCase(RetroAmbienceKind.Stairwell)]
        public void Ambience_IsDeterministicQuietAndLoopSafe(
            RetroAmbienceKind kind)
        {
            float[] first =
                RetroAmbienceSynthesis.GenerateSamples(kind);
            float[] second =
                RetroAmbienceSynthesis.GenerateSamples(kind);

            Assert.That(
                first.Length,
                Is.EqualTo(
                    Mathf.RoundToInt(
                        RetroAmbienceSynthesis.SampleRate *
                        RetroAmbienceSynthesis.Duration)));
            CollectionAssert.AreEqual(first, second);

            float peak = 0f;
            double energy = 0d;
            for (int index = 0; index < first.Length; index++)
            {
                float sample = first[index];
                Assert.That(float.IsNaN(sample), Is.False);
                Assert.That(float.IsInfinity(sample), Is.False);
                peak = Mathf.Max(peak, Mathf.Abs(sample));
                energy += sample * sample;
            }

            float rms = Mathf.Sqrt(
                (float)(energy / first.Length));
            Assert.That(peak, Is.InRange(0.02f, 0.72f));
            Assert.That(rms, Is.InRange(0.005f, 0.2f));
            Assert.That(
                Mathf.Abs(first[0] - first[first.Length - 1]),
                Is.LessThan(0.04f));
        }

        [Test]
        public void HomeAmbience_IsDistinctFromBar()
        {
            float[] home =
                RetroAmbienceSynthesis.GenerateSamples(
                    RetroAmbienceKind.Home);
            float[] bar =
                RetroAmbienceSynthesis.GenerateSamples(
                    RetroAmbienceKind.Bar);

            Assert.That(home.Length, Is.EqualTo(bar.Length));
            double squaredDifference = 0d;
            for (int index = 0; index < home.Length; index++)
            {
                float difference = home[index] - bar[index];
                squaredDifference += difference * difference;
            }

            float differenceRms = Mathf.Sqrt(
                (float)(squaredDifference / home.Length));
            Assert.That(
                differenceRms,
                Is.GreaterThan(0.01f),
                "The home loop must not reuse the bar ambience.");
        }
    }
}
