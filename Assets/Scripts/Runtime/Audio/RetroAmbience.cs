using System;
using UnityEngine;

namespace BarPromenade
{
    public enum RetroAmbienceKind
    {
        City = 0,
        Bar,
        Home,
        Stairwell
    }

    public static class RetroAmbienceSynthesis
    {
        public const int SampleRate = 22050;
        public const float Duration = 4f;

        public static float[] GenerateSamples(RetroAmbienceKind kind)
        {
            if (kind != RetroAmbienceKind.City &&
                kind != RetroAmbienceKind.Bar &&
                kind != RetroAmbienceKind.Home &&
                kind != RetroAmbienceKind.Stairwell)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            int sampleCount = Mathf.RoundToInt(
                SampleRate * Duration);
            var samples = new float[sampleCount];
            float inverseCount = 1f / sampleCount;

            for (int index = 0; index < sampleCount; index++)
            {
                float loopPhase =
                    index * inverseCount * Mathf.PI * 2f;
                float sample;
                switch (kind)
                {
                    case RetroAmbienceKind.City:
                        sample = GenerateCitySample(loopPhase);
                        break;
                    case RetroAmbienceKind.Bar:
                        sample = GenerateBarSample(loopPhase);
                        break;
                    case RetroAmbienceKind.Home:
                        sample = GenerateHomeSample(loopPhase);
                        break;
                    default:
                        sample = GenerateStairwellSample(loopPhase);
                        break;
                }

                samples[index] = Mathf.Clamp(sample, -0.72f, 0.72f);
            }

            return samples;
        }

