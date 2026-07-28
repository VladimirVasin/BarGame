using System;
using UnityEngine;

namespace BarPromenade
{
    public static class PlayerShadowResources
    {
        public const string ShadowCasterShaderResourcePath =
            "Shaders/PlayerSpriteShadowCaster";

        private static Material shadowCasterMaterial;

        public static Material ShadowCasterMaterial
        {
            get
            {
                if (shadowCasterMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        ShadowCasterShaderResourcePath);
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing Resources shader " +
                            $"'{ShadowCasterShaderResourcePath}'.");
                    }

                    shadowCasterMaterial = new Material(shader)
                    {
                        name = "Player Sprite Shadow Caster Shared",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                }

                return shadowCasterMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            shadowCasterMaterial = null;
        }
    }
}
