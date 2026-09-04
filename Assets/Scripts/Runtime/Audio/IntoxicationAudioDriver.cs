using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>
    /// Drives the one world tape bus after the player's presentation level is smoothed.
    /// Sources retain their own pitch, scheduling, spatialization and room sends.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class IntoxicationAudioDriver : MonoBehaviour
    {
        public const string EffectName = "Intoxication VHS";
        public const string IntensityParameter = "VhsIntensity";
        public const string PausedParameter = "VhsPaused";
        public const string ResetParameter = "VhsReset";

        private static IntoxicationAudioDriver instance;
        private AudioMixer mixer;
        private int resetEpoch;
        private bool hasStarted;
        private bool warningIssued;

        public float AppliedIntensity { get; private set; }
        public bool IsConfigured { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        public static IntoxicationAudioDriver EnsureInstalled()
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<IntoxicationAudioDriver>();
            }
            if (instance == null)
            {
                var root = new GameObject("[Bar Promenade] Intoxication Audio");
                instance = root.AddComponent<IntoxicationAudioDriver>();
            }
            return instance;
        }

        public static void ResetSession()
        {
            if (instance == null)
            {
                return;
            }
            instance.AppliedIntensity = 0f;
            instance.ClearHistory();
            if (instance.hasStarted && instance.mixer != null)
            {
                instance.mixer.SetFloat(IntensityParameter, 0f);
                instance.mixer.SetFloat(PausedParameter, 0f);
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            // AudioMixer.SetFloat is not safe in Awake/BeforeSceneLoad.
            hasStarted = true;
            mixer = GameAudioMixer.Mixer;
            ClearHistory();
            Apply();
        }

        private void LateUpdate()
        {
            if (hasStarted)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (mixer == null)
            {
                return;
            }
            AppliedIntensity = GameTimeScaleRuntime.PerceptionIntensity;
            // Inventory/journal freeze gameplay but intentionally keep ambient audio.
            bool paused = AudioListener.pause;
            bool intensitySet = mixer.SetFloat(IntensityParameter, AppliedIntensity);
            bool pauseSet = mixer.SetFloat(PausedParameter, paused ? 1f : 0f);
            IsConfigured = intensitySet && pauseSet;
            if (!IsConfigured && !warningIssued)
            {
                warningIssued = true;
                Debug.LogWarning("[Bar Promenade] VHS mixer controls are missing. " +
                    "Regenerate the canonical audio mixer with its native plug-in installed.");
            }
        }

        private void ClearHistory()
        {
            if (hasStarted && mixer != null &&
                mixer.GetFloat(ResetParameter, out float previousEpoch))
            {
                resetEpoch = Mathf.RoundToInt(previousEpoch);
            }
            resetEpoch = resetEpoch >= 1000000 ? 1 : resetEpoch + 1;
            if (hasStarted && mixer != null)
            {
                mixer.SetFloat(ResetParameter, resetEpoch);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                ClearHistory();
            }
        }

        private void OnDisable()
        {
            if (instance != this || !hasStarted || mixer == null)
            {
                return;
            }
            ClearHistory();
            mixer.SetFloat(IntensityParameter, 0f);
            mixer.SetFloat(PausedParameter, 0f);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
