using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// Owns the URP chromatic-aberration and lens-distortion overrides
    /// that rise with intoxication. Fed each presentation update by
    /// the intoxication status controller; both effects collapse to
    /// zero whenever the player disables the drunk lens toggle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IntoxicationLensVolumeDriver : MonoBehaviour
    {
        public const float VolumePriority = 8f;

        private Volume volume;
        private VolumeProfile profile;
        private ChromaticAberration chromaticAberration;
        private LensDistortion lensDistortion;

        public float AppliedChromaticAberration =>
            chromaticAberration != null
                ? chromaticAberration.intensity.value
                : 0f;
        public float AppliedLensDistortion =>
            lensDistortion != null
                ? lensDistortion.intensity.value
                : 0f;

        private void Awake()
        {
            GameObject host =
                new GameObject("Intoxication Lens Grade");
            host.transform.SetParent(transform, false);
            volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.weight = 1f;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Runtime Intoxication Lens Grade";
            profile.hideFlags = HideFlags.HideAndDontSave;
            volume.profile = profile;

            chromaticAberration =
                profile.Add<ChromaticAberration>(true);
            chromaticAberration.intensity.Override(0f);
            lensDistortion = profile.Add<LensDistortion>(true);
            lensDistortion.intensity.Override(0f);
        }

        public void Apply(
            float chromaticAberrationIntensity,
            float lensDistortionIntensity)
        {
            if (chromaticAberration == null)
            {
                return;
            }

            if (!GraphicsEffectsSettings.IntoxicationLensFxEnabled)
            {
                chromaticAberrationIntensity = 0f;
                lensDistortionIntensity = 0f;
            }

            chromaticAberration.intensity.Override(
                Mathf.Clamp01(chromaticAberrationIntensity));
            lensDistortion.intensity.Override(
                Mathf.Clamp(lensDistortionIntensity, -1f, 1f));
        }

        public void Clear()
        {
            Apply(0f, 0f);
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
