using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public abstract class SceneMusicPlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;

        public AudioSource Source => audioSource;
        public AudioClip ActiveClip => audioSource != null
            ? audioSource.clip
            : null;

        protected abstract string TrackResourcePath { get; }
        protected virtual float OutputVolume => 0.65f;

        protected virtual void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            ConfigureSource();
            LoadTheme();
        }

        private void ConfigureSource()
        {
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.dopplerLevel = 0f;
            audioSource.volume = OutputVolume;
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
