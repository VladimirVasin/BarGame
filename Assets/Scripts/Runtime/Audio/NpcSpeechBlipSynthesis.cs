using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Asset-free, mono, quantized keystrokes: one short blip per
    /// speaking design, marking that a letter was written.
    ///
    /// The one-shot contract here restates the ALPINE VILLAGE family's,
    /// exactly as <see cref="CemeteryRavenCallSynthesis"/> does and for
    /// the same reason — the village helpers are private, and a
    /// typewriter does not belong in the village enum: `22 050 Hz`
    /// mono, edge fade `min(1, n*160) * min(1, (1-n)*80)`, amplitude
    /// capped at `0.72` and quantized to `127` steps, first and last
    /// samples forced to zero. On top of that it carries the PS1
    /// decimation every <see cref="RetroSfxLibrary"/> effect wears —
    /// sample-hold and a one-pole low-pass — because a UI click that
    /// did not alias would be the only clean sound in the game.
    /// `RetroSfx`'s own `GlideSine`/`Triangle`/`Envelope` are private
    /// there too, so they are restated here rather than widening a
    /// table that is guarded on enum order.
    ///
    /// The waveform is a TRIANGLE and its inharmonic partial, not a
    /// glide: a pitch sweep inside `45 ms` reads as a chirp, and the
    /// brief is a mark that a letter was written. Triangle is also what
    /// survives a 127-step quantizer; a sine comes out as a staircase.
    /// </summary>
    public static class NpcSpeechBlipSynthesis
    {
        public const int SampleRate = CitySourceSoundSynthesis.SampleRate;
        public const int Channels = 1;
        public const float MaximumAmplitude = 0.72f;

        /// <summary>
        /// `45 ms`: eight to twenty-three cycles at the catalog's
        /// `138-262 Hz`, so the blip has a pitch to hear, and half the
        /// `90 ms` throttle, so two of them are never heard as one
        /// held tone.
        /// </summary>
        public const float DurationSeconds = 0.045f;

        /// <summary>Sample-and-hold decimation: an effective `7.35 kHz`,
        /// the house aliasing.</summary>
        public const int SampleHold = 3;

        public const float LowPassCoefficient = 0.42f;

        private const float QuantizationSteps = 127f;
        private const float AttackSeconds = 0.002f;
        private const float ReleasePower = 2.6f;
        /// <summary>
        /// The two partials sum to `0.72` — exactly <see
        /// cref="MaximumAmplitude"/> — on purpose. Louder and the
        /// clamp would flatten every blip against the same ceiling,
        /// which is the one thing this family must not do: clipping
        /// erases timbre, and timbre is the whole difference between
        /// eight speakers.
        /// </summary>
        private const float FundamentalShare = 0.50f;

        private const float PartialShare = 0.22f;
        private const float NoiseGain = 0.24f;

        public static int SampleCount =>
            Mathf.RoundToInt(SampleRate * DurationSeconds);

        /// <summary>
        /// One deterministic keystroke, byte-identical per profile on
        /// every call. The letter's own note is applied later, by the
        /// source's pitch — this is the timbre alone.
        /// </summary>
        public static float[] GenerateBlip(in NpcVoiceProfile voice)
        {
            int sampleCount = SampleCount;
            var samples = new float[sampleCount];
            uint noiseState = Seed(voice);
            float inverse = 1f / Mathf.Max(1f, sampleCount - 1f);
            float held = 0f;
            float filtered = 0f;
            float fundamental = Mathf.Max(1f, voice.FundamentalHz);
            float partial = fundamental *
                            Mathf.Max(1f, voice.TimbreRatio);

            for (int index = 0; index < sampleCount; index++)
            {
                if (index % SampleHold == 0)
                {
                    float time = index / (float)SampleRate;
                    float tone =
                        Triangle(fundamental, time) * FundamentalShare +
                        Triangle(partial, time) * PartialShare;
                    float grit =
                        NextNoise(ref noiseState) *
                        voice.NoiseShare *
                        NoiseGain;
                    held = (tone + grit) *
                           Envelope(
                               time,
                               DurationSeconds,
                               AttackSeconds,
                               ReleasePower);
                }

                filtered += (held - filtered) * LowPassCoefficient;
                float normalized = index * inverse;
                float edge =
                    Mathf.Min(1f, normalized * 160f) *
                    Mathf.Min(1f, (1f - normalized) * 80f);
                samples[index] = Quantize(filtered * edge);
            }

            samples[0] = 0f;
            samples[sampleCount - 1] = 0f;
            return samples;
        }

        /// <summary>
        /// The runtime clip a voice plays. DontSave for the reason
        /// every synthesized clip in the project carries it: the buffer
        /// is regenerated on demand and must never be serialized into a
        /// scene.
        /// </summary>
        public static AudioClip CreateRuntimeClip(
            in NpcVoiceProfile voice)
        {
            float[] samples = GenerateBlip(voice);
            AudioClip clip = AudioClip.Create(
                "NpcSpeechBlip_" + voice.Id,
                samples.Length,
                Channels,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float Triangle(float frequency, float time)
        {
            float phase = Mathf.Repeat(frequency * time, 1f);
            return 1f - 4f * Mathf.Abs(phase - 0.5f);
        }

        private static float Envelope(
            float time,
            float duration,
            float attack,
            float releasePower)
        {
            float attackAmount = attack <= 0f
                ? 1f
                : Mathf.Clamp01(time / attack);
            float remaining = Mathf.Clamp01(
                1f - time / Mathf.Max(0.0001f, duration));
            return attackAmount * Mathf.Pow(remaining, releasePower);
        }

        /// <summary>Never zero: xorshift32 is stuck at zero forever,
        /// and a profile whose grit silently vanished would be a bug
        /// nobody could hear until they compared two of them.</summary>
        private static uint Seed(in NpcVoiceProfile voice)
        {
            unchecked
            {
                uint seed =
                    0x9E3779B9u ^
                    (voice.Hash * 0x85EBCA6Bu) ^
                    0xB1150000u;
                return seed == 0u ? 0x9E3779B9u : seed;
            }
        }

        private static float NextNoise(ref uint state)
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return ((value & 0x00FFFFFFu) / 8388607.5f) - 1f;
        }

        private static float Quantize(float sample)
        {
            return Mathf.Round(
                       Mathf.Clamp(
                           sample,
                           -MaximumAmplitude,
                           MaximumAmplitude) *
                       QuantizationSteps) /
                   QuantizationSteps;
        }
    }
}
