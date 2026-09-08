using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>
    /// Runs the projector on the one world bus, from the same weight the
    /// picture is printed with.
    ///
    /// It only READS <see cref="BegottenModeRamp.Weight"/>. The ramp's clock
    /// belongs to the composite, which advances it once a frame; a second
    /// caller here would run the fifteen seconds at double speed. Reading it
    /// also means the sound cannot disagree with the picture: if the composite
    /// is not printing, the weight does not move and neither does the print in
    /// the ear.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class BegottenAudioDriver : MonoBehaviour
    {
        public const string EffectName = BegottenAudioRules.EffectName;
        public const string WeightParameter = BegottenAudioRules.WeightParameter;
        public const string PausedParameter = BegottenAudioRules.PausedParameter;
        public const string ResetParameter = BegottenAudioRules.ResetParameter;

        private static BegottenAudioDriver instance;
        private AudioMixer mixer;
        private int resetEpoch;
        private bool hasStarted;
        private bool warningIssued;

        public float AppliedWeight { get; private set; }
        public bool IsConfigured { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        public static BegottenAudioDriver EnsureInstalled()
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<BegottenAudioDriver>();
            }
            if (instance == null)
            {
                var root = new GameObject("[Bar Promenade] Begotten Audio");
                instance = root.AddComponent<BegottenAudioDriver>();
            }
            return instance;
        }

        public static void ResetSession()
        {
            if (instance == null)
            {
                return;
            }
            instance.AppliedWeight = 0f;
            instance.ClearHistory();
            if (instance.hasStarted && instance.mixer != null)
            {
                instance.mixer.SetFloat(WeightParameter, 0f);
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
            AppliedWeight = Mathf.Clamp01(BegottenModeRamp.Weight);
            // A paused menu is a still, and a still is not being projected.
            bool paused = AudioListener.pause;
            bool weightSet = mixer.SetFloat(WeightParameter, AppliedWeight);
            bool pauseSet = mixer.SetFloat(PausedParameter, paused ? 1f : 0f);
            IsConfigured = weightSet && pauseSet;
            if (!IsConfigured && !warningIssued)
            {
                warningIssued = true;
                Debug.LogWarning("[Bar Promenade] Begotten projector mixer " +
                    "controls are missing. Regenerate the canonical audio " +
                    "mixer with its native plug-in installed.");
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
            mixer.SetFloat(WeightParameter, 0f);
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
