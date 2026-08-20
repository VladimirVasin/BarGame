using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Owns the one material shared by every camera-relative mountain ridge.
    /// The backdrop deliberately has its own shader: ordinary Exp2 fog makes
    /// an object at the City's 48 m far plane indistinguishable from the
    /// clear colour, while this material bakes a restrained amount of the
    /// same haze into the ridge colour without applying distance fog again.
    /// </summary>
    internal static class CityMountainBackdropResources
    {
        public const string ShaderResourcePath =
            "Shaders/CityMountainBackdrop";

        private static readonly int HazeColorId =
            Shader.PropertyToID("_HazeColor");
        private static readonly int HazeStrengthId =
            Shader.PropertyToID("_HazeStrength");

        private static Material sharedMaterial;

        public static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        ShaderResourcePath);
                    if (shader == null || !shader.isSupported)
                    {
                        throw new InvalidOperationException(
                            "Missing or unsupported mountain backdrop " +
                            $"shader '{ShaderResourcePath}'.");
                    }

                    sharedMaterial = new Material(shader)
                    {
                        name = "City Mountain Backdrop (Shared)",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                    sharedMaterial.SetColor(
                        HazeColorId,
                        RuntimeSceneSetup.CityFogColor);
                    sharedMaterial.SetFloat(HazeStrengthId, 0.34f);
                }

                return sharedMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (sharedMaterial != null)
            {
                UnityEngine.Object.Destroy(sharedMaterial);
                sharedMaterial = null;
            }
        }
    }
}
