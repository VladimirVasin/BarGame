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
        internal static readonly int ReceiveSkyFillId = Shader.PropertyToID("_ReceiveCitySkyFill");

        private static Material emissiveMaterial;
        private static Material atmosphereMaterial;
        private static Material streetFogMaterial;
        private static Texture3D streetDensityNoise;

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

        public static Material AtmosphereMaterial => SharedMaterial(ref atmosphereMaterial,
            AtmosphereShaderResourcePath, "City Atmosphere Shared", true);

        internal static Material StreetFogMaterial
        {
            get
            {
                if (streetFogMaterial != null) return streetFogMaterial;
                SharedMaterial(ref streetFogMaterial, "Shaders/CityStreetFog", "City Street Fog Shared", false);
                // A repeating 4 KiB lattice. Hardware interpolates all three axes and the
                // shader samples it at two scales. Nothing is regenerated as the wind changes.
                const int side = 16;
                var values = new byte[side * side * side];
                uint state = 0xD10F356Bu;
                for (int i = 0; i < values.Length; i++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    values[i] = (byte)(state >> 24);
                }
                streetDensityNoise = new Texture3D(side, side, side, TextureFormat.R8, false)
                {
                    name = "Street fog density", wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
                };
                streetDensityNoise.SetPixelData(values, 0);
                streetDensityNoise.Apply(false, true);
                streetFogMaterial.SetTexture("_DensityNoise", streetDensityNoise);
                return streetFogMaterial;
            }
        }

        private static Material SharedMaterial(ref Material material, string path, string name, bool instanced)
        {
            if (material != null) return material;
            Shader shader = Resources.Load<Shader>(path);
            if (shader == null) throw new InvalidOperationException($"Missing Resources shader '{path}'.");
            material = new Material(shader)
            {
                name = name, hideFlags = HideFlags.HideAndDontSave, enableInstancing = instanced
            };
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            // The emissive material is a Resources asset - only the
            // cache is dropped. The atmosphere material is created here,
            // so with domain reload disabled it must be destroyed or
            // each play session leaks one.
            emissiveMaterial = null;
            if (atmosphereMaterial != null)
            {
                UnityEngine.Object.Destroy(atmosphereMaterial);
                atmosphereMaterial = null;
            }
            if (streetFogMaterial != null)
            {
                UnityEngine.Object.Destroy(streetFogMaterial);
                streetFogMaterial = null;
            }
            if (streetDensityNoise != null)
            {
                UnityEngine.Object.Destroy(streetDensityNoise);
                streetDensityNoise = null;
            }
        }
    }
}
