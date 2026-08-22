using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The shared drive behind every body of water in the city.
    ///
    /// Water carries no per-renderer variation at all - no property
    /// blocks anywhere - so night factor and rain intensity have to
    /// reach a whole body at once, by being written on its material.
    /// With one body that could live in <see cref="CityRiverResources"/>;
    /// with two it cannot, because the river and the sea are two
    /// materials of the same shader and the registries that push those
    /// values have no business knowing how many bodies exist.
    ///
    /// So each body owns its own material and registers it here, and the
    /// weather and night paths address this class alone. A material
    /// registered after the last push is brought up to date on the spot,
    /// which is what lets a sea built halfway through a rain slot
    /// arrive already wet.
    /// </summary>
    internal static class CityWaterResources
    {
        private static readonly int NightFactorId =
            Shader.PropertyToID("_NightFactor");
        private static readonly int RainIntensityId =
            Shader.PropertyToID("_RainIntensity");

        private static readonly List<Material> materials =
            new List<Material>();

        private static float nightFactor = 1f;
        private static float rainIntensity;

        public static float NightFactor => nightFactor;
        public static float RainIntensity => rainIntensity;
        public static int Count => materials.Count;

        /// <summary>
        /// Adds a water material to the drive and brings it up to the
        /// values already pushed. Registering the same material twice is
        /// a no-op, so a body may call this from its material getter.
        /// </summary>
        public static void Register(Material material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            if (!materials.Contains(material))
            {
                materials.Add(material);
            }

            Apply(material);
        }

        public static void Unregister(Material material)
        {
            if (material == null)
            {
                return;
            }

            materials.Remove(material);
        }

        public static void SetNightFactor(float factor)
        {
            nightFactor = Mathf.Clamp01(factor);
            ApplyAll();
        }

        public static void SetRainIntensity(float intensity)
        {
            rainIntensity = Mathf.Clamp01(intensity);
            ApplyAll();
        }

        private static void ApplyAll()
        {
            for (int index = materials.Count - 1; index >= 0; index--)
            {
                Material material = materials[index];
                if (material == null)
                {
                    materials.RemoveAt(index);
                    continue;
                }

                Apply(material);
            }
        }

        private static void Apply(Material material)
        {
            material.SetFloat(NightFactorId, nightFactor);
            material.SetFloat(RainIntensityId, rainIntensity);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            materials.Clear();
            nightFactor = 1f;
            rainIntensity = 0f;
        }
    }
}
