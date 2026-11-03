using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The dirty glass over the mirror's opening: one shared transparent
    /// Lit asset, so its straight-alpha shader variant is retained in player
    /// builds and the reflection reads through a faint tint of the plate.
    /// </summary>
    internal static class HomeBathroomMirrorResources
    {
        public const string GlassMaterialResourcePath = "Materials/HomeBathroomMirrorGlass";

        /// <summary>The plate's tint at the transparency the reflection is seen through.</summary>
        public static readonly Color GlassColor = new Color(0.22f, 0.28f, 0.27f, 0.22f);

        private static Material glassMaterial;

        public static Material GlassMaterial
        {
            get
            {
                if (glassMaterial == null)
                {
                    glassMaterial = Resources.Load<Material>(GlassMaterialResourcePath);
                }

                if (glassMaterial == null || glassMaterial.shader == null || !glassMaterial.shader.isSupported)
                {
                    throw new InvalidOperationException(
                        $"Missing or unsupported bathroom mirror material at Resources/{GlassMaterialResourcePath}.");
                }

                return glassMaterial;
            }
        }

        // Resources owns the shared asset; a new play session only drops the cache.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            glassMaterial = null;
        }
    }
}
