using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One transient shadowless directional light that renders the current
    /// deterministic lightning sample. It stays disabled outside a flash, so
    /// it never counts against the pooled city-atmosphere light budget.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityLightningFlashLight : MonoBehaviour
    {
        public const float MaximumIntensity = 1.9f;
        public const float ElevationDegrees = 55f;

        public static readonly Color FlashColor =
            new Color(0.82f, 0.87f, 1f);

        private Light flashLight;
        private long appliedStrikeId = long.MinValue;

        public Light FlashLight => flashLight;

        private void Awake()
        {
            flashLight = gameObject.AddComponent<Light>();
            flashLight.type = LightType.Directional;
            flashLight.color = FlashColor;
            flashLight.intensity = 0f;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;
        }

        public void Apply(LightningSample sample)
        {
            if (flashLight == null)
            {
                return;
            }

            if (!sample.IsFlashing)
            {
                if (flashLight.enabled)
                {
                    flashLight.enabled = false;
                    flashLight.intensity = 0f;
                }

                return;
            }

            if (sample.StrikeId != appliedStrikeId)
            {
                appliedStrikeId = sample.StrikeId;
                flashLight.transform.rotation = Quaternion.Euler(
                    ElevationDegrees,
                    sample.AzimuthDegrees,
                    0f);
            }

            flashLight.intensity =
                MaximumIntensity *
                sample.FlashIntensity *
                Mathf.Lerp(1f, 0.45f, sample.DistanceFactor);
            flashLight.enabled = flashLight.intensity > 0.001f;
        }
    }
}
