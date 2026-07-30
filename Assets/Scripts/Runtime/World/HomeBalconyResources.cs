using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class HomeBalconyResources
    {
        public const string GlassShaderResourcePath =
            "Shaders/HomeWindowGlass";
        public const int WindowLightCookieResolution = 64;

        private static Material glassMaterial;
        private static Texture2D windowLightCookie;

        public static Material GlassMaterial
        {
            get
            {
                if (glassMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        GlassShaderResourcePath);
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing Resources shader " +
                            $"'{GlassShaderResourcePath}'.");
                    }

                    glassMaterial = new Material(shader)
                    {
                        name = "Home Window Glass Shared",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                }

                return glassMaterial;
            }
        }

        public static Texture2D WindowLightCookie
        {
            get
            {
                if (windowLightCookie == null)
                {
                    windowLightCookie =
                        CreateWindowLightCookie();
                }

                return windowLightCookie;
            }
        }

        private static Texture2D CreateWindowLightCookie()
        {
            const int apertureMinimumX = 6;
            const int apertureMaximumX = 57;
            const int apertureMinimumY = 11;
            const int apertureMaximumY = 52;
            const int mullionMinimumX = 30;
            const int mullionMaximumX = 33;
            const int mullionMinimumY = 30;
            const int mullionMaximumY = 33;

            var texture = new Texture2D(
                WindowLightCookieResolution,
                WindowLightCookieResolution,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Home Window Light Cookie Shared",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            var pixels = new Color32[
                WindowLightCookieResolution *
                WindowLightCookieResolution];
            for (int y = 0;
                 y < WindowLightCookieResolution;
                 y++)
            {
                for (int x = 0;
                     x < WindowLightCookieResolution;
                     x++)
                {
                    bool insideAperture =
                        x >= apertureMinimumX &&
                        x <= apertureMaximumX &&
                        y >= apertureMinimumY &&
                        y <= apertureMaximumY;
                    bool onMullion =
                        x >= mullionMinimumX &&
                        x <= mullionMaximumX ||
                        y >= mullionMinimumY &&
                        y <= mullionMaximumY;
                    byte value = insideAperture && !onMullion
                        ? (byte)238
                        : (byte)0;
                    pixels[
                        y * WindowLightCookieResolution + x] =
                        new Color32(
                            value,
                            value,
                            value,
                            value);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            DestroyGeneratedResource(glassMaterial);
            DestroyGeneratedResource(windowLightCookie);
            glassMaterial = null;
            windowLightCookie = null;
        }

        private static void DestroyGeneratedResource(
            Object generatedResource)
        {
            if (generatedResource == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(generatedResource);
            }
            else
            {
                Object.DestroyImmediate(generatedResource);
            }
        }
    }
}
