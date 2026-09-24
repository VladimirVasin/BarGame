using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// One shared Bokeh depth-of-field volume for the modal close-up
    /// shots (bar counter, fridge, graves, board games, bus seat). At
    /// priority 10 its overridden parameters win over the scene's
    /// Gaussian grade while its weight blends in, and the Gaussian
    /// resumes when the weight fades back out. Contextual camera loans use
    /// scoped tokens so stale cleanup cannot retune or release a later shot.
    /// Legacy modal callers retain the direct Begin/End API.
    /// </summary>
    public static class CinematicDepthOfField
    {
        public const float VolumePriority = 10f;
        public const float BlendInSeconds = 0.35f;
        public const float BlendOutSeconds = 0.45f;
        public const float MinimumFocusDistance = 0.1f;

        private static CinematicDepthOfFieldOwner owner;
        private static object leaseOwner;

        public static bool IsActive =>
            owner != null && owner.IsEngaged;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            owner = null;
            leaseOwner = null;
        }

        public static void Begin(
            float focusDistanceMeters,
            float aperture = 4f,
            float focalLength = 50f)
        {
            leaseOwner = null;
            if (!GraphicsEffectsSettings.DepthOfFieldEnabled)
            {
                return;
            }

            if (owner == null)
            {
                GameObject host =
                    new GameObject("Cinematic Depth Of Field");
                owner = host
                    .AddComponent<CinematicDepthOfFieldOwner>();
            }

            owner.Engage(
                focusDistanceMeters,
                aperture,
                focalLength);
        }

        /// <summary>Scoped contextual cameras cannot end or retune a later owner's shot.</summary>
        internal static bool TryBeginOwned(object token, float distance, float aperture, float focalLength,
            float blendInSeconds = BlendInSeconds, float blendOutSeconds = BlendOutSeconds, bool smoothBlend = false)
        {
            if (token == null || IsActive || leaseOwner != null) return false;
            Begin(distance, aperture, focalLength);
            if (owner != null) owner.SetBlend(blendInSeconds, blendOutSeconds, smoothBlend);
            leaseOwner = token;
            return true;
        }

        internal static void SetOwnedFocusDistance(object token, float distance)
        {
            if (ReferenceEquals(leaseOwner, token)) SetFocusDistance(distance);
        }

        internal static void EndOwned(object token, bool immediately)
        {
            if (!ReferenceEquals(leaseOwner, token)) return;
            if (immediately)
            {
                leaseOwner = null;
                if (owner != null) owner.DisengageImmediately();
            }
            else if (owner != null) owner.Disengage();
        }

        public static void SetFocusDistance(float meters)
        {
            if (owner != null && owner.IsEngaged)
            {
                owner.SetFocusDistance(meters);
            }
        }

        public static void End()
        {
            leaseOwner = null;
            if (owner != null)
            {
                owner.Disengage();
            }
        }

        /// <summary>
        /// Releases the modal override in the same frame. Use only when the
        /// owning shot has already handed the camera back to ordinary play;
        /// a blend-out there would leave cinematic blur on the chase camera.
        /// </summary>
        public static void EndImmediately()
        {
            leaseOwner = null;
            if (owner != null)
            {
                owner.DisengageImmediately();
            }
        }
    }

    internal sealed class CinematicDepthOfFieldOwner : MonoBehaviour
    {
        private Volume volume;
        private VolumeProfile profile;
        private DepthOfField depthOfField;
        private float blendInSeconds = CinematicDepthOfField.BlendInSeconds;
        private float blendOutSeconds = CinematicDepthOfField.BlendOutSeconds;
        private float blendWeight;
        private bool smoothBlend;

        public bool IsEngaged { get; private set; }

        private void Awake()
        {
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = CinematicDepthOfField.VolumePriority;
            volume.weight = 0f;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Runtime Cinematic Depth Of Field";
            profile.hideFlags = HideFlags.HideAndDontSave;
            volume.profile = profile;

            depthOfField = profile.Add<DepthOfField>(true);
            depthOfField.mode.Override(DepthOfFieldMode.Bokeh);
            depthOfField.focusDistance.Override(1f);
            depthOfField.aperture.Override(4f);
            depthOfField.focalLength.Override(50f);
            depthOfField.bladeCount.Override(5);
            depthOfField.bladeCurvature.Override(0.55f);
        }

        public void Engage(
            float focusDistanceMeters,
            float aperture,
            float focalLength)
        {
            SetBlend(CinematicDepthOfField.BlendInSeconds, CinematicDepthOfField.BlendOutSeconds, false);
            gameObject.SetActive(true);
            depthOfField.focusDistance.Override(
                Mathf.Max(
                    CinematicDepthOfField.MinimumFocusDistance,
                    focusDistanceMeters));
            depthOfField.aperture.Override(
                Mathf.Clamp(aperture, 1f, 32f));
            depthOfField.focalLength.Override(
                Mathf.Clamp(focalLength, 1f, 300f));
            IsEngaged = true;
        }

        public void SetBlend(float entrySeconds, float exitSeconds, bool smooth)
        {
            blendInSeconds = Mathf.Max(.01f, entrySeconds);
            blendOutSeconds = Mathf.Max(.01f, exitSeconds);
            smoothBlend = smooth;
            blendWeight = volume.weight;
        }

        public void SetFocusDistance(float meters)
        {
            depthOfField.focusDistance.Override(
                Mathf.Max(
                    CinematicDepthOfField.MinimumFocusDistance,
                    meters));
        }

        public void Disengage()
        {
            IsEngaged = false;
        }

        public void DisengageImmediately()
        {
            IsEngaged = false;
            blendWeight = 0f;
            if (volume != null) volume.weight = 0f;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            // The bus ride is a modal shot the pause menu can open
            // over, so an already-engaged volume still honors a live
            // toggle change.
            if (IsEngaged &&
                !GraphicsEffectsSettings.DepthOfFieldEnabled)
            {
                IsEngaged = false;
            }

            if (PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;

            float target = IsEngaged ? 1f : 0f;
            float seconds = IsEngaged
                ? blendInSeconds
                : blendOutSeconds;
            blendWeight = Mathf.MoveTowards(
                blendWeight,
                target,
                Time.unscaledDeltaTime / seconds);
            float t = blendWeight;
            volume.weight = smoothBlend ? t * t * t * (t * (t * 6f - 15f) + 10f) : t;
            if (!IsEngaged && volume.weight <= 0f)
            {
                gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (profile == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(profile);
            }
            else
            {
                DestroyImmediate(profile);
            }

            profile = null;
        }
    }
}
