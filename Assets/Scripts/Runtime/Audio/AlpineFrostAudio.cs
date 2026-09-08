using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Dry frost fractures and softer wet releases, heard at the image edge.</summary>
    public sealed class AlpineFrostAudio : IDisposable
    {
        public enum CueKind { None, Freezing, Thawing }
        public const int SampleRate = 22050;
        public const float ClipSeconds = 1.35f;
        public const float ThawClipSeconds = 1.2f;
        private readonly AudioClip[] clips = new AudioClip[3];
        private readonly AudioClip[] thawClips = new AudioClip[3];
        private double untilCue = 0.8d;
        private int sequence;
        private int thawSequence;
        private bool hasThermalMode;
        private bool wasThawing;
        private float modeFadeRemaining;
        private bool paused;
        public AudioSource Source { get; }
        public int CuesPlayed { get; private set; }
        public int FreezingCuesPlayed => sequence;
        public int ThawCuesPlayed => thawSequence;
        public CueKind LastCueKind { get; private set; }

        public AlpineFrostAudio(GameObject owner)
        {
            Source = owner.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.loop = false;
            Source.spatialBlend = 0f;
            Source.volume = 0f;
            Source.priority = 180;
            Source.dopplerLevel = 0f;
            GameAudioMixer.Route(Source, GameAudioGroup.AmbienceDetails);
            for (int i = 0; i < clips.Length; i++)
            {
                float[] samples = GenerateSamples(i);
                clips[i] = AudioClip.Create("Alpine Frost Crystal " + i,
                    samples.Length, 1, SampleRate, false);
                clips[i].SetData(samples, 0);
                float[] thawSamples = GenerateThawSamples(i);
                thawClips[i] = AudioClip.Create("Alpine Frost Thaw " + i,
                    thawSamples.Length, 1, SampleRate, false);
                thawClips[i].SetData(thawSamples, 0);
            }
        }

        public void Step(float delta, float amount, bool thawing, bool suspended)
        {
            if (suspended)
            {
                if (!paused) Source.Pause();
                paused = true;
                return;
            }
            if (paused)
            {
                Source.UnPause();
                paused = false;
            }
            if (amount <= 0f)
            {
                Reset();
                return;
            }

            if (!hasThermalMode || wasThawing != thawing)
            {
                hasThermalMode = true;
                wasThawing = thawing;
                // A warm arrival must not inherit the next cold crack's long
                // wait. The same source releases its old tail before changing
                // clips, including when the hero immediately goes out again.
                untilCue = thawing ? 0.22d : 0.8d;
                modeFadeRemaining = Source.isPlaying ? 0.12f : 0f;
                if (modeFadeRemaining == 0f)
                {
                    Source.Stop();
                    Source.clip = null;
                    Source.volume = 0f;
                }
            }
            float gain = thawing ? 0.18f * Mathf.Sqrt(amount) :
                Mathf.Lerp(0.045f, 0.24f, Mathf.Sqrt(amount));
            if (modeFadeRemaining > 0f)
            {
                float remaining = Mathf.Max(0f, modeFadeRemaining - delta);
                Source.volume *= remaining / modeFadeRemaining;
                modeFadeRemaining = remaining;
                if (remaining == 0f)
                {
                    Source.Stop();
                    Source.clip = null;
                }
            }
            else Source.volume = Mathf.MoveTowards(Source.volume, gain, delta * 0.3f);
            untilCue -= delta;
            if (untilCue > 0d) return;
            uint hash;
            if (thawing)
            {
                hash = CitySoundStableHash.Combine(0x54484157u, unchecked((uint)thawSequence++));
                Source.clip = thawClips[(thawSequence - 1) % thawClips.Length];
                Source.pitch = Mathf.Lerp(0.95f, 1.05f, CitySoundStableHash.ToUnitFloat(hash));
                LastCueKind = CueKind.Thawing;
            }
            else
            {
                hash = CitySoundStableHash.Combine(0x494345u, unchecked((uint)sequence++));
                Source.clip = clips[(sequence - 1) % clips.Length];
                Source.pitch = Mathf.Lerp(0.88f, 1.12f, CitySoundStableHash.ToUnitFloat(hash));
                LastCueKind = CueKind.Freezing;
            }
            float pan = thawing ? 0.18f : 0.38f;
            Source.panStereo = Mathf.Lerp(-pan, pan,
                CitySoundStableHash.ToUnitFloat(CitySoundStableHash.Combine(hash, 17u)));
            Source.Play();
            CuesPlayed++;
            float interval = CitySoundStableHash.ToUnitFloat(CitySoundStableHash.Combine(hash, 31u));
            untilCue = thawing ? Mathf.Lerp(1.5f, 2.5f, interval) :
                (amount >= 0.999f ? 3.5f : 0f) + Mathf.Lerp(4.6f, 8.9f, interval);
        }

        public void Reset()
        {
            Source.Stop();
            Source.clip = null;
            Source.volume = 0f;
            untilCue = 0.8d;
            sequence = thawSequence = CuesPlayed = 0;
            LastCueKind = CueKind.None;
            hasThermalMode = wasThawing = false;
            modeFadeRemaining = 0f;
            paused = false;
        }

        public void Dispose()
        {
            foreach (AudioClip clip in clips)
                if (clip != null) UnityEngine.Object.Destroy(clip);
            foreach (AudioClip clip in thawClips)
                if (clip != null) UnityEngine.Object.Destroy(clip);
            if (Source != null) UnityEngine.Object.Destroy(Source);
        }

        public static float[] GenerateThawSamples(int variant)
        {
            var samples = new float[(int)(SampleRate * ThawClipSeconds)];
            uint random = unchecked(0x7A4AF012u + (uint)variant * 104729u);
            double[] releases = { 0.025, 0.18, 0.39, 0.68, 0.91 };
            foreach (double release in releases)
            {
                int start = (int)((release + Next(ref random) * 0.035d) * SampleRate);
                double duration = 0.11d + Next(ref random) * 0.07d;
                double amplitude = 0.20d + Next(ref random) * 0.16d;
                double frequency = 430d + Next(ref random) * 260d;
                double dampedNoise = 0d;
                for (int j = 0; j < duration * SampleRate && start + j < samples.Length; j++)
                {
                    double t = j / (double)SampleRate;
                    dampedNoise += 0.18d * ((Next(ref random) * 2d - 1d) - dampedNoise);
                    double envelope = Math.Min(1d, t / 0.007d) * Math.Exp(-t * 28d) *
                        Math.Min(1d, (duration - t) / 0.025d);
                    double crinkle = 0.48d + 0.52d * Math.Pow(
                        Math.Sin(t * (75d + frequency * 0.08d) * Math.PI), 2d);
                    // A damped low release beneath soft, uneven contact noise;
                    // no long high partial that could read as a glass chime.
                    double releaseTone = (Math.Sin(t * frequency * Math.PI * 2d) +
                        0.23d * Math.Sin(t * frequency * 1.61d * Math.PI * 2d)) *
                        Math.Exp(-t * 65d);
                    samples[start + j] += (float)(amplitude * envelope *
                        (dampedNoise * crinkle + releaseTone * 0.13d));
                }
            }
            // Two tiny drops caught by the loosening surface, not a stream.
            double[] drops = { 0.29, 0.79 };
            foreach (double drop in drops)
            {
                int start = (int)((drop + Next(ref random) * 0.045d) * SampleRate);
                double frequency = 680d + Next(ref random) * 170d;
                double amplitude = 0.042d + Next(ref random) * 0.022d;
                for (int j = 0; j < SampleRate * 0.14d && start + j < samples.Length; j++)
                {
                    double t = j / (double)SampleRate;
                    double envelope = Math.Min(1d, t / 0.004d) * Math.Exp(-t * 48d);
                    double bubble = Math.Sin(Math.PI * 2d *
                        (frequency * t + 950d * t * t));
                    double touch = (Next(ref random) * 2d - 1d) * Math.Exp(-t * 230d);
                    samples[start + j] += (float)(amplitude * envelope * (bubble + touch * 0.22d));
                }
            }
            float peak = 0f;
            foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak > 0f)
                for (int i = 0; i < samples.Length; i++) samples[i] *= 0.50f / peak;
            return samples;
        }

        public static float[] GenerateSamples(int variant)
        {
            var samples = new float[(int)(SampleRate * ClipSeconds)];
            uint random = unchecked(0xC01DF012u + (uint)variant * 7919u);
            double[] starts = { 0.025, 0.042, 0.071, 0.24, 0.277, 0.31, 0.57, 0.592, 0.83, 0.872, 1.04 };
            for (int click = 0; click < starts.Length; click++)
            {
                float variation = Next(ref random);
                int start = (int)((starts[click] + variation * 0.025) * SampleRate);
                double frequency = 1700d + variation * 1200d;
                double amplitude = 0.24d + 0.26d * Next(ref random);
                for (int j = 0; j < SampleRate * 0.22 && start + j < samples.Length; j++)
                {
                    double t = j / (double)SampleRate;
                    double attack = Math.Min(1d, t / 0.0012d);
                    double dry = (Next(ref random) * 2d - 1d) * Math.Exp(-t * 175d);
                    double ring = (Math.Sin(t * frequency * Math.PI * 2d) +
                        0.37d * Math.Sin(t * frequency * 1.783d * Math.PI * 2d) +
                        0.19d * Math.Sin(t * frequency * 2.317d * Math.PI * 2d)) *
                        Math.Exp(-t * 38d);
                    samples[start + j] += (float)(amplitude * attack * (dry * 0.72d + ring * 0.28d));
                }
            }
            float peak = 0f;
            foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak > 0f)
                for (int i = 0; i < samples.Length; i++) samples[i] *= 0.72f / peak;
            return samples;
        }

        private static float Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00ffffffu) / 16777216f;
        }
    }
}
