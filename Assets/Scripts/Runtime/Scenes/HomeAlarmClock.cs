using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class HomeAlarmClock : MonoBehaviour
    {
        public const int OwnedSourceCount = 1;
        public const int RuntimeClipCount = 1;
        public const int SampleRate =
            HomeAlarmClockSynthesis.SampleRate;
        public const string RuntimeClipName =
            "HomeAlarmClock_MechanicalRing";
        public const float RattleFrequency = 18f;
        public const float RattlePositionAmplitude = 0.008f;
        public const float RattleRotationAmplitude = 2.2f;
        public const int DisplayDigitCount = 4;
        public const int SegmentsPerDigit = 7;
        public const int DisplaySegmentCount =
            DisplayDigitCount * SegmentsPerDigit;
        public const int DisplayPunctuationCount = 2;

        private const float OutputVolume = 0.24f;
        private const float MinimumDistance = 0.7f;
        private const float MaximumDistance = 11f;

        [SerializeField] private AudioSource ringSource;

        private AudioClip generatedClip;
        private Renderer[] displaySegmentRenderers;
        private Renderer[] displayPunctuationRenderers;
        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private float rattleElapsedSeconds;
        private bool restPoseCaptured;

        private static readonly bool[,] DigitSegments =
        {
            { true, true, true, true, true, true, false },
            { false, true, true, false, false, false, false },
            { true, true, false, true, true, false, true },
            { true, true, true, true, false, false, true },
            { false, true, true, false, false, true, true },
            { true, false, true, true, false, true, true },
            { true, false, true, true, true, true, true },
            { true, true, true, false, false, false, false },
            { true, true, true, true, true, true, true },
            { true, true, true, true, false, true, true }
        };

        public bool IsInitialized { get; private set; }
        public bool IsRinging { get; private set; }
        public bool IsFollowingSessionTime { get; private set; }
        public AudioSource Source => ringSource;
        public AudioClip ActiveClip => generatedClip;
        public Vector3 RestLocalPosition => restLocalPosition;
        public Quaternion RestLocalRotation => restLocalRotation;
        public float RattleElapsedSeconds => rattleElapsedSeconds;
        public int DisplayedHour { get; private set; } = 6;
        public int DisplayedMinute { get; private set; }
        public bool DisplayVisible { get; private set; } = true;
        public string DisplayedTime =>
            $"{DisplayedHour:00}:{DisplayedMinute:00}";
        public int ConfiguredDisplaySegmentCount =>
            displaySegmentRenderers != null
                ? displaySegmentRenderers.Length
                : 0;
        public int ConfiguredDisplayPunctuationCount =>
            displayPunctuationRenderers != null
                ? displayPunctuationRenderers.Length
                : 0;
        public int LitDisplaySegmentCount
        {
            get
            {
                if (displaySegmentRenderers == null)
                {
                    return 0;
                }

                int count = 0;
                for (int index = 0;
                     index < displaySegmentRenderers.Length;
                     index++)
                {
                    if (displaySegmentRenderers[index] != null &&
                        displaySegmentRenderers[index]
                            .gameObject.activeSelf)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
        public int VisibleLitDisplaySegmentCount =>
            DisplayVisible
                ? LitDisplaySegmentCount
                : 0;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            if (IsFollowingSessionTime)
            {
                SyncSessionTime();
            }

            if (IsRinging &&
                ringSource != null &&
                !ringSource.isPlaying)
            {
                ringSource.Play();
            }
        }

        private void Update()
        {
            if (IsFollowingSessionTime)
            {
                SyncSessionTime();
            }

            AdvanceRattle(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            ringSource?.Stop();
            RestoreRestPose();
        }

        private void OnDestroy()
        {
            IsRinging = false;
            ringSource?.Stop();
            if (ringSource != null)
            {
                ringSource.clip = null;
            }

            RestoreRestPose();
            DestroyRuntimeClip();
            DestroyOwnedSource();
            ringSource = null;
            IsInitialized = false;
        }

        public void StartRinging()
        {
            if (!IsInitialized ||
                ringSource == null ||
                generatedClip == null)
            {
                EnsureInitialized();
            }

            if (IsRinging)
            {
                if (isActiveAndEnabled &&
                    ringSource != null &&
                    !ringSource.isPlaying)
                {
                    ringSource.Play();
                }

                return;
            }

            IsRinging = true;
            rattleElapsedSeconds = 0f;
            RestoreRestPose();
            if (isActiveAndEnabled &&
                ringSource != null &&
                !ringSource.isPlaying)
            {
                ringSource.Play();
            }
        }

        public void StopRinging()
        {
            IsRinging = false;
            ringSource?.Stop();
            rattleElapsedSeconds = 0f;
            RestoreRestPose();
        }

        public void SetDisplayTime(int hour, int minute)
        {
            if (hour < 0 || hour > 23)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(hour),
                    hour,
                    "Alarm-clock hour must be between 0 and 23.");
            }

            if (minute < 0 || minute > 59)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(minute),
                    minute,
                    "Alarm-clock minute must be between 0 and 59.");
            }

            if (DisplayedHour == hour &&
                DisplayedMinute == minute)
            {
                return;
            }

            DisplayedHour = hour;
            DisplayedMinute = minute;
            ApplyDisplayTime();
        }

        public void FollowSessionTime()
        {
            IsFollowingSessionTime = true;
            SetDisplayVisible(true);
            SyncSessionTime();
        }

        public void StopFollowingSessionTime()
        {
            IsFollowingSessionTime = false;
        }

        public void SetDisplayVisible(bool visible)
        {
            if (DisplayVisible == visible)
            {
                return;
            }

            DisplayVisible = visible;
            ApplyDisplayVisibility();
        }

        internal void ConfigureDisplay(
            GameObject[] segments,
            GameObject[] punctuation)
        {
            if (segments == null)
            {
                throw new System.ArgumentNullException(nameof(segments));
            }

            if (segments.Length != DisplaySegmentCount)
            {
                throw new System.ArgumentException(
                    $"The alarm-clock display requires exactly " +
                    $"{DisplaySegmentCount} segments.",
                    nameof(segments));
            }

            if (punctuation == null)
            {
                throw new System.ArgumentNullException(
                    nameof(punctuation));
            }

            if (punctuation.Length !=
                DisplayPunctuationCount)
            {
                throw new System.ArgumentException(
                    $"The alarm-clock display requires exactly " +
                    $"{DisplayPunctuationCount} punctuation elements.",
                    nameof(punctuation));
            }

            displaySegmentRenderers =
                new Renderer[DisplaySegmentCount];
            for (int index = 0;
                 index < DisplaySegmentCount;
                 index++)
            {
                Renderer renderer =
                    segments[index] != null
                        ? segments[index]
                            .GetComponent<Renderer>()
                        : null;
                if (renderer == null)
                {
                    throw new System.ArgumentException(
                        "Alarm-clock display segments require renderers.",
                        nameof(segments));
                }

                displaySegmentRenderers[index] = renderer;
            }

            displayPunctuationRenderers =
                new Renderer[DisplayPunctuationCount];
            for (int index = 0;
                 index < DisplayPunctuationCount;
                 index++)
            {
                Renderer renderer =
                    punctuation[index] != null
                        ? punctuation[index]
                            .GetComponent<Renderer>()
                        : null;
                if (renderer == null)
                {
                    throw new System.ArgumentException(
                        "Alarm-clock display punctuation requires renderers.",
                        nameof(punctuation));
                }

                displayPunctuationRenderers[index] =
                    renderer;
            }

            ApplyDisplayTime();
            ApplyDisplayVisibility();
        }

        public void AdvanceRattle(float unscaledDeltaTime)
        {
            if (!IsRinging ||
                !isActiveAndEnabled ||
                float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime <= 0f)
            {
                return;
            }

            rattleElapsedSeconds += unscaledDeltaTime;
            float phase =
                rattleElapsedSeconds *
                Mathf.PI *
                2f;
            float primary =
                Mathf.Sin(phase * RattleFrequency);
            float secondary =
                Mathf.Sin(phase * (RattleFrequency + 7f));

            transform.localPosition =
                restLocalPosition +
                new Vector3(
                    primary * RattlePositionAmplitude,
                    secondary *
                    RattlePositionAmplitude *
                    0.22f,
                    0f);
            transform.localRotation =
                restLocalRotation *
                Quaternion.Euler(
                    secondary *
                    RattleRotationAmplitude *
                    0.28f,
                    0f,
                    primary * RattleRotationAmplitude);
        }

        private void EnsureInitialized()
        {
            CaptureRestPose();
            if (ringSource == null)
            {
                var sourceObject =
                    new GameObject("Alarm Clock Ring Source");
                sourceObject.transform.SetParent(transform, false);
                ringSource =
                    sourceObject.AddComponent<AudioSource>();
            }

            ConfigureSource();
            if (generatedClip == null)
            {
                float[] samples =
                    HomeAlarmClockSynthesis
                        .GenerateMechanicalAlarmLoopSamples();
                generatedClip = AudioClip.Create(
                    RuntimeClipName,
                    samples.Length,
                    HomeAlarmClockSynthesis.Channels,
                    SampleRate,
                    false);
                generatedClip.SetData(samples, 0);
                generatedClip.hideFlags = HideFlags.DontSave;
            }

            ringSource.clip = generatedClip;
            IsInitialized = true;
        }

        private void CaptureRestPose()
        {
            if (restPoseCaptured)
            {
                return;
            }

            restLocalPosition = transform.localPosition;
            restLocalRotation = transform.localRotation;
            restPoseCaptured = true;
        }

        private void ConfigureSource()
        {
            ringSource.playOnAwake = false;
            ringSource.loop = true;
            ringSource.spatialBlend = 1f;
            ringSource.dopplerLevel = 0f;
            ringSource.rolloffMode = AudioRolloffMode.Linear;
            ringSource.minDistance = MinimumDistance;
            ringSource.maxDistance = MaximumDistance;
            ringSource.volume = OutputVolume;
            ringSource.priority = 92;
            ringSource.spread = 32f;
            ringSource.reverbZoneMix = 0.82f;
            GameAudioMixer.Route(
                ringSource,
                GameAudioGroup.SfxGameplay);
        }

        private void ApplyDisplayTime()
        {
            if (displaySegmentRenderers == null)
            {
                return;
            }

            int[] digits =
            {
                DisplayedHour / 10,
                DisplayedHour % 10,
                DisplayedMinute / 10,
                DisplayedMinute % 10
            };
            for (int digit = 0;
                 digit < DisplayDigitCount;
                 digit++)
            {
                for (int segment = 0;
                     segment < SegmentsPerDigit;
                     segment++)
                {
                    displaySegmentRenderers[
                        digit * SegmentsPerDigit + segment]
                        .gameObject.SetActive(
                            DigitSegments[
                                digits[digit],
                                segment]);
                }
            }
        }

        private void SyncSessionTime()
        {
            SetDisplayVisible(true);
            SetDisplayTime(
                GameSessionState.GameHour,
                GameSessionState.GameMinute);
        }

        private void ApplyDisplayVisibility()
        {
            SetDisplayRendererState(
                displaySegmentRenderers,
                DisplayVisible);
            SetDisplayRendererState(
                displayPunctuationRenderers,
                DisplayVisible);
        }

        private static void SetDisplayRendererState(
            Renderer[] renderers,
            bool visible)
        {
            if (renderers == null)
            {
                return;
            }

            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                Renderer renderer = renderers[index];
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }
        }

        private void RestoreRestPose()
        {
            if (!restPoseCaptured)
            {
                return;
            }

            transform.localPosition = restLocalPosition;
            transform.localRotation = restLocalRotation;
        }

        private void DestroyRuntimeClip()
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

        private void DestroyOwnedSource()
        {
            if (ringSource == null ||
                ringSource.gameObject == gameObject)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(ringSource.gameObject);
            }
            else
            {
                DestroyImmediate(ringSource.gameObject);
            }
        }
    }
}
