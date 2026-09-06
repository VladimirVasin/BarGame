using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The stream's looping gurgle: deterministic, finite, loud enough to
    /// carry and quiet enough not to clip, pumping at the flow's own beat,
    /// and closed into a loop whose seam carries no click and no silence.
    /// </summary>
    public sealed class HeroVomitStreamSoundTests
    {
        [Test]
        public void Loop_IsDeterministicFiniteAndTwoSecondsLong()
        {
            float[] first = HeroVomitStreamSound.GenerateLoopSamples();
            float[] second = HeroVomitStreamSound.GenerateLoopSamples();
            Assert.That(first, Is.EqualTo(second), "The same loop every time.");
            int expected = Mathf.CeilToInt(
                HeroVomitStreamSound.LoopSeconds * HeroVomitStreamSound.SampleRate) -
                Mathf.CeilToInt(
                    HeroVomitStreamSound.SeamCrossfadeSeconds * HeroVomitStreamSound.SampleRate);
            Assert.That(first.Length, Is.EqualTo(expected));
            float peak = 0f;
            double energy = 0d;
            for (int index = 0; index < first.Length; index++)
            {
                Assert.That(float.IsFinite(first[index]), Is.True, $"sample {index}");
                Assert.That(Mathf.Abs(first[index]), Is.LessThanOrEqualTo(0.98f), $"sample {index}");
                peak = Mathf.Max(peak, Mathf.Abs(first[index]));
                energy += first[index] * (double)first[index];
            }

            float rms = Mathf.Sqrt((float)(energy / first.Length));
            Assert.That(peak, Is.GreaterThan(0.35f), "Audible: it has to carry from the mouth to the camera.");
            Assert.That(rms, Is.InRange(0.08f, 0.45f), "A rush, not a square wave and not a whisper.");
        }

        [Test]
        public void Loop_PumpsAtTheFlowsBeat()
        {
            float[] samples = HeroVomitStreamSound.GenerateLoopSamples();
            // The RMS over each quarter of a pump period: a crest and a
            // trough must both be there, and the trough is never dry.
            int period = Mathf.RoundToInt(HeroVomitStreamSound.SampleRate / HeroVomitStreamSound.PumpHertz);
            int window = period / 4;
            float loudest = 0f;
            float quietest = float.PositiveInfinity;
            for (int start = 0; start + window <= Mathf.Min(samples.Length, period * 5); start += window)
            {
                double energy = 0d;
                for (int index = start; index < start + window; index++)
                {
                    energy += samples[index] * (double)samples[index];
                }

                float rms = Mathf.Sqrt((float)(energy / window));
                loudest = Mathf.Max(loudest, rms);
                quietest = Mathf.Min(quietest, rms);
            }

            Assert.That(loudest, Is.GreaterThan(quietest * 1.25f), "The pump is heard: crests over troughs.");
            Assert.That(quietest, Is.GreaterThan(0.03f), "It never runs dry between pushes.");
        }

        [Test]
        public void Loop_SeamCarriesNoClickAndNoSilence()
        {
            float[] samples = HeroVomitStreamSound.GenerateLoopSamples();
            int tail = 64;
            double headEnergy = 0d;
            double tailEnergy = 0d;
            for (int index = 0; index < tail; index++)
            {
                headEnergy += samples[index] * (double)samples[index];
                tailEnergy += samples[samples.Length - 1 - index] * (double)samples[samples.Length - 1 - index];
            }

            float headRms = Mathf.Sqrt((float)(headEnergy / tail));
            float tailRms = Mathf.Sqrt((float)(tailEnergy / tail));
            Assert.That(headRms, Is.GreaterThan(0.02f), "The loop opens on the rush, not on silence.");
            Assert.That(tailRms, Is.GreaterThan(0.02f), "And closes on it.");
            Assert.That(
                Mathf.Abs(samples[0] - samples[samples.Length - 1]),
                Is.LessThan(0.35f),
                "The wrap does not jump a whole amplitude: no click at the seam.");
        }

        [Test]
        public void Constants_MatchTheOneShotPoolAndTheFlow()
        {
            Assert.That(HeroVomitStreamSound.SampleRate, Is.EqualTo(RetroSfxLibrary.SampleRate));
            Assert.That(HeroVomitStreamSound.PumpHertz, Is.EqualTo(HeroVomitRules.PulseHertz));
            Assert.That(HeroVomitStreamSound.MaximumVolume, Is.InRange(0.4f, 0.8f));
            Assert.That(HeroVomitStreamSound.MinimumDistanceMetres, Is.EqualTo(1.2f));
            Assert.That(HeroVomitStreamSound.MaximumDistanceMetres, Is.EqualTo(13f));
        }
    }
}
