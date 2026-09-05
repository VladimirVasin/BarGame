using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Applies one canonical facial cell without cloning the hero's shared
    /// material.
    /// </summary>
    internal sealed class Player3DFaceAtlasPresenter
    {
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int LegacyMapId =
            Shader.PropertyToID("_MainTex");
        private static readonly int LegacyMapTransformId =
            Shader.PropertyToID("_MainTex_ST");

        private Player3DFaceAtlasBinding binding;
        private MaterialPropertyBlock properties;
        private MaterialPropertyBlock originalProperties;

        public bool IsConfigured => binding != null && binding.IsConfigured;

        public void Configure(Player3DFaceAtlasBinding configuredBinding)
        {
            Reset();
            binding = configuredBinding != null &&
                      configuredBinding.IsConfigured
                ? configuredBinding
                : null;
            if (binding != null)
            {
                originalProperties = new MaterialPropertyBlock();
                binding.Renderer.GetPropertyBlock(originalProperties);
            }
        }

        /// <summary>
        /// Shows the cell for <paramref name="expression"/>, or the cell
        /// of the nearest face the atlas does have when this one is
        /// missing (an atlas built before the drink's faces existed).
        /// With <paramref name="soiled"/> the soiled twin is preferred at
        /// every step; the binding itself drops to the clean cell when an
        /// atlas has no twin for that face.
        /// </summary>
        public bool Apply(PlayerFacialExpression expression, bool soiled = false)
        {
            if (!IsConfigured)
            {
                return false;
            }

            if (!binding.TryGetTextureTransform(
                    expression,
                    soiled,
                    out Vector4 textureTransform))
            {
                PlayerFacialExpression fallback =
                    PlayerFacialExpressionRules.Fallback(expression);
                if (fallback == expression ||
                    !binding.TryGetTextureTransform(
                        fallback,
                        soiled,
                        out textureTransform))
                {
                    return false;
                }
            }

            properties ??= new MaterialPropertyBlock();
            binding.Renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, binding.Texture);
            properties.SetVector(BaseMapTransformId, textureTransform);
            properties.SetTexture(LegacyMapId, binding.Texture);
            properties.SetVector(LegacyMapTransformId, textureTransform);
            binding.Renderer.SetPropertyBlock(properties);
            properties.Clear();
            return true;
        }

        public void Reset()
        {
            if (IsConfigured && originalProperties != null)
            {
                binding.Renderer.SetPropertyBlock(originalProperties);
            }

            binding = null;
            properties?.Clear();
            properties = null;
            originalProperties?.Clear();
            originalProperties = null;
        }
    }
}
