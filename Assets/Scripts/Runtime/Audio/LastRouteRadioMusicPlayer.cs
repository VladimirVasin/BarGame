using UnityEngine;

namespace BarPromenade
{
    /// <summary>Three optional stations, owned by the drawn dashboard.</summary>
    public sealed class LastRouteRadioMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/LastRouteRadio";
        public const string TrackName = "radio_theme";
        public const string ResourcePath = ResourceFolder + "/Station1/" + TrackName;
        public const float RadioVolume = 0.42f;
        public const float SpeakerLowPassHz = 3500f;
        public const float SpeakerHighPassHz = 180f;
        public const float SpeakerDistortion = 0.075f;
        public const float MinimumDistance = 1.2f;
        public const float MaximumDistance = 9f;

        private static readonly float[] savedPlaybackSeconds = new float[LastRouteCarRadioModel.DetentCount];
        private static readonly string[] savedClipNames = new string[LastRouteCarRadioModel.DetentCount];
        private static int sessionGeneration;
        private int ownerGeneration;
        private int stationIndex;
        private LastRouteRadioSpeakerTexture texture;
        private float exitGain = 1f;

        public int StationIndex => stationIndex;
        public static float SavedPlaybackSeconds =>
            SavedPlaybackSecondsForStation(GameSessionState.CarDashboard.TuningDetent);
        public static float SavedPlaybackSecondsForStation(int station) =>
            savedPlaybackSeconds[LastRouteCarRadioModel.WrapDetent(station)];
        public static string ResourcePathForStation(int station) =>
            ResourceFolder + "/Station" + (LastRouteCarRadioModel.WrapDetent(station) + 1) + "/" + TrackName;
        public static AudioClip LoadStationClip(int station)
        {
            AudioClip preferred = Resources.Load<AudioClip>(ResourcePathForStation(station));
            if (preferred != null) return preferred;
            string folder = ResourceFolder + "/Station" + (LastRouteCarRadioModel.WrapDetent(station) + 1);
            AudioClip chosen = null;
            foreach (AudioClip clip in Resources.LoadAll<AudioClip>(folder))
                if (chosen == null || System.StringComparer.Ordinal.Compare(clip.name, chosen.name) < 0)
                    chosen = clip;
            return chosen;
        }
        protected override AudioClip LoadThemeClip() => LoadStationClip(stationIndex);
        protected override string TrackResourcePath => ResourcePathForStation(stationIndex);
        protected override bool InitiallySuppressed =>
            !GameSessionState.CarDashboard.RadioOn;
        protected override float SuppressionFadeOutSeconds => 0f;
        protected override float OutputVolume => RadioVolume * exitGain;

        public static LastRouteRadioMusicPlayer Create(
            LastRouteCarAssetRegistry registry)
        {
            if (registry == null || registry.RadioDialRenderer == null)
            {
                return null;
            }

            // The imported model retains its unit scale; preserve world pose
            // and inherit the moving, sprung body through the actual dial.
            Renderer dial = registry.RadioDialRenderer;
            GameObject owner = new GameObject("Car Radio Music");
            owner.SetActive(false);
            owner.transform.SetParent(dial.transform, false);
            owner.transform.position = dial.bounds.center;
            owner.AddComponent<AudioSource>().playOnAwake = false;
            var player = owner.AddComponent<LastRouteRadioMusicPlayer>();
            owner.SetActive(true);
            return player;
        }

