using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Explicitly packaged shared presentation materials. Every generated
    /// glass, liquid volume and stream reuses these two material assets.
    /// </summary>
    public static class BarDrinkServiceResources
    {
        public const string GlassMaterialResourcePath =
            "Materials/BarDrinkGlass";
        public const string LiquidMaterialResourcePath =
            "Materials/BarDrinkLiquid";

        private static Material glassMaterial;
        private static Material liquidMaterial;

        public static Material GlassMaterial => Load(
            ref glassMaterial,
            GlassMaterialResourcePath);

        public static Material LiquidMaterial => Load(
            ref liquidMaterial,
            LiquidMaterialResourcePath);

        private static Material Load(
            ref Material cached,
            string resourcePath)
        {
            if (cached == null)
            {
                cached = Resources.Load<Material>(resourcePath);
            }

            if (cached == null ||
                cached.shader == null ||
                !cached.shader.isSupported)
            {
                throw new InvalidOperationException(
                    "Missing or unsupported bar drink material at " +
                    $"Resources/{resourcePath}.");
            }

            return cached;
        }
    }
}
