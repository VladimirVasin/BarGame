using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public abstract class SceneMusicPlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioLowPassFilter toneFilter;

        public AudioSource Source => audioSource;
        public AudioLowPassFilter ToneFilter => toneFilter;
        public AudioClip ActiveClip => audioSource != null
            ? audioSource.clip
            : null;

        protected abstract string TrackResourcePath { get; }
        protected virtual float OutputVolume => 0.65f;

        protected virtual void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            toneFilter = GetComponent<AudioLowPassFilter>();
            ConfigureSource();
            ConfigureTone();
            LoadTheme();
        }

        private void ConfigureSource()
        {
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.dopplerLevel = 0f;
            audioSource.volume = OutputVolume;
            audioSource.priority = 64;
            GameAudioMixer.Route(
                audioSource,
                GameAudioGroup.Music);
        }

        private void ConfigureTone()
        {
            // The music keeps its dynamics and timing. This only rounds the
            // brittle top octave, unlike the stronger baked treatment on SFX.
            toneFilter.cutoffFrequency = 15500f;
            toneFilter.lowpassResonanceQ = 1f;
        }

        private void LoadTheme()
        {
            AudioClip clip = Resources.Load<AudioClip>(TrackResourcePath);
            audioSource.clip = clip;
            if (clip != null)
            {
                audioSource.Play();
            }
        }
    }
}