        /// <summary>
        /// A two-pass handover is independent of component execution order:
        /// every outgoing source is silenced or claims its tail before any incoming Play.
        /// Only the city score yields to the dashboard's stored switch.
        /// </summary>
        public static void ApplyPowerState(bool radioOn)
        {
            SceneMusicPlayer[] players =
                Object.FindObjectsByType<SceneMusicPlayer>(FindObjectsSortMode.None);
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (SceneMusicPlayer player in players)
                {
                    bool suppress;
                    if (player is CityMusicPlayer)
                    {
                        suppress = radioOn;
                    }
                    else if (player is LastRouteRadioMusicPlayer)
                    {
                        suppress = !radioOn;
                    }
                    else
                    {
                        continue;
                    }

                    if (suppress == (pass == 0))
                    {
                        if (suppress && player is LastRouteRadioMusicPlayer radio)
                            radio.SwitchPowerOffImmediately();
                        else
                            player.SetPlaybackSuppressed(suppress);
                    }
                }
            }
        }

        private void SwitchPowerOffImmediately()
        {
            // Capture before Pause/Stop and mute the circuit noise in this
            // same call. The switch cuts power even to a detached old car.
            CapturePlayhead();
            texture?.SetHissGain(0f);
            if (IsSceneExitFadeRequested)
                CompleteSceneExitFadeImmediately();
            else
                SetPlaybackSuppressed(true);
        }

        public static void ApplyTuningState(int station)
        {
            foreach (LastRouteRadioMusicPlayer player in
                     Object.FindObjectsByType<LastRouteRadioMusicPlayer>(FindObjectsSortMode.None))
            {
                if (player.ownerGeneration == sessionGeneration && !player.IsSceneExitFadeRequested)
                    player.SelectStation(station);
            }
        }

        private void SelectStation(int station)
        {
            int next = LastRouteCarRadioModel.WrapDetent(station);
            if (stationIndex == next) return;
            CapturePlayhead();
            stationIndex = next;
            ReloadTheme();
        }

        public static void ResetSession()
        {
            sessionGeneration = unchecked(sessionGeneration + 1);
            System.Array.Clear(savedPlaybackSeconds, 0, savedPlaybackSeconds.Length);
            System.Array.Clear(savedClipNames, 0, savedClipNames.Length);
        }

        protected override void Awake()
        {
            ownerGeneration = sessionGeneration;
            stationIndex = GameSessionState.CarDashboard.TuningDetent;
            base.Awake();
            Source.spatialBlend = 1f;
            Source.rolloffMode = AudioRolloffMode.Linear;
            Source.minDistance = MinimumDistance;
            Source.maxDistance = MaximumDistance;
            Source.spread = 0f;
            Source.dopplerLevel = 0f;
            Source.bypassEffects = false;
            ToneFilter.cutoffFrequency = SpeakerLowPassHz;
            var highPass = gameObject.AddComponent<AudioHighPassFilter>();
            highPass.cutoffFrequency = SpeakerHighPassHz;
            var distortion = gameObject.AddComponent<AudioDistortionFilter>();
            distortion.distortionLevel = SpeakerDistortion;
            texture = gameObject.AddComponent<LastRouteRadioSpeakerTexture>();
        }

        protected override void Update()
        {
            // Capture before the shared fade can stop/destroy its old carrier.
            // A replacement waiting for that tail never writes a zero over it.
            CapturePlayhead();
            base.Update();
            texture.SetHissGain(Source.isPlaying ? Source.volume : 0f);
            float time = Time.unscaledTime;
            Source.pitch = 1f + Mathf.Sin(time * 2f * Mathf.PI * 0.45f) * 0.0022f +
                           Mathf.Sin(time * 2f * Mathf.PI * 6.1f) * 0.0007f;
        }

        protected override void PrepareForPlayback()
        {
            if (ownerGeneration == sessionGeneration &&
                ActiveClip != null && savedClipNames[stationIndex] == ActiveClip.name)
            {
                // Read at Play, not Awake: the previous scene's tail may still
                // have been advancing while this car was being constructed.
                Source.timeSamples = Mathf.Clamp(
                    Mathf.FloorToInt(savedPlaybackSeconds[stationIndex] * ActiveClip.frequency),
                    0, ActiveClip.samples - 1);
            }
        }

        protected override void PrepareForSceneExitFade()
        {
            CapturePlayhead();
            float nearestDistance = float.PositiveInfinity;
            foreach (AudioListener listener in
                     Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (listener.enabled)
                {
                    nearestDistance = Mathf.Min(nearestDistance,
                        Vector3.Distance(listener.transform.position, transform.position));
                }
            }

            exitGain = float.IsPositiveInfinity(nearestDistance) ? 0f :
                Mathf.Clamp01(1f - Mathf.Max(0f, nearestDistance - MinimumDistance) /
                    (MaximumDistance - MinimumDistance));
            Source.volume = OutputVolume * NormalizedGain;
            Source.spatialBlend = 0f;
        }

        private void CapturePlayhead()
        {
            if (ownerGeneration != sessionGeneration ||
                ActiveClip == null || (!Source.isPlaying && !IsPaused))
            {
                return;
            }

            // A never-played suppressed replacement is paused too. It must
            // not replace the previous car's remembered tape position.
            int sample = Source.timeSamples;
            if (!Source.isPlaying && sample == 0)
            {
                return;
            }

            savedClipNames[stationIndex] = ActiveClip.name;
            savedPlaybackSeconds[stationIndex] = sample / (float)ActiveClip.frequency;
        }

        protected override void OnDisable()
        {
            texture?.SetHissGain(0f);
            CapturePlayhead();
            base.OnDisable();
        }
    }
}
