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
            float electrical =
                Mathf.Sin(phase * 188f) * 0.035f +
                Mathf.Sin(phase * 376f + 0.35f) * 0.018f;
            return air * slowWind + electrical;
        }

        private static float GenerateBarSample(float phase)
        {
            float roomBreath =
                0.78f +
                Mathf.Sin(phase * 2f + 0.6f) * 0.09f;
            float ventilation =
                Mathf.Sin(phase * 61f + 0.8f) * 0.075f +
                Mathf.Sin(phase * 127f + 2.6f) * 0.045f +
                Mathf.Sin(phase * 241f + 4.4f) * 0.025f;
            float mains =
                Mathf.Sin(phase * 200f) * 0.055f +
                Mathf.Sin(phase * 400f + 0.25f) * 0.022f;
            float refrigerator =
                Mathf.Sin(phase * 288f + 1.1f) * 0.026f;
            return ventilation * roomBreath + mains + refrigerator;
        }

        private static float GenerateHomeSample(float phase)
        {
            float compressorCycle =
                0.64f +
                Mathf.Sin(phase * 2f + 0.35f) * 0.18f;
            float refrigerator =
                Mathf.Sin(phase * 192f + 0.7f) * 0.040f +
                Mathf.Sin(phase * 384f + 1.4f) * 0.016f;
            float mains =
                Mathf.Sin(phase * 200f) * 0.030f +
                Mathf.Sin(phase * 400f + 0.18f) * 0.012f;
            float pipes =
                Mathf.Sin(phase * 17f + 2.1f) * 0.014f +
                Mathf.Sin(phase * 43f + 0.4f) * 0.008f;

            float primaryDripEnvelope = Mathf.Pow(
                0.5f +
                0.5f * Mathf.Cos(phase * 3f - 0.9f),
                28f);
            float secondaryDripEnvelope = Mathf.Pow(
                0.5f +
                0.5f * Mathf.Cos(phase * 5f + 1.8f),
                36f);
            float dripTone =
                Mathf.Sin(phase * 367f + 0.3f) * 0.065f +
                Mathf.Sin(phase * 521f + 1.2f) * 0.032f;
            float drips =
                dripTone *
                (primaryDripEnvelope +
                 secondaryDripEnvelope * 0.52f);

            return refrigerator * compressorCycle +
                   mains +
                   pipes +
                   drips;
        }

        private static float GenerateStairwellSample(float phase)
        {
            float ventilationBreath =
                0.72f +
                Mathf.Sin(phase * 2f + 0.4f) * 0.12f +
                Mathf.Sin(phase * 5f + 2.1f) * 0.05f;
            float ventilation =
                Mathf.Sin(phase * 47f + 0.3f) * 0.068f +
                Mathf.Sin(phase * 83f + 1.8f) * 0.042f +
                Mathf.Sin(phase * 139f + 4.2f) * 0.021f;
            float mains =
                Mathf.Sin(phase * 200f) * 0.043f +
                Mathf.Sin(phase * 400f + 0.22f) * 0.018f;
            float pipes =
                Mathf.Sin(phase * 13f + 1.2f) * 0.018f +
                Mathf.Sin(phase * 29f + 3.4f) * 0.010f;
            float knockEnvelope = Mathf.Pow(
                0.5f +
                0.5f * Mathf.Cos(phase * 3f + 1.35f),
                42f);
            float knock =
                knockEnvelope *
                (Mathf.Sin(phase * 277f) * 0.075f +
                 Mathf.Sin(phase * 391f + 0.7f) * 0.032f);
            float dripEnvelope = Mathf.Pow(
                0.5f +
                0.5f * Mathf.Cos(phase * 7f - 0.5f),
                48f);
            float drip =
                dripEnvelope *
                Mathf.Sin(phase * 521f + 0.8f) * 0.055f;
            return ventilation * ventilationBreath +
                   mains +
                   pipes +
                   knock +
                   drip;
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
        protected override float OutputVolume => 0.075f;
        protected override float CutoffFrequency => 5200f;
    }

    public sealed class BarAmbiencePlayer : SceneAmbiencePlayer
    {
        protected override RetroAmbienceKind AmbienceKind =>
            RetroAmbienceKind.Bar;
        protected override float OutputVolume => 0.065f;
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