        internal static AudioClip CreateRuntimeClip(
            RetroAmbienceKind kind)
        {
            float[] samples = GenerateSamples(kind);
            AudioClip clip = AudioClip.Create(
                "RetroAmbience_" + kind,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float GenerateCitySample(float phase)
        {
            float slowWind =
                0.62f +
                Mathf.Sin(phase) * 0.13f +
                Mathf.Sin(phase * 3f + 1.7f) * 0.08f;
            float air =
                Mathf.Sin(phase * 73f + 0.4f) * 0.13f +
                Mathf.Sin(phase * 109f + 2.2f) * 0.10f +
                Mathf.Sin(phase * 157f + 4.1f) * 0.07f +
                Mathf.Sin(phase * 263f + 0.9f) * 0.045f +
                Mathf.Sin(phase * 431f + 3.4f) * 0.025f;
            // City infrastructure now speaks from real spatial owners.
            // This legacy bed is only the diffuse pressure of outdoor air.
            return air * slowWind * 0.58f;
        }

        private static float GenerateBarSample(float phase)
        {
            float roomBreath =
                0.78f +
                Mathf.Sin(phase * 2f + 0.6f) * 0.09f +
                Mathf.Sin(phase * 5f + 2.2f) * 0.035f;
            float ventilation =
                Mathf.Sin(phase * 61f + 0.8f) * 0.075f +
                Mathf.Sin(phase * 127f + 2.6f) * 0.045f +
                Mathf.Sin(phase * 241f + 4.4f) * 0.025f;
            float mains =
                Mathf.Sin(phase * 200f) * 0.055f +
                Mathf.Sin(phase * 400f + 0.25f) * 0.022f;
            float refrigerator =
                Mathf.Sin(phase * 288f + 1.1f) * 0.026f;
            // A broad, quiet room-air band gives the two positioned crowd
            // pockets something to sit inside. It remains abstract room
            // pressure, not a third disembodied group of voices.
            float occupiedAir =
                Mathf.Sin(phase * 673f + 0.25f) * 0.018f +
                Mathf.Sin(phase * 997f + 2.4f) * 0.013f +
                Mathf.Sin(phase * 1481f + 4.7f) * 0.008f;
            return ventilation * roomBreath +
                   mains +
                   refrigerator +
                   occupiedAir;
        }

        private static float GenerateHomeSample(float phase)
        {
            float roomBreath =
                0.78f +
                Mathf.Sin(phase + 0.35f) * 0.08f +
                Mathf.Sin(phase * 3f + 2.1f) * 0.035f;
            float softAir =
                Mathf.Sin(phase * 29f + 0.7f) * 0.028f +
                Mathf.Sin(phase * 53f + 1.4f) * 0.019f +
                Mathf.Sin(phase * 91f + 2.8f) * 0.010f;
            float mains =
                Mathf.Sin(phase * 200f) * 0.014f +
                Mathf.Sin(phase * 400f + 0.18f) * 0.005f;
            float warmBody =
                Mathf.Sin(phase * 11f + 1.1f) * 0.012f +
                Mathf.Sin(phase * 19f + 3.2f) * 0.007f;
            return softAir * roomBreath + mains + warmBody;
        }

        private static float GenerateStairwellSample(float phase)
        {
            float concreteBreath =
                0.74f +
                Mathf.Sin(phase * 2f + 0.4f) * 0.10f +
                Mathf.Sin(phase * 5f + 2.1f) * 0.035f;
            float roomAir =
                Mathf.Sin(phase * 37f + 0.3f) * 0.045f +
                Mathf.Sin(phase * 71f + 1.8f) * 0.026f +
                Mathf.Sin(phase * 131f + 4.2f) * 0.013f;
            float mains =
                Mathf.Sin(phase * 200f) * 0.028f +
                Mathf.Sin(phase * 400f + 0.22f) * 0.011f;
            float concreteBody =
                Mathf.Sin(phase * 9f + 0.6f) * 0.018f +
                Mathf.Sin(phase * 17f + 2.7f) * 0.012f +
                Mathf.Sin(phase * 31f + 4.1f) * 0.007f;
            return roomAir * concreteBreath +
                   mains +
                   concreteBody;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public abstract class SceneAmbiencePlayer : MonoBehaviour
    {
        private AudioClip generatedClip;

        public AudioSource Source { get; private set; }
        public AudioLowPassFilter ToneFilter { get; private set; }
        public AudioClip ActiveClip => generatedClip;

        protected abstract RetroAmbienceKind AmbienceKind { get; }
        protected abstract float OutputVolume { get; }
        protected abstract float CutoffFrequency { get; }

        protected virtual void Awake()
        {
            Source = GetComponent<AudioSource>();
            ToneFilter = GetComponent<AudioLowPassFilter>();
            generatedClip = RetroAmbienceSynthesis.CreateRuntimeClip(
                AmbienceKind);

            Source.playOnAwake = false;
            Source.loop = true;
            Source.spatialBlend = 0f;
            Source.dopplerLevel = 0f;
            Source.priority = 160;
            Source.volume = OutputVolume;
            Source.clip = generatedClip;
            GameAudioMixer.Route(
                Source,
                GameAudioGroup.AmbienceBeds);

            ToneFilter.cutoffFrequency = CutoffFrequency;
            ToneFilter.lowpassResonanceQ = 1f;
            Source.Play();
        }

        protected virtual void OnDestroy()
        {
            if (generatedClip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedClip);
            }
            else
            {
                DestroyImmediate(generatedClip);
            }

            generatedClip = null;
        }
    }

    public sealed class CityAmbiencePlayer : SceneAmbiencePlayer
    {
        protected override RetroAmbienceKind AmbienceKind =>
            RetroAmbienceKind.City;
        protected override float OutputVolume => 0.025f;
        protected override float CutoffFrequency => 5200f;
    }

    public sealed class BarAmbiencePlayer : SceneAmbiencePlayer
    {
        protected override RetroAmbienceKind AmbienceKind =>
            RetroAmbienceKind.Bar;
        protected override float OutputVolume => 0.09f;
        protected override float CutoffFrequency => 4300f;
    }

    public sealed class HomeAmbiencePlayer : SceneAmbiencePlayer
    {
        protected override RetroAmbienceKind AmbienceKind =>
            RetroAmbienceKind.Home;
        protected override float OutputVolume => 0.025f;
        protected override float CutoffFrequency => 3200f;
    }

    public sealed class StairwellAmbiencePlayer :
        SceneAmbiencePlayer
    {
        protected override RetroAmbienceKind AmbienceKind =>
            RetroAmbienceKind.Stairwell;
        protected override float OutputVolume => 0.036f;
        protected override float CutoffFrequency => 2900f;
    }
}
