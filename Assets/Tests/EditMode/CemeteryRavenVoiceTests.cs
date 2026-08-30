using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CemeteryRavenVoiceTests
    {
        [Test]
        public void Caw_VariantsAreDeterministicFiniteAndEdgeSafe()
        {
            Assert.That(
                CemeteryRavenCallSynthesis.SampleRate,
                Is.EqualTo(CitySourceSoundSynthesis.SampleRate));
            Assert.That(
                CemeteryRavenCallSynthesis.Channels,
                Is.EqualTo(1));
            // The village one-shot family's cap, NOT the city source
            // family's 0.82 — the two contracts must never blur.
            Assert.That(
                CemeteryRavenCallSynthesis.MaximumAmplitude,
                Is.EqualTo(0.72f));

            int expectedLength = Mathf.RoundToInt(
                CemeteryRavenCallSynthesis.SampleRate *
                CemeteryRavenCallSynthesis.DurationSeconds);
            for (int variant = 0;
                 variant < CemeteryRavenCallSynthesis.VariantCount;
                 variant++)
            {
                float[] first =
                    CemeteryRavenCallSynthesis.GenerateCaw(variant);
                float[] second =
                    CemeteryRavenCallSynthesis.GenerateCaw(variant);

                Assert.That(
                    first.Length,
                    Is.EqualTo(expectedLength),
                    "variant " + variant);
                CollectionAssert.AreEqual(first, second);
                Assert.That(first[0], Is.Zero,
                    "variant " + variant + " start");
                Assert.That(first[first.Length - 1], Is.Zero,
                    "variant " + variant + " end");

                float peak = 0f;
                for (int index = 0; index < first.Length; index++)
                {
                    float sample = first[index];
                    Assert.That(float.IsNaN(sample), Is.False);
                    Assert.That(float.IsInfinity(sample), Is.False);
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                }

                Assert.That(
                    peak,
                    Is.LessThanOrEqualTo(
                        CemeteryRavenCallSynthesis
                            .MaximumAmplitude),
                    "variant " + variant + " peak");
                Assert.That(
                    peak,
                    Is.GreaterThan(0.05f),
                    "variant " + variant + " must be audible.");
            }
        }

        [Test]
        public void Caw_VariantsAreAudiblyDistinct()
        {
            int count = CemeteryRavenCallSynthesis.VariantCount;
            var signals = new float[count][];
            var rms = new float[count];
            var crossings = new int[count];
            for (int variant = 0; variant < count; variant++)
            {
                signals[variant] =
                    CemeteryRavenCallSynthesis.GenerateCaw(variant);
                rms[variant] = Rms(signals[variant]);
                crossings[variant] =
                    ZeroCrossings(signals[variant]);
            }

            for (int first = 0; first < count; first++)
            {
                for (int second = first + 1;
                     second < count;
                     second++)
                {
                    string pair =
                        "variants " + first + " and " + second;
                    // Sample-wise they must genuinely differ...
                    Assert.That(
                        MeanAbsoluteDifference(
                            signals[first],
                            signals[second]),
                        Is.GreaterThan(0.003f),
                        pair + " are effectively identical");
                    // ...and the difference must be one the EAR gets:
                    // loudness (RMS) or pitch/texture (crossings).
                    float rmsShift = Mathf.Abs(
                        rms[first] - rms[second]) /
                        Mathf.Max(rms[first], rms[second]);
                    float crossingShift = Mathf.Abs(
                        crossings[first] - crossings[second]) /
                        (float)Mathf.Max(
                            crossings[first],
                            crossings[second]);
                    Assert.That(
                        rmsShift > 0.005f || crossingShift > 0.005f,
                        Is.True,
                        pair + " differ neither in RMS nor in " +
                        "zero-crossing texture");
                }
            }
        }

        [Test]
        public void Caw_RuntimeClipIsTransientAndMatchesTheContract()
        {
            AudioClip clip =
                CemeteryRavenCallSynthesis.CreateRuntimeClip(0);
            try
            {
                Assert.That(clip, Is.Not.Null);
                Assert.That(
                    clip.hideFlags,
                    Is.EqualTo(HideFlags.DontSave),
                    "A synthesized clip must never be serialized " +
                    "into a scene.");
                Assert.That(clip.channels, Is.EqualTo(1));
                Assert.That(
                    clip.frequency,
                    Is.EqualTo(
                        CemeteryRavenCallSynthesis.SampleRate));
                Assert.That(
                    clip.samples,
                    Is.EqualTo(Mathf.RoundToInt(
                        CemeteryRavenCallSynthesis.SampleRate *
                        CemeteryRavenCallSynthesis
                            .DurationSeconds)));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void Schedule_NeverAccumulatesCatchUpEvents()
        {
            var host = new GameObject("Raven Voice Test Host");
            CemeteryRavenVoice voice = null;
            try
            {
                voice = CemeteryRavenVoice.Create(host.transform, 7);

                // One 300-second chunk crosses many hashed due
                // moments, but Advance is a single gate: at most one
                // firing, and the schedule re-arms from NOW rather
                // than draining a backlog — the village's rule.
                voice.Advance(300f, true);
                double dueAfterChunk = ReadNextDueSeconds(voice);
                Assert.That(
                    dueAfterChunk,
                    Is.GreaterThanOrEqualTo(300d +
                        CemeteryRavenVoice
                            .MinimumCallIntervalSeconds),
                    "A silenced or fired due moment must re-arm " +
                    "from the present, never from the missed past.");
                Assert.That(
                    dueAfterChunk,
                    Is.LessThanOrEqualTo(300d +
                        CemeteryRavenVoice
                            .MaximumCallIntervalSeconds));

                // The next window shorter than the minimum interval
                // is therefore guaranteed quiet.
                voice.Source.Stop();
                float quiet =
                    CemeteryRavenVoice.MinimumCallIntervalSeconds -
                    1f;
                for (float t = 0f; t < quiet; t += 1f)
                {
                    voice.Advance(1f, true);
                }

                Assert.That(
                    voice.Source.isPlaying,
                    Is.False,
                    "No backlog may burst out after a long chunk.");
            }
            finally
            {
                if (voice != null)
                {
                    voice.Dispose();
                }

                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Schedule_IsSilentDuringAGraveWorkSession()
        {
            var host = new GameObject("Raven Voice Test Host");
            CemeteryRavenVoice voice = null;
            try
            {
                voice = CemeteryRavenVoice.Create(host.transform, 7);

                // canCall == false is the controller's session flag:
                // every due moment inside the window passes silently
                // and still re-arms, so nothing plays afterwards
                // either until a fresh interval elapses.
                voice.Advance(300f, false);
                Assert.That(
                    voice.Source.isPlaying,
                    Is.False,
                    "A caw over a burial act would be a comment.");
                voice.Advance(1f, true);
                Assert.That(
                    voice.Source.isPlaying,
                    Is.False,
                    "The silenced due moment must have re-armed.");
            }
            finally
            {
                if (voice != null)
                {
                    voice.Dispose();
                }

                Object.DestroyImmediate(host);
            }
        }

        private static double ReadNextDueSeconds(
            CemeteryRavenVoice voice)
        {
            System.Reflection.FieldInfo field =
                typeof(CemeteryRavenVoice).GetField(
                    "nextDueSeconds",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            return (double)field.GetValue(voice);
        }

        [Test]
        public void Caw_RejectsAnUnknownVariant()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                CemeteryRavenCallSynthesis.GenerateCaw(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                CemeteryRavenCallSynthesis.GenerateCaw(
                    CemeteryRavenCallSynthesis.VariantCount));
        }

        private static float Rms(float[] samples)
        {
            double sumSquares = 0d;
            for (int index = 0; index < samples.Length; index++)
            {
                sumSquares += samples[index] * samples[index];
            }

            return Mathf.Sqrt(
                (float)(sumSquares / samples.Length));
        }

        private static int ZeroCrossings(float[] samples)
        {
            int count = 0;
            for (int index = 1; index < samples.Length; index++)
            {
                if (samples[index - 1] * samples[index] < 0f)
                {
                    count++;
                }
            }

            return count;
        }

        private static float MeanAbsoluteDifference(
            float[] first,
            float[] second)
        {
            Assert.That(second.Length, Is.EqualTo(first.Length));
            double difference = 0d;
            for (int index = 0; index < first.Length; index++)
            {
                difference += Mathf.Abs(
                    first[index] - second[index]);
            }

            return (float)(difference / first.Length);
        }
    }
}
