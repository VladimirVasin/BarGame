using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public sealed class HomeSmokingMusicPlayer : MonoBehaviour
    {
        public const string ResourceFolder = "Audio/SmokingMusic";
        public const string TrackName = "smoking_theme";
        public const string ResourcePath =
            ResourceFolder + "/" + TrackName;
        public const float TargetVolume = 0.50f;
        public const float CutoffFrequency = 15500f;

        public AudioSource Source { get; private set; }
        public AudioLowPassFilter ToneFilter { get; private set; }
        public AudioClip ActiveClip => Source != null
            ? Source.clip
            : null;
        public float NormalizedGain { get; private set; }

        private void Awake()
        {
            Source = GetComponent<AudioSource>();
            ToneFilter = GetComponent<AudioLowPassFilter>();

            ConfigureSource();
            ConfigureTone();
            Source.clip = Resources.Load<AudioClip>(ResourcePath);
            ApplyNormalizedGain(0f);
        }

        public void BeginFromStart()
        {
            if (Source == null)
            {
                return;
            }

            Source.Stop();
            if (Source.clip != null)
            {
                Source.timeSamples = 0;
            }

            ApplyNormalizedGain(0f);
            if (Source.clip != null)
            {
                Source.Play();
            }
        }

        public void ApplyNormalizedGain(float normalizedGain)
        {
            NormalizedGain = Mathf.Clamp01(normalizedGain);
            if (Source != null)
            {
                Source.volume = TargetVolume * NormalizedGain;
            }
        }

        public void StopImmediate()
        {
            if (Source != null)
            {
                Source.Stop();
                if (Source.clip != null)
                {
                    Source.timeSamples = 0;
                }
            }

            ApplyNormalizedGain(0f);
        }

        private void OnDisable()
        {
            StopImmediate();
        }

        private void OnDestroy()
        {
            StopImmediate();
        }

        private void ConfigureSource()
        {
            Source.playOnAwake = false;
            Source.loop = true;
            Source.spatialBlend = 0f;
            Source.dopplerLevel = 0f;
            Source.priority = 64;
            GameAudioMixer.Route(Source, GameAudioGroup.Music);
        }

        private void ConfigureTone()
        {
            ToneFilter.cutoffFrequency = CutoffFrequency;
            ToneFilter.lowpassResonanceQ = 1f;
        }
    }
}
