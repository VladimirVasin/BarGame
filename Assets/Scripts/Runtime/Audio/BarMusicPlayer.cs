using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bar theme is diegetic: it comes from the grille of the visible
    /// jukebox, through the narrow bandwidth and light saturation of its
    /// cabinet. A quiet motor/record texture shares that exact point so the
    /// machine still has a physical voice between musical transients.
    /// </summary>
    public sealed class BarMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/BarMusic";
        public const string TrackName = "bar_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;
        public const float ThemeOutputVolume =
            MusicMix.BarOutputVolume;
        public const float SpatialBlend = 1f;
        public const float MinimumDistance = 2.5f;
        public const float DefaultMaximumDistance = 26f;
        public const float SpeakerSpreadDegrees = 18f;
        public const float SpeakerLowPassFrequency = 5600f;
        public const float SpeakerHighPassFrequency = 120f;
        public const float SpeakerDistortionLevel = 0.055f;
        public const float CabinetOutputVolume = 0.11f;
        public const float CabinetMaximumDistance = 9f;
        public const int CabinetSampleRate = 22050;
        public const float CabinetLoopDuration = 8f;

        [SerializeField] private AudioHighPassFilter speakerHighPass;
        [SerializeField] private AudioDistortionFilter speakerDistortion;
        [SerializeField] private AudioSource cabinetSource;
        [SerializeField] private AudioLowPassFilter cabinetLowPass;

        private AudioClip cabinetClip;
        private float jukeboxGain = 1f;
        private float exitTailScale = 1f;
        private float cabinetExitTailScale = 1f;

        public AudioHighPassFilter SpeakerHighPass => speakerHighPass;
        public AudioDistortionFilter SpeakerDistortion =>
            speakerDistortion;
        public AudioSource CabinetSource => cabinetSource;
        public AudioClip CabinetClip => cabinetClip;

        protected override string TrackResourcePath => ResourcePath;
        protected override float OutputVolume =>
            ThemeOutputVolume * jukeboxGain * exitTailScale;

        protected override void Awake()
        {
            base.Awake();
            ConfigureSpeaker();
            EnsureCabinetTexture();
        }

        protected override void Update()
        {
            base.Update();
            RefreshCabinetPlayback();
        }

        public void ConfigureJukebox(
            float maximumDistance,
            float volumeScale)
        {
            jukeboxGain = Mathf.Clamp01(volumeScale);
            Source.maxDistance = Mathf.Max(
                MinimumDistance + 0.1f,
                maximumDistance);
            Source.volume = OutputVolume * NormalizedGain;

            if (cabinetSource != null)
            {
                cabinetSource.maxDistance = Mathf.Min(
                    CabinetMaximumDistance,
                    Source.maxDistance);
            }

            RefreshCabinetPlayback();
        }

        protected override void PrepareForSceneExitFade()
        {
            AudioListener[] listeners =
                FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Exclude);
            AudioListener nearest = null;
            float nearestDistanceSquared = float.PositiveInfinity;
            for (int index = 0; index < listeners.Length; index++)
            {
                AudioListener listener = listeners[index];
                if (listener == null || !listener.enabled)
                {
                    continue;
                }

                float distanceSquared =
                    (listener.transform.position - transform.position)
                    .sqrMagnitude;
                if (distanceSquared < nearestDistanceSquared)
                {
                    nearest = listener;
                    nearestDistanceSquared = distanceSquared;
                }
            }

            PrepareSpatialExitTail(
                nearest != null
                    ? nearest.transform.position
                    : transform.position);
        }

        internal void PrepareSpatialExitTail(Vector3 listenerPosition)
        {
            exitTailScale = MeasureLinearAttenuation(
                Source,
                listenerPosition);
            cabinetExitTailScale = MeasureLinearAttenuation(
                cabinetSource,
                listenerPosition);
            Source.volume = OutputVolume * NormalizedGain;
            if (cabinetSource != null)
            {
                cabinetSource.volume = ActiveClip != null
                    ? CabinetOutputVolume * jukeboxGain *
                      cabinetExitTailScale * NormalizedGain
                    : 0f;
            }

            Source.spatialBlend = 0f;
            if (cabinetSource != null)
            {
                cabinetSource.spatialBlend = 0f;
            }
        }

        private void ConfigureSpeaker()
        {
            Source.spatialBlend = SpatialBlend;
            Source.rolloffMode = AudioRolloffMode.Linear;
            Source.minDistance = MinimumDistance;
            Source.maxDistance = DefaultMaximumDistance;
            Source.spread = SpeakerSpreadDegrees;
            Source.dopplerLevel = 0f;
            Source.bypassEffects = false;
            Source.bypassListenerEffects = false;
            Source.bypassReverbZones = false;

            ToneFilter.cutoffFrequency = SpeakerLowPassFrequency;
            ToneFilter.lowpassResonanceQ = 1.15f;

            speakerHighPass = GetComponent<AudioHighPassFilter>();
            if (speakerHighPass == null)
            {
                speakerHighPass =
                    gameObject.AddComponent<AudioHighPassFilter>();
            }

            speakerHighPass.cutoffFrequency =
                SpeakerHighPassFrequency;
            speakerHighPass.highpassResonanceQ = 1.05f;

            speakerDistortion = GetComponent<AudioDistortionFilter>();
            if (speakerDistortion == null)
            {
                speakerDistortion =
                    gameObject.AddComponent<AudioDistortionFilter>();
            }

            speakerDistortion.distortionLevel =
                SpeakerDistortionLevel;
        }

        private void EnsureCabinetTexture()
        {
            if (cabinetSource == null)
            {
                GameObject textureObject =
                    new GameObject("Jukebox Cabinet Texture");
                textureObject.transform.SetParent(transform, false);
                cabinetSource =
                    textureObject.AddComponent<AudioSource>();
                cabinetLowPass =
                    textureObject.AddComponent<AudioLowPassFilter>();
            }

            cabinetLowPass = cabinetLowPass != null
                ? cabinetLowPass
                : cabinetSource.GetComponent<AudioLowPassFilter>();
            if (cabinetLowPass == null)
            {
                cabinetLowPass =
                    cabinetSource.gameObject.AddComponent<
                        AudioLowPassFilter>();
            }

            cabinetClip = CreateCabinetClip();
            cabinetSource.playOnAwake = false;
            cabinetSource.loop = true;
            cabinetSource.spatialBlend = SpatialBlend;
            cabinetSource.rolloffMode = AudioRolloffMode.Linear;
            cabinetSource.minDistance = 1.1f;
            cabinetSource.maxDistance = CabinetMaximumDistance;
            cabinetSource.dopplerLevel = 0f;
            cabinetSource.spread = 0f;
            cabinetSource.priority = 182;
            cabinetSource.clip = cabinetClip;
            cabinetSource.volume = 0f;
            GameAudioMixer.Route(
                cabinetSource,
                GameAudioGroup.AmbienceDetails);

            cabinetLowPass.cutoffFrequency = 3100f;
            cabinetLowPass.lowpassResonanceQ = 1f;
            if (ActiveClip != null && isActiveAndEnabled)
            {
                cabinetSource.Play();
            }
        }

        private void RefreshCabinetPlayback()
        {
            if (cabinetSource == null)
            {
                return;
            }

            cabinetSource.volume = ActiveClip != null
                ? CabinetOutputVolume * jukeboxGain *
                  cabinetExitTailScale * NormalizedGain
                : 0f;
            if (ActiveClip != null &&
                isActiveAndEnabled &&
                !cabinetSource.isPlaying)
            {
                cabinetSource.Play();
            }
        }

        private static float MeasureLinearAttenuation(
            AudioSource source,
            Vector3 listenerPosition)
        {
            if (source == null)
            {
                return 1f;
            }

            float distance = Vector3.Distance(
                source.transform.position,
                listenerPosition);
            float range = Mathf.Max(
                0.001f,
                source.maxDistance - source.minDistance);
            return Mathf.Clamp01(
                1f -
                Mathf.Max(0f, distance - source.minDistance) /
                range);
        }

        private static AudioClip CreateCabinetClip()
        {
            int sampleCount = Mathf.RoundToInt(
                CabinetSampleRate * CabinetLoopDuration);
            var samples = new float[sampleCount];
            float[] crackleTimes = { 0.72f, 2.18f, 3.91f, 5.36f, 7.14f };
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)CabinetSampleRate;
                float motor =
                    Mathf.Sin(2f * Mathf.PI * 50f * time) * 0.085f +
                    Mathf.Sin(2f * Mathf.PI * 100f * time + 0.3f) *
                    0.035f;
                float flutter =
                    0.78f +
                    Mathf.Sin(2f * Mathf.PI * 0.5f * time) * 0.08f +
                    Mathf.Sin(2f * Mathf.PI * 1.25f * time + 1.4f) *
                    0.045f;
                float surface =
                    Mathf.Sin(2f * Mathf.PI * 617f * time + 0.8f) *
                    0.018f +
                    Mathf.Sin(2f * Mathf.PI * 941f * time + 2.1f) *
                    0.012f +
                    Mathf.Sin(2f * Mathf.PI * 1499f * time + 3.6f) *
                    0.008f;
                float crackle = 0f;
                for (int crack = 0;
                     crack < crackleTimes.Length;
                     crack++)
                {
                    float age = time - crackleTimes[crack];
                    if (age >= 0f && age < 0.018f)
                    {
                        crackle +=
                            Mathf.Sin(
                                2f * Mathf.PI *
                                (1280f + crack * 173f) * age) *
                            Mathf.Exp(-age * 190f) *
                            0.16f;
                    }
                }

                samples[index] = Quantize(
                    motor * flutter + surface + crackle);
            }

            AudioClip clip = AudioClip.Create(
                "BarJukebox_Cabinet",
                sampleCount,
                1,
                CabinetSampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float Quantize(float sample)
        {
            return Mathf.Clamp(
                Mathf.Round(sample * 127f) / 127f,
                -0.9f,
                0.9f);
        }

        private void OnDestroy()
        {
            if (cabinetSource != null)
            {
                cabinetSource.Stop();
                cabinetSource.clip = null;
            }

            if (cabinetClip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(cabinetClip);
            }
            else
            {
                DestroyImmediate(cabinetClip);
            }

            cabinetClip = null;
        }
    }
}
