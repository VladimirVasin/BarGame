using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityRiverResources
    {
        private const string ShaderName =
            "Bar Promenade/City River Water";

        private static readonly int NightFactorId =
            Shader.PropertyToID("_NightFactor");
        private static readonly int RainIntensityId =
            Shader.PropertyToID("_RainIntensity");

        private static Material waterMaterial;

        public static Material WaterMaterial
        {
            get
            {
                if (waterMaterial == null)
                {
                    Shader shader = Shader.Find(ShaderName);
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing river shader '{ShaderName}'.");
                    }

                    waterMaterial = new Material(shader)
                    {
                        name = "City River Water (Shared)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                return waterMaterial;
            }
        }

        public static void SetNightFactor(float factor)
        {
            WaterMaterial.SetFloat(
                NightFactorId,
                Mathf.Clamp01(factor));
        }

        public static void SetRainIntensity(float intensity)
        {
            WaterMaterial.SetFloat(
                RainIntensityId,
                Mathf.Clamp01(intensity));
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (waterMaterial != null)
            {
                UnityEngine.Object.Destroy(waterMaterial);
                waterMaterial = null;
            }
        }
    }
}
