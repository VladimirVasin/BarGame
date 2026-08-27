using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CityWindowFamily
    {
        Off = 0,
        Cold = 1,
        Warm = 2,
        Bar = 3,
        Home = 4,
        Supermarket = 5
    }

    /// <summary>
    /// Shared appearance for every facade window pane in the City and its
    /// bounded Home view. Each lit family owns one runtime material carrying
    /// the window sheet, so the day-night controller dims every lit window
    /// in the city by touching five materials instead of thousands of
    /// renderers; per-pane variety comes only from a UV quadrant chosen by
    /// the pane's stable hash. Dark panes stay on the lit default material,
    /// tinted like unlit glazing, and never change with the clock.
    /// </summary>
    internal static class CityWindowAppearance
    {
        public const string TextureResourcePath = "Textures/CityWindowAlbedo";
        public const int VariantCount = 4;

        // What a lit family's glass shows once the night factor reaches
        // zero: unlit glazing, a shade lighter than the always-dark panes
        // so a room that lights up at dusk still reads as glass by day.
        public static readonly Color DayGlass =
            new Color(0.045f, 0.055f, 0.062f);

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int BaseMapStId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int GlobalNightFactorId =
            Shader.PropertyToID("_CityWindowNightFactor");

        private static Texture2D texture;
        private static Material[] litMaterials;
        private static float nightFactor = 1f;

        public static float NightFactor => nightFactor;

        public static Texture2D Texture
        {
            get
            {
                if (texture == null)
                {
                    texture = Resources.Load<Texture2D>(
                        TextureResourcePath);
                }

                if (texture == null)
                {
                    throw new InvalidOperationException(
                        $"Missing window texture '{TextureResourcePath}'.");
                }

                return texture;
            }
        }

        public static Color ResolveLitColor(CityWindowFamily family)
        {
            switch (family)
            {
                case CityWindowFamily.Cold:
                    return CityExteriorAppearance.ColdWindow;
                case CityWindowFamily.Warm:
                    return CityExteriorAppearance.WarmWindow;
                case CityWindowFamily.Bar:
                    return CityExteriorAppearance.BarWindow;
                case CityWindowFamily.Home:
                    return CityExteriorAppearance.HomeWindow;
                case CityWindowFamily.Supermarket:
                    return CityExteriorAppearance.SupermarketWindow;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(family),
                        family,
                        "Only lit window families carry a glow colour.");
            }
        }

        public static Material ResolveLitMaterial(CityWindowFamily family)
        {
            if (family == CityWindowFamily.Off)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(family),
                    family,
                    "Dark panes use the default lit material.");
            }

            if (litMaterials == null)
            {
                litMaterials = new Material[6];
            }

            int index = (int)family;
            Material material = litMaterials[index];
            if (material == null)
            {
                material = new Material(
                    CityNightResources.EmissiveMaterial)
                {
                    name = $"City Window {family}",
                    hideFlags = HideFlags.HideAndDontSave
                };
                material.SetTexture(BaseMapId, Texture);
                ApplyNightFactor(material, family);
                litMaterials[index] = material;
            }

            return material;
        }

        /// <summary>
        /// Dims or lights every lit window in the scene at once. Called
        /// from the same night-factor path that drives the street lamp
        /// bulbs, in both the City and the bounded Home exterior.
        /// </summary>
        public static void SetNightFactor(float factor)
        {
            float clamped = Mathf.Clamp01(factor);
            Shader.SetGlobalFloat(GlobalNightFactorId, clamped);
            if (clamped.Equals(nightFactor))
            {
                return;
            }

            nightFactor = clamped;
            if (litMaterials == null)
            {
                return;
            }

            for (int index = 0; index < litMaterials.Length; index++)
            {
                if (litMaterials[index] != null)
                {
                    ApplyNightFactor(
                        litMaterials[index],
                        (CityWindowFamily)index);
                }
            }
        }

        /// <summary>
        /// Selects the pane's texture variant without touching colour, so
        /// the shared family material keeps full authority over the glow.
        /// </summary>
        public static void ApplyLitPane(
            Renderer renderer,
            uint paneHash)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetVector(
                BaseMapStId,
                ResolveVariantScaleOffset(paneHash));
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Gives a permanently dark pane the same frame-and-glass sheet on
        /// the ordinary lit material, so day and night it reads as glazing
        /// rather than as a painted rectangle.
        /// </summary>
        public static void ApplyDarkPane(
            Renderer renderer,
            uint paneHash)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, Texture);
            properties.SetVector(
                BaseMapStId,
                ResolveVariantScaleOffset(paneHash));
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Locks a pane to the plain-glass quadrant of the sheet, for
        /// storefront glazing where curtains or blinds would be wrong.
        /// </summary>
        public static void ApplyPlainPane(Renderer renderer)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetVector(
                BaseMapStId,
                PlainPaneScaleOffset);
            renderer.SetPropertyBlock(properties);
        }

        // The authored sheet keeps its plain cell top-left, which is
        // the (0, 0.5) quadrant once the image lands in UV space.
        public static readonly Vector4 PlainPaneScaleOffset =
            new Vector4(0.5f, 0.5f, 0f, 0.5f);

        public static Vector4 ResolveVariantScaleOffset(uint paneHash)
        {
            uint variant = (paneHash >> 8) % (uint)VariantCount;
            return new Vector4(
                0.5f,
                0.5f,
                (variant & 1u) * 0.5f,
                ((variant >> 1) & 1u) * 0.5f);
        }

        private static void ApplyNightFactor(
            Material material,
            CityWindowFamily family)
        {
            Color color = Color.Lerp(
                DayGlass,
                ResolveLitColor(family),
                nightFactor);
            material.SetColor(BaseColorId, color);
            material.SetColor(ColorId, color);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            // The sheet is a Resources asset - only the cache is
            // dropped. The lit materials are created here, so with
            // domain reload disabled they must be destroyed or each
            // play session leaks up to six.
            texture = null;
            if (litMaterials != null)
            {
                for (int index = 0;
                     index < litMaterials.Length;
                     index++)
                {
                    if (litMaterials[index] != null)
                    {
                        UnityEngine.Object.Destroy(
                            litMaterials[index]);
                    }
                }

                litMaterials = null;
            }

            nightFactor = 1f;
            Shader.SetGlobalFloat(GlobalNightFactorId, nightFactor);
        }
    }
}
