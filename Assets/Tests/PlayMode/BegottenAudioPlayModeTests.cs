using System;
using System.Collections;
using System.Collections.Generic;
using BarPromenade.Rendering;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The projector runs on the one world bus, from the one weight the
    /// picture is printed with, and it never runs beside the tape.
    /// </summary>
    public sealed class BegottenAudioPlayModeTests
    {
        private bool capturing;

        [UnityTest]
        public IEnumerator Projector_FollowsThePrintAndSilencesTheTape()
        {
            bool previousPause = AudioListener.pause;
            GameSessionState.BeginNewGame();
            BegottenAudioDriver projector = BegottenAudioDriver.EnsureInstalled();
            IntoxicationAudioDriver tape = IntoxicationAudioDriver.EnsureInstalled();
            try
            {
                BegottenModeRamp.DebugWeightOverride = 0f;
                // As drunk as the game gets, so the exclusion below is the
                // print's doing rather than a sober bus.
                GameSessionState.UpdateDrinkingProgress(100, DrinkId.None, 0);
                GameTimeScaleRuntime.SetIntoxicationLevel(100f);
                yield return null;
                yield return null;

                Assert.That(
                    projector.IsConfigured,
                    Is.True,
                    "The native projector controls must be exposed.");
                Assert.That(
                    BegottenAudioDriver.EnsureInstalled(),
                    Is.SameAs(projector));
                Assert.That(projector.AppliedWeight, Is.Zero);
                AssertParameter(BegottenAudioRules.WeightParameter, 0f);
                AssertParameter(IntoxicationAudioDriver.IntensityParameter, 1f);

                // Half arrived: the print is half printed and the tape has
                // given up exactly half of itself.
                BegottenModeRamp.DebugWeightOverride = 0.5f;
                yield return null;
                yield return null;
                Assert.That(
                    projector.AppliedWeight,
                    Is.EqualTo(0.5f).Within(0.0001f));
                AssertParameter(BegottenAudioRules.WeightParameter, 0.5f);
                AssertParameter(IntoxicationAudioDriver.IntensityParameter, 0.5f);

                // Fully arrived: one apparatus, never two stacked.
                BegottenModeRamp.DebugWeightOverride = 1f;
                yield return null;
                yield return null;
                AssertParameter(BegottenAudioRules.WeightParameter, 1f);
                Assert.That(
                    tape.AppliedIntensity,
                    Is.Zero,
                    "The tape and the print are mutually exclusive.");
                AssertParameter(IntoxicationAudioDriver.IntensityParameter, 0f);

                // A paused menu is a still, and a still is not projected.
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    AudioListener.pause = true;
                    yield return null;
                    yield return null;
                    AssertParameter(BegottenAudioRules.PausedParameter, 1f);
                    AudioListener.pause = false;
                }
                yield return null;
                yield return null;
                AssertParameter(BegottenAudioRules.PausedParameter, 0f);
                AssertParameter(BegottenAudioRules.WeightParameter, 1f);

                // A new game threads a fresh reel.
                GameAudioMixer.Mixer.GetFloat(
                    BegottenAudioRules.ResetParameter, out float oldEpoch);
                BegottenModeRamp.DebugWeightOverride = null;
                GameSessionState.BeginNewGame();
                yield return null;
                yield return null;
                AssertParameter(BegottenAudioRules.WeightParameter, 0f);
                GameAudioMixer.Mixer.GetFloat(
                    BegottenAudioRules.ResetParameter, out float newEpoch);
                Assert.That(
                    newEpoch,
                    Is.Not.EqualTo(oldEpoch),
                    "A new game must thread a fresh reel.");
            }
            finally
            {
                BegottenModeRamp.DebugWeightOverride = null;
                AudioListener.pause = previousPause;
                GameSessionState.BeginNewGame();
            }
        }

        /// <summary>
        /// The one assertion that was missing, and the only one that can fail
        /// when the parameters are perfect: that audio actually PASSES THROUGH
        /// the projector. Reading a mixer parameter back only proves the C#
        /// side stored it - a native effect the running plug-in never provided
        /// leaves the sound untouched and reports nothing at all.
        ///
        /// So this renders the real mix offline and measures it.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePrint_ActuallyReachesTheWorldBus()
        {
            float previousVolume = AudioListener.volume;
            bool previousPause = AudioListener.pause;
            GameObject carrier = null;
            try
            {
                BegottenAudioDriver.EnsureInstalled();
                AudioListener.pause = false;
                // The run-wide guard silences the listener; offline rendering
                // reaches no device, so the capture may have its level back.
                AudioListener.volume = 1f;

                carrier = new GameObject("Begotten Capture Source");
                // An isolated test scene has no listener at all, and without
                // one nothing is mixed and the capture is silent.
                carrier.AddComponent<AudioListener>();
                AudioSource source = carrier.AddComponent<AudioSource>();
                source.clip = UncorrelatedNoise();
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0.5f;
                Assert.That(
                    GameAudioMixer.Route(source, GameAudioGroup.Music),
                    Is.True,
                    "The capture must ride the same bus the themes do.");
                source.Play();

                yield return null;

                // One recording session for both weights. Starting and
                // stopping the renderer twice was unreliable: the first
                // session came back with no samples at all.
                var dryFrames = new List<float>();
                var printedFrames = new List<float>();

                // The FIRST recording session of a play session comes back
                // with no samples at all, every time; the second one records.
                // Prime it with a throwaway session rather than measuring
                // silence and calling it a result.
                AudioRenderer.Start();
                capturing = true;
                yield return Pump(20, null);
                AudioRenderer.Stop();
                capturing = false;
                yield return null;

                // Offline DSP time advances only as far as we render, so the
                // phases are sized in SAMPLES, not frames: a settle counted in
                // frames gave 21 ms of audio, less than the effect's own 60 ms
                // de-zipper, and measured a half-arrived print.
                const int quarterSecond = 48000 / 2;   // interleaved stereo
                const int halfSecond = 48000;
                AudioRenderer.Start();
                capturing = true;
                yield return Pump(quarterSecond, null);
                BegottenModeRamp.DebugWeightOverride = 0f;
                yield return Pump(quarterSecond, null);
                yield return Pump(halfSecond, dryFrames);
                BegottenModeRamp.DebugWeightOverride = 1f;
                yield return Pump(halfSecond, null);
                yield return Pump(halfSecond, printedFrames);
                AudioRenderer.Stop();
                capturing = false;

                float[] dry = dryFrames.ToArray();
                float[] printed = printedFrames.ToArray();
                Debug.Log(
                    $"[capture] samples={dry.Length}/{printed.Length} " +
                    $"dryLevel={Level(dry):E3} printedLevel={Level(printed):E3} " +
                    $"dryHigh={HighFraction(dry):F4} printedHigh={HighFraction(printed):F4} " +
                    $"dryFold={ChannelCorrelation(dry):F4} printedFold={ChannelCorrelation(printed):F4}");

                Assert.That(dry, Is.Not.Null.And.Not.Empty);
                Assert.That(printed, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    Level(dry),
                    Is.GreaterThan(0.0005f),
                    "The capture is silent, so it measures nothing. Offline " +
                    "rendering or the routing is broken, not the print.");

                float dryHigh = HighFraction(dry);
                float printedHigh = HighFraction(printed);
                float dryFold = ChannelCorrelation(dry);
                float printedFold = ChannelCorrelation(printed);

                // Both are enormous when the effect is in the graph and both
                // are exactly zero when it is not, which is the whole point.
                Assert.That(
                    printedHigh,
                    Is.LessThan(dryHigh * 0.5f),
                    $"The optical gate never closed on the real bus: energy " +
                    $"above 5 kHz went {dryHigh:F3} -> {printedHigh:F3}. The " +
                    $"effect is most likely absent from the running mixer.");
                Assert.That(
                    printedFold,
                    Is.GreaterThan(dryFold + 0.4f),
                    $"The channels never folded into one optical track: " +
                    $"correlation went {dryFold:F3} -> {printedFold:F3}.");
            }
            finally
            {
                BegottenModeRamp.DebugWeightOverride = null;
                if (capturing)
                {
                    AudioRenderer.Stop();
                    capturing = false;
                }

                AudioListener.volume = previousVolume;
                AudioListener.pause = previousPause;
                if (carrier != null)
                {
                    UnityEngine.Object.Destroy(carrier);
                }
            }
        }

        /// <summary>Renders frames of the live mix. A started renderer must be
        /// pumped every frame or the audio system stalls, so the frames that
        /// are only there to settle the de-zipper are rendered and
        /// discarded rather than skipped.</summary>
        private static IEnumerator Pump(int targetSamples, List<float> sink)
        {
            int rendered = 0;
            for (int frame = 0; rendered < targetSamples && frame < 20000; frame++)
            {
                yield return null;
                int samples = AudioRenderer.GetSampleCountForCaptureFrame();
                // Render must be called EVERY capture frame, including the
                // frames that offer no samples yet: that call is what drives
                // the offline clock. Skipping it leaves the recorder at zero
                // samples forever.
                var buffer = new NativeArray<float>(
                    Math.Max(0, samples) * 2, Allocator.Temp);
                AudioRenderer.Render(buffer);
                rendered += buffer.Length;
                if (sink != null)
                {
                    for (int index = 0; index < buffer.Length; index++)
                    {
                        sink.Add(buffer[index]);
                    }
                }

                buffer.Dispose();
            }
        }

        /// <summary>Two seconds of independent noise per channel: broadband
        /// enough to show the gate, uncorrelated enough to show the fold.
        /// </summary>
        private static AudioClip UncorrelatedNoise()
        {
            const int rate = 48000;
            var samples = new float[rate * 2 * 2];
            uint state = 0x2F6B1A7Du;
            for (int index = 0; index < samples.Length; index++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                samples[index] = (state & 0x00FFFFFFu) / 8388608f - 1f;
            }

            AudioClip clip = AudioClip.Create(
                "BegottenCaptureNoise", rate * 2, 2, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float Level(float[] interleaved)
        {
            double sum = 0;
            for (int index = 0; index < interleaved.Length; index++)
            {
                sum += interleaved[index] * (double)interleaved[index];
            }

            return (float)Math.Sqrt(sum / Math.Max(1, interleaved.Length));
        }

        /// <summary>
        /// Share of the energy above 5 kHz, by a one-pole split rather than a
        /// transform: the gate closing is a large effect and does not need a
        /// spectrum to be seen.
        /// </summary>
        private static float HighFraction(float[] interleaved)
        {
            double coefficient = 1.0 - Math.Exp(-2.0 * Math.PI * 5000.0 / 48000.0);
            double low = 0;
            double lowEnergy = 0;
            double highEnergy = 0;
            for (int index = 0; index < interleaved.Length; index += 2)
            {
                double value = interleaved[index];
                low += coefficient * (value - low);
                lowEnergy += low * low;
                highEnergy += (value - low) * (value - low);
            }

            return (float)(highEnergy / Math.Max(1e-12, lowEnergy + highEnergy));
        }

        private static float ChannelCorrelation(float[] interleaved)
        {
            double left = 0, right = 0, both = 0;
            for (int index = 0; index + 1 < interleaved.Length; index += 2)
            {
                double a = interleaved[index];
                double b = interleaved[index + 1];
                left += a * a;
                right += b * b;
                both += a * b;
            }

            return (float)(both / Math.Max(1e-12, Math.Sqrt(left * right)));
        }

        private static void AssertParameter(string name, float expected)
        {
            Assert.That(
                GameAudioMixer.Mixer.GetFloat(name, out float actual),
                Is.True,
                name);
            Assert.That(actual, Is.EqualTo(expected).Within(0.0001f), name);
        }
    }
}
