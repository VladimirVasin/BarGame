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
    /// resumes when the weight fades back out. Only one modal shot
    /// runs at a time, so a single static owner needs no tokens;
    /// End() while inactive is a safe no-op.
    /// </summary>
    public static class CinematicDepthOfField
    {
        public const float VolumePriority = 10f;
        public const float BlendInSeconds = 0.35f;
        public const float BlendOutSeconds = 0.45f;
        public const float MinimumFocusDistance = 0.1f;

        private static CinematicDepthOfFieldOwner owner;

        public static bool IsActive =>
            owner != null && owner.IsEngaged;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            owner = null;
        }

        public static void Begin(
            float focusDistanceMeters,
            float aperture = 4f,
            float focalLength = 50f)
        {
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

        public static void SetFocusDistance(float meters)
        {
            if (owner != null && owner.IsEngaged)
            {
                owner.SetFocusDistance(meters);
            }
        }

        public static void End()
        {
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
            volume.weight = 0f;
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

            float target = IsEngaged ? 1f : 0f;
            float seconds = IsEngaged
                ? CinematicDepthOfField.BlendInSeconds
                : CinematicDepthOfField.BlendOutSeconds;
            volume.weight = Mathf.MoveTowards(
                volume.weight,
                target,
                Time.unscaledDeltaTime / seconds);
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
