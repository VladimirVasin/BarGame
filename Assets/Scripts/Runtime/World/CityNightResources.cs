using System;
using UnityEngine;

namespace BarPromenade
{
    public static class CityNightResources
    {
        public const string EmissiveMaterialResourcePath =
            "Materials/CityNoirEmission";
        public const string AtmosphereShaderResourcePath =
            "Shaders/CityAtmosphereParticle";

        private static Material emissiveMaterial;
        private static Material atmosphereMaterial;

        public static Material EmissiveMaterial
        {
            get
            {
                if (emissiveMaterial == null)
                {
                    emissiveMaterial = Resources.Load<Material>(
                        EmissiveMaterialResourcePath);
                }

                if (emissiveMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Missing Resources material " +
                        $"'{EmissiveMaterialResourcePath}'.");
                }

                return emissiveMaterial;
            }
        }

        public static Material AtmosphereMaterial
        {
            get
            {
                if (atmosphereMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        AtmosphereShaderResourcePath);
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing Resources shader " +
                            $"'{AtmosphereShaderResourcePath}'.");
                    }

                    atmosphereMaterial = new Material(shader)
                    {
                        name = "City Atmosphere Shared",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                }

                return atmosphereMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            emissiveMaterial = null;
            atmosphereMaterial = null;
        }
    }
}
